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

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A default base implementation of the channel handler that ignores all events.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DefaultSockNetChannelHandler<T> : SockNetChannelIncomingHandler<T>, SockNetChannelOutgoingHandler<T>, SockNetChannelHandler
    {
        /// <summary>
        /// Invoked when the channel opens.
        /// </summary>
        /// <param name="channel">the channel that opened</param>
        public virtual void OnOpen(ISockNetChannel channel)
        {
            // noop
        }

        /// <summary>
        /// Invoked when the channel closes.
        /// </summary>
        /// <param name="channel">the channel that closed</param>
        public virtual void OnClose(ISockNetChannel channel)
        {
            // noop
        }

        /// <summary>
        /// Invoked when there is incoming data on the given channel.
        /// </summary>
        /// <param name="channel">the channel with the data</param>
        /// <param name="data">the data</param>
        public virtual void OnIncomingData(ISockNetChannel channel, ref T data)
        {
            // noop
        }

        /// <summary>
        /// Invoked when there is outgoing data on the given channel.
        /// </summary>
        /// <param name="channel">the channel with the data</param>
        /// <param name="data">the data</param>
        public virtual void OnOutgoingData(ISockNetChannel channel, ref T data)
        {
            // noop
        }
    }
}
