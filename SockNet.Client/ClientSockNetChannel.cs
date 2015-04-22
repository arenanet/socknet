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
using System.Threading;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Client
{
    /// <summary>
    /// States for this socknet channel.
    /// </summary>
    public enum ClientSockNetChannelState
    {
        Connecting,
        Connected,
        Disconnecting,
        Disconnected
    }

    /// <summary>
    /// A channel that can be used to connect to remote endpoints
    /// </summary>
    public class ClientSockNetChannel : BaseSockNetChannel<ClientSockNetChannelState>
    {
        private IPEndPoint connectEndpoint = null;

        private bool isSsl;
        private RemoteCertificateValidationCallback certificateValidationCallback;

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
                    return ClientSockNetChannelState.Connected.Equals(State) && (!this.Socket.Poll(1, SelectMode.SelectRead) || this.Socket.Available != 0);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Creates a socknet client that can connect to the given address and port using the given buffer pool.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="bufferPool"></param>
        public ClientSockNetChannel(IPAddress address, int port, ObjectPool<byte[]> bufferPool)
            : this(new IPEndPoint(address, port), bufferPool)
        {
        }

        /// <summary>
        /// Creates a SockNet client with an endpoint and a buffer pool
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="bufferPool"></param>
        public ClientSockNetChannel(IPEndPoint endpoint, ObjectPool<byte[]> bufferPool) :
            base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), bufferPool)
        {
            this.connectEndpoint = endpoint;

            this.State = ClientSockNetChannelState.Disconnected;
        }

        /// <summary>
        /// Specify whether this client should use the Nagle algorithm.
        /// </summary>
        /// <param name="noDelay"></param>
        /// <returns></returns>
        public ClientSockNetChannel WithNoDelay(bool noDelay)
        {
            this.Socket.NoDelay = noDelay;

            return this;
        }

        /// <summary>
        /// Specify the packet TTL value.
        /// </summary>
        /// <param name="ttl"></param>
        /// <returns></returns>
        public ClientSockNetChannel WithTtl(short ttl)
        {
            this.Socket.Ttl = ttl;

            return this;
        }

        /// <summary>
        /// Attempts to connect to the configured IPEndpoint and performs a TLS handshake.
        /// </summary>
        public Promise<ISockNetChannel> ConnectWithTLS(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            return Connect(true, certificateValidationCallback);
        }

        /// <summary>
        /// Attempts to connect to the configured IPEndpoint.
        /// </summary>
        public Promise<ISockNetChannel> Connect()
        {
            return Connect(false, null);
        }

        /// <summary>
        /// Connects to the configured endpoint and sets security variables.
        /// </summary>
        /// <param name="isSsl"></param>
        /// <param name="certificateValidationCallback"></param>
        /// <returns></returns>
        private Promise<ISockNetChannel> Connect(bool isSsl, RemoteCertificateValidationCallback certificateValidationCallback)
        {
            Promise<ISockNetChannel> promise = new Promise<ISockNetChannel>();

            if (TryFlaggingAs(ClientSockNetChannelState.Connecting, ClientSockNetChannelState.Disconnected))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Connecting to [{0}]...", connectEndpoint);

                this.isSsl = isSsl;
                this.certificateValidationCallback = certificateValidationCallback;

                Socket.BeginConnect((EndPoint)connectEndpoint, new AsyncCallback(ConnectCallback), promise);
            }
            else
            {
                throw new Exception("The client is already connected.");
            }

            return promise;
        }

        /// <summary>
        /// A callback that gets invoked after we connect.
        /// </summary>
        /// <param name="result"></param>
        private void ConnectCallback(IAsyncResult result)
        {
            if (TryFlaggingAs(ClientSockNetChannelState.Connected, ClientSockNetChannelState.Connecting))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Connected to [{0}].", connectEndpoint);

                Socket.EndConnect(result);

                // create inital fake promise fulfilment delegate
                Promise<ISockNetChannel>.OnFulfilledDelegate onFulfilled = new Promise<ISockNetChannel>.OnFulfilledDelegate(
                    (ISockNetChannel value, Exception e, Promise<ISockNetChannel> promise) => { });

                onFulfilled = (ISockNetChannel value, Exception e, Promise<ISockNetChannel> promise) =>
                {
                    promise.OnFulfilled = null;

                    Pipe.HandleOpened();

                    ((Promise<ISockNetChannel>)result.AsyncState).CreateFulfiller().Fulfill(this);
                };

                if (isSsl)
                {
                    AttachAsSslClient(certificateValidationCallback).OnFulfilled = onFulfilled;
                }
                else
                {
                    Attach().OnFulfilled = onFulfilled;
                }
            }
            else
            {
                throw new Exception("The client isn't connecting.");
            }
        }

        /// <summary>
        /// Disconnects from the IPEndpoint.
        /// </summary>
        public Promise<ISockNetChannel> Disconnect()
        {
            Promise<ISockNetChannel> promise = new Promise<ISockNetChannel>();

            if (TryFlaggingAs(ClientSockNetChannelState.Disconnecting, ClientSockNetChannelState.Connected))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Disconnecting from [{0}]...", RemoteEndpoint);

                Socket.BeginDisconnect(true, new AsyncCallback(DisconnectCallback), promise);
            }
            else
            {
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
            if (TryFlaggingAs(ClientSockNetChannelState.Disconnected, ClientSockNetChannelState.Disconnecting))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Disconnected from [{0}]", RemoteEndpoint);

                Socket.EndDisconnect(result);

                stream.Close();

                Pipe.HandleClosed();

                Promise<ISockNetChannel> promise = (Promise<ISockNetChannel>)result.AsyncState;
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
    }
}
