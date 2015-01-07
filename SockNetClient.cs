using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Net.Sockets;

namespace ArenaNet.SockNet
{
    /// <summary>
    /// A simplification layer on top of System.Net.Sockets that enables users to implement custom stream handlers easier.
    /// </summary>
    public class SockNetClient
    {
        // our buffer for incomming packets
        private readonly byte[] buffer;

        // the raw socket
        private Socket socket;

        // an ssl stream that sits on top of the raw TCP stream
        private SslStream sslStream;

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
                    return !this.socket.Poll(1, SelectMode.SelectRead) || this.socket.Available != 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Returns true if this client uses SSL.
        /// </summary>
        public bool IsSsl { get; private set; }

        /// <summary>
        /// Returns true if this client is connected and the connection is encrypted.
        /// </summary>
        public bool IsConnectionEncrypted
        {
            get
            {
                return this.sslStream != null && this.sslStream.IsAuthenticated && this.sslStream.IsEncrypted;
            }
        }

        /// <summary>
        /// Gets or sets the certificate validation callback.
        /// </summary>
        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }

        /// <summary>
        /// Gets the IPEncpoint of this client.
        /// </summary>
        public IPEndPoint Endpoint { get; private set; }
        
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
        /// Creates a SockNet client with an endpoint, security settings, and optional buffer size.
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="useSsl"></param>
        /// <param name="bufferSize"></param>
        public SockNetClient(IPEndPoint endpoint, bool useSsl, int bufferSize = 10240)
        {
            this.Endpoint = endpoint;
            this.State = SockNetState.DISCONNECTED;
            this.IsSsl = useSsl;

            this.buffer = new byte[bufferSize];
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
        /// Attempts to connect to the configured IPEndpoint.
        /// </summary>
        public void Connect()
        {
            lock (this)
            {
                if (State != SockNetState.DISCONNECTED)
                {
                    throw new Exception("Must be disconnected.");
                }

                Logger(LogLevel.INFO, string.Format("Connecting to [{0}]...", Endpoint));

                State = SockNetState.CONNECTING;

                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.BeginConnect((EndPoint)Endpoint, new AsyncCallback(ConnectCallback), socket);
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

            Logger(LogLevel.INFO, string.Format("Connected to [{0}].", Endpoint));

            if (IsSsl)
            {
                Logger(LogLevel.INFO, string.Format("Authenticating SSL with [{0}]...", Endpoint));
                sslStream = new SslStream((Stream)new NetworkStream(socket), false, CertificateValidationCallback);

                sslStream.BeginAuthenticateAsClient(Endpoint.Address.ToString(), new AsyncCallback(SslAuthCallback), sslStream);
            }
            else
            {
                Logger(LogLevel.DEBUG, string.Format("Reading data from [{0}]...", Endpoint));
                State = SockNetState.CONNECTED;
                OnConnect(this);

                ((Socket)result.AsyncState).BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), socket);
            }
        }

        /// <summary>
        /// A callback the gets invoked after SSL auth completes.
        /// </summary>
        /// <param name="result"></param>
        private void SslAuthCallback(IAsyncResult result)
        {
            ((SslStream)result.AsyncState).EndAuthenticateAsClient(result);

            State = SockNetState.CONNECTED;

            Logger(LogLevel.INFO, string.Format("Authenticated SSL with [{0}].", Endpoint));
            Logger(LogLevel.DEBUG, string.Format("(SSL) Reading data from [{0}]...", Endpoint));

            OnConnect(this);

            ((Stream)result.AsyncState).BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReceiveCallback), sslStream);
        }

        /// <summary>
        /// A callback that gets invoked when we have incoming data in the pipe.
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveCallback(IAsyncResult result)
        {
            int count = -1;

            if (result.AsyncState is Socket)
            {
                count = ((Socket)result.AsyncState).EndReceive(result);
                
                Logger(LogLevel.DEBUG, string.Format("Received [{0}] bytes from [{1}].", count, Endpoint));
            }
            else if (result.AsyncState is SslStream)
            {
                count = ((Stream)result.AsyncState).EndRead(result);
                
                Logger(LogLevel.DEBUG, string.Format("(SSL) Received [{0}] bytes from [{1}].", count, Endpoint));
            }

            if (count > 0)
            {
                try
                {
                    byte[] numArray = new byte[count];
                    Buffer.BlockCopy((Array)buffer, 0, (Array)numArray, 0, count);

                    object obj = numArray;

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
                    if (result.AsyncState is SslStream)
                    {
                        ((Stream)result.AsyncState).BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReceiveCallback), sslStream);
                    }
                    else if (result.AsyncState is Socket)
                    {
                        ((Socket)result.AsyncState).BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), socket);
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

                    if (IsSsl)
                    {
                        Logger(LogLevel.DEBUG, string.Format("(SSL) Sending [{0}] bytes to [{1}]...", rawSendableData.Length, Endpoint));
                        sslStream.BeginWrite(rawSendableData, 0, rawSendableData.Length, new AsyncCallback(SendCallback), sslStream);
                    }
                    else
                    {
                        Logger(LogLevel.DEBUG, string.Format("Sending [{0}] bytes to [{1}]...", rawSendableData.Length, Endpoint));
                        socket.BeginSend(rawSendableData, 0, rawSendableData.Length, SocketFlags.None, new AsyncCallback(SendCallback), socket);
                    }
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
            if (result.AsyncState is Socket)
            {
                Logger(LogLevel.DEBUG, string.Format("Sent [{0}] bytes to [{1}].", ((Socket)result.AsyncState).EndSend(result), Endpoint));
            }
            else if (result.AsyncState is SslStream)
            {
                ((Stream)result.AsyncState).EndWrite(result);
                Logger(LogLevel.DEBUG, string.Format("(SSL) Sent data to [{1}].", Endpoint));
            }
            // else nothing - maybe log?
        }

        /// <summary>
        /// Disconnects from the IPEndpoint.
        /// </summary>
        public void Disconnect()
        {
            lock (this)
            {
                if (!(State == SockNetState.CONNECTED || State == SockNetState.CONNECTING) || socket == null)
                {
                    throw new Exception("Must be connected.");
                }

                Logger(LogLevel.INFO, string.Format("Disconnecting from [{0}]...", Endpoint));
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

            Logger(LogLevel.INFO, string.Format("Disconnected from [{0}].", Endpoint));
            State = SockNetState.DISCONNECTED;
            OnDisconnect(this);

            ((Socket)result.AsyncState).EndDisconnect(result);

            sslStream.Dispose();
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
