using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Net.Sockets;
using ArenaNet.SockNet.IO;

namespace ArenaNet.SockNet
{
    /// <summary>
    /// A simplification layer on top of System.Net.Sockets that enables users to implement custom stream handlers easier.
    /// </summary>
    public class SockNetClient
    {
        // the pool for byte chunks
        private readonly ByteChunkPool chunkPool;
        public ByteChunkPool ChunkPool { get { return chunkPool; } }

        // the receive stream that will be sent to handlers to handle data
        private readonly ChunkedMemoryStream chunkedReceiveStream;

        // the raw socket
        private Socket socket;

        // the current stream
        private Stream stream;

        // data handlers for incomming and outgoing messages
        private IterableLinkedList<IDelegateReference> outgoingDataHandlers = new IterableLinkedList<IDelegateReference>();
        private IterableLinkedList<IDelegateReference> incomingDataHandlers = new IterableLinkedList<IDelegateReference>();

        /// <summary>
        /// Returns true if this socket is connected.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    return State == SockNetState.CONNECTED && (!this.socket.Poll(1, SelectMode.SelectRead) || this.socket.Available != 0);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns true if this client is connected and the connection is encrypted.
        /// </summary>
        public bool IsConnectionEncrypted
        {
            get
            {
                return this.stream != null && this.stream is SslStream && ((SslStream)this.stream).IsAuthenticated && ((SslStream)this.stream).IsEncrypted;
            }
        }

        /// <summary>
        /// Gets the remote IPEndPoint of this client.
        /// </summary>
        public IPEndPoint RemoteEndpoint { get; private set; }

        /// <summary>
        /// Gets the local IPEndPoint of this client.
        /// </summary>
        public IPEndPoint LocalEndpoint
        {
            get
            {
                return (IPEndPoint)socket.LocalEndPoint;
            }
        }
        
        /// <summary>
        /// Gets the current state of the client.
        /// </summary>
        public SockNetState State { get; private set; }

        /// <summary>
        /// Gets or sets the Logger of this client.
        /// </summary>
        public OnLogDelegate Logger
        {
            get
            {
                return _logger;
            }
            set
            {
                if (value == null)
                {
                    _logger = DEFAULT_LOGGER;
                }
                else
                {
                    _logger = value;
                }
            }
        }
        private static readonly OnLogDelegate DEFAULT_LOGGER = (SockNetClient.OnLogDelegate)((level, text) => Console.WriteLine(System.Enum.GetName(level.GetType(), level) + " - " + text));
        private OnLogDelegate _logger = DEFAULT_LOGGER;

        /// <summary>
        /// An event that will be triggered when this client is connected to its IPEndpoint.
        /// </summary>
        public event OnConnectedDelegate OnConnect;

        /// <summary>
        /// An event that will be triggered when this client is disconnected from its IPEndpoint.
        /// </summary>
        public event OnDisconnectedDelegate OnDisconnect;

        /// <summary>
        /// The current async receive result.
        /// </summary>
        private IAsyncResult currentAsyncReceive = null;

        private RemoteCertificateValidationCallback certificateValidationCallback;

        private bool isSsl = false;

        /// <summary>
        /// Creates a SockNet client with an endpoint, security settings, and optional buffer size.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="useSsl"></param>
        /// <param name="bufferSize"></param>
        public SockNetClient(IPEndPoint endpoint, int bufferSize = 1024)
        {
            this.RemoteEndpoint = endpoint;
            this.State = SockNetState.DISCONNECTED;

            this.chunkPool = new ByteChunkPool(bufferSize);
            this.chunkedReceiveStream = new ChunkedMemoryStream(chunkPool.Borrow, chunkPool.Return);

            this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        /// <summary>
        /// Adds a incoming data handler {dataDelegate} before the given handler {previous}.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <param name="previous"></param>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool AddIncomingDataHandlerBefore<T, R>(OnDataDelegate<T> previous, OnDataDelegate<R> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                return incomingDataHandlers.AddBefore(new DelegateReference<T>(previous), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the incoming data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddIncomingDataHandlerFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                incomingDataHandlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the incoming data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddIncomingDataHandlerLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                incomingDataHandlers.AddLast(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds a incoming data handler {dataDelegate} after the given handler {next}.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <param name="next"></param>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool AddIncomingDataHandlerAfter<T, R>(OnDataDelegate<T> next, OnDataDelegate<R> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                return incomingDataHandlers.AddAfter(new DelegateReference<T>(next), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Removes the given incoming data handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool RemoveIncomingDataHandler<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                return incomingDataHandlers.Remove(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds a outgoing data handler {dataDelegate} before the given handler {previous}.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <param name="previous"></param>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool AddOutgoingDataHandlerBefore<T, R>(OnDataDelegate<T> previous, OnDataDelegate<R> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                return outgoingDataHandlers.AddBefore(new DelegateReference<T>(previous), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the first handler in the outgoing data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddOutgoingDataHandlerFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                outgoingDataHandlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds this given data handler as the last handler in the incoming data handler chain.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        public void AddOutgoingDataHandlerLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                outgoingDataHandlers.AddLast(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Adds a outgoing data handler {dataDelegate} after the given handler {next}.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="R"></typeparam>
        /// <param name="next"></param>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool AddOutgoingDataHandlerAfter<T, R>(OnDataDelegate<T> next, OnDataDelegate<R> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                return outgoingDataHandlers.AddAfter(new DelegateReference<T>(next), new DelegateReference<R>(dataDelegate));
            }
        }

        /// <summary>
        /// Removes the given outgoing data handler.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataDelegate"></param>
        /// <returns></returns>
        public bool RemoveOutgoingDataHandler<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                return outgoingDataHandlers.Remove(new DelegateReference<T>(dataDelegate));
            }
        }

        /// <summary>
        /// Specify whether this client should use the Nagle algorithm.
        /// </summary>
        /// <param name="noDelay"></param>
        /// <returns></returns>
        public SockNetClient WithNoDelay(bool noDelay)
        {
            this.socket.NoDelay = noDelay;

            return this;
        }

        /// <summary>
        /// Specify the packet TTL value.
        /// </summary>
        /// <param name="ttl"></param>
        /// <returns></returns>
        public SockNetClient WithTtl(short ttl)
        {
            this.socket.Ttl = ttl;

            return this;
        }

        /// <summary>
        /// Attempts to connect to the configured IPEndpoint.
        /// </summary>
        public void Connect(bool isSsl = false, RemoteCertificateValidationCallback certificateValidationCallback = null)
        {
            lock (this)
            {
                if (State != SockNetState.DISCONNECTED)
                {
                    throw new Exception("Must be disconnected.");
                }

                this.isSsl = isSsl;
                this.certificateValidationCallback = certificateValidationCallback;

                Logger(LogLevel.INFO, string.Format("Connecting to [{0}]...", RemoteEndpoint));

                State = SockNetState.CONNECTING;

                socket.BeginConnect((EndPoint)RemoteEndpoint, new AsyncCallback(ConnectCallback), socket);
            }
        }

        /// <summary>
        /// A callback that gets invoked after we connect.
        /// </summary>
        /// <param name="result"></param>
        private void ConnectCallback(IAsyncResult result)
        {
            if (State != SockNetState.CONNECTING)
            {
                throw new Exception("Must be connecting.");
            }

            ((Socket)result.AsyncState).EndConnect(result);

            Logger(LogLevel.INFO, string.Format("Connected to [{0}].", RemoteEndpoint));

            stream = new NetworkStream(socket, true);

            if (isSsl)
            {
                EnableSsl();
            }
            else
            {
                Logger(LogLevel.DEBUG, string.Format("Reading data from [{0}]...", RemoteEndpoint));
                State = SockNetState.CONNECTED;

                byte[] buffer = chunkPool.Borrow();

                currentAsyncReceive = stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReceiveCallback), buffer);

                OnConnect(this);
            }
        }

        /// <summary>
        /// Enables SSL on this connection.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        private void EnableSsl()
        {
            Logger(LogLevel.INFO, string.Format("Authenticating SSL with [{0}]...", RemoteEndpoint));

            SslStream sslStream = new SslStream(stream, false, certificateValidationCallback);
            sslStream.BeginAuthenticateAsClient(RemoteEndpoint.Address.ToString(), new AsyncCallback(EnableSslCallback), sslStream);
        }

        /// <summary>
        /// A callback the gets invoked after SSL auth completes.
        /// </summary>
        /// <param name="result"></param>
        private void EnableSslCallback(IAsyncResult result)
        {
            SslStream sslStream = (SslStream)result.AsyncState;

            sslStream.EndAuthenticateAsClient(result);

            State = SockNetState.CONNECTED;

            Logger(LogLevel.INFO, string.Format("Authenticated SSL with [{0}].", RemoteEndpoint));
            Logger(LogLevel.DEBUG, string.Format("(SSL) Reading data from [{0}]...", RemoteEndpoint));

            this.stream = sslStream;

            byte[] buffer = chunkPool.Borrow();

            currentAsyncReceive = stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReceiveCallback), buffer);

            OnConnect(this);
        }

        /// <summary>
        /// A callback that gets invoked when we have incoming data in the pipe.
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveCallback(IAsyncResult result)
        {
            int count = stream.EndRead(result);

            byte[] buffer = (byte[])result.AsyncState;

            Logger(LogLevel.DEBUG, string.Format((result.AsyncState is SslStream ? "[SSL] " : "") + "Received [{0}] bytes from [{1}].", count, RemoteEndpoint));

            if (count > 0)
            {
                try
                {
                    long startingPosition = chunkedReceiveStream.Position;

                    chunkedReceiveStream.OfferChunk(buffer, 0, count);

                    chunkedReceiveStream.Position = startingPosition;

                    object obj = chunkedReceiveStream;

                    lock (incomingDataHandlers)
                    {
                        foreach (IDelegateReference delegateRef in incomingDataHandlers)
                        {
                            if (delegateRef != null && delegateRef.DelegateType.IsAssignableFrom(obj.GetType()))
                            {
                                object[] args = new object[2]
                                {
                                  this,
                                  obj
                                };

                                delegateRef.Delegate.DynamicInvoke(args);
                                obj = args[1];
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger(LogLevel.ERROR, ex.Message);
                }
                finally
                {
                    chunkedReceiveStream.Flush();

                    if (IsConnected)
                    {
                        try
                        {
                            buffer = chunkPool.Borrow();

                            currentAsyncReceive = stream.BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReceiveCallback), buffer);
                        }
                        catch (Exception)
                        {
                            Disconnect();
                        }
                    }
                }
            }
            else
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Sets the given message to the IPEndpoint.
        /// </summary>
        /// <param name="data"></param>
        public void Send(object data)
        {
            if (State != SockNetState.CONNECTED)
            {
                throw new Exception("Must be connected.");
            }

            object obj = data;

            try
            {
                lock (outgoingDataHandlers)
                {
                    foreach (IDelegateReference delegateRef in outgoingDataHandlers)
                    {
                        if (delegateRef != null && delegateRef.DelegateType.IsAssignableFrom(obj.GetType()))
                        {
                            object[] args = new object[2]
                            {
                                this,
                                obj
                            };

                            delegateRef.Delegate.DynamicInvoke(args);
                            obj = args[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger(LogLevel.ERROR, ex.Message);
            }
            finally
            {
                if (obj is byte[])
                {
                    byte[] rawSendableData = (byte[])obj;

                    Logger(LogLevel.DEBUG, string.Format((IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", rawSendableData.Length, RemoteEndpoint));
                    stream.BeginWrite(rawSendableData, 0, rawSendableData.Length, new AsyncCallback(SendCallback), stream);
                }
                else
                {
                    Logger(LogLevel.ERROR, "Unable to send object: " + obj);
                }
            }
        }

        /// <summary>
        /// A callback that gets invoked after a successful send.
        /// </summary>
        /// <param name="result"></param>
        private void SendCallback(IAsyncResult result)
        {
            ((Stream)result.AsyncState).EndWrite(result);
            Logger(LogLevel.DEBUG, string.Format((result.AsyncState is SslStream ? "[SSL] " : "") + "Sent data to [{0}].", RemoteEndpoint));
            // else nothing - maybe log?
        }

        /// <summary>
        /// Disconnects from the IPEndpoint.
        /// </summary>
        public void Disconnect()
        {
            if (!(State == SockNetState.CONNECTED || State == SockNetState.CONNECTING) || socket == null)
            {
                OnDisconnect(this);

                return;
            }

            lock (this)
            {
                Logger(LogLevel.INFO, string.Format("Disconnecting from [{0}]...", RemoteEndpoint));
                State = SockNetState.DISCONNECTING;
                socket.BeginDisconnect(true, new AsyncCallback(DisconnectCallback), socket);
            }
        }

        /// <summary>
        /// A callback that gets invoked after we disconnect from the IPEndpoint.
        /// </summary>
        /// <param name="result"></param>
        private void DisconnectCallback(IAsyncResult result)
        {
            if (State != SockNetState.DISCONNECTING)
            {
                throw new Exception("Must be disconnecting.");
            }

            Logger(LogLevel.INFO, string.Format("Disconnected from [{0}].", RemoteEndpoint));
            State = SockNetState.DISCONNECTED;

            ((Socket)result.AsyncState).EndDisconnect(result);

            stream.Close();

            OnDisconnect(this);
        }

        /// <summary>
        /// Log leves.
        /// </summary>
        public enum LogLevel
        {
            DEBUG,
            INFO,
            ERROR
        }

        /// <summary>
        /// A delegate that is used for logging.
        /// </summary>
        /// <param name="logLevel"></param>
        /// <param name="text"></param>
        public delegate void OnLogDelegate(LogLevel logLevel, string text);
        
        /// <summary>
        /// A delegate that is used for connect notifications.
        /// </summary>
        /// <param name="sockNet"></param>
        public delegate void OnConnectedDelegate(SockNetClient sockNet);

        /// <summary>
        /// A delegate that is used for disconnect notification.
        /// </summary>
        /// <param name="sockNet"></param>
        public delegate void OnDisconnectedDelegate(SockNetClient sockNet);

        /// <summary>
        /// A delegate that is used for incomming data.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="sockNet"></param>
        /// <param name="data"></param>
        public delegate void OnDataDelegate<T>(SockNetClient sockNet, ref T data);

        /// <summary>
        /// State of the SockNetClient
        /// </summary>
        public enum SockNetState
        {
            DISCONNECTING,
            DISCONNECTED,
            CONNECTING,
            CONNECTED,
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
