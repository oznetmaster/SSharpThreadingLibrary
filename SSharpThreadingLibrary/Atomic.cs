using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using System.Globalization;
using Wintellect.Threading;

namespace SSharp.Threading
	{
	public class Atomic<T> where T : struct, IConvertible
		{
		private int m_value;

		public T CompareAndExchange (T expected, T newval)
			{
			return
				(T)
				Convert.ChangeType (
				                    Interlocked.CompareExchange (ref m_value, newval.ToInt32 (CultureInfo.InvariantCulture), expected.ToInt32 (CultureInfo.InvariantCulture)),
				                    typeof(T), CultureInfo.InvariantCulture);
			}

		public static Atomic<T> FromValue (T value)
			{
			return new Atomic<T>
				{
				Value = value
				};
			}

		public T Exchange (T newVal)
			{
			return (T)Convert.ChangeType (Interlocked.Exchange (ref m_value, newVal.ToInt32 (CultureInfo.InvariantCulture)), typeof(T), CultureInfo.InvariantCulture);
			}

		public T Value
			{
			get { return (T)Convert.ChangeType (m_value, typeof(T), CultureInfo.InvariantCulture); }
			set { m_value = value.ToInt32 (CultureInfo.InvariantCulture); }
			}

		public bool Equals (Atomic<T> rhs)
			{
			return this.m_value == rhs.m_value;
			}

		public override bool Equals (object rhs)
			{
			return rhs is Atomic<T> && Equals ((Atomic<T>)rhs);
			}

		public override int GetHashCode ()
			{
			return m_value.GetHashCode ();
			}

		public static implicit operator T (Atomic<T> rhs)
			{
			return (T)Convert.ChangeType (rhs, typeof(T), CultureInfo.InvariantCulture);
			}

		public static implicit operator Atomic<T> (T rhs)
			{
			return FromValue (rhs);
			}

		public static bool operator == (Atomic<T> lhs, Atomic<T> rhs)
			{
			if (ReferenceEquals (lhs, null))
				return ReferenceEquals (rhs, null);
			return !ReferenceEquals (rhs, null) && lhs.m_value == rhs.m_value;
			}

		public static bool operator != (Atomic<T> lhs, Atomic<T> rhs)
			{
			return !(lhs == rhs);
			}

		public static bool operator < (Atomic<T> lhs, Atomic<T> rhs)
			{
			if (ReferenceEquals (lhs, null) || ReferenceEquals (rhs, null))
				throw new ArgumentNullException ();

			return lhs.m_value < rhs.m_value;
			}

		public static bool operator >= (Atomic<T> lhs, Atomic<T> rhs)
			{
			return !(lhs < rhs);
			}

		public static bool operator > (Atomic<T> lhs, Atomic<T> rhs)
			{
			if (ReferenceEquals (lhs, null) || ReferenceEquals (rhs, null))
				throw new ArgumentNullException ();

			return lhs.m_value > rhs.m_value;
			}

		public static bool operator <= (Atomic<T> lhs, Atomic<T> rhs)
			{
			return !(lhs > rhs);
			}

		public static Atomic<T> operator ++(Atomic<T> rhs)
			{
			Interlocked.Increment (ref rhs.m_value);

			return rhs;
			}

		public static Atomic<T> operator --(Atomic<T> rhs)
			{
			Interlocked.Decrement (ref rhs.m_value);

			return rhs;
			}

		public static Atomic<T> operator +(Atomic<T> lhs, int rhs)
			{
			InterlockedEx.Add (ref lhs.m_value, rhs);

			return lhs;
			}

		public static Atomic<T> operator -(Atomic<T> lhs, int rhs)
			{
			InterlockedEx.Add (ref lhs.m_value, -rhs);

			return lhs;
			}

		public static Atomic<T> operator |(Atomic<T> lhs, int rhs)
			{
			InterlockedEx.Or (ref lhs.m_value, -rhs);

			return lhs;
			}

		public static Atomic<T> operator &(Atomic<T> lhs, int rhs)
			{
			InterlockedEx.And (ref lhs.m_value, -rhs);

			return lhs;
			}

		public static Atomic<T> operator ^(Atomic<T> lhs, int rhs)
			{
			InterlockedEx.Xor (ref lhs.m_value, -rhs);

			return lhs;
			}
		}

	public class AtomicValue<T> where T : struct, IConvertible
		{
		private int m_value;

		public T CompareAndExchange (T expected, T newval)
			{
			return
				(T)
				Convert.ChangeType (
				                    Interlocked.CompareExchange (ref m_value, newval.ToInt32 (CultureInfo.InvariantCulture), expected.ToInt32 (CultureInfo.InvariantCulture)),
				                    typeof(T), CultureInfo.InvariantCulture);
			}

		public static AtomicValue<T> FromValue (T value)
			{
			return new AtomicValue<T>
				{
				Value = value
				};
			}

		public T Exchange (T newVal)
			{
			return (T)Convert.ChangeType (Interlocked.Exchange (ref m_value, newVal.ToInt32 (CultureInfo.InvariantCulture)), typeof(T), CultureInfo.InvariantCulture);
			}

		public T Value
			{
			get { return (T)Convert.ChangeType (m_value, typeof(T), CultureInfo.InvariantCulture); }
			set { m_value = value.ToInt32 (CultureInfo.InvariantCulture); }
			}

		public bool Equals (AtomicValue<T> rhs)
			{
			return this.m_value == rhs.m_value;
			}

		public override bool Equals (object rhs)
			{
			return rhs is AtomicValue<T> && Equals ((AtomicValue<T>)rhs);
			}

		public override int GetHashCode ()
			{
			return m_value.GetHashCode ();
			}

		public static implicit operator T (AtomicValue<T> rhs)
			{
			return (T)Convert.ChangeType (rhs, typeof(T), CultureInfo.InvariantCulture);
			}

		public static implicit operator AtomicValue<T> (T rhs)
			{
			return FromValue (rhs);
			}

		public static bool operator == (AtomicValue<T> lhs, AtomicValue<T> rhs)
			{
			if (ReferenceEquals (lhs, null))
				return ReferenceEquals (rhs, null);
			return !ReferenceEquals (rhs, null) && lhs.m_value == rhs.m_value;
			}

		public static bool operator != (AtomicValue<T> lhs, AtomicValue<T> rhs)
			{
			return !(lhs == rhs);
			}

		public static bool operator < (AtomicValue<T> lhs, AtomicValue<T> rhs)
			{
			if (ReferenceEquals (lhs, null) || ReferenceEquals (rhs, null))
				throw new ArgumentNullException ();

			return lhs.m_value < rhs.m_value;
			}

		public static bool operator >= (AtomicValue<T> lhs, AtomicValue<T> rhs)
			{
			return !(lhs < rhs);
			}

		public static bool operator > (AtomicValue<T> lhs, AtomicValue<T> rhs)
			{
			if (ReferenceEquals (lhs, null) || ReferenceEquals (rhs, null))
				throw new ArgumentNullException ();

			return lhs.m_value > rhs.m_value;
			}

		public static bool operator <= (AtomicValue<T> lhs, AtomicValue<T> rhs)
			{
			return !(lhs > rhs);
			}

		public static AtomicValue<T> operator ++(AtomicValue<T> rhs)
			{
			Interlocked.Increment (ref rhs.m_value);

			return rhs;
			}

		public static AtomicValue<T> operator --(AtomicValue<T> rhs)
			{
			Interlocked.Decrement (ref rhs.m_value);

			return rhs;
			}

		public static AtomicValue<T> operator +(AtomicValue<T> lhs, int rhs)
			{
			InterlockedEx.Add (ref lhs.m_value, rhs);

			return lhs;
			}

		public static AtomicValue<T> operator -(AtomicValue<T> lhs, int rhs)
			{
			InterlockedEx.Add (ref lhs.m_value, -rhs);

			return lhs;
			}

		public static AtomicValue<T> operator |(AtomicValue<T> lhs, int rhs)
			{
			InterlockedEx.Or (ref lhs.m_value, -rhs);

			return lhs;
			}

		public static AtomicValue<T> operator &(AtomicValue<T> lhs, int rhs)
			{
			InterlockedEx.And (ref lhs.m_value, -rhs);

			return lhs;
			}

		public static AtomicValue<T> operator ^(AtomicValue<T> lhs, int rhs)
			{
			InterlockedEx.Xor (ref lhs.m_value, -rhs);

			return lhs;
			}
	}
