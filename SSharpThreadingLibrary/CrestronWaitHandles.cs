using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SSharp.Threading
	{
	public class CEventWH : CEventWaitHandle
		{
		public CEventWH (bool bAutoOrManualEvent, bool initialState)
			: base (initialState, bAutoOrManualEvent ? EventResetMode.AutoReset : EventResetMode.ManualReset)
			{
			}
		}

	public class AutoResetEvent : CEventWaitHandle
		{
		public AutoResetEvent (bool initialState)
			: base (initialState, EventResetMode.AutoReset)
			{
			}
		}

	public class ManualResetEvent : CEventWaitHandle
		{
		public ManualResetEvent (bool initialState)
			: base (initialState, EventResetMode.ManualReset)
			{
			}
		}

	public class CMutexWH : WaitHandle
		{
		private CMutex cm;

		public CMutexWH (bool bGrabMutexOnStartUp)
			{
			cm = new CMutex (bGrabMutexOnStartUp);
			waitObject = cm;
			}

		public override void Close ()
			{
			cm.Close ();

			base.Close ();
			}

		public void ReleaseMutex ()
			{
			cm.ReleaseMutex ();
			}

		protected override bool WaitOneInternal (int timeout)
			{
			return cm.WaitForMutex (timeout);
			}

		internal override void SetHandle ()
			{
			ReleaseMutex ();
			}
		}
	}