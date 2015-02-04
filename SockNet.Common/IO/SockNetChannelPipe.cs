using System;
using System.Collections.Generic;
using System.Text;
using ArenaNet.SockNet.Common.Collections;

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A pipe in SockNet.
    /// </summary>
    public class SockNetChannelPipe
    {
        private IterableLinkedList<IDelegateReference> handlers = new IterableLinkedList<IDelegateReference>();

        private ISockNetChannel parent;

        /// <summary>
        /// Creates a pipe with the given parent.
        /// </summary>
        /// <param name="parent"></param>
        public SockNetChannelPipe(ISockNetChannel parent)
        {
            this.parent = parent;
        }

        /// <summary>
        /// Handles a message.
        /// </summary>
        public void HandleMessage(ref object message)
        {
            lock (handlers)
            {
                foreach (IDelegateReference delegateRef in handlers)
                {
                    if (delegateRef != null && delegateRef.DelegateType.IsAssignableFrom(message.GetType()))
                    {
                        object[] args = new object[2]
                                {
                                  parent,
                                  message
                                };

                        delegateRef.Delegate.DynamicInvoke(args);
                        message = args[1];
                    }
                }
            }
        }

        /// <summary>
        /// Adds a data handler {dataDelegate} before the given handler {previous}.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <param name="previous"></param>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool AddBefore<T, R>(OnDataDelegate<T> previous, OnDataDelegate<R> dataDelegate)
        {
            lock (handlers)
            {
                return handlers.AddBefore(new DelegateReference<T>(previous), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (handlers)
            {
                handlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (handlers)
            {
                handlers.AddLast(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds a data handler {dataDelegate} after the given handler {next}.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <param name="next"></param>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool AddAfter<T, R>(OnDataDelegate<T> next, OnDataDelegate<R> dataDelegate)
        {
            lock (handlers)
            {
                return handlers.AddAfter(new DelegateReference<T>(next), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Removes the given  data handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool Remove<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (handlers)
            {
                return handlers.Remove(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// The interface of a reference to a delegate.
        /// </summary>
        private interface IDelegateReference
        {
            Delegate Delegate { get; }

            Type DelegateType { get; }
        }

        /// <summary>
        /// A reference to a delegate.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private class DelegateReference<T> : IDelegateReference
        {
            public Delegate Delegate { get; private set; }

            public Type DelegateType { get; private set; }

            public DelegateReference(OnDataDelegate<T> dataDelegate)
            {
                Delegate = (Delegate)dataDelegate;
                DelegateType = typeof(T);
            }

            public override bool Equals(object obj)
            {
                if (obj == null || GetType() != obj.GetType())
                {
                    return false;
                }

                return Delegate.Equals(((IDelegateReference)obj).Delegate);
            }

            public override int GetHashCode()
            {
                return Delegate.GetHashCode();
            }
        }
    }
}
