/*
 * Copyright 2015 ArenaNet, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this 
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * 	 http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under 
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF 
 * ANY KIND, either express or implied. See the License for the specific language governing 
 * permissions and limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Text;
using ArenaNet.Medley.Collections;

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A pipe in SockNet.
    /// </summary>
    public class SockNetChannelPipe
    {
        /// <summary>
        /// A delegate that is used for notifying when a channel is open.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="channel"></param>
        public delegate void OnOpenedDelegate(ISockNetChannel channel);

        /// <summary>
        /// A delegate that is used for notifying when a channel is open.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="channel"></param>
        public delegate void OnClosedDelegate(ISockNetChannel channel);

        /// <summary>
        /// A delegate that is used for incoming and outgoing data.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        public delegate void OnDataDelegate<T>(ISockNetChannel channel, ref T data);

        private IterableLinkedList<OnOpenedDelegate> openedHandlers = new IterableLinkedList<OnOpenedDelegate>();
        private IterableLinkedList<OnClosedDelegate> closedHandlers = new IterableLinkedList<OnClosedDelegate>();

        private IterableLinkedList<IDelegateReference> incomingHandlers = new IterableLinkedList<IDelegateReference>();
        private IterableLinkedList<IDelegateReference> outgoingHandlers = new IterableLinkedList<IDelegateReference>();

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
        /// Clones this pipe and sets the given parent.
        /// </summary>
        /// <param name="newParent"></param>
        /// <returns></returns>
        public SockNetChannelPipe Clone(ISockNetChannel newParent)
        {
            SockNetChannelPipe newPipe = new SockNetChannelPipe(newParent);

            lock (openedHandlers)
            {
                foreach (OnOpenedDelegate del in openedHandlers)
                {
                    newPipe.openedHandlers.AddLast(del);
                }
            }

            lock (closedHandlers)
            {
                foreach (OnClosedDelegate del in closedHandlers)
                {
                    newPipe.closedHandlers.AddLast(del);
                }
            }

            lock (incomingHandlers)
            {
                foreach (IDelegateReference del in incomingHandlers)
                {
                    newPipe.incomingHandlers.AddLast(del);
                }
            }

            lock (outgoingHandlers)
            {
                foreach (IDelegateReference del in outgoingHandlers)
                {
                    newPipe.outgoingHandlers.AddLast(del);
                }
            }

            return newPipe;
        }

        /// <summary>
        /// Handles opened channel.
        /// </summary>
        public void HandleOpened()
        {
            lock (openedHandlers)
            {
                foreach (OnOpenedDelegate delegateRef in openedHandlers)
                {
                    try
                    {
                        if (delegateRef != null)
                        {
                            delegateRef(parent);
                        }
                    }
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, parent, "Pipe opened invokation failed on: " + delegateRef.Target + "." + delegateRef.Method, e);
                    }
                }
            }
        }

        /// <summary>
        /// Handles opened channel.
        /// </summary>
        public void HandleClosed()
        {
            lock (closedHandlers)
            {
                foreach (OnClosedDelegate delegateRef in closedHandlers)
                {
                    try
                    {
                        if (delegateRef != null)
                        {
                            delegateRef(parent);
                        }
                    }
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, parent, "Pipe closed invokation failed on: " + delegateRef.Target + "." + delegateRef.Method, e);
                    }
                }
            }
        }

        /// <summary>
        /// Handles outgoing data.
        /// </summary>
        public void HandleOutgoingData(ref object message)
        {
            lock (outgoingHandlers)
            {
                foreach (IDelegateReference delegateRef in outgoingHandlers)
                {
                    try
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
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, parent, "Pipe outgoing invokation failed on: " + delegateRef.Delegate.Target + "." + delegateRef.Delegate.Method, e);
                    }
                }
            }
        }

        /// <summary>
        /// Handles incoming data.
        /// </summary>
        public void HandleIncomingData(ref object message)
        {
            lock (incomingHandlers)
            {
                foreach (IDelegateReference delegateRef in incomingHandlers)
                {
                    try
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
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, parent, "Pipe incoming invokation failed on: " + delegateRef.Delegate.Target + "." + delegateRef.Delegate.Method, e);
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
        public bool AddOpenedBefore(OnOpenedDelegate previous, OnOpenedDelegate dataDelegate)
        {
            lock (openedHandlers)
            {
                return openedHandlers.AddBefore(previous, dataDelegate);
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddOpenedFirst(OnOpenedDelegate dataDelegate)
        {
            lock (openedHandlers)
            {
                openedHandlers.AddFirst(dataDelegate);
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddOpenedLast(OnOpenedDelegate dataDelegate)
        {
            lock (openedHandlers)
            {
                openedHandlers.AddLast(dataDelegate);
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
        public bool AddOpenedAfter(OnOpenedDelegate next, OnOpenedDelegate dataDelegate)
        {
            lock (openedHandlers)
            {
                return openedHandlers.AddAfter(next, dataDelegate);
            }
        }

        /// <summary>
        /// Removes the given handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool RemoveOpened(OnOpenedDelegate dataDelegate)
        {
            lock (openedHandlers)
            {
                return openedHandlers.Remove(dataDelegate);
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
        public bool AddClosedBefore(OnClosedDelegate previous, OnClosedDelegate dataDelegate)
        {
            lock (closedHandlers)
            {
                return closedHandlers.AddBefore(previous, dataDelegate);
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddClosedFirst(OnClosedDelegate dataDelegate)
        {
            lock (closedHandlers)
            {
                closedHandlers.AddFirst(dataDelegate);
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddClosedLast(OnClosedDelegate dataDelegate)
        {
            lock (closedHandlers)
            {
                closedHandlers.AddLast(dataDelegate);
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
        public bool AddClosedAfter(OnClosedDelegate next, OnClosedDelegate dataDelegate)
        {
            lock (closedHandlers)
            {
                return closedHandlers.AddAfter(next, dataDelegate);
            }
        }

        /// <summary>
        /// Removes the given handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool RemoveClosed(OnClosedDelegate dataDelegate)
        {
            lock (closedHandlers)
            {
                return closedHandlers.Remove(dataDelegate);
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
        public bool AddOutgoingBefore<T, R>(OnDataDelegate<T> previous, OnDataDelegate<R> dataDelegate)
        {
            lock (outgoingHandlers)
            {
                return outgoingHandlers.AddBefore(new DelegateReference<T>(previous), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddOutgoingFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingHandlers)
            {
                outgoingHandlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddOutgoingLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingHandlers)
            {
                outgoingHandlers.AddLast(new DelegateReference<T>(dataDelegate));
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
        public bool AddOutgoingAfter<T, R>(OnDataDelegate<T> next, OnDataDelegate<R> dataDelegate)
        {
            lock (outgoingHandlers)
            {
                return outgoingHandlers.AddAfter(new DelegateReference<T>(next), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Removes the given  data handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool RemoveOutgoing<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingHandlers)
            {
                return outgoingHandlers.Remove(new DelegateReference<T>(dataDelegate));
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
        public bool AddIncomingBefore<T, R>(OnDataDelegate<T> previous, OnDataDelegate<R> dataDelegate)
        {
            lock (incomingHandlers)
            {
                return incomingHandlers.AddBefore(new DelegateReference<T>(previous), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddIncomingFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingHandlers)
            {
                incomingHandlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the  data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddIncomingLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingHandlers)
            {
                incomingHandlers.AddLast(new DelegateReference<T>(dataDelegate));
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
        public bool AddIncomingAfter<T, R>(OnDataDelegate<T> next, OnDataDelegate<R> dataDelegate)
        {
            lock (incomingHandlers)
            {
                return incomingHandlers.AddAfter(new DelegateReference<T>(next), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Removes the given handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool RemoveIncoming<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingHandlers)
            {
                return incomingHandlers.Remove(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds the given handler first.
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void AddFirst<T>(SockNetChannelHandler handler)
        {
            lock (openedHandlers)
            {
                openedHandlers.AddFirst(handler.OnOpen);
            }

            lock (closedHandlers)
            {
                closedHandlers.AddFirst(handler.OnClose);
            }

            if (handler is SockNetChannelIncomingHandler<T>)
            {
                lock (incomingHandlers)
                {
                    incomingHandlers.AddFirst(new DelegateReference<T>(((SockNetChannelIncomingHandler<T>)handler).OnIncomingData));
                }
            }

            if (handler is SockNetChannelOutgoingHandler<T>)
            {
                lock (outgoingHandlers)
                {
                    incomingHandlers.AddFirst(new DelegateReference<T>(((SockNetChannelOutgoingHandler<T>)handler).OnOutgoingData));
                }
            }
        }

        /// <summary>
        /// Adds the given handler last.
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void AddLast<T>(SockNetChannelHandler handler)
        {
            lock (openedHandlers)
            {
                openedHandlers.AddLast(handler.OnOpen);
            }

            lock (closedHandlers)
            {
                closedHandlers.AddLast(handler.OnClose);
            }

            if (handler is SockNetChannelIncomingHandler<T>)
            {
                lock (incomingHandlers)
                {
                    incomingHandlers.AddLast(new DelegateReference<T>(((SockNetChannelIncomingHandler<T>)handler).OnIncomingData));
                }
            }

            if (handler is SockNetChannelOutgoingHandler<T>)
            {
                lock (outgoingHandlers)
                {
                    incomingHandlers.AddLast(new DelegateReference<T>(((SockNetChannelOutgoingHandler<T>)handler).OnOutgoingData));
                }
            }
        }

        /// <summary>
        /// Removes this handler from the pipe.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handler"></param>
        public void Remove<T>(SockNetChannelHandler handler)
        {
            lock (openedHandlers)
            {
                openedHandlers.Remove(handler.OnOpen);
            }

            lock (closedHandlers)
            {
                closedHandlers.Remove(handler.OnClose);
            }

            if (handler is SockNetChannelIncomingHandler<T>)
            {
                lock (incomingHandlers)
                {
                    incomingHandlers.Remove(new DelegateReference<T>(((SockNetChannelIncomingHandler<T>)handler).OnIncomingData));
                }
            }

            if (handler is SockNetChannelOutgoingHandler<T>)
            {
                lock (outgoingHandlers)
                {
                    incomingHandlers.Remove(new DelegateReference<T>(((SockNetChannelOutgoingHandler<T>)handler).OnOutgoingData));
                }
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
