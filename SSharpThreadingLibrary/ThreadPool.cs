using System;
using Crestron.SimplSharp;
using WaitCallback = Crestron.SimplSharp.CrestronSharpHelperDelegate;
using Crestron.SimplSharp.Reflection;

namespace SSharp.Threading
	{
	public static class ThreadPool
		{
		public static bool QueueUserWorkItem (WaitCallback callBack)
			{
			CrestronInvoke.BeginInvoke (callBack);

			return true;
			}

		public static bool QueueUserWorkItem (WaitCallback callback, object state)
			{
			CrestronInvoke.BeginInvoke (callback, state);

			return true;
			}

		private static void doIt (object state)
			{
			var del = (Delegate)((object[])state)[0];
			var args = (object[])((object[])state)[1];

			del.GetMethod ().Invoke (del.Target, args);
			}

		public static RegisteredWaitHandle RegisterWaitForSingleObject (WaitHandle waitObject, WaitOrTimerCallback callBack, object state,
		                                                                int millisecondsTimeOutInterval, bool executeOnlyOnce)
			{
			return RegisterWaitForSingleObject (waitObject, callBack, state, (long)millisecondsTimeOutInterval, executeOnlyOnce);
			}

		public static RegisteredWaitHandle RegisterWaitForSingleObject (WaitHandle waitObject, WaitOrTimerCallback callBack, object state,
		                                                                long millisecondsTimeOutInterval, bool executeOnlyOnce)
			{
			if (waitObject == null)
				throw new ArgumentNullException ("waitObject");

			if (callBack == null)
				throw new ArgumentNullException ("callBack");

			if (millisecondsTimeOutInterval < -1)
				throw new ArgumentOutOfRangeException ("timeout", "timeout < -1");

			if (millisecondsTimeOutInterval > Int32.MaxValue)
				throw new NotSupportedException ("Timeout is too big. Maximum is Int32.MaxValue");

			var timeout = new TimeSpan (0, 0, 0, 0, (int)millisecondsTimeOutInterval);

			var waiter = new RegisteredWaitHandle (waitObject, callBack, state, timeout, executeOnlyOnce);

			QueueUserWorkItem (waiter.Wait, null);

			return waiter;
			}

		public static RegisteredWaitHandle RegisterWaitForSingleObject (WaitHandle waitObject, WaitOrTimerCallback callBack, object state, TimeSpan timeout,
		                                                                bool executeOnlyOnce)
			{
			return RegisterWaitForSingleObject (waitObject, callBack, state, (long)timeout.TotalMilliseconds, executeOnlyOnce);
			}

		public static RegisteredWaitHandle RegisterWaitForSingleObject (WaitHandle waitObject, WaitOrTimerCallback callBack, object state,
		                                                                uint millisecondsTimeOutInterval, bool executeOnlyOnce)
			{
			return RegisterWaitForSingleObject (waitObject, callBack, state, (long)millisecondsTimeOutInterval, executeOnlyOnce);
			}
		}
	}