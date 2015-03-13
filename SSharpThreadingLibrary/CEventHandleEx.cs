using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace Crestron.SimplSharp
	{
	public class CEventHandleEx
		{
		public const int WaitTimeout = -1;

		public static int WaitAny (CEventHandle[] waitHandles, TimeSpan timeout)
			{
			long ms = (long)timeout.TotalMilliseconds;

			if (ms < -1 || ms > Int32.MaxValue)
				throw new ArgumentOutOfRangeException ("timeout");

			return WaitAny (waitHandles, (int)ms);
			}

		public static int WaitAny (CEventHandle[] waitHandles, int millisecondsTimeout)
			{
			if (millisecondsTimeout < Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("millisecondsTimeout");

			if (waitHandles == null || waitHandles.Any (w => w == null))
				throw new ArgumentNullException ("waitHandles");

			long endTime = millisecondsTimeout == Timeout.Infinite ? Int64.MaxValue : millisecondsTimeout;
			var sw = new Stopwatch ();
			sw.Start ();

			int sleepTime = 0;

			while (sw.ElapsedMilliseconds < endTime)
				{
				for (int ix = 0; ix < waitHandles.Length; ++ix)
					{
					if (waitHandles[ix].Wait (0))
						return ix;
					}
				CrestronEnvironment.Sleep (++sleepTime);
				}

			return WaitTimeout;
			}

#if false
		public static bool WaitAll (CEventHandle[] waitHandles)
			{
			return WaitAll (waitHandles, Timeout.Infinite);
			}

		public static bool WaitAll (CEventHandle[] waitHandles, TimeSpan timeout)
			{
			long ms = (long)timeout.TotalMilliseconds;

			if (ms < -1 || ms > Int32.MaxValue)
				throw new ArgumentOutOfRangeException ("timeout");

			return WaitAll (waitHandles, (int)ms);
			}

		public static bool WaitAll (CEventHandle[] waitHandles, int millisecondsTimeout)
			{
			if (millisecondsTimeout < Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("millisecondsTimeout");

			if (waitHandles.Length > 64)
				throw new NotSupportedException ("Maximum of 64 wait handles");

			if (waitHandles == null || waitHandles.Any (w => w == null))
				throw new ArgumentNullException ("waitHandles");

			if (waitHandles.Distinct ().Count () != waitHandles.Length)
				throw new DuplicateWaitObjectException ();

			long endTime = millisecondsTimeout == Timeout.Infinite ? Int64.MaxValue : millisecondsTimeout;
			var sw = new Stopwatch ();
			sw.Start ();

			int sleepTime = 0;
			int signaledHandles = 0;

			while (sw.ElapsedMilliseconds < endTime)
				{
				int ix;
				for (ix = 0; ix < waitHandles.Length; ++ix)
					{
					if ((signaledHandles & (1 << ix)) != 0)
						continue;
					if (waitHandles[ix].Wait ())
						signaledHandles |= (1 << ix);
					else
						break;
					}

				if (ix >= waitHandles.Length)
					return true;

				CrestronEnvironment.Sleep (sleepTime++);
				}

			for (int ix = 0; ix < waitHandles.Length; ++ix)
				{
				if ((signaledHandles & (1 << ix)) == 0)
					continue;

				waitHandles[ix].SetHandle ();
				}

			return false;
			}
#endif
		}

	public class DuplicateWaitObjectException : Exception
		{

		}
	}