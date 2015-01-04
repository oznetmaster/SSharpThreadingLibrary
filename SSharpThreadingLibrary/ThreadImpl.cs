using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Reflection;

namespace SSharp.Threading
	{
	public class ThreadImpl
		{
		private readonly object InternalThreadObject;
		private int? m_managedThreadId;
		private eThreadType? m_eThreadTypeImpl;

		protected internal ThreadImpl (object internalThreadObject)
			{
			InternalThreadObject = internalThreadObject;
			}

		public object Thread
			{
			get { return InternalThreadObject; }
			}

		private static readonly bool isSimplSharpPro = CrestronEnvironment.RuntimeEnvironment == eRuntimeEnvironment.SimplSharpPro;

		public static bool IsSimpleSharpPro
			{
			get { return isSimplSharpPro; }
			}

		private static readonly CType TypeNativeThread = Type.GetType ("System" + ".Threading.Thread");
		private static readonly CType TypeCrestronThread = isSimplSharpPro ? Type.GetType ("Crestron" + ".SimplSharpPro.CrestronThread.Thread, SimplSharpPro") : null;
		private static readonly CType TypeThread = isSimplSharpPro ? TypeCrestronThread : TypeNativeThread;

		//private static readonly PropertyInfo propertyInfoCurrentThread = TypeThread.GetPropertyEx ("CurrentThread");

		private delegate object Get_CurrentThreadDelegate ();

		private static Get_CurrentThreadDelegate m_get_CurrentThreadDelegate;

		public static ThreadImpl CurrentThread
			{
			get
				{
				if (m_get_CurrentThreadDelegate == null)
					m_get_CurrentThreadDelegate =
						(Get_CurrentThreadDelegate)
						Delegate.CreateDelegate (typeof(Get_CurrentThreadDelegate), null, TypeThread.GetProperty ("CurrentThread").GetGetMethod ());

				return new ThreadImpl (m_get_CurrentThreadDelegate ());

				//return new ThreadImpl (propertyInfoCurrentThread.GetValue (null, null));
				}
			}

		private static PropertyInfo propertyInfoManagedThreadId;

		public int ManagedThreadId
			{
			get
				{
				if (propertyInfoManagedThreadId == null)
					propertyInfoManagedThreadId = TypeThread.GetPropertyEx ("ManagedThreadId");

				return m_managedThreadId.HasValue
					       ? m_managedThreadId.Value
					       : (m_managedThreadId = (int)propertyInfoManagedThreadId.GetValue (InternalThreadObject, null)).Value;
				}
			}

		private static readonly PropertyInfo propertyInfoName = TypeThread.GetPropertyEx ("Name");
		private static MethodInfo methodInfoGet_Name;

		private delegate string Get_NameDelegate ();

		private Get_NameDelegate m_get_NameDelegate;

		public string Name
			{
			get
				{
				if (methodInfoGet_Name == null)
					methodInfoGet_Name = propertyInfoName.GetGetMethod ();

				if (m_get_NameDelegate == null)
					m_get_NameDelegate =
						(Get_NameDelegate)TypeExtenders.DelegateCreateDelegate (typeof(Get_NameDelegate), InternalThreadObject, methodInfoGet_Name);

				return m_get_NameDelegate ();

				//return (string)propertyInfoName.GetValue (InternalThreadObject, null);
				}
			set
				{
				propertyInfoName.SetValue (InternalThreadObject, value, null);
				}
			}

		private static PropertyInfo propertyInfoPriority;
		private static readonly Type TypeThreadPriorityNative = Type.GetType ("System" + ".Threading.ThreadPriority");

		private static readonly Type TypeThreadPriorityCrestron = isSimplSharpPro
			                                                          ? Type.GetType ("Crestron" + ".SimplSharpPro.CrestronThread.eThreadPriority, SimplSharpPro")
			                                                          : null;

		private static object ConvertToSystemThreadPriority (ThreadPriority threadPriority)
			{
			return Enum.ToObject (TypeThreadPriorityNative, (int)threadPriority);
			}

		private static object ConvertToCrestronThreadPriority (ThreadPriority threadPriority)
			{
			return Enum.ToObject (TypeThreadPriorityCrestron, dictToCrestronThreadPriorities[threadPriority]);
			}

		private static readonly Dictionary<ThreadPriority, int> dictToCrestronThreadPriorities = new Dictionary<ThreadPriority, int>
			{
			{ThreadPriority.Lowest, 0xff},
			{ThreadPriority.BelowNormal, 0xfe},
			{ThreadPriority.Normal, 0xfd},
			{ThreadPriority.AboveNormal, 0xfc},
			{ThreadPriority.Highest, 0xfb}
			};

		private static readonly Dictionary<int, ThreadPriority> dictFromCrestronThreadPriorities = new Dictionary<int, ThreadPriority>
			{
			{0xff, ThreadPriority.Lowest},
			{0xfe, ThreadPriority.BelowNormal},
			{0xfd, ThreadPriority.Normal},
			{0xfc, ThreadPriority.AboveNormal},
			{0xfb, ThreadPriority.Highest}
			};

		public ThreadPriority Priority
			{
			get
				{
				if (propertyInfoPriority == null)
					propertyInfoPriority = TypeThread.GetPropertyEx ("Priority");

				var priority = (int)propertyInfoPriority.GetValue (InternalThreadObject, null);
				return isSimplSharpPro ? dictFromCrestronThreadPriorities[priority] : (ThreadPriority)priority;
				}
			set
				{
				if (propertyInfoPriority == null)
					propertyInfoPriority = TypeThread.GetPropertyEx ("Priority");

				var priority = isSimplSharpPro ? ConvertToCrestronThreadPriority (value) : ConvertToSystemThreadPriority (value);
				propertyInfoPriority.SetValue (InternalThreadObject, priority, null);
				}
			}

		private static MethodInfo methodInfoAbort;

		public void Abort ()
			{
			if (!isSimplSharpPro)
				throw new NotSupportedException ();

			if (methodInfoAbort == null)
				methodInfoAbort = TypeCrestronThread.GetMethodEx ("Abort", TypeExtenders.EmptyTypes);

			methodInfoAbort.Invoke (InternalThreadObject, new object[] {});
			}

		private static PropertyInfo propertyInfoNativeThread;

		private object NativeThreadObject
			{
			get
				{
				if (CrestronEnvironment.RuntimeEnvironment == eRuntimeEnvironment.SIMPL)
					return InternalThreadObject;

				if (propertyInfoNativeThread == null)
					propertyInfoNativeThread = TypeCrestronThread.GetProperty ("_internalThread", BindingFlags.Instance | BindingFlags.NonPublic);

				return propertyInfoNativeThread.GetValue (InternalThreadObject, null);
				}
			}

		private enum eThreadState
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

		private static MethodInfo methodInfoGet_CrestronThreadState;
		private static MethodInfo methodInfoNativeJoin;

		private delegate int Get_CrestronThreadStateDelegate ();

		private delegate bool NativeJoin_IntDelegate (int millisecondTimeout);

		private Get_CrestronThreadStateDelegate m_getCrestronThreadStateDelegate;
		private NativeJoin_IntDelegate m_nativeJoin_IntDelegate;

		private eThreadState ThreadState
			{
			get
				{
				if (Equals (CurrentThread))
					return eThreadState.ThreadRunning;

				if (!isSimplSharpPro)
					{
					if (methodInfoNativeJoin == null)
						methodInfoNativeJoin = TypeNativeThread.GetMethodEx ("Join", new Type[] {typeof(int)});

					if (m_nativeJoin_IntDelegate == null)
						m_nativeJoin_IntDelegate =
							(NativeJoin_IntDelegate)TypeExtenders.DelegateCreateDelegate (typeof(NativeJoin_IntDelegate), InternalThreadObject, methodInfoNativeJoin);

					try
						{
						return m_nativeJoin_IntDelegate (0) ? eThreadState.ThreadFinished : eThreadState.ThreadRunning;

						//var result = (bool)methodInfoNativeJoin.Invoke (InternalThreadObject, new object[] {0});
						//return result ? eThreadState.ThreadFinished : eThreadState.ThreadRunning;
						}
					catch (Exception)
						{
						return eThreadState.ThreadCreated;
						}
					}

				if (methodInfoGet_CrestronThreadState == null)
					methodInfoGet_CrestronThreadState = TypeCrestronThread.GetPropertyEx ("ThreadState").GetGetMethod ();

				if (m_getCrestronThreadStateDelegate == null)
					m_getCrestronThreadStateDelegate =
						(Get_CrestronThreadStateDelegate)TypeExtenders.DelegateCreateDelegate (typeof(Get_CrestronThreadStateDelegate), InternalThreadObject, methodInfoGet_CrestronThreadState);

				return (eThreadState)m_getCrestronThreadStateDelegate ();

				//return (eThreadState)(int)propertyInfoCrestronThreadState.GetValue (InternalThreadObject, null);
				}
			}

		private static PropertyInfo propertyInfoCrestronThreadType;

		private eThreadType ThreadType
			{
			get
				{
				if (!isSimplSharpPro)
					return eThreadType.SystemThread;

				if (propertyInfoCrestronThreadType == null)
					propertyInfoCrestronThreadType = TypeCrestronThread.GetPropertyEx ("ThreadType");

				return m_eThreadTypeImpl.HasValue
					       ? m_eThreadTypeImpl.Value
					       : (m_eThreadTypeImpl = (eThreadType)(int)propertyInfoCrestronThreadType.GetValue (InternalThreadObject, null)).Value;

				//return (eThreadType)(int)propertyInfoCrestronThreadType.GetValue (InternalThreadObject, null);
				}
			}

		public void Join ()
			{
			if (!isSimplSharpPro || ThreadType == eThreadType.SystemThread)
				throw new NotSupportedException ();

			if (ThreadState == eThreadState.ThreadCreated)
				throw new ThreadStateException ();

			var priority = CurrentThread.Priority;
			CurrentThread.Priority = Priority;

			int sleepTime = 0;
			while (ThreadState == eThreadState.ThreadRunning)
				{
				Sleep (sleepTime++);
				}

			CurrentThread.Priority = priority;
			}

		public bool Join (int millisecondsTimeout)
			{
			if (!isSimplSharpPro || ThreadType == eThreadType.SystemThread)
				throw new NotSupportedException ();

			if (ThreadState == eThreadState.ThreadCreated)
				throw new ThreadStateException ();

			if (millisecondsTimeout == Timeout.Infinite)
				{
				Join ();
				return true;
				}

			if (CurrentThread == this)
				return false;

			var priority = CurrentThread.Priority;
			CurrentThread.Priority = Priority;

			int sleepTime = 0;
			var endtime = DateTime.Now.AddMilliseconds (millisecondsTimeout);
			try
				{
				while (DateTime.Now < endtime)
					{
					if (ThreadState != eThreadState.ThreadRunning)
						return true;

					Sleep (sleepTime++);
					}

				return false;
				}
			finally
				{
				CurrentThread.Priority = priority;
				}
			}

		private static PropertyInfo propertyInfoNativeIsBackground;

		public bool IsBackground
			{
			get
				{
				if (propertyInfoNativeIsBackground == null)
					propertyInfoNativeIsBackground = TypeNativeThread.GetPropertyEx ("IsBackground");

				return (bool)propertyInfoNativeIsBackground.GetValue (NativeThreadObject, null);
				}
			set
				{
				if (propertyInfoNativeIsBackground == null)
					propertyInfoNativeIsBackground = TypeNativeThread.GetPropertyEx ("IsBackground");

				propertyInfoNativeIsBackground.SetValue (NativeThreadObject, value, null);
				}
			}

		public void Sleep (int millisecondsTimeout)
			{
			CrestronEnvironment.Sleep (millisecondsTimeout);
			}

		public override int GetHashCode ()
			{
			return ManagedThreadId.GetHashCode ();
			}

		public override bool Equals (object obj)
			{
			var thread = obj as ThreadImpl;
			if (thread == null)
				return false;

			return ManagedThreadId == thread.ManagedThreadId;
			}

		public static bool operator == (ThreadImpl a, ThreadImpl b)
			{
			if (a == null)
				return b == null;

			if (b == null)
				return false;

			return a.ManagedThreadId == b.ManagedThreadId;
			}

		public static bool operator != (ThreadImpl a, ThreadImpl b)
			{
			if (a == null)
				return b != null;

			if (b == null)
				return true;

			return a.ManagedThreadId != b.ManagedThreadId;
			}

		private delegate LocalDataStoreSlot AllocateNamedDataSlotDelegate (string name);

		private static AllocateNamedDataSlotDelegate m_allocateNamedDataSlotDelegate;
		public static LocalDataStoreSlot AllocateNamedDataSlot (string name)
			{
			if (m_allocateNamedDataSlotDelegate == null)
				m_allocateNamedDataSlotDelegate =
					(AllocateNamedDataSlotDelegate)Delegate.CreateDelegate (typeof(AllocateNamedDataSlotDelegate), null, TypeNativeThread.GetMethod ("AllocateNamedDataSlot"));

			return m_allocateNamedDataSlotDelegate (name);
			}

		private delegate LocalDataStoreSlot AllocateDataSlotDelegate ();

		private static AllocateDataSlotDelegate m_allocateDataSlotDelegate;
		public static LocalDataStoreSlot AllocateDataSlot ()
			{
			if (m_allocateDataSlotDelegate == null)
				m_allocateDataSlotDelegate =
					(AllocateDataSlotDelegate)Delegate.CreateDelegate (typeof(AllocateDataSlotDelegate), null, TypeNativeThread.GetMethod ("AllocateDataSlot"));

			return m_allocateDataSlotDelegate ();
			}

		private delegate void FreeNamedDataSlotDelegate (string name);

		private static FreeNamedDataSlotDelegate m_freeNamedDataSlotDelegate;
		public static void FreeNamedDataSlot (string name)
			{
			if (m_freeNamedDataSlotDelegate == null)
				m_freeNamedDataSlotDelegate =
					(FreeNamedDataSlotDelegate)Delegate.CreateDelegate (typeof(FreeNamedDataSlotDelegate), null, TypeNativeThread.GetMethod ("FreeNamedDataSlot"));

			m_freeNamedDataSlotDelegate (name);
			}

		private delegate LocalDataStoreSlot GetNamedDataSlotDelegate (string name);

		private static GetNamedDataSlotDelegate m_getNamedDataSlotDelegate;
		public static LocalDataStoreSlot GetNamedDataSlot (string name)
			{
			if (m_getNamedDataSlotDelegate == null)
				m_getNamedDataSlotDelegate =
					(GetNamedDataSlotDelegate)Delegate.CreateDelegate (typeof(GetNamedDataSlotDelegate), null, TypeNativeThread.GetMethod ("GetNamedDataSlot"));

			return m_getNamedDataSlotDelegate (name);
			}

		private delegate object GetDataDelegate (LocalDataStoreSlot slot);

		private static GetDataDelegate m_getDataDelegate;

		public static object GetData (LocalDataStoreSlot slot)
			{
			if (m_getDataDelegate == null)
				m_getDataDelegate = (GetDataDelegate)Delegate.CreateDelegate (typeof(GetDataDelegate), null, TypeNativeThread.GetMethod ("GetData"));

			return m_getDataDelegate (slot);
			}

		private delegate void SetDataDelegate (LocalDataStoreSlot slot, object data);

		private static SetDataDelegate m_setDataDelegate;

		public static void SetData (LocalDataStoreSlot slot, object data)
			{
			if (m_setDataDelegate == null)
				m_setDataDelegate = (SetDataDelegate)Delegate.CreateDelegate (typeof(SetDataDelegate), null, TypeNativeThread.GetMethod ("SetData"));

			m_setDataDelegate (slot, data);
			}

		private delegate void MemoryBarrierDelegate ();

		private static MemoryBarrierDelegate m_memoryBarrierDelegate;
		public static void MemoryBarrier ()
			{
			if (m_memoryBarrierDelegate == null)
				m_memoryBarrierDelegate = (MemoryBarrierDelegate)Delegate.CreateDelegate (typeof(MemoryBarrierDelegate), null, TypeNativeThread.GetMethod ("MemoryBarrier"));

			m_memoryBarrierDelegate ();
			}
		}

	public class ThreadStateException : Exception
		{
		
		}
	}