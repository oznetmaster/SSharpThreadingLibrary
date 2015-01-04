using System;
using System.Collections;
using System.Collections.Generic;
using Crestron.SimplSharp;

#pragma warning disable 420

namespace ProcessHacker.Common.Threading
	{
	public class FastStack<T> : IEnumerable<T>
		{
		private volatile FastStackNode<T> _bottom;
		private int _count = 0;

		public int Count
			{
			get { return _count; }
			}

		public IEnumerator<T> GetEnumerator ()
			{
			FastStackNode<T> entry = _bottom;

			// Start the enumeration.
			while (entry != null)
				{
				yield return entry.Value;
				entry = entry.Next;
				}
			}

		IEnumerator IEnumerable.GetEnumerator ()
			{
			return ((IEnumerable<T>)this).GetEnumerator ();
			}

		public T Peek ()
			{
			FastStackNode<T> bottom = _bottom;

			if (bottom == null)
				throw new InvalidOperationException ("The stack is empty.");

			return bottom.Value;
			}

		public T Pop ()
			{
			// Atomically replace the bottom of the stack.
			while (true)
				{
				FastStackNode<T> bottom = _bottom;

				// If the bottom of the stack is null, the 
				// stack is empty.
				if (bottom == null)
					throw new InvalidOperationException ("The stack is empty.");

				// Try to replace the pointer.
				if (Interlocked.CompareExchange (ref _bottom, bottom.Next, bottom) == bottom)
					{
					// Success.
					Interlocked.Decrement (ref _count);
					return bottom.Value;
					}
				}
			}

		public void Push (T value)
			{
			var entry = new FastStackNode<T> {Value = value};

			// Atomically replace the bottom of the stack.
			while (true)
				{
				FastStackNode<T> bottom = _bottom;
				entry.Next = bottom;

				// Try to replace the pointer.
				if (Interlocked.CompareExchange (ref _bottom, entry, bottom) == bottom)
					{
					// Success.
					break;
					}
				}
			Interlocked.Increment (ref _count);
			}

		private class FastStackNode<U>
			{
			public FastStackNode<U> Next;
			public U Value;
			}
		}
	}