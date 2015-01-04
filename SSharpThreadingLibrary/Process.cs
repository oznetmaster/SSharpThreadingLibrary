using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SSMono.Diagnostics
	{
	public class Process
		{
		internal static Type TypeProcessNative = Type.GetType ("System" + ".Diagnostics.Process, System");
		private object InternalProcessObject;

		internal protected Process (object internalProcessObject)
			{
			InternalProcessObject = internalProcessObject;
			}

		public Process ()
			{
			InternalProcessObject = TypeProcessNative.GetConstructor (new Type [0]).Invoke (null);
			}

		private delegate object GetCurrentProcessDelegate ();

		private static GetCurrentProcessDelegate m_getCurrentProcessDelegate;

		//private static MethodInfo methodInfoGetCurrentProcess;
		public static Process GetCurrentProcess ()
			{
			if (m_getCurrentProcessDelegate == null)
				m_getCurrentProcessDelegate =
					(GetCurrentProcessDelegate)Delegate.CreateDelegate (typeof(GetCurrentProcessDelegate), null, TypeProcessNative.GetMethod ("GetCurrentProcess"));

			return new Process (m_getCurrentProcessDelegate ());

			/*
			if (methodInfoGetCurrentProcess == null)
				methodInfoGetCurrentProcess = TypeProcessNative.GetMethodEx ("GetCurrentProcess");

			return new Process (methodInfoGetCurrentProcess.Invoke (null, null));
			*/
			}

		private delegate int Get_IdDelegate ();

		private Get_IdDelegate m_get_IdDelegate;

		//private static PropertyInfo propertyInfoId;

		public int Id
			{
			get
				{
				if (m_get_IdDelegate == null)
					m_get_IdDelegate =
						(Get_IdDelegate)Delegate.CreateDelegate (typeof(Get_IdDelegate), InternalProcessObject, TypeProcessNative.GetProperty ("Id").GetGetMethod ());

				return m_get_IdDelegate ();

				/*
				if (propertyInfoId == null)
					propertyInfoId = TypeProcessNative.GetPropertyEx ("Id");

				return (int)propertyInfoId.GetValue (InternalProcessObject, null);
				*/
				}
			}

		}
	}