using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using ArenaNet.SockNet.Common;

namespace ArenaNet.SockNet.Client
{
    /// <summary>
    /// A channel that can be used to connect to remote endpoints
    /// </summary>
    public class ClientSockNetChannel : BaseSockNetChannel
    {
        private IPEndPoint connectEndpoint = null;

        private bool isSsl;
        private RemoteCertificateValidationCallback certificateValidationCallback;

        /// <summary>
        /// Creates a socknet client that can connect to the given address and port using a receive buffer size.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="bufferSize"></param>
        internal ClientSockNetChannel(IPAddress address, int port, int bufferSize = BaseSockNetChannel.DefaultBufferSize) : this(new IPEndPoint(address, port), bufferSize)
        {
        }

        /// <summary>
        /// Creates a SockNet client with an endpoint and a receive buffer size.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="bufferSize"></param>
        internal ClientSockNetChannel(IPEndPoint endpoint, int bufferSize = BaseSockNetChannel.DefaultBufferSize) : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), bufferSize)
        {
            this.connectEndpoint = endpoint;
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
        public WaitHandle ConnectWithTLS(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            return Connect(true, certificateValidationCallback);
        }

        /// <summary>
        /// Attempts to connect to the configured IPEndpoint.
        /// </summary>
        public WaitHandle Connect()
        {
            return Connect(false, null);
        }

        /// <summary>
        /// Connects to the configured endpoint and sets security variables.
        /// </summary>
        /// <param name="isSsl"></param>
        /// <param name="certificateValidationCallback"></param>
        /// <returns></returns>
        private WaitHandle Connect(bool isSsl, RemoteCertificateValidationCallback certificateValidationCallback)
        {
            if (TryFlaggingAs(SockNetState.CONNECTING, SockNetState.DISCONNECTED))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, "Connecting to [{0}]...", connectEndpoint);

                this.isSsl = isSsl;
                this.certificateValidationCallback = certificateValidationCallback;

                Socket.BeginConnect((EndPoint)connectEndpoint, new AsyncCallback(ConnectCallback), Socket);
            }
            else
            {
                throw new Exception("The client is already connected.");
            }

            return connectWaitHandle;
        }

        /// <summary>
        /// A callback that gets invoked after we connect.
        /// </summary>
        /// <param name="result"></param>
        private void ConnectCallback(IAsyncResult result)
        {
            if (TryFlaggingAs(SockNetState.CONNECTED, SockNetState.CONNECTING))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, "Connected to [{0}].", connectEndpoint);

                Socket.EndConnect(result);

                Attach(isSsl, certificateValidationCallback);
            }
            else
            {
                throw new Exception("The client isn't connecting.");
            }
        }
    }
}
