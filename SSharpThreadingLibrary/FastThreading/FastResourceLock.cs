/*
 * Process Hacker - 
 *   fast resource lock
 * 
 * Copyright (C) 2009 wj32
 * 
 * This file is part of Process Hacker.
 * 
 * Process Hacker is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Process Hacker is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Process Hacker.  If not, see <http://www.gnu.org/licenses/>.
 */

#define DEFER_EVENT_CREATION
//#define ENABLE_STATISTICS
//#define RIGOROUS_CHECKS

using System;
using Crestron.SimplSharp;
using SSharp.Threading;

#if RIGOROUS_CHECKS
using SSMono.Diagnostics;
#endif

#pragma warning disable 420

namespace ProcessHacker.Common.Threading
	{
	/// <summary>
	///     Provides a fast resource (reader-writer) lock.
	/// </summary>
	/// <remarks>
	///     There are three types of acquire methods in this lock:
	///     Normal methods (AcquireExclusive, AcquireShared) are preferred
	///     for general purpose use.
	///     Busy wait methods (SpinAcquireExclusive, SpinAcquireShared) are
	///     preferred if very little time is spent while the lock is acquired.
	///     However, these do not give exclusive acquires precedence over
	///     shared acquires.
	///     Try methods (TryAcquireExclusive, TryAcquireShared) can be used to
	///     quickly test if the lock is available.
	///     Note that all three types of functions can be used concurrently
	///     in the same class instance.
	/// </remarks>
	public sealed class FastResourceLock : IDisposable, IResourceLock
		{
		// Details
		//
		// Resource lock value width: 32 bits.
		// Lock owned (either exclusive or shared): L (1 bit).
		// Exclusive waking: W (1 bit).
		// Shared owners count: SC (8 bits).
		// Shared waiters count: SW (8 bits).
		// Exclusive waiters count: EW (8 bits).
		// Convert to exclusive waiters count: CEW (6 bits)
		//
		// Acquire exclusive:
		// {L=0,W=0,SC=0,SW,EW=0,CEW=0} -> {L=1,W=0,SC=0,SW,EW=0,CEW=0}
		// {L=0,W=1,SC=0,SW,EW,CEW} or {L=1,W,SC,SW,EW,CEW} ->
		//     {L,W,SC,SW,EW+1,CEW},
		//     wait on event,
		//     {L=0,W=1,SC=0,SW,EW,CEW} -> {L=1,W=0,SC=0,SW,EW,CEW}
		//
		// Acquire shared:
		// {L=0,W=0,SC=0,SW,EW=0,CEW=0} -> {L=1,W=0,SC=1,SW,EW=0,CEW=0}
		// {L=1,W=0,SC>0,SW,EW=0,CEW=0} -> {L=1,W=0,SC+1,SW,EW=0,CEW=0}
		// {L=1,W=0,SC=0,SW,EW=0,CEW=0} or {L,W=1,SC,SW,EW,CEW=0} or
		//     {L,W,SC,SW,EW>0,CEW=0} or {L,W,SC,SW,EW=0,CEW>0} or
		//	   {L,W,SC,SW,EW>0,CEW>0} -> {L,W,SC,SW+1,EW,CEW},
		//     wait on event,
		//     retry.
		//
		// Release exclusive:
		// {L=1,W=0,SC=0,SW,EW>0,CEW} ->
		//     {L=0,W=1,SC=0,SW,EW-1,CEW},
		//     release one exclusive waiter.
		// {L=1,W=0,SC=0,SW,EW=0,CEW} ->
		//     {L=0,W=0,SC=0,SW=0,EW=0,CEW},
		//     release all shared waiters.
		//
		// Note that we never do a direct acquire when W=1 
		// (i.e. L=0 if W=1), so here we don't have to check 
		// the value of W.
		//
		// Release shared:
		// {L=1,W=0,SC>1,SW,EW,CEW} -> {L=1,W=0,SC-1,SW,EW,CEW}
		// {L=1,W=0,SC=1,SW,EW=0,CEW=0} -> {L=0,W=0,SC=0,SW,EW=0,CEW=0}
		// {L=1,W=0,SC=1,SW,EW,CEW>0} ->
		//     {L=0,W=1,SC=0,SW,EW,CEW-1},
		//     release one convert to exclusive waiter.
		// {L=1,W=0,SC=1,SW,EW>0} ->
		//     {L=0,W=1,SC=0,SW,EW-1},
		//     release one exclusive waiter.
		//
		// Again, we don't need to check the value of W.
		//
		// Convert exclusive to shared:
		// {L=1,W=0,SC=0,SW,EW,CEW} ->
		//     {L=1,W=0,SC=1,SW=0,EW,CEW},
		//     release all shared waiters.
		//
		// Convert shared to exclusive:
		// {L=1,W=0,SC=1,SW,EW,CEW} ->
		//     {L=1,W=0,SC=0,SW,EW,CEW}
		// {L=1,W=0,SC>1,SW,EW} ->
		//     {L,W,SC,SW,EW,CEW+1},
		//     wait on event,
		//     {L=0,W=1,SC=0,SW,EW,CEW} -> {L=1,W=0,SC=0,SW,EW,CEW}
		// 
		//

		/* */

		// Note: I have included many small optimizations in the code 
		// because of the CLR's dumbass JIT compiler.

		#region Constants

		public const string MsgFailedToWaitIndefinitely = "Failed to wait indefinitely on an object.";

		// Lock owned: 1 bit.
		private const int LockOwned = 0x1;

		// Exclusive waking: 1 bit.
		private const int LockExclusiveWaking = 0x2;

		// Shared owners count: 8 bits.
		private const int LockSharedOwnersShift = 2;
		private const int LockSharedOwnersMask = 0xff;
		private const int LockSharedOwnersIncrement = 0x4;

		// Shared waiters count: 8 bits.
		private const int LockSharedWaitersShift = 10;
		private const int LockSharedWaitersMask = 0xff;
		private const int LockSharedWaitersIncrement = 0x400;

		// Exclusive waiters count: 8 bits.
		private const int LockExclusiveWaitersShift = 18;
		private const int LockExclusiveWaitersMask = 0xff;
		private const int LockExclusiveWaitersIncrement = 0x40000;

		// Convert to Exclusive waiters count: 6 bits
		private const int LockConvertToExclusiveWaitersShift = 26;
		private const int LockConvertToExclusiveWaitersMask = 0x3f;
		private const int LockConvertToExclusiveWaitersIncrement = 0x400000;

		private const int ExclusiveMask = LockExclusiveWaking | (LockExclusiveWaitersMask << LockExclusiveWaitersShift) | (LockConvertToExclusiveWaitersMask << LockConvertToExclusiveWaitersShift);

		#endregion

		public struct Statistics
			{
			/// <summary>
			///     The number of times the lock has been acquired in exclusive mode.
			/// </summary>
			public int AcqExcl;

			/// <summary>
			///     The number of times either the fast path was retried due to the
			///     spin count or the exclusive waiter went to sleep.
			/// </summary>
			/// <remarks>
			///     This number is usually much higher than AcqExcl, and indicates
			///     a good spin count if AcqExclSlp is very small.
			/// </remarks>
			public int AcqExclCont;

			/// <summary>
			///     The number of times exclusive waiters have gone to sleep.
			/// </summary>
			/// <remarks>
			///     If this number is high and not much time is spent in the
			///     lock, consider increasing the spin count.
			/// </remarks>
			public int AcqExclSlp;

			/// <summary>
			///     The number of times the lock has been converted to exclusive mode.
			/// </summary>
			public int CvtExcl;

			/// <summary>
			///     The number of times either the fast path was retried due to the
			///     spin count or the conver to exclusive waiter went to sleep.
			/// </summary>
			/// <remarks>
			///     This number is usually much higher than CvtExcl, and indicates
			///     a good spin count if AcqExclSlp is very small.
			/// </remarks>
			public int CvtExclCont;

			/// <summary>
			///     The number of times conver to exclusive waiters have gone to sleep.
			/// </summary>
			/// <remarks>
			///     If this number is high and not much time is spent in the
			///     lock, consider increasing the spin count.
			/// </remarks>
			public int CvtExclSlp;

			/// <summary>
			///     The number of times the lock has been acquired in shared mode.
			/// </summary>
			public int AcqShrd;

			/// <summary>
			///     The number of times either the fast path was retried due to the
			///     spin count or the shared waiter went to sleep.
			/// </summary>
			/// <remarks>
			///     This number is usually much higher than AcqShrd, and indicates
			///     a good spin count if AcqShrdSlp is very small.
			/// </remarks>
			public int AcqShrdCont;

			/// <summary>
			///     The number of times shared waiters have gone to sleep.
			/// </summary>
			/// <remarks>
			///     If this number is high and not much time is spent in the
			///     lock, consider increasing the spin count.
			/// </remarks>
			public int AcqShrdSlp;

			/// <summary>
			///     The highest number of exclusive waiters at any one time.
			/// </summary>
			public int PeakExclWtrsCount;

			/// <summary>
			///     The highest number of convert to exclusive waiters at any one time.
			/// </summary>
			public int PeakCvtExclWtrsCount;

			/// <summary>
			///     The highest number of shared waiters at any one time.
			/// </summary>
			public int PeakShrdWtrsCount;
			}

		// The number of times to spin before going to sleep.
		private const int SpinCount = 0;

		private volatile int _value;
		private volatile Semaphore _sharedWakeEvent;
		private volatile Semaphore _exclusiveWakeEvent;
		private volatile Semaphore _convertToExclusiveWakeEvent;
		private readonly IDisposable _fastResourceLockContext;

		private class FastResourceLockContext : IDisposable
			{
			private readonly FastResourceLock _lock;

			public FastResourceLockContext (FastResourceLock frl)
				{
				_lock = frl;
				}

			#region IDisposable Members

			public void Dispose ()
				{
				_lock.ReleaseAny ();
				_lock.Dispose ();
				}

			#endregion
			}

#if ENABLE_STATISTICS
		private int _acqExclCount = 0;
		private int _cvtExclCount = 0;
		private int _acqShrdCount = 0;
		private int _acqExclContCount = 0;
		private int _cvtExclContCount = 0;
		private int _acqShrdContCount = 0;
		private int _acqExclSlpCount = 0;
		private int _cvtExclSlpCount = 0;
		private int _acqShrdSlpCount = 0;
		private int _peakExclWtrsCount = 0;
		private int _peakCvtExclWtrsCount = 0;
		private int _peakShrdWtrsCount = 0;
#endif

		/// <summary>
		///     Creates a FastResourceLock.
		/// </summary>
		public FastResourceLock ()
			{
			_value = 0;

			_fastResourceLockContext = new FastResourceLockContext (this);

#if !DEFER_EVENT_CREATION
			_sharedWakeEvent = new Semaphore (0, int.MaxValue);
			_exclusiveWakeEvent = new Semaphore (0, int.MaxValue);
			_convertToExclusiveWakeEvent = new Semaphore (0, 1);
#endif
			}

		~FastResourceLock ()
			{
			Dispose (false);
			}

		private bool disposed;

		private void Dispose (bool disposing)
			{
			if (disposed)
				return;

			disposed = true;

			if (_sharedWakeEvent != null)
				{
				_sharedWakeEvent.Close ();
				_sharedWakeEvent = null;
				}

			if (_exclusiveWakeEvent != null)
				{
				_exclusiveWakeEvent.Close ();
				_exclusiveWakeEvent = null;
				}

			if (_convertToExclusiveWakeEvent == null)
				return;

			_convertToExclusiveWakeEvent.Close ();
			_convertToExclusiveWakeEvent = null;
			}

		/// <summary>
		///     Disposes resources associated with the FastResourceLock.
		/// </summary>
		public void Dispose ()
			{
			Dispose (true);
			CrestronEnvironment.GC.SuppressFinalize (this);
			}

		/// <summary>
		///     Gets the number of exclusive waiters.
		/// </summary>
		public int ExclusiveWaiters
			{
			get { return (_value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask; }
			}

		/// <summary>
		///     Gets the number of convert to exclusive waiters.
		/// </summary>
		public int ConvertToExclusiveWaiters
			{
			get { return (_value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask; }
			}

		/// <summary>
		///     Gets whether the lock is owned in either
		///     exclusive or shared mode.
		/// </summary>
		public bool Owned
			{
			get { return (_value & LockOwned) != 0; }
			}

		/// <summary>
		///     Gets the number of shared owners.
		/// </summary>
		public int SharedOwners
			{
			get { return (_value >> LockSharedOwnersShift) & LockSharedOwnersMask; }
			}

		/// <summary>
		///     Gets the number of shared waiters.
		/// </summary>
		public int SharedWaiters
			{
			get { return (_value >> LockSharedWaitersShift) & LockSharedWaitersMask; }
			}

		/// <summary>
		///     Acquires the lock in exclusive mode, blocking
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are given precedence over shared
		///     acquires.
		/// </remarks>
		public IDisposable AcquireExclusive ()
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _acqExclCount);

#endif
			while (true)
				{
				int value = _value;

				// Case 1: lock not owned AND an exclusive waiter is not waking up.
				// Here we don't have to check if there are exclusive waiters, because 
				// if there are the lock would be owned, and we are checking that anyway.
				if ((value & (LockOwned | LockExclusiveWaking)) == 0)
					{
#if RIGOROUS_CHECKS
                    Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);
                    Trace.Assert(((value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask) == 0);
                    Trace.Assert(((value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask) == 0);
#endif
					if (Interlocked.CompareExchange (ref _value, value + LockOwned, value) == value)
						break;
					}

					// Case 2: lock owned OR lock not owned and an exclusive waiter is waking up 
				// The second case means an exclusive waiter has just been woken up and is 
				// going to acquire the lock. We have to go to sleep to make sure we don't 
				// steal the lock.
				else if (i >= SpinCount)
					{
#if DEFER_EVENT_CREATION
					// This call must go *before* the next operation. Otherwise, 
					// we will have a race condition between potential releasers 
					// and us.
					EnsureEventCreated (ref _exclusiveWakeEvent);

#endif
					if (Interlocked.CompareExchange (ref _value, value + LockExclusiveWaitersIncrement, value) == value)
						{
#if ENABLE_STATISTICS
						Interlocked.Increment (ref _acqExclSlpCount);

						int exclWtrsCount = (value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask;

						Interlocked2.Set (
							ref _peakExclWtrsCount,
							p => p < exclWtrsCount,
							p => exclWtrsCount
							);

#endif
						// Go to sleep.
						if (!_exclusiveWakeEvent.WaitOne ())
							Break (MsgFailedToWaitIndefinitely);

						// Acquire the lock. 
						// At this point *no one* should be able to steal the lock from us.
						do
							{
							value = _value;
#if RIGOROUS_CHECKS

                            Trace.Assert((value & LockOwned) == 0);
                            Trace.Assert((value & LockExclusiveWaking) != 0);
#endif
							}
						while (Interlocked.CompareExchange (ref _value, value + LockOwned - LockExclusiveWaking, value) != value);

						break;
						}
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqExclContCount);
#endif
				i++;
				}

			return _fastResourceLockContext;
			}

		/// <summary>
		///     Acquires the lock in shared mode, blocking
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are given precedence over shared
		///     acquires.
		/// </remarks>
		public IDisposable AcquireShared ()
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _acqShrdCount);

#endif
			while (true)
				{
				int value = _value;

				// Case 1: lock not owned AND no exclusive waiter is waking up AND 
				// there are no shared owners AND there are no exclusive waiters AND no convert to exclusive is waiting
				if ((value & (LockOwned | (LockSharedOwnersMask << LockSharedOwnersShift) | ExclusiveMask)) == 0)
					{
					if (Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value)
						break;
					}
				// Case 2: lock is owned AND no exclusive waiter is waking up AND 
				// there are shared owners AND there are no exclusive waiters AND no convert to exclusive is waiting
				else if ((value & LockOwned) != 0 && ((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0 && (value & ExclusiveMask) == 0)
					{
					if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
						break;
					}
				// Other cases.
				else if (i >= SpinCount)
					{
#if DEFER_EVENT_CREATION
					EnsureEventCreated (ref _sharedWakeEvent);

#endif
					if (Interlocked.CompareExchange (ref _value, value + LockSharedWaitersIncrement, value) == value)
						{
#if ENABLE_STATISTICS
						Interlocked.Increment (ref _acqShrdSlpCount);

						int shrdWtrsCount = (value >> LockSharedWaitersShift) & LockSharedWaitersMask;

						Interlocked2.Set (
							ref _peakShrdWtrsCount,
							p => p < shrdWtrsCount,
							p => shrdWtrsCount
							);

#endif
						// Go to sleep.
						if (!_sharedWakeEvent.WaitOne ())
							Break (MsgFailedToWaitIndefinitely);

						// Go back and try again.
						continue;
						}
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqShrdContCount);
#endif
				i++;
				}

			return _fastResourceLockContext;
			}

		/// <summary>
		///     Converts the ownership mode from exclusive to shared.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are not given a chance to acquire
		///     the lock before this function does - as a result,
		///     this function will never block.
		/// </remarks>
		public void ConvertExclusiveToShared ()
			{
			while (true)
				{
				int value = _value;
#if RIGOROUS_CHECKS

                    Trace.Assert((value & LockOwned) != 0);
                    Trace.Assert((value & LockExclusiveWaking) == 0);
                    Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);
#endif

				if (Interlocked.CompareExchange (ref _value, (value + LockSharedOwnersIncrement) & ~(LockSharedWaitersMask << LockSharedWaitersShift), value) != value)
					continue;

				int sharedWaiters = (value >> LockSharedWaitersShift) & LockSharedWaitersMask;

				if (sharedWaiters != 0)
					_sharedWakeEvent.Release (sharedWaiters);

				break;
				}
			}

#if DEFER_EVENT_CREATION
		/// <summary>
		///     Checks if the specified event has been created, and
		///     if not, creates it.
		/// </summary>
		/// <param name="handle">A reference to the event handle.</param>
		private static void EnsureEventCreated (ref Semaphore handle)
			{
			if (handle != null)
				return;

			var eventHandle = new Semaphore (0, int.MaxValue);

			if (Interlocked.CompareExchange (ref handle, eventHandle, null) != null)
				eventHandle.Close ();
			}
#endif

		/// <summary>
		///     Gets statistics information for the lock.
		/// </summary>
		/// <returns>A structure containing statistics.</returns>
		public Statistics GetStatistics ()
			{
#if ENABLE_STATISTICS
			return new Statistics
			{
				AcqExcl = _acqExclCount,
				CvtExcl = _cvtExclCount,
				AcqShrd = _acqShrdCount,
				AcqExclCont = _acqExclContCount,
				CvtExclCont = _cvtExclContCount,
				AcqShrdCont = _acqShrdContCount,
				AcqExclSlp = _acqExclSlpCount,
				CvtExclSlp = _cvtExclSlpCount,
				AcqShrdSlp = _acqShrdSlpCount,
				PeakExclWtrsCount = _peakCvtExclWtrsCount,
				PeakCvtExclWtrsCount = _peakExclWtrsCount,
				PeakShrdWtrsCount = _peakShrdWtrsCount
			};
#else
			return new Statistics ();
#endif
			}

		/// <summary>
		///     Releases the lock in any mode.
		/// </summary>
		public void ReleaseAny ()
			{
#if RIGOROUS_CHECKS
			Trace.Assert((_value & LockOwned) != 0);
#endif
			if (((_value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0)
				ReleaseShared ();
			else
				ReleaseExclusive ();
			}

		/// <summary>
		///     Releases the lock in exclusive mode.
		/// </summary>
		public void ReleaseExclusive ()
			{
			while (true)
				{
				int value = _value;
#if RIGOROUS_CHECKS

                Trace.Assert((value & LockOwned) != 0);
                Trace.Assert((value & LockExclusiveWaking) == 0);
                Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);
#endif
				// Case 1: if we have convert to exclusive waiters, release one
				if (((value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask) != 0)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockOwned + LockExclusiveWaking - LockConvertToExclusiveWaitersIncrement, value) != value)
						continue;

					_convertToExclusiveWakeEvent.Release (1);

					break;
					}

				// Case 2: if we have exclusive waiters, release one.
				if (((value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask) != 0)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockOwned + LockExclusiveWaking - LockExclusiveWaitersIncrement, value) != value)
						continue;

					_exclusiveWakeEvent.Release (1);

					break;
					}

				// Case 3: if we have shared waiters, release all of them.
				if (Interlocked.CompareExchange (ref _value, value & ~(LockOwned | (LockSharedWaitersMask << LockSharedWaitersShift)), value) != value)
					continue;

				int sharedWaiters = (value >> LockSharedWaitersShift) & LockSharedWaitersMask;

				if (sharedWaiters != 0)
					_sharedWakeEvent.Release (sharedWaiters);
				break;
				}
			}

		/// <summary>
		///     Releases the lock in shared mode.
		/// </summary>
		public void ReleaseShared ()
			{
			while (true)
				{
				int value = _value;
#if RIGOROUS_CHECKS

                Trace.Assert((value & LockOwned) != 0);
                Trace.Assert((value & LockExclusiveWaking) == 0);
                Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0);
#endif

				// Case 1: there are multiple shared owners.
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) > 1)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
						break;
					}
				// Case 2: we are the last shared owner AND there are convert to exclusive waiters
				else if (((value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask) != 0)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockOwned + LockExclusiveWaking - LockSharedOwnersIncrement - LockConvertToExclusiveWaitersIncrement, value) == value)
						{
						_convertToExclusiveWakeEvent.Release (1);

						break;
						}
					}
				// Case 3: we are the last shared owner AND there are exclusive waiters.
				else if (((value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask) != 0)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockOwned + LockExclusiveWaking - LockSharedOwnersIncrement - LockExclusiveWaitersIncrement, value)
						== value)
						{
						_exclusiveWakeEvent.Release (1);

						break;
						}
					}
				// Case 4: we are the last shared owner AND there are no exclusive waiters AND there are no convert to exclusive waiters.
				else
					{
					if (Interlocked.CompareExchange (ref _value, value - LockOwned - LockSharedOwnersIncrement, value) == value)
						break;
					}
				}
			}

		/// <summary>
		///     Acquires the lock in exclusive mode, busy waiting
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are *not* given precedence over shared
		///     acquires for busy wait methods.
		/// </remarks>
		public IDisposable SpinAcquireExclusive ()
			{
			int ncount = 0;

			while (true)
				{
				int value = _value;

				if ((value & (LockOwned | LockExclusiveWaking)) == 0)
					{
					if (Interlocked.CompareExchange (ref _value, value + LockOwned, value) == value)
						break;
					}

				CrestronEnvironment.Sleep (++ncount % 10 == 0 ? 1 : 0);
				}

			return _fastResourceLockContext;
			}

		/// <summary>
		///     Acquires the lock in shared mode, busy waiting
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are *not* given precedence over shared
		///     acquires for busy wait methods.
		/// </remarks>
		public IDisposable SpinAcquireShared ()
			{
			int ncount = 0;

			while (true)
				{
				int value = _value;

				if ((value & ExclusiveMask) == 0)
					{
					if ((value & LockOwned) == 0)
						{
						if (Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value)
							break;
						}
					else if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0)
						{
						if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
							break;
						}
					}

				CrestronEnvironment.Sleep (++ncount % 10 == 0 ? 1 : 0);
				}

			return _fastResourceLockContext;
			}

		/// <summary>
		///     Converts the ownership mode from shared to exclusive,
		///     busy waiting if necessary.
		/// </summary>
		public void SpinConvertSharedToExclusive ()
			{
			int ncount = 0;

			while (true)
				{
				int value = _value;

				// Can't convert if there are other shared owners.
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 1)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
						break;
					}

				CrestronEnvironment.Sleep (++ncount % 10 == 0 ? 1 : 0);
				}
			}

		/// <summary>
		///     Attempts to acquire the lock in exclusive mode.
		/// </summary>
		/// <returns>Whether the lock was acquired.</returns>
		public bool TryAcquireExclusive ()
			{
			int value = _value;

			if ((value & (LockOwned | LockExclusiveWaking)) != 0)
				return false;

			return Interlocked.CompareExchange (ref _value, value + LockOwned, value) == value;
			}

		/// <summary>
		///     Attempts to acquire the lock in shared mode.
		/// </summary>
		/// <returns>Whether the lock was acquired.</returns>
		public bool TryAcquireShared ()
			{
			int value = _value;

			if ((value & ExclusiveMask) != 0)
				return false;

			if ((value & LockOwned) == 0)
				return Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value;

			if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0)
				return Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value;

			return false;
			}

		/// <summary>
		///     Attempts to convert the ownership mode from shared
		///     to exclusive.
		/// </summary>
		/// <returns>Whether the lock was converted.</returns>
		public bool TryConvertSharedToExclusive ()
			{
			while (true)
				{
				int value = _value;
#if RIGOROUS_CHECKS

                    Trace.Assert((value & LockOwned) != 0);
                    Trace.Assert((value & LockExclusiveWaking) == 0);
                    Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0);
#endif

				// Can't convert if there are other shared owners.
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 1)
					return false;

				if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
					return true;
				}
			}

		/// <summary>
		///     Converts the ownership mode from shared
		///     to exclusive.
		/// </summary>
		public void ConvertSharedToExclusive ()
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _cvtExclCount);
#endif

			while (true)
				{
				int value = _value;

#if RIGOROUS_CHECKS
                    Trace.Assert((value & LockOwned) != 0);
                    Trace.Assert((value & LockExclusiveWaking) == 0);
                    Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0);
#endif

				// Case 1: We are the last shared owner
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 1)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
						return;
					}
				// Case 2: There are still other shared owners
				else if (i >= SpinCount)
					{
#if DEFER_EVENT_CREATION
					// This call must go *before* the next operation. Otherwise, 
					// we will have a race condition between potential releasers 
					// and us.
					EnsureEventCreated (ref _convertToExclusiveWakeEvent);

#endif
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement + LockConvertToExclusiveWaitersIncrement, value) == value)
						{
#if ENABLE_STATISTICS
						Interlocked.Increment (ref _cvtExclSlpCount);

						int cvtExclWtrsCount = (value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask;

						Interlocked2.Set (
							ref _peakCvtExclWtrsCount,
							p => p < cvtExclWtrsCount,
							p => cvtExclWtrsCount
							);

#endif
						// Go to sleep.
						if (!_convertToExclusiveWakeEvent.WaitOne ())
							Break (MsgFailedToWaitIndefinitely);

						// Acquire the lock. 
						// At this point *no one* should be able to steal the lock from us.
						do
							{
							value = _value;
#if RIGOROUS_CHECKS

                            Trace.Assert((value & LockOwned) == 0);
                            Trace.Assert((value & LockExclusiveWaking) != 0);
#endif
							}
						while (Interlocked.CompareExchange (ref _value, value + LockOwned - LockExclusiveWaking, value) != value);

						break;
						}
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqExclContCount);
#endif
				i++;
				}
			}

		/// <summary>
		///     Converts the ownership mode from shared
		///     to exclusive, releasing the shared lock and
		///     acquiring an exclusive lock if necessary
		/// </summary>
		public void ConvertSharedToExclusiveWithRelease ()
			{
			if (TryConvertSharedToExclusive ())
				return;

			ReleaseShared ();
			AcquireExclusive ();
			}

		public static void Break (string logMessage)
			{
			ErrorLog.Error (logMessage);
			//Debugger.Break ();
			}

		/// <summary>
		///     Try to acquire the lock in exclusive mode, blocking for a specified time
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are given precedence over shared
		///     acquires.
		/// </remarks>
		public bool TryAcquireExclusive (int msecTimeout)
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _acqExclCount);

#endif
			while (true)
				{
				int value = _value;

				// Case 1: lock not owned AND an exclusive waiter is not waking up.
				// Here we don't have to check if there are exclusive waiters, because 
				// if there are the lock would be owned, and we are checking that anyway.
				if ((value & (LockOwned | LockExclusiveWaking)) == 0)
					{
#if RIGOROUS_CHECKS
                    Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);
                    Trace.Assert(((value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask) == 0);
                    Trace.Assert(((value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask) == 0);
#endif
					if (Interlocked.CompareExchange (ref _value, value + LockOwned, value) == value)
						break;
					}

					// Case 2: lock owned OR lock not owned and an exclusive waiter is waking up 
				// The second case means an exclusive waiter has just been woken up and is 
				// going to acquire the lock. We have to go to sleep to make sure we don't 
				// steal the lock.
				else if (i >= SpinCount)
					{
#if DEFER_EVENT_CREATION
					// This call must go *before* the next operation. Otherwise, 
					// we will have a race condition between potential releasers 
					// and us.
					EnsureEventCreated (ref _exclusiveWakeEvent);

#endif
					if (Interlocked.CompareExchange (ref _value, value + LockExclusiveWaitersIncrement, value) == value)
						{
#if ENABLE_STATISTICS
						Interlocked.Increment (ref _acqExclSlpCount);

						int exclWtrsCount = (value >> LockExclusiveWaitersShift) & LockExclusiveWaitersMask;

						Interlocked2.Set (
							ref _peakExclWtrsCount,
							p => p < exclWtrsCount,
							p => exclWtrsCount
							);

#endif
						// Go to sleep.
						if (!_exclusiveWakeEvent.WaitOne (msecTimeout))
							{
							do
								{
								value = _value;
								if (Interlocked.CompareExchange (ref _value, value - LockExclusiveWaitersIncrement, value) == value)
									return false;
								}
							while (!_exclusiveWakeEvent.WaitOne (0));
							}

						// Acquire the lock. 
						// At this point *no one* should be able to steal the lock from us.
						do
							{
							value = _value;
#if RIGOROUS_CHECKS

                            Trace.Assert((value & LockOwned) == 0);
                            Trace.Assert((value & LockExclusiveWaking) != 0);
#endif
							}
						while (Interlocked.CompareExchange (ref _value, value + LockOwned - LockExclusiveWaking, value) != value);

						break;
						}
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqExclContCount);
#endif
				i++;
				}

			return true;
			}

		/// <summary>
		///     Try to acquire the lock in shared mode, blocking for a specified time
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are given precedence over shared
		///     acquires.
		/// </remarks>
		public bool TryAcquireShared (int msecTimeout)
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _acqShrdCount);

#endif
			while (true)
				{
				int value = _value;

				// Case 1: lock not owned AND no exclusive waiter is waking up AND 
				// there are no shared owners AND there are no exclusive waiters AND no convert to exclusive is waiting
				if ((value & (LockOwned | (LockSharedOwnersMask << LockSharedOwnersShift) | ExclusiveMask)) == 0)
					{
					if (Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value)
						break;
					}
				// Case 2: lock is owned AND no exclusive waiter is waking up AND 
				// there are shared owners AND there are no exclusive waiters AND no convert to exclusive is waiting
				else if ((value & LockOwned) != 0 && ((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0 && (value & ExclusiveMask) == 0)
					{
					if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
						break;
					}
				// Other cases.
				else if (i >= SpinCount)
					{
#if DEFER_EVENT_CREATION
					EnsureEventCreated (ref _sharedWakeEvent);

#endif
					if (Interlocked.CompareExchange (ref _value, value + LockSharedWaitersIncrement, value) == value)
						{
#if ENABLE_STATISTICS
						Interlocked.Increment (ref _acqShrdSlpCount);

						int shrdWtrsCount = (value >> LockSharedWaitersShift) & LockSharedWaitersMask;

						Interlocked2.Set (
							ref _peakShrdWtrsCount,
							p => p < shrdWtrsCount,
							p => shrdWtrsCount
							);

#endif
						// Go to sleep.
						if (!_sharedWakeEvent.WaitOne (msecTimeout))
							{
							do
								{
								value = _value;
								if (Interlocked.CompareExchange (ref _value, value - LockSharedWaitersIncrement, value) == value)
									return false;
								}
							while (!_sharedWakeEvent.WaitOne (0));
							}
						// Go back and try again.
						continue;
						}
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqShrdContCount);
#endif
				i++;
				}

			return true;
			}

		/// <summary>
		///     Try to convert the ownership mode from shared
		///     to exclusive.
		/// </summary>
		public bool TryConvertSharedToExclusive (int msecTimeout)
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _cvtExclCount);
#endif

			while (true)
				{
				int value = _value;

#if RIGOROUS_CHECKS
                    Trace.Assert((value & LockOwned) != 0);
                    Trace.Assert((value & LockExclusiveWaking) == 0);
                    Trace.Assert(((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0);
#endif

				// Case 1: We are the last shared owner
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 1)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
						return true;
					}
				// Case 2: There are still other shared owners
				else if (i >= SpinCount)
					{
#if DEFER_EVENT_CREATION
					// This call must go *before* the next operation. Otherwise, 
					// we will have a race condition between potential releasers 
					// and us.
					EnsureEventCreated (ref _convertToExclusiveWakeEvent);

#endif
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement + LockConvertToExclusiveWaitersIncrement, value) == value)
						{
#if ENABLE_STATISTICS
						Interlocked.Increment (ref _cvtExclSlpCount);

						int cvtExclWtrsCount = (value >> LockConvertToExclusiveWaitersShift) & LockConvertToExclusiveWaitersMask;

						Interlocked2.Set (
							ref _peakCvtExclWtrsCount,
							p => p < cvtExclWtrsCount,
							p => cvtExclWtrsCount
							);

#endif
						// Go to sleep.
						if (!_convertToExclusiveWakeEvent.WaitOne (msecTimeout))
							{
							do
								{
								value = _value;
								if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement - LockConvertToExclusiveWaitersIncrement, value) == value)
									return false;
								}
							while (!_convertToExclusiveWakeEvent.WaitOne (0));
							}

						// Acquire the lock. 
						// At this point *no one* should be able to steal the lock from us.
						do
							{
							value = _value;
#if RIGOROUS_CHECKS

                            Trace.Assert((value & LockOwned) == 0);
                            Trace.Assert((value & LockExclusiveWaking) != 0);
#endif
							}
						while (Interlocked.CompareExchange (ref _value, value + LockOwned - LockExclusiveWaking, value) != value);

						break;
						}
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqExclContCount);
#endif
				i++;
				}

			return true;
			}

		}
	}