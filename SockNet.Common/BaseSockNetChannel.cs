﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Runtime.Remoting.Messaging;
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
    /// <typeparam name="S">The type representing the state.</typeparam>
    public abstract class BaseSockNetChannel<S> : ISockNetChannel where S : struct, IComparable, IFormattable, IConvertible
    {
        // the pool for byte chunks
        private readonly ObjectPool<byte[]> bufferPool;
        public ObjectPool<byte[]> BufferPool { get { return bufferPool; } }

        // async read/write delegates
        private delegate int ReadDelegate(byte[] buffer, int offset, int count);
        private delegate void WriteDelegate(byte[] buffer, int offset, int count);

        // the receive stream that will be sent to incomingHandlers to handle data
        private readonly ChunkedBuffer chunkedBuffer;

        // the raw socket
        protected Socket Socket { get; private set; }

        // the installed modules
        protected Dictionary<ISockNetChannelModule, bool> modules = new Dictionary<ISockNetChannelModule, bool>();

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
        private readonly Array stateValues;
        public int state;
        public Enum State { get { return (Enum)stateValues.GetValue(state); } protected set { state = ((IConvertible)value).ToInt32(null); } }

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
            this.chunkedBuffer = new ChunkedBuffer(bufferPool);

            this.stateValues = Enum.GetValues(typeof(S));
        }

        /// <summary>
        /// Adds a module into this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        public ISockNetChannel AddModule(ISockNetChannelModule module)
        {
            lock (modules)
            {
                if (!modules.ContainsKey(module))
                {
                    modules[module] = true;

                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Installing module: [{0}]", module);

                    module.Install(this);
                }
                else
                {
                    throw new Exception("Module [" + module + "] already installed.");
                }
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
            lock (modules)
            {
                if (modules.Remove(module))
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Uninstalling module: [{0}]", module);

                    module.Uninstall(this);
                }
                else
                {
                    throw new Exception("Module [" + module + "] not installed.");
                }
            }

            return this;
        }

        /// <summary>
        /// Flag this channel as connecting.
        /// </summary>
        /// <returns></returns>
        protected bool TryFlaggingAs(S state, S previous)
        {
            int actualState = state.ToInt32(null);
            int actualPrevious = previous.ToInt32(null);

            return Interlocked.CompareExchange(ref this.state, actualState, actualPrevious) == actualPrevious;
        }

        /// <summary>
        /// Attaches as a non-ssl channel.
        /// </summary>
        protected Promise<ISockNetChannel> Attach()
        {
            stream = new NetworkStream(Socket, true);

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Reading data from [{0}]...", RemoteEndpoint);

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            BeginRead(stream, buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

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
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "[SSL] Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            BeginRead(stream, buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

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
            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "[SSL] Reading data from [{0}]...", RemoteEndpoint);

            this.stream = sslStream;

            PooledObject<byte[]> buffer = bufferPool.Borrow();

            BeginRead(stream, buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);

            attachPromiseFulfiller.Fulfill(this);
        }

        /// <summary>
        /// A callback that gets invoked when we have incoming data in the pipe.
        /// </summary>
        /// <param name="result"></param>
        private void ReceiveCallback(IAsyncResult result)
        {
            int count = 0;

            try
            {
                count = EndRead(result);
            }
            catch (Exception)
            {
                // this means that the connection closed...
                Close();
                return;
            }

            PooledObject<byte[]> buffer = (PooledObject<byte[]>)result.AsyncState;

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (this.stream is SslStream ? "[SSL] " : "") + "Received [{0}] bytes from [{1}].", count, RemoteEndpoint);

            if (count > 0)
            {
                try
                {
                    chunkedBuffer.OfferChunk(buffer, 0, count);

                    object obj = chunkedBuffer;

                    Pipe.HandleIncomingData(ref obj);
                }
                catch (Exception ex)
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, ex.Message);
                }
                finally
                {
                    try
                    {
                        chunkedBuffer.Flush();

                        buffer = bufferPool.Borrow();

                        SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (this.stream is SslStream ? "[SSL] " : "") + "Reading data from [{0}]...", RemoteEndpoint);

                        BeginRead(stream, buffer.Value, 0, buffer.Value.Length, new AsyncCallback(ReceiveCallback), buffer);
                    }
                    catch (Exception e)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to begin reading.", e);

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
        /// Begins an async read.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private IAsyncResult BeginRead(Stream stream, byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ReadDelegate read = stream.Read;
            return read.BeginInvoke(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// Completes the async read.
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns></returns>
        private int EndRead(IAsyncResult asyncResult)
        {
            AsyncResult result = (AsyncResult)asyncResult;
            ReadDelegate caller = (ReadDelegate)result.AsyncDelegate;
            return caller.EndInvoke(asyncResult);
        }

        /// <summary>
        /// Begins an async write.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        private IAsyncResult BeginWrite(Stream stream, byte[] buffer, int offset,
                  int count, AsyncCallback callback, object state)
        {
            WriteDelegate write = stream.Write;
            return write.BeginInvoke(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// Completes an async write.
        /// </summary>
        /// <param name="asyncResult"></param>
        private void EndWrite(IAsyncResult asyncResult)
        {
            AsyncResult result = (AsyncResult)asyncResult;
            WriteDelegate caller = (WriteDelegate)result.AsyncDelegate;
            caller.EndInvoke(asyncResult);
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
                        BeginWrite(stream, rawSendableData, 0, rawSendableData.Length, new AsyncCallback(SendCallback), stream);
                    }
                    else
                    {
                        throw new ThreadStateException("Unable to obtain lock to write message.");
                    }
                }
                else if (obj is ChunkedBuffer)
                {
                    ChunkedBuffer sendableStream = ((ChunkedBuffer)obj);
                    PooledObject<byte[]> sendableDataBuffer = bufferPool.Borrow();

                    BeginRead(sendableStream.Stream,
                        sendableDataBuffer.Value,
                        0,
                        (int)Math.Min(sendableDataBuffer.Value.Length, sendableStream.AvailableBytesToRead),
                        StreamSendCallback,
                        new StreamSendState() { stream = sendableStream.Stream, buffer = sendableDataBuffer });
                }
                else if (obj is Stream)
                {
                    Stream sendableStream = ((Stream)obj);
                    PooledObject<byte[]> sendableDataBuffer = bufferPool.Borrow();

                    BeginRead(sendableStream,
                        sendableDataBuffer.Value, 
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

            int bytesSending = EndRead(result);

            SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, (IsConnectionEncrypted ? "[SSL] " : "") + "Sending [{0}] bytes to [{1}]...", bytesSending, RemoteEndpoint);

            if (streamWriteSemaphore.WaitOne(10000))
            {
                BeginWrite(stream, state.buffer.Value, 0, bytesSending, new AsyncCallback(SendCallback), state.buffer);
            }
            else
            {
                throw new ThreadStateException("Unable to obtain lock to write message.");
            }

            if (state.stream.Position < state.stream.Length)
            {
                state.buffer = bufferPool.Borrow();

                BeginRead(state.stream,
                    state.buffer.Value,
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
                EndWrite(result);
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
