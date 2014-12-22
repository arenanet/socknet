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
        private readonly byte[] buffer;

        private Socket socket;
        private SslStream sslStream;

        private IterableLinkedList<IDelegateReference> outgoingDataHandlers = new IterableLinkedList<IDelegateReference>();
        private IterableLinkedList<IDelegateReference> incomingDataHandlers = new IterableLinkedList<IDelegateReference>();

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

        public bool IsSsl { get; private set; }

        public bool IsConnectionEncrypted
        {
            get
            {
                return this.sslStream != null && this.sslStream.IsAuthenticated && this.sslStream.IsEncrypted;
            }
        }

        public RemoteCertificateValidationCallback CertificateValidationCallback { get; set; }

        public IPEndPoint Endpoint { get; private set; }

        public SockNetState State { private set; get; }

        public OnLogDelegate Logger;

        public event OnConnectedDelegate OnConnect;

        public event OnDisconnectedDelegate OnDisconnect;

        public SockNetClient(IPEndPoint endpoint, bool useSsl, int bufferSize = 10240)
        {
            this.Logger = (SockNetClient.OnLogDelegate)((level, text) => Console.WriteLine(System.Enum.GetName(level.GetType(), level) + " - " + text));
            this.Endpoint = endpoint;
            this.State = SockNetState.DISCONNECTED;
            this.IsSsl = useSsl;

            this.buffer = new byte[bufferSize];
        }

        public bool AddIncomingDataHandlerBefore<T, R>(OnDataDelegate<T> point, OnDataDelegate<R> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                return incomingDataHandlers.AddBefore(new DelegateReference<T>(point), new DelegateReference<R>(dataDelegate));
            }
        }

        public void AddIncomingDataHandlerFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                incomingDataHandlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        public void AddIncomingDataHandlerLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                incomingDataHandlers.AddLast(new DelegateReference<T>(dataDelegate));
            }
        }

        public bool AddIncomingDataHandlerAfter<T, R>(OnDataDelegate<T> point, OnDataDelegate<R> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                return incomingDataHandlers.AddAfter(new DelegateReference<T>(point), new DelegateReference<R>(dataDelegate));
            }
        }

        public bool RemoveIncomingDataHandler<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (incomingDataHandlers)
            {
                return incomingDataHandlers.Remove(new DelegateReference<T>(dataDelegate));
            }
        }

        public bool AddOutgoingDataHandlerBefore<T, R>(OnDataDelegate<T> point, OnDataDelegate<R> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                return outgoingDataHandlers.AddBefore(new DelegateReference<T>(point), new DelegateReference<R>(dataDelegate));
            }
        }

        public void AddOutgoingDataHandlerFirst<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                outgoingDataHandlers.AddFirst(new DelegateReference<T>(dataDelegate));
            }
        }

        public void AddOutgoingDataHandlerLast<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                outgoingDataHandlers.AddLast(new DelegateReference<T>(dataDelegate));
            }
        }

        public bool AddOutgoingDataHandlerAfter<T, R>(OnDataDelegate<T> point, OnDataDelegate<R> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                return outgoingDataHandlers.AddAfter(new DelegateReference<T>(point), new DelegateReference<R>(dataDelegate));
            }
        }

        public bool RemoveOutgoingDataHandler<T>(OnDataDelegate<T> dataDelegate)
        {
            lock (outgoingDataHandlers)
            {
                return outgoingDataHandlers.Remove(new DelegateReference<T>(dataDelegate));
            }
        }

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

        private void SslAuthCallback(IAsyncResult result)
        {
            ((SslStream)result.AsyncState).EndAuthenticateAsClient(result);

            State = SockNetState.CONNECTED;

            Logger(LogLevel.INFO, string.Format("Authenticated SSL with [{0}].", Endpoint));
            Logger(LogLevel.DEBUG, string.Format("(SSL) Reading data from [{0}]...", Endpoint));

            OnConnect(this);

            ((Stream)result.AsyncState).BeginRead(buffer, 0, buffer.Length, new AsyncCallback(ReceiveCallback), sslStream);
        }

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

        public enum LogLevel
        {
            DEBUG,
            INFO,
            ERROR,
        }

        public delegate void OnLogDelegate(LogLevel logLevel, string text);

        public delegate void OnConnectedDelegate(SockNetClient sockNet);

        public delegate void OnDisconnectedDelegate(SockNetClient sockNet);

        public delegate void OnDataDelegate<T>(SockNetClient sockNet, ref T data);

        public enum SockNetState
        {
            DISCONNECTING,
            DISCONNECTED,
            CONNECTING,
            CONNECTED,
        }

        private interface IDelegateReference
        {
            Delegate Delegate { get; }

            Type DelegateType { get; }
        }

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
