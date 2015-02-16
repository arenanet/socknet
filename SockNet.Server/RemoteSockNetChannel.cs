using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Server
{
    /// <summary>
    /// States for this socknet channel.
    /// </summary>
    public enum RemoteSockNetChannelState
    {
        CONNECTED,
        DISCONNECTING,
        DISCONNECTED
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
                    return RemoteSockNetChannelState.CONNECTED.Equals(State) && (!this.Socket.Poll(1, SelectMode.SelectRead) || this.Socket.Available != 0);
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
        internal RemoteSockNetChannel(ServerSockNetChannel parent, Socket socket, ObjectPool<byte[]> bufferPool)
            : base(socket, bufferPool)
        {
            this.parent = parent;

            this.Pipe = parent.Pipe.Clone(this);

            this.State = RemoteSockNetChannelState.DISCONNECTED;

            if (parent.IsSsl)
            {
                AttachAsSslServer(parent.CertificateValidationCallback, parent.ServerCertificate).OnFulfilled += OnAttached;
            }
            else
            {
                Attach().OnFulfilled += OnAttached;
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
            promise.OnFulfilled -= OnAttached;

            Pipe.HandleOpened();

            State = RemoteSockNetChannelState.CONNECTED;
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

            if (TryFlaggingAs(RemoteSockNetChannelState.DISCONNECTING, RemoteSockNetChannelState.CONNECTED))
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
            if (TryFlaggingAs(RemoteSockNetChannelState.DISCONNECTED, RemoteSockNetChannelState.DISCONNECTING))
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
