/*
 * Process Hacker - 
 *   fast event
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

using Crestron.SimplSharp;
using Wintellect.Threading;
using SSharp.Threading;

#pragma warning disable 420

namespace ProcessHacker.Common.Threading
	{
	/// <summary>
	///     Provides a fast synchronization event.
	/// </summary>
	/// <remarks>
	///     This event structure will not create any kernel-mode
	///     event object until necessary.
	/// </remarks>
	public struct FastEvent
		{
		private const int EventSet = 0x1;
		private const int EventRefCountShift = 1;
		private const int EventRefCountIncrement = 0x2;
		private readonly bool _autoReset;

		private volatile CEvent _event;
		private volatile int _value;


		/// <summary>
		///     Creates a synchronization event.
		/// </summary>
		/// <param name="autoReset">
		///     Sepcifies if this is an auto-reset event.
		/// </param>
		/// <param name="value">
		///     The initial value of the event.
		/// </param>
		public FastEvent (bool autoReset, bool value)
			{
			_autoReset = autoReset;
			// Set value; need one reference for the Set method.
			_value = (value ? EventSet : 0); // + EventRefCountIncrement;
			_event = null;
			}

		/// <summary>
		///     Gets the current value of the event.
		/// </summary>
		public bool Value
			{
			get { return (_value & EventSet) != 0; }
			}

		/// <summary>
		///     Dereferences the event, closing it if necessary.
		/// </summary>
		private void DerefEvent ()
			{
			if ((InterlockedEx.Add (ref _value, -EventRefCountIncrement) >> EventRefCountShift) == 0)
				{
				if (_event != null)
					{
					_event.Close ();
					_event = null;
					}
				}
			}

		/// <summary>
		///     References the event.
		/// </summary>
		private void RefEvent ()
			{
			InterlockedEx.Add (ref _value, EventRefCountIncrement);
			}

		/// <summary>
		///     Resets the event.
		/// </summary>
		public void Reset ()
			{
			InterlockedEx.And (ref _value, ~EventSet);
			}

		/// <summary>
		///     Sets the event.
		/// </summary>
		public void Set ()
			{
			// 1. Value = 1.
			// 2. Event = Global Event.
			// 3. Set Event.
			// 4. [Optional] Dereference the Global Event.

			if ((InterlockedEx.Or (ref _value, EventSet) & EventSet) != 0)
				return;

			RefEvent ();

			// Do an update-to-date read.
			CEvent localEvent = _event;

			// Set the event if we had one.
			if (localEvent != null)
				localEvent.Set ();

			// Note that at this point we don't need to worry about anyone 
			// creating the event and waiting for it, because if they did 
			// they would check the value first. It would be 1, so they 
			// wouldn't wait at all.

			DerefEvent ();
			}

		/// <summary>
		///     Waits for the event to be set by busy waiting.
		/// </summary>
		public void SpinWait ()
			{
			if ((_value & EventSet) != 0)
				return;

			while ((_value & EventSet) == 0)
				CrestronEnvironment.Sleep (0);
			}

		/// <summary>
		///     Waits for the event to be set by busy waiting.
		/// </summary>
		/// <param name="spinCount">The number of times to check the value.</param>
		/// <returns>Whether the event was set during the wait period.</returns>
		public bool SpinWait (int spinCount)
			{
			if ((_value & EventSet) != 0)
				return true;

			for (int i = 0; i < spinCount; i++)
				{
				if ((_value & EventSet) != 0)
					return true;
				}

			return false;
			}

		/// <summary>
		///     Waits for the event to be set.
		/// </summary>
		public void Wait ()
			{
			Wait (Timeout.Infinite);
			}

		/// <summary>
		///     Waits for the event to be set.
		/// </summary>
		/// <param name="millisecondsTimeout">The number of milliseconds to wait.</param>
		/// <returns>Whether the event was set before the timeout period elapsed.</returns>
		public bool Wait (int millisecondsTimeout)
			{
			// 1. [Optional] If Value = 1, Return.
			// 2. [Optional] If Timeout = 0 And Value = 0, Return.
			// 3. [Optional] Reference the Global Event.
			// 4. [Optional] If Global Event is present, skip Step 5.
			// 5. Create Event.
			// 6. Global Event = Event only if Global Event is not present.
			// 7. If Value = 1, Return (rather, go to Step 9).
			// 8. Wait for Global Event.
			// 9. [Optional] Dereference the Global Event.


			int result = _autoReset ? InterlockedEx.And (ref _value, ~EventSet) : _value;

			// Shortcut: return immediately if the event is set.
			if ((result & EventSet) != 0)
				return true;

			// Shortcut: if the timeout is 0, return immediately if 
			// the event isn't set.
			if (millisecondsTimeout == 0)
				return false;

			// Prevent the event from being closed or invalidated.
			RefEvent ();

			// Shortcut: don't bother creating an event if we already have one.
			CEvent newEvent = _event;

			// If we don't have an event, create one and try to set it.
			if (newEvent == null)
				{
				// Create an event. We might not need it, though.
				newEvent = new CEvent (_autoReset, false);

				// Atomically use the event only if we don't already 
				// have one.
				if (Interlocked.CompareExchange (ref _event, newEvent, null) != null)
					{
					// Someone else set the event before we did.
					newEvent.Close ();
					}
				}

			try
				{
				// Check the value to see if we are meant to wait. This step 
				// is essential, because if someone set the event before we 
				// created the event (previous step), we would be waiting 
				// on an event no one knows about.
				if ((_value & EventSet) != 0)
					return true;

				return _event.Wait (millisecondsTimeout);
				}
			finally
				{
				// We don't need the event anymore.
				DerefEvent ();
				}
			}
		}

	public class FastEventWH : EventWaitHandle
		{
		private FastEvent fe;

		public FastEventWH (bool autoReset, bool value)
			{
			fe = new FastEvent (autoReset, value);
			waitObject = fe;
			}

		public void Reset ()
			{
			fe.Reset ();
			}

		public void Set ()
			{
			fe.Set ();
			}

		public void SpinWait ()
			{
			fe.SpinWait ();
			}

		public bool SpinWait (int spincount)
			{
			return fe.SpinWait (spincount);
			}

		protected override bool WaitOneInternal (int timeout)
			{
			return fe.Wait (timeout);
			}

		internal override void SetHandle ()
			{
			Set ();
			}

		internal override void ResetHandle ()
			{
			Reset ();
			}
		}
	}