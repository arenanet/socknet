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
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using ArenaNet.SockNet.Common;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;

namespace ArenaNet.SockNet.Server
{
    /// <summary>
    /// States for this socknet channel.
    /// </summary>
    public enum RemoteSockNetChannelState
    {
        Connected,
        Disconnecting,
        Disconnected
    }

    /// <summary>
    /// A channel that is used for tracking remote clients.
    /// </summary>
    public class RemoteSockNetChannel : BaseSockNetChannel<RemoteSockNetChannelState>
    {
        private ServerSockNetChannel parent;

        /// <summary>
        /// Returns true if this channel is active.
        /// </summary>
        public override bool IsActive { get { return IsConnected; } }

        /// <summary>
        /// Returns true if this socket is connected.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    return RemoteSockNetChannelState.Connected.Equals(State) && (!this.Socket.Poll(1, SelectMode.SelectRead) || this.Socket.Available != 0);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Creates a SockNet remote client with an endpoint and a receive buffer pool.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        internal RemoteSockNetChannel(ServerSockNetChannel parent, Socket socket, ObjectPool<byte[]> bufferPool, ICollection<ISockNetChannelModule> modules)
            : base(socket, bufferPool)
        {
            this.parent = parent;

            this.Pipe = parent.Pipe.Clone(this, false);

            foreach (ISockNetChannelModule module in modules)
            {
                this.AddModule(module);
            }

            this.State = RemoteSockNetChannelState.Disconnected;

            if (parent.IsSsl)
            {
                AttachAsSslServer(parent.CertificateValidationCallback, parent.ServerCertificate).OnFulfilled = OnAttached;
            }
            else
            {
                Attach().OnFulfilled = OnAttached;
            }
        }

        /// <summary>
        /// Invoked when the channel has sucessfully attached.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="error"></param>
        /// <param name="promise"></param>
        private void OnAttached(ISockNetChannel channel, Exception error, Promise<ISockNetChannel> promise)
        {
            promise.OnFulfilled = null;

            Pipe.HandleOpened();

            State = RemoteSockNetChannelState.Connected;
        }

        /// <summary>
        /// Specify whether this client should use the Nagle algorithm.
        /// </summary>
        /// <param name="noDelay"></param>
        /// <returns></returns>
        public RemoteSockNetChannel WithNoDelay(bool noDelay)
        {
            Socket.NoDelay = noDelay;

            return this;
        }

        /// <summary>
        /// Specify the packet TTL value.
        /// </summary>
        /// <param name="ttl"></param>
        /// <returns></returns>
        public RemoteSockNetChannel WithTtl(short ttl)
        {
            Socket.Ttl = ttl;

            return this;
        }

        /// <summary>
        /// Disconnects from the IPEndpoint.
        /// </summary>
        public Promise<ISockNetChannel> Disconnect()
        {
            Promise<ISockNetChannel> promise = new Promise<ISockNetChannel>();

            if (TryFlaggingAs(RemoteSockNetChannelState.Disconnecting, RemoteSockNetChannelState.Connected))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Disconnecting from [{0}]...", RemoteEndpoint);

                Socket.BeginDisconnect(true, new AsyncCallback(DisconnectCallback), promise);
            }
            else
            {
                parent.RemoteChannelDisconnected(this);

                promise.CreateFulfiller().Fulfill(this);
            }

            return promise;
        }

        /// <summary>
        /// A callback that gets invoked after we disconnect from the IPEndpoint.
        /// </summary>
        /// <param name="result"></param>
        private void DisconnectCallback(IAsyncResult result)
        {
            if (TryFlaggingAs(RemoteSockNetChannelState.Disconnected, RemoteSockNetChannelState.Disconnecting))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Disconnected from [{0}]", RemoteEndpoint);

                Socket.EndDisconnect(result);

                stream.Close();

                Pipe.HandleClosed();

                Promise<ISockNetChannel> promise = (Promise<ISockNetChannel>)result.AsyncState;

                parent.RemoteChannelDisconnected(this);

                promise.CreateFulfiller().Fulfill(this);
            }
            else
            {
                throw new Exception("Must be disconnecting.");
            }
        }

        /// <summary>
        /// Closes this channel.
        /// </summary>
        /// <returns></returns>
        public override Promise<ISockNetChannel> Close()
        {
            return Disconnect();
        }

        /// <summary>
        /// Always returns true.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public override bool ShouldInstallModule(ISockNetChannelModule module)
        {
            return true;
        }
    }
}
