using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.Collections;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Server
{
    /// <summary>
    /// States for this socknet channel.
    /// </summary>
    public class ServerSockNetChannelStates : SockNetStates
    {
        private static int NumberOfStates = 0;

        public static SockNetState BINDING = new SockNetState("BINDING", NumberOfStates++);
        public static SockNetState BOUND = new SockNetState("BOUND", NumberOfStates++);
        public static SockNetState CLOSING = new SockNetState("CLOSING", NumberOfStates++);
        public static SockNetState CLOSED = new SockNetState("CLOSED", NumberOfStates++);

        public static ServerSockNetChannelStates Instance { get; set; }
        static ServerSockNetChannelStates()
        {
            Instance = new ServerSockNetChannelStates(BINDING, BOUND, CLOSING, CLOSED);
        }

        public ServerSockNetChannelStates(params SockNetState[] states)
            : base(states)
        {
        }
    }

    /// <summary>
    /// A server/binding SockNetChannel.
    /// </summary>
    public class ServerSockNetChannel : BaseSockNetChannel
    {
        public bool IsSsl { get; private set; }
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; private set; }
        public X509Certificate ServerCertificate { get; private set; }

        public const int DefaultBacklog = 100;

        /// <summary>
        /// Returns true if this channel is active.
        /// </summary>
        public override bool IsActive { get { return State == ServerSockNetChannelStates.BOUND; } }

        private IPEndPoint bindEndpoint = null;
        private int backlog;

        private ConcurrentDictionary<IPEndPoint, RemoteSockNetChannel> remoteChannels = new ConcurrentDictionary<IPEndPoint, RemoteSockNetChannel>();

        /// <summary>
        /// Creates a socknet client that can connect to the given address and port using a receive buffer size.
        /// </summary>
        /// <param name="bindAddress"></param>
        /// <param name="bindPort"></param>
        /// <param name="bufferSize"></param>
        public ServerSockNetChannel(IPAddress bindAddress, int bindPort, ObjectPool<byte[]> bufferPool, int backlog = DefaultBacklog)
            : this(new IPEndPoint(bindAddress, bindPort), bufferPool, backlog)
        {
        }

        /// <summary>
        /// Creates a SockNet client with an endpoint and a receive buffer size.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="bufferSize"></param>
        public ServerSockNetChannel(IPEndPoint bindEndpoint, ObjectPool<byte[]> bufferPool, int backlog = DefaultBacklog)
            : base(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), bufferPool)
        {
            this.bindEndpoint = bindEndpoint;
            this.backlog = backlog;

            this.State = ServerSockNetChannelStates.CLOSED;
        }

        /// <summary>
        /// Specify whether this client should use the Nagle algorithm.
        /// </summary>
        /// <param name="noDelay"></param>
        /// <returns></returns>
        public ServerSockNetChannel WithNoDelay(bool noDelay)
        {
            this.Socket.NoDelay = noDelay;

            return this;
        }

        /// <summary>
        /// Specify the packet TTL value.
        /// </summary>
        /// <param name="ttl"></param>
        /// <returns></returns>
        public ServerSockNetChannel WithTtl(short ttl)
        {
            this.Socket.Ttl = ttl;

            return this;
        }

        /// <summary>
        /// Attempts to bind to the configured IPEndpoint and performs a TLS handshake for incoming clients.
        /// </summary>
        public Promise<ISockNetChannel> BindWithTLS(X509Certificate serverCertificate, RemoteCertificateValidationCallback certificateValidationCallback)
        {
            return BindInternal(true, serverCertificate, certificateValidationCallback);
        }

        /// <summary>
        /// Attempts to bind to the configured IPEndpoint.
        /// </summary>
        public Promise<ISockNetChannel> Bind()
        {
            return BindInternal(false, null, null);
        }

        /// <summary>
        /// Binds to the configured endpoint and sets security variables.
        /// </summary>
        /// <param name="isSsl"></param>
        /// <param name="certificateValidationCallback"></param>
        /// <returns></returns>
        private Promise<ISockNetChannel> BindInternal(bool isSsl, X509Certificate serverCertificate, RemoteCertificateValidationCallback certificateValidationCallback)
        {
            Promise<ISockNetChannel> promise = new Promise<ISockNetChannel>();

            if (TryFlaggingAs(ServerSockNetChannelStates.BINDING, ServerSockNetChannelStates.CLOSED))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Binding to [{0}]...", bindEndpoint);

                this.IsSsl = isSsl;
                this.CertificateValidationCallback = certificateValidationCallback;
                this.ServerCertificate = serverCertificate;

                Socket.Bind(bindEndpoint);
                Socket.Listen(backlog);

                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Bound to [{0}].", LocalEndpoint);

                Socket.BeginAccept(new AsyncCallback(AcceptCallback), Socket);

                this.State = ServerSockNetChannelStates.BOUND;

                promise.CreateFulfiller().Fulfill(this);
            }
            else
            {
                throw new Exception("The client is already connected.");
            }

            return promise;
        }

        /// <summary>
        /// A callback that gets invoked after we bind.
        /// </summary>
        /// <param name="result"></param>
        private void AcceptCallback(IAsyncResult result)
        {
            if (!IsActive)
            {
                return;
            }

            Socket remoteSocket = null;

            try
            {
                remoteSocket = Socket.EndAccept(result);

                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Accepted connection from: [{0}]", remoteSocket.RemoteEndPoint);
            }
            finally
            {
                Socket.BeginAccept(new AsyncCallback(AcceptCallback), Socket);
            }

            if (remoteSocket != null)
            {
                RemoteSockNetChannel channel = new RemoteSockNetChannel(this, remoteSocket, BufferPool);
                remoteChannels[channel.RemoteEndpoint] = channel;
            }
        }

        /// <summary>
        /// Closes this server.
        /// </summary>
        /// <returns></returns>
        public override Promise<ISockNetChannel> Close()
        {
            if (TryFlaggingAs(ServerSockNetChannelStates.CLOSING, ServerSockNetChannelStates.BOUND))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Unbinding from [{0}]...", LocalEndpoint);

                Socket.Close();

                this.State = ServerSockNetChannelStates.CLOSED;

                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Not bound to [{0}].", bindEndpoint);
            }

            return new Promise<ISockNetChannel>(this);
        }
    }
}
