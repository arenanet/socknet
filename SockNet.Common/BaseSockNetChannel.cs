using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Net.Sockets;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;
using ArenaNet.SockNet.Common.Collections;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// A simplification layer on top of System.Net.Sockets that enables users to implement custom stream handlers easier.
    /// </summary>
    public abstract class BaseSockNetChannel : ISockNetChannel
    {
        public const int DefaultBufferSize = 1024;

        // the pool for byte chunks
        private readonly ObjectPool<byte[]> bufferPool;
        public ObjectPool<byte[]> BufferPool { get { return bufferPool; } }

        // the receive stream that will be sent to handlers to handle data
        private readonly PooledMemoryStream chunkedReceiveStream;

        // the raw socket
        protected Socket Socket { get; private set; }

        // the installed modules
        protected ConcurrentDictionary<ISockNetChannelModule, bool> modules = new ConcurrentDictionary<ISockNetChannelModule, bool>();

        // the current stream
        protected Stream stream;

        // data handlers for incomming and outgoing messages
        public SockNetChannelPipe InPipe { get; private set; }
        public SockNetChannelPipe OutPipe { get; private set; }

        /// <summary>
        /// Returns true if this socket is connected.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                try
                {
                    return State == SockNetState.CONNECTED && (!this.Socket.Poll(1, SelectMode.SelectRead) || this.Socket.Available != 0);
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
        public IPEndPoint RemoteEndpoint
        {
            get
            {
                return (IPEndPoint)Socket.RemoteEndPoint;
            }
        }

        /// <summary>
        /// Gets the local IPEndPoint of this client.
        /// </summary>
        public IPEndPoint LocalEndpoint
        {
            get
            {
                return (IPEndPoint)Socket.LocalEndPoint;
            }
        }
        
        /// <summary>
        /// Gets the current state of the client.
        /// </summary>
        private int state = -1;
        public SockNetState State 
        {
            get
            {
                return (SockNetState)state;
            }
        }

        /// <summary>
        /// A wait handle that can be used to block a thread until this client connects.
        /// </summary>
        protected EventWaitHandle connectWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
        
        /// <summary>
        /// A wait handle that can be used to block a thread until this client disconnects.
        /// </summary>
        protected EventWaitHandle disconnectWaitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);

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
        protected IAsyncResult currentAsyncReceive = null;

        protected Semaphore streamWriteSemaphore = new Semaphore(1, 1);

        /// <summary>
        /// Creates a socknet client that can connect to the given address and port using a receive buffer size.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        /// <param name="bufferSize"></param>
        public BaseSockNetChannel(Socket socket, int bufferSize = DefaultBufferSize)
        {
            this.Socket = socket;

            this.connectWaitHandle.Reset();

            this.InPipe = new SockNetChannelPipe(this);
            this.OutPipe = new SockNetChannelPipe(this);
            
            this.state = (int)SockNetState.DISCONNECTED;

            this.bufferPool = new ObjectPool<byte[]>(() => { return new byte[bufferSize]; });
            this.chunkedReceiveStream = new PooledMemoryStream(bufferPool);
        }

        /// <summary>
        /// Adds a module into this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public ISockNetChannel AddModule(ISockNetChannelModule module)
        {
            if (modules.GetOrAdd(module, true))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, "Installing module: [{0}]", module);

                module.Install(this);
            }
            else
            {
                throw new Exception("Module [" + module + "] already installed.");
            }

            return this;
        }

        /// <summary>
        /// Removes a module from this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public ISockNetChannel RemoveModule(ISockNetChannelModule module)
        {
            bool value = true;

            if (modules.TryRemove(module, out value) && value)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, "Uninstalling module: [{0}]", module);

                module.Uninstall(this);
            }
            else
            {
                throw new Exception("Module [" + module + "] not installed.");
            }

            return this;
        }

        /// <summary>
        /// Flag this channel as connecting.
        /// </summary>
        /// <returns></returns>
        protected bool TryFlaggingAs(SockNetState state, SockNetState previous)
        {
            return Interlocked.CompareExchange(ref this.state, (int)state, (int)previous) == (int)previous;
        }

        /// <summary>
        /// A callback that gets invoked after we connect.
        /// </summary>
        /// <param name="result"></param>
        public void Attach(bool establishSsl, RemoteCertificateValidationCallback certificateValidationCallback = null)
        {
            stream = new NetworkStream(Socket, true);

            if (establishSsl)
            {
                EnableSsl(certificateValidationCallback);
            }
            else
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, "Reading data from [{0}]...", RemoteEndpoint);

                PooledObject<byte[]> buffer = bufferPool.Borrow();

                currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

                if (OnConnect != null)
                {
                    OnConnect(this);
                }

                connectWaitHandle.Set();
            }
        }

        /// <summary>
        /// Enables SSL on this connection.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        private void EnableSsl(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, "Authenticating SSL with [{0}]...", RemoteEndpoint);

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

            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, "Authenticated SSL with [{0}].", RemoteEndpoint);
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, "(SSL) Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

            if (OnConnect != null)
            {
                OnConnect(this);
            }

            connectWaitHandle.Set();
        }

        /// <summary>
        /// A callback that gets invoked when we have incoming data in the pipe.
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveCallback(IAsyncResult result)
        {
            int count = stream.EndRead(result);

            PooledObject<byte[]> buffer = (PooledObject<byte[]>)result.AsyncState;

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, (result.AsyncState is SslStream ? "[SSL] " : "") + "Received [{0}] bytes from [{1}].", count, RemoteEndpoint);

            if (count > 0)
            {
                try
                {
                    long startingPosition = chunkedReceiveStream.Position;

                    chunkedReceiveStream.OfferChunk(buffer, 0, count);

                    chunkedReceiveStream.Position = startingPosition;

                    object obj = chunkedReceiveStream;

                    InPipe.HandleMessage(ref obj);
                }
                catch (Exception ex)
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, ex.Message);
                }
                finally
                {
                    chunkedReceiveStream.Flush();

                    if (IsConnected)
                    {
                        try
                        {
                            buffer = bufferPool.Borrow();

                            currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);
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
                OutPipe.HandleMessage(ref obj);
            }
            catch (Exception ex)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, ex.Message);
            }
            finally
            {
                if (obj is byte[])
                {
                    byte[] rawSendableData = (byte[])obj;

                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", rawSendableData.Length, RemoteEndpoint);

                    if (streamWriteSemaphore.WaitOne(10000))
                    {
                        stream.BeginWrite(rawSendableData, 0, rawSendableData.Length, new AsyncCallback(SendCallback), stream);
                    }
                    else
                    {
                        throw new ThreadStateException("Unable to obtain lock to write message.");
                    }
                }
                else if (obj is Stream)
                {
                    Stream sendableStream = ((Stream)obj);
                    PooledObject<byte[]> sendableDataBuffer = bufferPool.Borrow();

                    sendableStream.BeginRead(sendableDataBuffer.Value, 
                        0,
                        (int)Math.Min(sendableDataBuffer.Value.Length, sendableStream.Length - sendableStream.Position), 
                        StreamSendCallback,
                        new StreamSendState() { stream = sendableStream, buffer = sendableDataBuffer });
                }
                else
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, "Unable to send object: {0}", obj);
                }
            }
        }

        /// <summary>
        /// A state class used between streamed sends.
        /// </summary>
        private class StreamSendState
        {
            public Stream stream;
            public PooledObject<byte[]> buffer;
        }

        /// <summary>
        /// A callback when streaming a send.
        /// </summary>
        /// <param name="result"></param>
        private void StreamSendCallback(IAsyncResult result)
        {
            StreamSendState state = ((StreamSendState)result.AsyncState);

            int bytesSending = state.stream.EndRead(result);

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", bytesSending, RemoteEndpoint);

            if (streamWriteSemaphore.WaitOne(10000))
            {
                stream.BeginWrite(state.buffer.Value, 0, bytesSending, new AsyncCallback(SendCallback), state.buffer);
            }
            else
            {
                throw new ThreadStateException("Unable to obtain lock to write message.");
            }

            if (state.stream.Position < state.stream.Length)
            {
                state.buffer = bufferPool.Borrow();

                state.stream.BeginRead(state.buffer.Value,
                    0,
                    (int)Math.Min(state.buffer.Value.Length, state.stream.Length - state.stream.Position),
                    StreamSendCallback,
                    state);
            }
            else
            {
                bufferPool.Return(state.buffer);
                state.stream.Close();
            }
        }

        /// <summary>
        /// A callback that gets invoked after a successful send.
        /// </summary>
        /// <param name="result"></param>
        private void SendCallback(IAsyncResult result)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, (result.AsyncState is SslStream ? "[SSL] " : "") + "Sent data to [{0}].", RemoteEndpoint);

            if (result.AsyncState is PooledObject<byte[]>)
            {
                ((PooledObject<byte[]>)result.AsyncState).Return();
            }

            try
            {
                stream.EndWrite(result);
            }
            finally
            {
                streamWriteSemaphore.Release();
            }
        }

        /// <summary>
        /// Disconnects from the IPEndpoint.
        /// </summary>
        public WaitHandle Disconnect()
        {
            if (TryFlaggingAs(SockNetState.DISCONNECTING, SockNetState.CONNECTED))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, "Disconnecting from [{0}]...", RemoteEndpoint);

                disconnectWaitHandle.Reset();

                Socket.BeginDisconnect(true, new AsyncCallback(DisconnectCallback), Socket);
            }
            else
            {
                if (OnDisconnect != null)
                {
                    OnDisconnect(this);
                }
            }

            return disconnectWaitHandle;
        }

        /// <summary>
        /// A callback that gets invoked after we disconnect from the IPEndpoint.
        /// </summary>
        /// <param name="result"></param>
        private void DisconnectCallback(IAsyncResult result)
        {
            if (TryFlaggingAs(SockNetState.DISCONNECTED, SockNetState.DISCONNECTING))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, "Disconnected from [{0}].", RemoteEndpoint);

                ((Socket)result.AsyncState).EndDisconnect(result);

                stream.Close();

                if (OnDisconnect != null)
                {
                    OnDisconnect(this);
                }

                disconnectWaitHandle.Set();
            }
            else
            {
                throw new Exception("Must be disconnecting.");
            }
        }
    }
}
