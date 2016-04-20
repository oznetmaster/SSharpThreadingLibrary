using System;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Reflection;

namespace SSharp.CrestronThread
	{
	public delegate object ThreadCallbackFunction (object userSpecific);

	public class Thread
		{
		public enum eThreadPriority
			{
			HighPriority = 0xfb,
			LowestPriority = 0xff,
			MediumPriority = 0xfd,
			NotSet = -1,
			UberPriority = 250
			}

		public enum eThreadStartOptions
			{
			CreateSuspended,
			Running
			}

		public enum eThreadStates
			{
			ThreadInvalidState,
			ThreadCreated,
			ThreadRunning,
			ThreadSuspended,
			ThreadAborting,
			ThreadFinished
			}

		public enum eThreadType
			{
			SystemThread,
			UserThread
			}


		private delegate object DelGetCurrentThread ();
		private delegate void DelAbort ();
		private delegate LocalDataStoreSlot DelAllocateDataSlot ();
		private delegate LocalDataStoreSlot DelAllocateNamedDataSlot (string name);
		private delegate bool DelAllowOtherAppsToRun ();
		private delegate bool DelEquals (object obj);
		private delegate void DelFreeNamedDataSlot (string name);
		private delegate object DelGetData (LocalDataStoreSlot slot);
		private delegate int DelGetHashCode ();
		private delegate LocalDataStoreSlot DelGetNamedDataSlot (string name);
		private delegate void DelJoin ();
		private delegate bool DelJoinTimeout (int millisecondTimeout);
		private delegate void DelSetData (LocalDataStoreSlot slot, object obj);
		private delegate void DelSleep (int timeoutInMs);
		private delegate void DelStartObject (object obj);
		private delegate void DelStart ();

		private delegate int DelGetMaxNumberOfUserThreads ();
		private delegate void DelSetMaxNumberOfUserThreads (int maxNumberOfUserThreads);
		//private delegate object DelGetEventHandlerInitialThreadPriority ();
		//private delegate void DelSetEventHandlerInitialThreadPriority (object EventHandlerInitialThreadPriority);

		private delegate string DelGet_Name ();
		private delegate void DelSet_Name (string name);
		private delegate int DelGet_ManagedThreadId ();

		private static bool _isPro;
		private static readonly Assembly _assCrestronThread;
		private static readonly CType _ctypeThread;
		private static readonly CType _ctypeThreadCallbackFunction;
		private static readonly CType _ctypeThreadStartOptions;
		private static readonly CType _ctypeThreadPriority;
		private static readonly CType _ctypeThreadState;
		private static readonly CType _ctypeThreadType;
		private static readonly PropertyInfo _propCurrentThread;
		private static DelGetCurrentThread _delGetCurrentThread;
		private static readonly PropertyInfo _propName;
		private DelGet_Name _delGet_Name;
		private DelSet_Name _delSet_Name;
		private static readonly PropertyInfo _propPriority;
		private static readonly PropertyInfo _propManagedThreadId;
		private DelGet_ManagedThreadId _delGet_ManagedThreadId;
		private static readonly PropertyInfo _propMaxNumberOfUserThreads;
		private static DelGetMaxNumberOfUserThreads _delGetMaxNumberOfUserThreads;
		private static DelSetMaxNumberOfUserThreads _delSetMaxNumberOfUserThreads;
		private static readonly PropertyInfo _propEventHandlerInitialThreadPriority;
		//private static DelGetEventHandlerInitialThreadPriority _delGetEvenHandlerInitialThreadPriority;
		//private static DelSetEventHandlerInitialThreadPriority _delSetEventHandlerInitialThreadPriority;
		private static readonly PropertyInfo _propThreadState;
		private static readonly PropertyInfo _propThreadType;
		private DelAbort _delAbort;
		private static DelAllocateDataSlot _delAllocateDataSlot;
		private static DelAllocateNamedDataSlot _delAllocateNamedDataSlot;
		private static DelAllowOtherAppsToRun _delAllowOtherAppsToRun;
		private DelEquals _delEquals;
		private static DelFreeNamedDataSlot _delFreeNamedDataSlot;
		private static DelGetData _delGetData;
		private DelGetHashCode _delGetHashCode;
		private static DelGetNamedDataSlot _delGetNamedDataSlot;
		private DelJoin _delJoin;
		private DelJoinTimeout _delJoinTimeOut;
		private static DelSetData _delSetData;
		private static DelSleep _delSleep;
		private DelStartObject _delStartObject;
		private DelStart _delStart;
		private static ConstructorInfo _ciThread;
		private static ConstructorInfo _ciThreadWithOption;

		static Thread ()
			{
			_isPro = CrestronEnvironment.RuntimeEnvironment == eRuntimeEnvironment.SimplSharpPro;

			try
				{
				_ctypeThread = Type.GetType ("Crestron.SimplSharpPro.CrestronThread.Thread, SimplSharpPro");
				}
			catch
				{
				}

			if (_ctypeThread == null)
				return;

			_assCrestronThread = _ctypeThread.Assembly;

			_ctypeThreadCallbackFunction = _assCrestronThread.GetType ("Crestron.SimplSharpPro.CrestronThread.ThreadCallbackFunction");
			_ctypeThreadStartOptions = _assCrestronThread.GetType ("Crestron.SimplSharpPro.CrestronThread.Thread+eThreadStartOptions");
			_ctypeThreadPriority = _assCrestronThread.GetType ("Crestron.SimplSharpPro.CrestronThread.Thread+eThreadPriority");
			_ctypeThreadState = _assCrestronThread.GetType ("Crestron.SimplSharpPro.CrestronThread.Thread+eThreadStates");
			_ctypeThreadType = _assCrestronThread.GetType ("Crestron.SimplSharpPro.CrestronThread.Thread+eThreadType");

			_propCurrentThread = _ctypeThread.GetProperty ("CurrentThread");
			_propPriority = _ctypeThread.GetProperty ("Priority");
			_propName = _ctypeThread.GetProperty ("Name");
			_propManagedThreadId = _ctypeThread.GetProperty ("ManagedThreadId");
			_propMaxNumberOfUserThreads = _ctypeThread.GetProperty ("MaxNumberOfUserThreads");
			_propEventHandlerInitialThreadPriority = _ctypeThread.GetProperty ("EventHandlerInitialThreadPriority");
			_propThreadState = _ctypeThread.GetProperty ("ThreadState");
			_propThreadType = _ctypeThread.GetProperty ("ThreadType");

			_ciThread = _ctypeThread.GetConstructor (new CType[] { _ctypeThreadCallbackFunction, typeof (object) });
			_ciThreadWithOption = _ctypeThread.GetConstructor (new CType[] { _ctypeThreadCallbackFunction, typeof (object), _ctypeThreadStartOptions });
			}

		private static object ConvertToCrestronThreadPriority (eThreadPriority priority)
			{
			return Enum.ToObject (_ctypeThreadPriority, (int)priority);
			}

		private static object ConvertToCrestronThreadStartOption (eThreadStartOptions startOption)
			{
			return Enum.ToObject (_ctypeThreadStartOptions, (int)startOption);
			}

		private static object ConvertToCrestronThreadState (eThreadStates threadState)
			{
			return Enum.ToObject (_ctypeThreadState, (int)threadState);
			}

		private static object ConvertToCrestronThreadType (eThreadType threadType)
			{
			return Enum.ToObject (_ctypeThreadType, (int)threadType);
			}

		private static object ConvertToCrestronThreadCallbackFunction (ThreadCallbackFunction func)
			{
			return CDelegate.CreateDelegate (_ctypeThreadCallbackFunction, func.Target, func.GetMethod ());
			}

		public static bool IsPro
			{
			get { return _isPro && _ctypeThread != null; }
			}

		public static bool IsMiniPro
			{
			get { return _ctypeThread != null; }
			}

		public static Thread CurrentThread
			{
			get
				{
				if (_ctypeThread == null)
					return new Thread (null);

				if (_delGetCurrentThread == null)
					_delGetCurrentThread = CDelegateEx.CreateDelegate<DelGetCurrentThread> (_propCurrentThread.GetGetMethod ());

				return new Thread (_delGetCurrentThread ());
				}
			}

		public static eThreadPriority EventHandlerInitialThreadPriority
			{
			set
				{
				if (_ctypeThreadType == null)
					return;

				/*
				if (_delSetEventHandlerInitialThreadPriority == null)
					_delSetEventHandlerInitialThreadPriority = (DelSetEventHandlerInitialThreadPriority)CDelegate.CreateDelegate (_ctypeThread, null, _propEventHandlerInitialThreadPriority.GetSetMethod ());

				_delSetEventHandlerInitialThreadPriority (ConvertToCrestronThreadPriority (value));
				*/

				_propEventHandlerInitialThreadPriority.SetValue (null, ConvertToCrestronThreadPriority (value), null);
				}
			}

		public static int MaxNumberOfUserThreads
			{
			get
				{
				if (_ctypeThreadType == null)
					return 0;

				if (_delGetMaxNumberOfUserThreads == null)
					_delGetMaxNumberOfUserThreads = CDelegateEx.CreateDelegate<DelGetMaxNumberOfUserThreads> (_propMaxNumberOfUserThreads.GetGetMethod ());

				return _delGetMaxNumberOfUserThreads ();
				}
			set
				{
				if (_ctypeThreadType == null)
					return;

				if (_delSetMaxNumberOfUserThreads == null)
					_delSetMaxNumberOfUserThreads = CDelegateEx.CreateDelegate<DelSetMaxNumberOfUserThreads> (_propMaxNumberOfUserThreads.GetSetMethod ());

				_delSetMaxNumberOfUserThreads (value);
				}
			}

		public static LocalDataStoreSlot AllocateDataSlot ()
			{
			if (_ctypeThreadType == null)
				return null;

			if (_delAllocateDataSlot == null)
				_delAllocateDataSlot = CDelegateEx.CreateDelegate <DelAllocateDataSlot> (_ctypeThread, "AllocateDataSlot");

			return _delAllocateDataSlot ();
			}

		public static LocalDataStoreSlot AllocateNamedDataSlot (string name)
			{
			if (_ctypeThreadType == null)
				return null;

			if (_delAllocateNamedDataSlot == null)
				_delAllocateNamedDataSlot = CDelegateEx.CreateDelegate<DelAllocateNamedDataSlot> (_ctypeThread, "AllocateNamedDataSlot");

			return _delAllocateNamedDataSlot (name);
			}

		public static bool AllowOtherAppsToRun (string name)
			{
			if (_ctypeThreadType == null)
				return false;

			if (_delAllowOtherAppsToRun == null)
				_delAllowOtherAppsToRun = CDelegateEx.CreateDelegate<DelAllowOtherAppsToRun> (_ctypeThread, "AllowOtherAppsToRun");

			return _delAllowOtherAppsToRun ();
			}

		public static void FreeNamedDataSlot (string name)
			{
			if (_ctypeThreadType == null)
				return;

			if (_delFreeNamedDataSlot == null)
				_delFreeNamedDataSlot = CDelegateEx.CreateDelegate<DelFreeNamedDataSlot> (_ctypeThread, "FreeNamedDataSlot");

			_delFreeNamedDataSlot (name);
			}

		public static object GetData (LocalDataStoreSlot slot)
			{
			if (_ctypeThreadType == null)
				return null;

			if (_delGetData == null)
				_delGetData = CDelegateEx.CreateDelegate<DelGetData> (_ctypeThread, "GetData");

			return _delGetData (slot);
			}

		public static LocalDataStoreSlot GetNamedDataSlot (string name)
			{
			if (_ctypeThreadType == null)
				return null;

			if (_delGetNamedDataSlot == null)
				_delGetNamedDataSlot = CDelegateEx.CreateDelegate<DelGetNamedDataSlot> (_ctypeThread, "GetNamedDataSlot");

			return _delGetNamedDataSlot (name);
			}

		public static void SetData (LocalDataStoreSlot slot, object data)
			{
			if (_ctypeThreadType == null)
				return;

			if (_delSetData == null)
				_delSetData = CDelegateEx.CreateDelegate<DelSetData> (_ctypeThread, "SetData");

			_delSetData (slot, data);
			}

		private readonly object m_cThread;

		private Thread (object cThread)
			{
			m_cThread = cThread;
			}

		public Thread (ThreadCallbackFunction threadCallbackFunction, object userDefinedObject)
			{
			m_cThread = _ciThread.Invoke (new object[] { ConvertToCrestronThreadCallbackFunction (threadCallbackFunction), userDefinedObject });
			}

		public Thread (ThreadCallbackFunction threadCallbackFunction, object userDefinedObject, eThreadStartOptions threadStartOption)
			{
			m_cThread = _ciThreadWithOption.Invoke (new object[] { ConvertToCrestronThreadCallbackFunction (threadCallbackFunction), userDefinedObject, ConvertToCrestronThreadStartOption (threadStartOption) });
			}

		public int ManagedThreadId
			{
			get
				{
				if (m_cThread == null)
					return 0;

				if (_delGet_ManagedThreadId == null)
					_delGet_ManagedThreadId = CDelegateEx.CreateDelegate<DelGet_ManagedThreadId> (m_cThread, _propManagedThreadId.GetGetMethod ());

				return _delGet_ManagedThreadId ();
				}
			}

		public string Name
			{
			get
				{
				if (m_cThread == null)
					return String.Empty;

				if (_delGet_Name == null)
					_delGet_Name = CDelegateEx.CreateDelegate<DelGet_Name> (m_cThread, _propName.GetGetMethod ());

				return _delGet_Name ();
				}

			set
				{
				if (m_cThread == null)
					return;

				if (_delSet_Name == null)
					_delSet_Name = CDelegateEx.CreateDelegate<DelSet_Name> (m_cThread, _propName.GetSetMethod ());

				_delSet_Name (value);
				}
			}

		public eThreadPriority Priority
			{
			get
				{
				if (m_cThread == null)
					return eThreadPriority.MediumPriority;

				return (eThreadPriority)(int)_propPriority.GetValue (m_cThread, null);
				}
			set
				{
				if (m_cThread == null)
					return;

				_propPriority.SetValue (m_cThread, ConvertToCrestronThreadPriority (value), null);
				}
			}

		public eThreadStates ThreadState
			{
			get
				{
				if (m_cThread == null)
					return eThreadStates.ThreadRunning;

				return (eThreadStates)(int)_propThreadState.GetValue (m_cThread, null);
				}
			}

		public eThreadType ThreadType
			{
			get
				{
				if (m_cThread == null)
					return eThreadType.SystemThread;

				return (eThreadType)(int)_propThreadType.GetValue (m_cThread, null);
				}
			}

		public void Abort ()
			{
			if (m_cThread == null)
				return;

			if (_delAbort == null)
				_delAbort = CDelegateEx.CreateDelegate<DelAbort> (m_cThread, "Abort");

			_delAbort ();
			}

		public override bool Equals (object obj)
			{
			if (m_cThread == null)
				return false;

			if (_delEquals == null)
				_delEquals = CDelegateEx.CreateDelegate<DelEquals> (m_cThread, "Equals");

			return _delEquals (obj);
			}

		public override int GetHashCode ()
			{
			if (m_cThread == null)
				return base.GetHashCode ();

			if (_delGetHashCode == null)
				_delGetHashCode = CDelegateEx.CreateDelegate<DelGetHashCode> (m_cThread, "GetHashCode");

			return _delGetHashCode ();
			}

		public void Join ()
			{
			if (m_cThread == null)
				return;

			if (_delJoin == null)
				_delJoin = CDelegateEx.CreateDelegate<DelJoin> (m_cThread, _ctypeThread.GetMethod ("Join", CTypeExtensions.CTypeEmptyArray));

			_delJoin ();
			}

		public bool Join (int millisecondTimeout)
			{
			if (m_cThread == null)
				return false;

			if (_delJoinTimeOut == null)
				_delJoinTimeOut = CDelegateEx.CreateDelegate<DelJoinTimeout> (m_cThread, _ctypeThread.GetMethod ("Join", new CType[] { typeof (int) }));

			return _delJoinTimeOut (millisecondTimeout);
			}

		public void Sleep (int timeoutInMs)
			{
			if (m_cThread == null)
				return;

			if (_delSleep == null)
				_delSleep = CDelegateEx.CreateDelegate<DelSleep> (m_cThread, "Sleep");

			_delSleep (timeoutInMs);
			}

		public void Start ()
			{
			if (m_cThread == null)
				return;
			
			if (_delStart == null)
				_delStart = CDelegateEx.CreateDelegate<DelStart> (m_cThread, _ctypeThread.GetMethod ("Start", CTypeExtensions.CTypeEmptyArray));

			_delStart ();
			}

		public void Start (object obj)
			{
			if (m_cThread == null)
				return;
			
			if (_delStartObject == null)
				_delStartObject = CDelegateEx.CreateDelegate<DelStartObject> (m_cThread, _ctypeThread.GetMethod ("Start", new CType[] { typeof (object) }));

			_delStartObject (obj);
			}

		public static bool operator == (Thread thread1, Thread thread2)
			{
			if (thread1 == null)
				return thread2 == null;

			return thread2 != null && thread1.m_cThread != null && thread2.m_cThread != null && thread1.ManagedThreadId == thread2.ManagedThreadId;
			}

		public static bool operator != (Thread thread1, Thread thread2)
			{
			if (thread1 == null)
				return thread2 != null;

			return thread2 == null || thread1.m_cThread == null || thread2.m_cThread == null || thread1.ManagedThreadId != thread2.ManagedThreadId;
			}
		}
	}