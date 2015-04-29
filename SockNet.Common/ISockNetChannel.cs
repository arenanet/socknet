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
using System.Net;
using System.Threading;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// Globals for SockNet.
    /// </summary>
    public static class SockNetChannelGlobals
    {
        public const int DefaultBufferSize = 1024;

        public static readonly ObjectPool<byte[]> GlobalBufferPool = new ObjectPool<byte[]>(() => { return new byte[DefaultBufferSize]; });
    }

    /// <summary>
    /// A SockNet channel.
    /// </summary>
    public interface ISockNetChannel
    {
        /// <summary>
        /// The SockNetPipe.
        /// </summary>
        SockNetChannelPipe Pipe { get; }

        /// <summary>
        /// The buffer pool
        /// </summary>
        ObjectPool<byte[]> BufferPool { get; }

        /// <summary>
        /// The remote endpoint
        /// </summary>
        IPEndPoint RemoteEndpoint { get; }

        /// <summary>
        /// The local endpoint
        /// </summary>
        IPEndPoint LocalEndpoint { get; }

        /// <summary>
        /// Adds a module into this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        ISockNetChannel AddModule(ISockNetChannelModule module);

        /// <summary>
        /// Removes a module from this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        ISockNetChannel RemoveModule(ISockNetChannelModule module);

        /// <summary>
        /// Returns true if the module is active in this channel;
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        bool HasModule(ISockNetChannelModule module);
        
        /// <summary>
        /// Returns true if this channel is active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Gets the current state
        /// </summary>
        Enum State { get; }

        /// <summary>
        /// Sends the given message to this channel
        /// </summary>
        /// <param name="data"></param>
        Promise<ISockNetChannel> Send(object data);

        /// <summary>
        /// Closes this channel.
        /// </summary>
        /// <returns></returns>
        Promise<ISockNetChannel> Close();
    }
}
