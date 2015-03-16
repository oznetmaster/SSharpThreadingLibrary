using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SSharp.Threading
	{
	public abstract class WaitHandle : IDisposable
		{
		private bool disposed;
		protected object waitObject;

		public const int WaitTimeout = -1;

		protected WaitHandle ()
			{
			
			}

		~WaitHandle ()
			{
			Dispose (false);
			}

		internal abstract void SetHandle ();

		public virtual void Close ()
			{
			Dispose (true);

			CrestronEnvironment.GC.SuppressFinalize (this);
			}

		public virtual bool WaitOne ()
			{
			CheckDisposed ();

			return WaitOneInternal (Timeout.Infinite);
			}

		public virtual bool WaitOne (int millisecondsTimeout, bool exitContext)
			{
			CheckDisposed ();
			// check negative - except for -1 (which is Timeout.Infinite)
			if (millisecondsTimeout < Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("millisecondsTimeout");

			return WaitOneInternal (millisecondsTimeout);
			}

		public virtual bool WaitOne (int millisecondsTimeout)
			{
			return WaitOne (millisecondsTimeout, false);
			}

		public virtual bool WaitOne (TimeSpan timeout)
			{
			return WaitOne (timeout, false);
			}

		public virtual bool WaitOne (TimeSpan timeout, bool exitContext)
			{
			CheckDisposed ();
			long ms = (long)timeout.TotalMilliseconds;
			if (ms < -1 || ms > Int32.MaxValue)
				throw new ArgumentOutOfRangeException ("timeout");

			return WaitOneInternal ((int)ms);
			}

		internal void CheckDisposed ()
			{
			if (disposed || waitObject == null)
				throw new ObjectDisposedException (GetType ().FullName);
			}

		protected abstract bool WaitOneInternal (int timeout);

		public static int WaitAny (WaitHandle[] waitHandles)
			{
			return WaitAny (waitHandles, Timeout.Infinite);
			}

		public static int WaitAny (WaitHandle[] waitHandles, TimeSpan timeout)
			{
			long ms = (long)timeout.TotalMilliseconds;

			if (ms < -1 || ms > Int32.MaxValue)
				throw new ArgumentOutOfRangeException ("timeout");

			return WaitAny (waitHandles, (int)ms);
			}

		public static int WaitAny (WaitHandle[] waitHandles, int millisecondsTimeout)
			{
			if (millisecondsTimeout < Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("millisecondsTimeout");

			if (waitHandles == null || waitHandles.Any (w => w == null))
				throw new ArgumentNullException ("waitHandles");

			if (waitHandles.Length == 0)
				throw new ArgumentException ("waitHandles");


			long endTime = millisecondsTimeout == Timeout.Infinite ? Int64.MaxValue : millisecondsTimeout;
			var sw = Stopwatch.StartNew ();

			int sleepTime = 0;

			while (sw.ElapsedMilliseconds < endTime)
				{
				for (int ix = 0; ix < waitHandles.Length; ++ix)
					{
					if (waitHandles[ix].WaitOne (0))
						return ix;
					}
				CrestronEnvironment.Sleep (++sleepTime);
				}

			return WaitTimeout;
			}

		public static bool WaitAll (WaitHandle[] waitHandles)
			{
			return WaitAll (waitHandles, Timeout.Infinite);
			}

		public static bool WaitAll (WaitHandle[] waitHandles, TimeSpan timeout)
			{
			long ms = (long)timeout.TotalMilliseconds;

			if (ms < -1 || ms > Int32.MaxValue)
				throw new ArgumentOutOfRangeException ("timeout");

			return WaitAll (waitHandles, (int)ms);
			}

		public static bool WaitAll (WaitHandle[] waitHandles, int millisecondsTimeout)
			{
			if (millisecondsTimeout < Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("millisecondsTimeout");

			if (waitHandles == null || waitHandles.Length == 0 || waitHandles.Any (w => w == null))
				throw new ArgumentNullException ("waitHandles");

			if (waitHandles.Distinct ().Count () != waitHandles.Length)
				throw new DuplicateWaitObjectException ();

			int timeLeft = millisecondsTimeout;
			var sw = Stopwatch.StartNew ();

			for (int ix = 0; ix < waitHandles.Length; ++ix)
				{
				if (!waitHandles[ix].WaitOne (timeLeft == Timeout.Infinite ? Timeout.Infinite : Math.Max (timeLeft - (int)sw.ElapsedMilliseconds, 0)))
					{
					for (int iy = 0; iy < ix; ++iy)
						waitHandles[iy].SetHandle ();

					return false;
					}
				}

			return true;
			}

		protected virtual void Dispose (bool explicitDisposing)
			{
			if (!disposed)
				{

				//
				// This is only the case if the handle was never properly initialized
				// most likely a bug in the derived class
				//
				if (waitObject == null)
					return;

				lock (this)
					{
					if (disposed)
						return;

					disposed = true;

					var o = waitObject as IDisposable;
					if (o != null)
						o.Dispose ();

					waitObject = null;
					}
				}
			}

		#region IDisposable Members

		public void Dispose ()
			{
			Close ();
			}

		#endregion
		}

	public abstract class EventWaitHandle : WaitHandle
		{
		internal abstract void ResetHandle ();
		}

	public class CEventWaitHandle : EventWaitHandle
		{
		private readonly CEvent ce;

		public CEventWaitHandle (bool initialState, EventResetMode mode)
			{
			ce = new CEvent (!IsManualReset (mode), initialState);
			waitObject = ce;
			}

		public CEventWaitHandle (CEvent cEvent)
			{
			ce = cEvent;
			waitObject = ce;
			}

		protected override bool WaitOneInternal (int timeout)
			{
			return ce.Wait (timeout);
			}

		private static bool IsManualReset (EventResetMode mode)
			{
			if ((mode < EventResetMode.AutoReset) || (mode > EventResetMode.ManualReset))
				throw new ArgumentException ("mode");
			return (mode == EventResetMode.ManualReset);
			}

		public bool Reset ()
			{
			/* This needs locking since another thread could dispose the handle */
			lock (this)
				{
				CheckDisposed ();

				return ce.Reset ();
				}
			}

		public bool Set ()
			{
			lock (this)
				{
				CheckDisposed ();

				return ce.Set ();
				}
			}

		internal override void SetHandle ()
			{
			Set ();
			}

		internal override void ResetHandle ()
			{
			Reset ();
			}

		public static implicit operator CEvent (CEventWaitHandle ewh)
			{
			return ewh.ce;
			}

		public static implicit operator CEventWaitHandle (CEvent ce)
			{
			return new CEventWaitHandle (ce);
			}
		}

	public enum EventResetMode
		{
		AutoReset = 0,
		ManualReset = 1,
		}

	public class DuplicateWaitObjectException : Exception
		{
		
		}

	}