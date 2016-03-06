using System;
using ProcessHacker.Common.Threading;
using Crestron.SimplSharp;
using Wintellect.Threading;

namespace SSharp.Threading
	{
	public class Semaphore : WaitHandle
		{
#if USE_FAST_EVENT
		private FastEvent m_gate;
		private bool m_isClosed;
#else
		private CEvent m_gate;
#endif
		private int m_currentCount;
		private readonly int m_maximumCount;

		public Semaphore (int initialCount, int maximumCount)
			{
			if (initialCount > maximumCount)
				throw new ArgumentException ("initialCount must be <= maximumCount");
			if (maximumCount < 1)
				throw new ArgumentOutOfRangeException ("maximumCount", "maximumCount must be > 0");
			if (initialCount < 0)
				throw new ArgumentOutOfRangeException ("initialCount", "initialCount must be >= 0");

			m_currentCount = initialCount;
			m_maximumCount = maximumCount;

#if USE_FAST_EVENT
			m_gate = new FastEvent (true, m_currentCount > 0);
#else
			m_gate = new CEvent (true, m_currentCount > 0);
#endif
			waitObject = m_gate;
			}

		public int Release ()
			{
			return Release (1);
			}

		public int Release (int count)
			{
#if USE_FAST_EVENT
			if (m_isClosed)
#else
			if (m_gate == null)
#endif
				throw new ObjectDisposedException ("Semaphore already closed");

			if (count < 1)
				throw new ArgumentOutOfRangeException ("count", "count must be > 0");

			int cc = m_currentCount;
			if (InterlockedEx.Add (ref m_currentCount, count) > m_maximumCount)
				throw new SemaphoreFullException ("count exceeded maximum count");

			m_gate.Set (); //Open gate

			return cc;
			}

		public override void Close ()
			{
#if USE_FAST_EVENT
			m_isClosed = true;
#else
			CEvent oldGate = Interlocked.CompareExchange (ref m_gate, null, m_gate);
			if (oldGate != null)
				oldGate.Close ();
#endif
			base.Close ();
			}

		protected override bool WaitOneInternal (int timeout)
			{
#if USE_FAST_EVENT
			if (m_isClosed)
#else
			if (m_gate == null)
#endif
				throw new ObjectDisposedException ("Semaphore already closed");

			if (timeout < Timeout.Infinite)
				throw new ArgumentOutOfRangeException ("timeout", "timeout must be >= 0");

			if (!m_gate.Wait (timeout)) // try to enter and close gate
				return false;

			if (Interlocked.Decrement (ref m_currentCount) > 0)
				m_gate.Set ();

			return true;
			}

		internal override void SetHandle ()
			{
			Release ();
			}
		}

	public class SemaphoreFullException : Exception
		{
		public SemaphoreFullException (string message)
			: base (message)
			{
			}
		}
	}