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

			int timeLeft = millisecondsTimeout;
			var sw = Stopwatch.StartNew ();

			for (int ix = 0; ix < waitHandles.Length; ++ix)
				{
				if (!waitHandles[ix].Wait (timeLeft == Timeout.Infinite ? Timeout.Infinite : Math.Max (timeLeft - (int)sw.ElapsedMilliseconds, 0)))
					{
					for (int iy = 0; iy < ix; ++iy)
						{
						var ce = waitHandles[iy] as CEvent;
						if (ce != null)
							ce.Set ();
						}
					return false;
					}
				}

			return true;
			}
		}

	public class DuplicateWaitObjectException : Exception
		{

		}
	}