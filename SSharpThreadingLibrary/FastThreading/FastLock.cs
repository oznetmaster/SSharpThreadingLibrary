using System;
using Crestron.SimplSharp;

#pragma warning disable 420

namespace ProcessHacker.Common.Threading
	{
	public struct FastLock : IDisposable
		{
		private const int SpinCount = 4000;
		private const bool SpinEnabled = false;

		private volatile CEvent _event;
		private volatile int _value;

		public FastLock (bool value)
			{
			_value = value ? 1 : 0;
			_event = null;
			}

		public void Dispose ()
			{
			Release ();

			if (_event != null)
				{
				_event.Dispose ();
				_event = null;
				}
			}

		public void Acquire ()
			{
			// Fast path.

			if (Interlocked.CompareExchange (ref _value, 1, 0) == 0)
				return;

			// Slow path 1 - spin for a while.

			/*
            if (SpinEnabled)
            {
                for (int i = 0; i < SpinCount; i++)
                {
                    if (Interlocked.CompareExchange(ref _value, 1, 0) == 0)
                        return;
                }
            }
			*/

			// Slow path 2 - wait on the event.

			// Note: see FastEvent.cs for a more detailed explanation of this 
			// technique.

			CEvent newEvent = Interlocked.CompareExchange (ref _event, null, null);

			if (newEvent == null)
				{
				newEvent = new CEvent (true, false);

				if (Interlocked.CompareExchange (ref _event, newEvent, null) != null)
					newEvent.Close ();
				}

			// Loop trying to acquire the lock. Note that after we 
			// get woken up another thread might have acquired the lock, 
			// and that's why we need a loop.
			while (true)
				{
				if (Interlocked.CompareExchange (ref _value, 1, 0) == 0)
					break;

				if (!_event.Wait (Timeout.Infinite))
					Break ("Failed to wait indefinitely on an object.");
				}
			}

		public void Release ()
			{
			Interlocked.Exchange (ref _value, 0);

			// Wake up a thread. Note that that thread might 
			// not actually get to acquire the lock because 
			// another thread may have acquired it already.
			if (_event != null)
				_event.Set ();
			}

		public bool TryAcquire ()
			{
			return Interlocked.CompareExchange (ref _value, 1, 0) == 0;
			}

		public static void Break (string logMessage)
			{
			ErrorLog.Error (logMessage);
			Debugger.Break ();
			}
		}
	}