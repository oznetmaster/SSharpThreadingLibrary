/*
 * Process Hacker - 
 *   fair resource lock
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Crestron.SimplSharp;
using SSharp.Threading;
using Stopwatch = Crestron.SimplSharp.Stopwatch;
#if RIGPROUS_CHECKS
using Trace = SSMono.Diagnostics.Trace;
#endif

namespace ProcessHacker.Common.Threading
	{
	/// <summary>
	///     Provides a fast and fair resource (reader-writer) lock.
	/// </summary>
	/// <remarks>
	///     FairResourceLock has slightly more overhead than FastResourceLock,
	///     but guarantees that waiters will be released in FIFO order and
	///     provides better ownership conversion functions. In most cases
	///     FairResourceLock will also perform better under heavy contention.
	/// </remarks>
	public sealed class FairResourceLock : IDisposable, IResourceLock
		{
		#region Constants

		public const string MsgFailedToWaitIndefinitely = "Failed to wait indefinitely on an object.";

		// Lock owned
		private const int LockOwned = 0x1;
		// Waiters present
		private const int LockWaiters = 0x2;

		// Shared owners count
		private const int LockSharedOwnersShift = 2;
		private const int LockSharedOwnersMask = 0x3fffffff;
		private const int LockSharedOwnersIncrement = 0x4;

		private const int WaiterExclusive = 0x1;
		private const int WaiterSpinning = 0x2;
		private const int WaiterFlags = 0x3;

		#endregion

		public struct Statistics
			{
			/// <summary>
			///     The number of times the lock has been acquired in exclusive mode.
			/// </summary>
			public int AcqExcl;

			/// <summary>
			///     The number of times exclusive waiters have blocked on their
			///     wait blocks.
			/// </summary>
			public int AcqExclBlk;

			/// <summary>
			///     The number of times either the fast path was retried due to the
			///     spin count or the exclusive waiter blocked on its wait block.
			/// </summary>
			/// <remarks>
			///     This number is usually much higher than AcqExcl, and indicates
			///     a good spin count if AcqExclBlk/Slp is very small.
			/// </remarks>
			public int AcqExclCont;

			/// <summary>
			///     The number of times exclusive waiters have gone to sleep.
			/// </summary>
			public int AcqExclSlp;

			/// <summary>
			///     The number of times the lock has been acquired in shared mode.
			/// </summary>
			public int AcqShrd;

			/// <summary>
			///     The number of times shared waiters have blocked on their
			///     wait blocks.
			/// </summary>
			public int AcqShrdBlk;

			/// <summary>
			///     The number of times either the fast path was retried due to the
			///     spin count or the shared waiter blocked on its wait block.
			/// </summary>
			/// <remarks>
			///     This number is usually much higher than AcqShrd, and indicates
			///     a good spin count if AcqShrdBlk/Slp is very small.
			/// </remarks>
			public int AcqShrdCont;

			/// <summary>
			///     The number of times shared waiters have gone to sleep.
			/// </summary>
			public int AcqShrdSlp;

			/// <summary>
			///     The number of times the waiters bit was unable to be
			///     set while the waiters list spinlock was acquired in
			///     order to insert a wait block.
			/// </summary>
			public int InsWaitBlkRetry;

			/// <summary>
			///     The highest number of exclusive waiters at any one time.
			/// </summary>
			public int PeakExclWtrsCount;

			/// <summary>
			///     The highest number of shared waiters at any one time.
			/// </summary>
			public int PeakShrdWtrsCount;
			}

		private class FairResourceLockContext : IDisposable
			{
			private readonly FairResourceLock _lock;

			public FairResourceLockContext (FairResourceLock frl)
				{
				_lock = frl;
				}

			#region IDisposable Members

			public void Dispose ()
				{
				_lock.ReleaseAny ();
				}

			#endregion
			}

		private class WaitBlockFlags
			{
			public int Flags;

			public WaitBlockFlags (int flags)
				{
				Flags = flags;
				}

			public WaitBlockFlags ()
				{
				}
			}

		private enum ListPosition
			{
			/// <summary>
			///     The wait block will be inserted ahead of all other wait blocks.
			/// </summary>
			First,

			/// <summary>
			///     The wait block will be inserted behind all exclusive wait blocks
			///     but ahead of the first shared wait block (if any).
			/// </summary>
			LastExclusive,

			/// <summary>
			///     The wait block will be inserted behind all other wait blocks.
			/// </summary>
			Last
			}

		private int _value;
		//private int _spinCount;
		private const int _spinCount = 0;
		private EventWaitHandle _wakeEvent;
		private EventWaitHandle _sleepEvent;

		private SpinLock _lock;
		private LinkedList<WaitBlockFlags> _waitersList;
		private LinkedListNode<WaitBlockFlags> _firstSharedWaiter;

		private readonly IDisposable _fairResourceLockContext;

		private readonly static long TicksPerMsec = Stopwatch.Frequency / 1000;

#if ENABLE_STATISTICS
		private int _exclusiveWaitersCount;
		private int _sharedWaitersCount;

		private int _acqExclCount;
		private int _acqShrdCount;
		private int _acqExclContCount;
		private int _acqShrdContCount;
		private int _acqExclBlkCount;
		private int _acqShrdBlkCount;
		private int _acqExclSlpCount;
		private int _acqShrdSlpCount;
		private int _insWaitBlkRetryCount;
		private int _peakExclWtrsCount;
		private int _peakShrdWtrsCount;
#endif

		/// <summary>
		///     Creates a FairResourceLock.
		/// </summary>
		public FairResourceLock ()
			: this ( /*NativeMethods.SpinCount*/ 0)
			{
			}

		/// <summary>
		///     Creates a FairResourceLock, specifying a spin count.
		/// </summary>
		/// <param name="spinCount">
		///     The number of times to spin before going to sleep.
		/// </param>
		public FairResourceLock (int spinCount)
			{
			_value = 0;
			_lock = new SpinLock ();
			//_spinCount = Environment.ProcessorCount != 1 ? spinCount : 0;
			//_spinCount = 0;

			_waitersList = new LinkedList<WaitBlockFlags> ();
			_waitersList.AddFirst (new WaitBlockFlags ());
			_firstSharedWaiter = _waitersList.First;

			_fairResourceLockContext = new FairResourceLockContext (this);

#if !DEFER_EVENT_CREATION
            _wakeEvent = CreateEvent (false);
			_sleepEvent = CreateEvent (true);
#endif
			}

		~FairResourceLock ()
			{
			Dispose (false);
			}

		private void Dispose (bool disposing)
			{
			_waitersList.Clear ();
			_waitersList = null;

			_wakeEvent.Close ();
			_wakeEvent = null;
			}

		/// <summary>
		///     Disposes resources associated with the FairResourceLock.
		/// </summary>
		public void Dispose ()
			{
			Dispose (true);
			CrestronEnvironment.GC.SuppressFinalize (this);
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
		///     Gets the number of times to spin before going to sleep.
		/// </summary>
		public int SpinCount
			{
			get { return _spinCount; }
			set { /*_spinCount = value;*/ }
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

				// Try to obtain the lock.
				if ((value & LockOwned) == 0)
					{
#if RIGOROUS_CHECKS
					Trace.Assert (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);

#endif
					if (Interlocked.CompareExchange (ref _value, value + LockOwned, value) == value)
						break;
					}
				else if (i >= _spinCount)
					{
					// We need to wait.
					var waitBlock = new LinkedListNode<WaitBlockFlags> (new WaitBlockFlags (WaiterExclusive | WaiterSpinning));


					// Obtain the waiters list lock.
					_lock.Acquire ();

					try
						{
						// Try to set the waiters bit.
						if (Interlocked.CompareExchange (ref _value, value | LockWaiters, value) != value)
							{
#if ENABLE_STATISTICS
							Interlocked.Increment (ref _insWaitBlkRetryCount);

#endif
							// Unfortunately we have to go back. This is 
							// very wasteful since the waiters list lock 
							// must be released again, but must happen since 
							// the lock may have been released.
							continue;
							}

						// Put our wait block behind other exclusive waiters but 
						// in front of all shared waiters.
						//this.InsertWaitBlock (&waitBlock, ListPosition.LastExclusive);
						InsertWaitBlock (waitBlock, ListPosition.LastExclusive);
#if ENABLE_STATISTICS

						_exclusiveWaitersCount++;

						if (_peakExclWtrsCount < _exclusiveWaitersCount)
							_peakExclWtrsCount = _exclusiveWaitersCount;
#endif
						}
					finally
						{
						_lock.Release ();
						}

#if ENABLE_STATISTICS
					Interlocked.Increment (ref _acqExclBlkCount);
#endif
					Block (waitBlock);

					// Go back and try again.
					continue;
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqExclContCount);
#endif
				i++;
				}

			return _fairResourceLockContext;
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

				// Try to obtain the lock.
				// Note that we don't acquire if there are waiters and 
				// the lock is already owned in shared mode, in order to 
				// give exclusive acquires precedence.
				if ((value & LockOwned) == 0 || ((value & LockWaiters) == 0 && ((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0))
					{
					if ((value & LockOwned) == 0)
						{
#if RIGOROUS_CHECKS
						Trace.Assert (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);

#endif
						if (Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value)
							break;
						}
					else
						{
						if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
							break;
						}
					}
				else if (i >= _spinCount)
					{
					// We need to wait.
					var waitBlock = new LinkedListNode<WaitBlockFlags> (new WaitBlockFlags (WaiterSpinning));


					// Obtain the waiters list lock.
					_lock.Acquire ();

					try
						{
						// Try to set the waiters bit.
						if (Interlocked.CompareExchange (ref _value, value | LockWaiters, value) != value)
							{
#if ENABLE_STATISTICS
							Interlocked.Increment (ref _insWaitBlkRetryCount);

#endif
							continue;
							}

						// Put our wait block behind other waiters.
						InsertWaitBlock (waitBlock, ListPosition.Last);

						// Set the first shared waiter pointer.
						if (waitBlock.Previous == null || (waitBlock.Previous.Value.Flags & WaiterExclusive) != 0)
							_firstSharedWaiter = waitBlock;
#if ENABLE_STATISTICS

						_sharedWaitersCount++;

						if (_peakShrdWtrsCount < _sharedWaitersCount)
							_peakShrdWtrsCount = _sharedWaitersCount;
#endif
						}
					finally
						{
						_lock.Release ();
						}

#if ENABLE_STATISTICS
					Interlocked.Increment (ref _acqShrdBlkCount);
#endif
					Block (waitBlock);

					// Go back and try again.
					continue;
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqShrdContCount);
#endif
				i++;
				}

			return _fairResourceLockContext;
			}

#if DEFER_EVENT_CREATION
		private void CreateEvents ()
			{
			EventWaitHandle wakeEvent = Interlocked.CompareExchange (ref _wakeEvent, null, null);

			if (wakeEvent == null)
				{
				wakeEvent = CreateEvent (false);

				if (Interlocked.CompareExchange (ref _wakeEvent, wakeEvent, null) != null)
					wakeEvent.Close ();
				}

			EventWaitHandle sleepEvent = Interlocked.CompareExchange (ref _sleepEvent, null, null);

			if (sleepEvent == null)
				{
				sleepEvent = CreateEvent (true);

				if (Interlocked.CompareExchange (ref _sleepEvent, sleepEvent, null) != null)
					sleepEvent.Close ();
				}
			}
#endif

		/// <summary>
		///     Blocks on a wait block.
		/// </summary>
		/// <param name="waitBlock">The wait block to block on.</param>
		private void Block (LinkedListNode<WaitBlockFlags> waitBlock)
			{
			int flags;

#if DEFER_EVENT_CREATION
			CreateEvents ();
#endif
			// Clear the spinning flag.
			do
				{
				flags = waitBlock.Value.Flags;
				}
			while (Interlocked.CompareExchange (ref waitBlock.Value.Flags, flags & ~WaiterSpinning, flags) != flags);

			// Go to sleep if necessary.
			if ((flags & WaiterSpinning) != 0)
				{
#if ENABLE_STATISTICS
				if ((waitBlock.Value.Flags & WaiterExclusive) != 0)
					Interlocked.Increment (ref _acqExclSlpCount);
				else
					Interlocked.Increment (ref _acqShrdSlpCount);

#endif
				while (waitBlock.List == _waitersList)
					{
					_sleepEvent.WaitOne ();

					if (!_wakeEvent.WaitOne ())
						throw new Exception (MsgFailedToWaitIndefinitely);
					}
				}
			}

		/// <summary>
		///     Blocks on a wait block.
		/// </summary>
		/// <param name="waitBlock">The wait block to block on.</param>
		/// <param name="msecTimeout">The time to wait on the waitblock.</param>
		private bool Block (LinkedListNode<WaitBlockFlags> waitBlock, int msecTimeout)
			{
			int flags;

			if (msecTimeout == Timeout.Infinite)
				{
				Block (waitBlock);
				return true;
				}

#if DEFER_EVENT_CREATION
			CreateEvents ();
#endif
			// Clear the spinning flag.
			do
				{
				flags = waitBlock.Value.Flags;
				}
			while (Interlocked.CompareExchange (ref waitBlock.Value.Flags, flags & ~WaiterSpinning, flags) != flags);

			// Go to sleep if necessary.
			if ((flags & WaiterSpinning) != 0)
				{
#if ENABLE_STATISTICS
				if ((waitBlock.Value.Flags & WaiterExclusive) != 0)
					Interlocked.Increment (ref _acqExclSlpCount);
				else
					Interlocked.Increment (ref _acqShrdSlpCount);

#endif
				long startTicks = Stopwatch.GetTimestamp ();
				long endTicks = startTicks + msecTimeout * TicksPerMsec;
				long currentTicks;

				while (waitBlock.List == _waitersList  && (currentTicks = Stopwatch.GetTimestamp ()) <= endTicks)
					{
					_sleepEvent.WaitOne ();

					if (!_wakeEvent.WaitOne ((int)((endTicks - currentTicks) / TicksPerMsec)))
						{
						// Set the spinning flag.
						do
							{
							flags = waitBlock.Value.Flags;
							if (Interlocked.CompareExchange (ref waitBlock.Value.Flags, flags | WaiterSpinning, flags) == flags)
								return false;
							}
						while (_sleepEvent.WaitOne () && !_wakeEvent.WaitOne (0));
						}
					}

				if (waitBlock.List != _waitersList)
					return true;

				// Set the spinning flag.
				do
					{
					flags = waitBlock.Value.Flags;
					}
				while (Interlocked.CompareExchange (ref waitBlock.Value.Flags, flags | WaiterSpinning, flags) != flags);

				return false;
				}

			return true;
			}

		/// <summary>
		///     Converts the ownership mode from exclusive to shared.
		/// </summary>
		/// <remarks>
		///     This operation is almost the same as releasing then
		///     acquiring in shared mode, except that exclusive waiters
		///     are not given a chance to acquire the lock.
		/// </remarks>
		public void ConvertExclusiveToShared ()
			{
			while (true)
				{
				int value = _value;

				if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
					{
					if ((value & LockWaiters) != 0)
						WakeShared ();

					break;
					}
				}
			}

		/// <summary>
		///     Converts the ownership mode from shared to exclusive,
		///     blocking if necessary.
		/// </summary>
		/// <remarks>
		///     This operation is almost the same as releasing then
		///     acquiring in exclusive mode, except that the caller is
		///     placed ahead of all other waiters when acquiring.
		/// </remarks>
		public void ConvertSharedToExclusive ()
			{
			int i = 0;

			while (true)
				{
				int value = _value;

				// Are we the only shared waiter? If so, acquire in exclusive mode, 
				// otherwise wait.
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 1)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
						break;
					}
				else if (i >= _spinCount)
					{
					// We need to wait.
					var waitBlock = new LinkedListNode<WaitBlockFlags> (new WaitBlockFlags (WaiterExclusive | WaiterSpinning));

					// Obtain the waiters list lock.
					_lock.Acquire ();

					try
						{
						// Try to set the waiters bit.
						if (Interlocked.CompareExchange (ref _value, value | LockWaiters, value) != value)
							{
#if ENABLE_STATISTICS
							Interlocked.Increment (ref _insWaitBlkRetryCount);

#endif
							continue;
							}

						// Put our wait block ahead of all other waiters.
						InsertWaitBlock (waitBlock, ListPosition.First);
#if ENABLE_STATISTICS

						_exclusiveWaitersCount++;

						if (_peakExclWtrsCount < _exclusiveWaitersCount)
							_peakExclWtrsCount = _exclusiveWaitersCount;
#endif
						}
					finally
						{
						_lock.Release ();
						}

					Block (waitBlock);

					// Go back and try again.
					continue;
					}

				i++;
				}
			}

		/// <summary>
		///     Creates a wake event.
		/// </summary>
		/// <returns>A handle to the event.</returns>
		private EventWaitHandle CreateEvent (bool initialState)
			{
#if USE_FAST_EVENT
			return new FastEventWH (false, initialState);
#else
			return new ManualResetEvent (initialState);
#endif
			}

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
				AcqShrd = _acqShrdCount,
				AcqExclCont = _acqExclContCount,
				AcqShrdCont = _acqShrdContCount,
				AcqExclBlk = _acqExclBlkCount,
				AcqShrdBlk = _acqShrdBlkCount,
				AcqExclSlp = _acqExclSlpCount,
				AcqShrdSlp = _acqShrdSlpCount,
				InsWaitBlkRetry = _insWaitBlkRetryCount,
				PeakExclWtrsCount = _peakExclWtrsCount,
				PeakShrdWtrsCount = _peakShrdWtrsCount
				};
#else
			return new Statistics ();
#endif
			}

		/// <summary>
		///     Inserts a wait block into the waiters list.
		/// </summary>
		/// <param name="waitBlock">The wait block to insert.</param>
		/// <param name="position">Specifies where to insert the wait block.</param>
		private void InsertWaitBlock (LinkedListNode<WaitBlockFlags> waitBlock, ListPosition position)
			{
			switch (position)
				{
				case ListPosition.First:
					_waitersList.AddFirst (waitBlock);
					break;
				case ListPosition.LastExclusive:
					_waitersList.AddBefore (_firstSharedWaiter, waitBlock);
					break;
				case ListPosition.Last:
					_waitersList.AddLast (waitBlock);
					break;
				}
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

				Trace.Assert ((value & LockOwned) != 0);
				Trace.Assert (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);
#endif

				if (Interlocked.CompareExchange (ref _value, value - LockOwned, value) == value)
					{
					if ((value & LockWaiters) != 0)
						Wake ();

					break;
					}
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

				Trace.Assert ((value & LockOwned) != 0);
				Trace.Assert (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0);
#endif

				int sharedOwners = (value >> LockSharedOwnersShift) & LockSharedOwnersMask;

				int newValue;
				if (sharedOwners > 1)
					newValue = value - LockSharedOwnersIncrement;
				else
					newValue = value - LockOwned - LockSharedOwnersIncrement;

				if (Interlocked.CompareExchange (ref _value, newValue, value) == value)
					{
					// Only wake if we are the last out.
					if (sharedOwners == 1 && (value & LockWaiters) != 0)
						WakeExclusive ();

					break;
					}
				}
			}

		/// <summary>
		///		Release the lock in any mode
		/// </summary>
		public void ReleaseAny ()
			{
			if (((_value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0)
				ReleaseShared ();
			else
				ReleaseExclusive ();
			}

		/// <summary>
		///     Attempts to acquire the lock in exclusive mode.
		/// </summary>
		/// <returns>Whether the lock was acquired.</returns>
		public bool TryAcquireExclusive ()
			{
			int value = _value;

			if ((value & LockOwned) != 0)
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

			if ((value & LockOwned) == 0 || ((value & LockWaiters) == 0 && ((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0))
				{
				if ((value & LockOwned) == 0)
					{
					if (Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value)
						return true;
					}
				else
					{
					if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
						return true;
					}
				}

			return false;
			}

		/// <summary>
		///     Attempts to convert the ownership mode from shared to exclusive.
		/// </summary>
		/// <returns>Whether the lock was converted.</returns>
		public bool TryConvertSharedToExclusive ()
			{
			int value = _value;

			if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 1)
				return false;

			return Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value;
			}

		/// <summary>
		///     Tries to acquire the lock in exclusive mode, blocking
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are given precedence over shared
		///     acquires.
		/// </remarks>
		/// <param name="msecTimeout">Timeout to wait for the aquisition of the lock</param>
		/// <returns>Whether the lock was acquired.</returns>
		public bool TryAcquireExclusive (int msecTimeout)
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _acqExclCount);

#endif
			while (true)
				{
				int value = _value;

				// Try to obtain the lock.
				if ((value & LockOwned) == 0)
					{
#if RIGOROUS_CHECKS
					Trace.Assert (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);

#endif
					if (Interlocked.CompareExchange (ref _value, value + LockOwned, value) == value)
						break;
					}
				else if (i >= _spinCount)
					{
					// We need to wait.
					var waitBlock = new LinkedListNode<WaitBlockFlags> (new WaitBlockFlags (WaiterExclusive | WaiterSpinning));


					// Obtain the waiters list lock.
					_lock.Acquire ();

					try
						{
						// Try to set the waiters bit.
						if (Interlocked.CompareExchange (ref _value, value | LockWaiters, value) != value)
							{
#if ENABLE_STATISTICS
							Interlocked.Increment (ref _insWaitBlkRetryCount);

#endif
							// Unfortunately we have to go back. This is 
							// very wasteful since the waiters list lock 
							// must be released again, but must happen since 
							// the lock may have been released.
							continue;
							}

						// Put our wait block behind other exclusive waiters but 
						// in front of all shared waiters.
						//this.InsertWaitBlock (&waitBlock, ListPosition.LastExclusive);
						InsertWaitBlock (waitBlock, ListPosition.LastExclusive);
#if ENABLE_STATISTICS

						_exclusiveWaitersCount++;

						if (_peakExclWtrsCount < _exclusiveWaitersCount)
							_peakExclWtrsCount = _exclusiveWaitersCount;
#endif
						}
					finally
						{
						_lock.Release ();
						}

#if ENABLE_STATISTICS
					Interlocked.Increment (ref _acqExclBlkCount);
#endif
					if (!Block (waitBlock, msecTimeout))
						{
						_lock.Acquire ();
						try
							{
							if (waitBlock.List != null)
								{
								_waitersList.Remove (waitBlock);

								if (_waitersList.Count == 1)
									{
									// No more waiters. Clear the waiters bit.
									do
										{
										value = _value;
										}
									while (Interlocked.CompareExchange (ref _value, value & ~LockWaiters, value) != value);
									}

								return false;
								}
							}
						finally
							{
							_lock.Release ();
							}
						}

					// Go back and try again.
					continue;
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqExclContCount);
#endif
				i++;
				}

			return true;
			}

		/// <summary>
		///     Tries to acquire the lock in shared mode, blocking
		///     if necessary.
		/// </summary>
		/// <remarks>
		///     Exclusive acquires are given precedence over shared
		///     acquires.
		/// </remarks>
		/// <param name="msecTimeout">Timeout to wait for the aquisition of the lock</param>
		/// <returns>Whether the lock was acquired.</returns>
		public bool TryAcquireShared (int msecTimeout)
			{
			int i = 0;

#if ENABLE_STATISTICS
			Interlocked.Increment (ref _acqShrdCount);

#endif
			while (true)
				{
				int value = _value;

				// Try to obtain the lock.
				// Note that we don't acquire if there are waiters and 
				// the lock is already owned in shared mode, in order to 
				// give exclusive acquires precedence.
				if ((value & LockOwned) == 0 || ((value & LockWaiters) == 0 && ((value >> LockSharedOwnersShift) & LockSharedOwnersMask) != 0))
					{
					if ((value & LockOwned) == 0)
						{
#if RIGOROUS_CHECKS
						Trace.Assert (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 0);

#endif
						if (Interlocked.CompareExchange (ref _value, value + LockOwned + LockSharedOwnersIncrement, value) == value)
							break;
						}
					else
						{
						if (Interlocked.CompareExchange (ref _value, value + LockSharedOwnersIncrement, value) == value)
							break;
						}
					}
				else if (i >= _spinCount)
					{
					// We need to wait.
					var waitBlock = new LinkedListNode<WaitBlockFlags> (new WaitBlockFlags (WaiterSpinning));


					// Obtain the waiters list lock.
					_lock.Acquire ();

					try
						{
						// Try to set the waiters bit.
						if (Interlocked.CompareExchange (ref _value, value | LockWaiters, value) != value)
							{
#if ENABLE_STATISTICS
							Interlocked.Increment (ref _insWaitBlkRetryCount);

#endif
							continue;
							}

						// Put our wait block behind other waiters.
						InsertWaitBlock (waitBlock, ListPosition.Last);

						// Set the first shared waiter pointer.
						if (waitBlock.Previous == null || (waitBlock.Previous.Value.Flags & WaiterExclusive) != 0)
							_firstSharedWaiter = waitBlock;
#if ENABLE_STATISTICS

						_sharedWaitersCount++;

						if (_peakShrdWtrsCount < _sharedWaitersCount)
							_peakShrdWtrsCount = _sharedWaitersCount;
#endif
						}
					finally
						{
						_lock.Release ();
						}

#if ENABLE_STATISTICS
					Interlocked.Increment (ref _acqShrdBlkCount);
#endif
					if (!Block (waitBlock, msecTimeout))
						{
						_lock.Acquire ();
						try
							{
							if (waitBlock.List != null)
								{
								if (_firstSharedWaiter == waitBlock)
									_firstSharedWaiter = waitBlock.Next;

								_waitersList.Remove (waitBlock);

								if (_waitersList.Count == 1)
									{
									// No more waiters. Clear the waiters bit.
									do
										{
										value = _value;
										}
									while (Interlocked.CompareExchange (ref _value, value & ~LockWaiters, value) != value);
									}

								return false;
								}
							}
						finally
							{
							_lock.Release ();
							}
						}

					// Go back and try again.
					continue;
					}

#if ENABLE_STATISTICS
				Interlocked.Increment (ref _acqShrdContCount);
#endif
				i++;
				}

			return true;
			}

		/// <summary>
		///     Try to convert the ownership mode from shared to exclusive,
		///     blocking if necessary.
		/// </summary>
		/// <remarks>
		///     This operation is almost the same as releasing then
		///     acquiring in exclusive mode, except that the caller is
		///     placed ahead of all other waiters when acquiring.
		/// </remarks>
		/// <param name="msecTimeout">Timeout to wait for the aquisition of the lock</param>
		/// <returns>Whether the lock was acquired.</returns>
		public bool TryConvertSharedToExclusive (int msecTimeout)
			{
			int i = 0;

			while (true)
				{
				int value = _value;

				// Are we the only shared waiter? If so, acquire in exclusive mode, 
				// otherwise wait.
				if (((value >> LockSharedOwnersShift) & LockSharedOwnersMask) == 1)
					{
					if (Interlocked.CompareExchange (ref _value, value - LockSharedOwnersIncrement, value) == value)
						break;
					}
				else if (i >= _spinCount)
					{
					// We need to wait.
					var waitBlock = new LinkedListNode<WaitBlockFlags> (new WaitBlockFlags (WaiterExclusive | WaiterSpinning));

					// Obtain the waiters list lock.
					_lock.Acquire ();

					try
						{
						// Try to set the waiters bit.
						if (Interlocked.CompareExchange (ref _value, value | LockWaiters, value) != value)
							{
#if ENABLE_STATISTICS
							Interlocked.Increment (ref _insWaitBlkRetryCount);

#endif
							continue;
							}

						// Put our wait block ahead of all other waiters.
						InsertWaitBlock (waitBlock, ListPosition.First);
#if ENABLE_STATISTICS

						_exclusiveWaitersCount++;

						if (_peakExclWtrsCount < _exclusiveWaitersCount)
							_peakExclWtrsCount = _exclusiveWaitersCount;
#endif
						}
					finally
						{
						_lock.Release ();
						}

					if (!Block (waitBlock, msecTimeout))
						{
						_lock.Acquire ();
						try
							{
							if (waitBlock.List != null)
								{
								_waitersList.Remove (waitBlock);

								if (_waitersList.Count == 1)
									{
									// No more waiters. Clear the waiters bit.
									do
										{
										value = _value;
										}
									while (Interlocked.CompareExchange (ref _value, value & ~LockWaiters, value) != value);
									}

								return false;
								}
							}
						finally
							{
							_lock.Release ();
							}
						}

					// Go back and try again.
					continue;
					}

				i++;
				}

			return true;
			}

		/// <summary>
		///     Unblocks a wait block.
		/// </summary>
		/// <param name="waitBlock">The wait block to unblock.</param>
		private bool Unblock (LinkedListNode<WaitBlockFlags> waitBlock)
			{
			int flags;

			// Clear the spinning flag.
			do
				{
				flags = waitBlock.Value.Flags;
				}
			while (Interlocked.CompareExchange (ref waitBlock.Value.Flags, flags & ~WaiterSpinning, flags) != flags);

			if ((flags & WaiterSpinning) == 0)
				{
#if RIGOROUS_CHECKS
				Trace.Assert (_wakeEvent != null);

#endif
				return true;
				}

			return false;
			}

		private object _lockEvents = new object ();

		private void Unblock ()
			{
			lock (_lockEvents)
				{
				_sleepEvent.ResetHandle ();
				_wakeEvent.SetHandle ();
				_wakeEvent.ResetHandle ();
				_sleepEvent.SetHandle ();
				}
			}

		[Conditional ("RIGOROUS_CHECKS")]
		private void VerifyWaitersList ()
			{
#if RIGOROUS_CHECKS
			/*
			bool firstSharedWaiterInList = false;

			if (_firstSharedWaiter == _waitersListHead)
				firstSharedWaiterInList = true;

			for (
				WaitBlock* wb = _waitersListHead->Flink;
				wb != _waitersListHead;
				wb = wb->Flink
				)
				{
				System.Diagnostics.Trace.Assert (wb == wb->Flink->Blink);
				System.Diagnostics.Trace.Assert ((wb->Flags & ~WaiterFlags) == 0);

				if (_firstSharedWaiter == wb)
					firstSharedWaiterInList = true;
				}
			System.Diagnostics.Trace.Assert (firstSharedWaiterInList);
			*/

			Trace.Assert (_waitersList.Any (wbf => wbf == _firstSharedWaiter.Value) && _waitersList.All (wbf => (wbf.Flags & ~WaiterFlags) == 0));
#endif
			}

		/// <summary>
		///     Wakes either one exclusive waiter or multiple shared waiters.
		/// </summary>
		private void Wake ()
			{
			var wakeList = new LinkedList<WaitBlockFlags> ();
			LinkedListNode<WaitBlockFlags> wb;
			LinkedListNode<WaitBlockFlags> exclusiveWb = null;

			_lock.Acquire ();

			try
				{
				bool first = true;

				while (true)
					{
					wb = _waitersList.First;

					if (_waitersList.Count == 1)
						{
						int value;

						// No more waiters. Clear the waiters bit.
						do
							{
							value = _value;
							}
						while (Interlocked.CompareExchange (ref _value, value & ~LockWaiters, value) != value);

						break;
						}

					// If this is an exclusive waiter, don't wake 
					// anyone else.
					if (first && (wb.Value.Flags & WaiterExclusive) != 0)
						{
						exclusiveWb = _waitersList.First;
						_waitersList.RemoveFirst ();
#if ENABLE_STATISTICS
						_exclusiveWaitersCount--;

#endif
						break;
						}

#if RIGOROUS_CHECKS
					// If this is not the first waiter we have looked at 
					// and it is an exclusive waiter, then we have a bug - 
					// we should have stopped upon encountering the first 
					// exclusive waiter (previous block), so this means 
					// we have an exclusive waiter *behind* shared waiters.
					if (!first && (wb.Value.Flags & WaiterExclusive) != 0)
						Trace.Fail ("Exclusive waiter behind shared waiters!");

#endif
					// Remove the (shared) waiter and add it to the wake list.
					wb = _waitersList.First;
					_waitersList.RemoveFirst ();
					wakeList.AddLast (wb);
#if ENABLE_STATISTICS
					_sharedWaitersCount--;
#endif

					first = false;
					}

				if (exclusiveWb == null)
					{
					// If we removed shared waiters, we removed all of them. 
					// Reset the first shared waiter pointer.
					// Note that this also applies if we haven't woken anyone 
					// at all; this just becomes a redundant assignment.
					//_firstSharedWaiter = _waitersListHead;
					_firstSharedWaiter = _waitersList.First;
					}
				}
			finally
				{
				_lock.Release ();
				}

			// If we removed one exclusive waiter, unblock it.
			if (exclusiveWb != null)
				{
				if (Unblock (exclusiveWb))
					Unblock ();
				return;
				}

			// Carefully traverse the wake list and wake each shared waiter.
			wb = wakeList.First;
			bool unblock = false;
			while (wb != null)
				{
				unblock |= Unblock (wb);
				wb = wb.Next;
				}
			if (unblock)
				Unblock ();
			}

		/// <summary>
		///     Wakes one exclusive waiter.
		/// </summary>
		private void WakeExclusive ()
			{
			LinkedListNode<WaitBlockFlags> wb;
			LinkedListNode<WaitBlockFlags> exclusiveWb = null;

			_lock.Acquire ();

			try
				{
				wb = _waitersList.First;

				if (_waitersList.Count != 1 && (wb.Value.Flags & WaiterExclusive) != 0)
					{
					exclusiveWb = wb;
					_waitersList.RemoveFirst ();

#if ENABLE_STATISTICS
					_exclusiveWaitersCount--;
#endif
					}

				if (_waitersList.Count == 1)
					{
					int value;

					// No more waiters. Clear the waiters bit.
					do
						{
						value = _value;
						}
					while (Interlocked.CompareExchange (ref _value, value & ~LockWaiters, value) != value);
					}
				}
			finally
				{
				_lock.Release ();
				}

			if (exclusiveWb != null)
				{
				if (Unblock (exclusiveWb))
					Unblock ();
				}
			}

		/// <summary>
		///     Wakes multiple shared waiters.
		/// </summary>
		private void WakeShared ()
			{
			var wakeList = new LinkedList<WaitBlockFlags> ();
			LinkedListNode<WaitBlockFlags> wb;

			_lock.Acquire ();

			try
				{
				wb = _firstSharedWaiter;

				while (true)
					{
					if (_waitersList.Count == 1)
						{
						int value;

						// No more waiters. Clear the waiters bit.
						do
							{
							value = _value;
							}
						while (Interlocked.CompareExchange (ref _value, value & ~LockWaiters, value) != value);

						break;
						}
#if RIGOROUS_CHECKS
					// We shouldn't have *any* exclusive waiters at this 
					// point since we started at _firstSharedWaiter.
					if ((wb.Value.Flags & WaiterExclusive) != 0)
						Trace.Fail ("Exclusive waiter behind shared waiters!");

#endif

					// Remove the waiter and add it to the wake list.
					LinkedListNode<WaitBlockFlags> t = wb.Next;
					_waitersList.Remove (wb);
					wakeList.AddLast (wb);
#if ENABLE_STATISTICS
					_sharedWaitersCount--;
#endif
					wb = t;
					}

				// Reset the first shared waiter pointer.
				//_firstSharedWaiter = _waitersListHead;
				_firstSharedWaiter = _waitersList.First;
				}
			finally
				{
				_lock.Release ();
				}

			// Carefully traverse the wake list and wake each waiter.

			wb = wakeList.First;
			bool unblock = false;
			while (wb != null)
				{
				unblock |= Unblock (wb);
				wb = wb.Next;
				}
			if (unblock)
				Unblock ();
			}
		}
	}