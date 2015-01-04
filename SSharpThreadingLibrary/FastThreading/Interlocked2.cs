using System;
#if SSHARP
using Crestron.SimplSharp;
#else
using System.Threading;
#endif

namespace ProcessHacker.Common.Threading
	{
	public static class Interlocked2
		{
		public static int Set (ref int location, Predicate<int> predicate, Func<int, int> transform)
			{
			while (true)
				{
				int value = location;

				if (!predicate (value))
					return value;

				if (Interlocked.CompareExchange (ref location, transform (value), value) == value)
					return value;
				}
			}
		}
	}