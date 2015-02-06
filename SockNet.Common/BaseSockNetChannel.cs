using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Net.Sockets;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;
using ArenaNet.SockNet.Common.Collections;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// A simplification layer on top of System.Net.Sockets that enables users to implement custom stream incomingHandlers easier.
    /// </summary>
    public abstract class BaseSockNetChannel : ISockNetChannel
    {
        public const int DefaultBufferSize = 1024;

        // the pool for byte chunks
        private readonly ObjectPool<byte[]> bufferPool;
        public ObjectPool<byte[]> BufferPool { get { return bufferPool; } }

        // the receive stream that will be sent to incomingHandlers to handle data
        private readonly PooledMemoryStream chunkedReceiveStream;

        // the raw socket
        protected Socket Socket { get; private set; }

        // the installed modules
        protected ConcurrentDictionary<ISockNetChannelModule, bool> modules = new ConcurrentDictionary<ISockNetChannelModule, bool>();

        // the current stream
        protected Stream stream;

        /// <summary>
        /// Promise fulfiller for attachment.
        /// </summary>
        private PromiseFulfiller<ISockNetChannel> attachPromiseFulfiller = null;

        // data incomingHandlers for incomming and outgoing messages
        public SockNetChannelPipe Pipe { get; protected set; }

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
        private SockNetState state;
        public SockNetState State { get { return state; } set { state = value; } }

        /// <summary>
        /// The current async receive result.
        /// </summary>
        protected IAsyncResult currentAsyncReceive = null;

        protected Semaphore streamWriteSemaphore = new Semaphore(1, 1);

        /// <summary>
        /// Creates a base socknet channel given a socket and a buffer pool.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        protected BaseSockNetChannel(Socket socket, ObjectPool<byte[]> bufferPool)
        {
            this.Socket = socket;

            this.Pipe = new SockNetChannelPipe(this);
            
            this.bufferPool = bufferPool;
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
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Installing module: [{0}]", module);

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
                SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Uninstalling module: [{0}]", module);

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
            return Interlocked.CompareExchange<SockNetState>(ref this.state, state, previous) == previous;
        }

        /// <summary>
        /// Attaches as a non-ssl channel.
        /// </summary>
        protected Promise<ISockNetChannel> Attach()
        {
            stream = new NetworkStream(Socket, true);

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Reading data from [{0}]...", RemoteEndpoint);

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

            return (attachPromiseFulfiller = new Promise<ISockNetChannel>(this).CreateFulfiller()).Promise;
        }

        /// <summary>
        /// Attaches as a SSL channel with client auth.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        protected Promise<ISockNetChannel> AttachAsSslClient(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            stream = new NetworkStream(Socket, true);

            EnableClientSsl(certificateValidationCallback);

            return (attachPromiseFulfiller = new Promise<ISockNetChannel>().CreateFulfiller()).Promise;
        }

        /// <summary>
        /// Attaches as a SSL channel with client auth.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        protected Promise<ISockNetChannel> AttachAsSslServer(RemoteCertificateValidationCallback certificateValidationCallback, X509Certificate serverCert)
        {
            stream = new NetworkStream(Socket, true);

            EnableServerSsl(certificateValidationCallback, serverCert);

            return (attachPromiseFulfiller = new Promise<ISockNetChannel>().CreateFulfiller()).Promise;
        }

        /// <summary>
        /// Enables SSL on this connection.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        private void EnableClientSsl(RemoteCertificateValidationCallback certificateValidationCallback)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticating SSL with [{0}]...", RemoteEndpoint);

            SslStream sslStream = new SslStream(stream, false, certificateValidationCallback);
            sslStream.BeginAuthenticateAsClient(RemoteEndpoint.Address.ToString(), new AsyncCallback(EnableClientSslCallback), sslStream);
        }

        /// <summary>
        /// A callback the gets invoked after SSL auth completes.
        /// </summary>
        /// <param name="result"></param>
        private void EnableClientSslCallback(IAsyncResult result)
        {
            SslStream sslStream = (SslStream)result.AsyncState;

            sslStream.EndAuthenticateAsClient(result);

            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticated SSL with [{0}].", RemoteEndpoint);
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "(SSL) Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

            attachPromiseFulfiller.Fulfill(this);
        }

        /// <summary>
        /// Enables SSL on this connection.
        /// </summary>
        /// <param name="certificateValidationCallback"></param>
        private void EnableServerSsl(RemoteCertificateValidationCallback certificateValidationCallback, X509Certificate serverCert)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticating SSL with [{0}]...", RemoteEndpoint);

            SslStream sslStream = new SslStream(stream, false, certificateValidationCallback);
            sslStream.BeginAuthenticateAsServer(serverCert, new AsyncCallback(EnableServerSslCallback), sslStream);
        }

        /// <summary>
        /// A callback the gets invoked after SSL auth completes.
        /// </summary>
        /// <param name="result"></param>
        private void EnableServerSslCallback(IAsyncResult result)
        {
            SslStream sslStream = (SslStream)result.AsyncState;

            sslStream.EndAuthenticateAsServer(result);

            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Authenticated SSL with [{0}].", RemoteEndpoint);
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "(SSL) Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

            attachPromiseFulfiller.Fulfill(this);
        }

        /// <summary>
        /// A callback that gets invoked when we have incoming data in the pipe.
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveCallback(IAsyncResult result)
        {
            int count = stream.EndRead(result);

            PooledObject<byte[]> buffer = (PooledObject<byte[]>)result.AsyncState;

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (result.AsyncState is SslStream ? "[SSL] " : "") + "Received [{0}] bytes from [{1}].", count, RemoteEndpoint);

            if (count > 0)
            {
                try
                {
                    long startingPosition = chunkedReceiveStream.Position;

                    chunkedReceiveStream.OfferChunk(buffer, 0, count);

                    chunkedReceiveStream.Position = startingPosition;

                    object obj = chunkedReceiveStream;

                    Pipe.HandleIncomingData(ref obj);
                }
                catch (Exception ex)
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, ex.Message);
                }
                finally
                {
                    chunkedReceiveStream.Flush();

                    try
                    {
                        buffer = bufferPool.Borrow();

                        currentAsyncReceive = stream.BeginRead(buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);
                    }
                    catch (Exception)
                    {
                        Close();
                    }
                }
            }
            else
            {
                Close();
            }
        }

        /// <summary>
        /// Sets the given message to the IPEndpoint.
        /// </summary>
        /// <param name="data"></param>
        public void Send(object data)
        {
            object obj = data;

            try
            {
                Pipe.HandleOutgoingData(ref obj);
            }
            catch (Exception ex)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, ex.Message);
            }
            finally
            {
                if (obj is byte[])
                {
                    byte[] rawSendableData = (byte[])obj;

                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", rawSendableData.Length, RemoteEndpoint);

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
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to send object: {0}", obj);
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

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", bytesSending, RemoteEndpoint);

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
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (result.AsyncState is SslStream ? "[SSL] " : "") + "Sent data to [{0}].", RemoteEndpoint);

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
        /// Closes this channel.
        /// </summary>
        /// <returns></returns>
        public abstract Promise<ISockNetChannel> Close();

        /// <summary>
        /// Returns true if this channel is active.
        /// </summary>
        public abstract bool IsActive { get; }
    }
}
