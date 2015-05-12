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
using System.IO;
using System.Collections.Generic;
using System.Text;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A chunked buffer.
    /// </summary>
    public sealed class ChunkedBuffer : IDisposable
    {
        private ObjectPool<byte[]> pool = null;

        public bool IsClosed { private set; get; }

        /// <summary>
        /// The root in the memory chunk chain
        /// </summary>
        private MemoryChunkNode rootChunk;

        /// <summary>
        /// Represents a memory chunk in the chain.
        /// </summary>
        private class MemoryChunkNode
        {
            public PooledObject<byte[]> pooledObject;
            public byte[] pooledBytes;
            public int offset;
            public int count;

            public MemoryChunkNode next;
        }

        /// <summary>
        /// The state during drains.
        /// </summary>
        private class DrainChunksState
        {
            public MemoryChunkNode currentChunk;
            public Stream stream;
            public Promise<ChunkedBuffer> promise;
        }

        private long _readPosition = 0;
        public long ReadPosition
        {
            set
            {
                if (value > WritePosition)
                {
                    throw new InvalidOperationException("Value cannot ve greated then write position.");
                }

                _readPosition = value;
            }
            get {
                return _readPosition;
            }
        }

        public long WritePosition { private set; get; }
        public long AvailableBytesToRead { get { return Math.Max(0, WritePosition - ReadPosition); } }

        public ChunkedBufferStream Stream { private set; get; }
        
        /// <summary>
        /// Creates a pooled memory stream with the given pool.
        /// </summary>
        /// <param name="pool"></param>
        public ChunkedBuffer(ObjectPool<byte[]> pool)
        {
            this.pool = pool;

            this.Stream = new ChunkedBufferStream(this);
            this.IsClosed = false;
            this.WritePosition = 0;
            this.ReadPosition = 0;
        }

        /// <summary>
        /// Closes this stream and returns all pooled memory chunks into the pool.
        /// </summary>
        public void Close()
        {
            lock (this)
            {
                if (IsClosed)
                {
                    return;
                }

                ReadPosition = WritePosition;

                try
                {
                    Flush();
                }
                finally
                {
                    IsClosed = true;
                }
            }
        }

        /// <summary>
        /// Flushes this stream and clears any read pooled memory chunks.
        /// </summary>
        public void Flush()
        {
            lock (this)
            {
                ValidateBuffer();

                MemoryChunkNode currentChunk = rootChunk;

                while (currentChunk != null)
                {
                    if (currentChunk.count > ReadPosition)
                    {
                        break;
                    }

                    if (currentChunk.pooledObject != null && currentChunk.pooledObject.RefCount.Decrement() < 1)
                    {
                        if (currentChunk.pooledObject.State == PooledObject<byte[]>.PooledObjectState.USED)
                        {
                            currentChunk.pooledObject.Return();
                        }
                        else
                        {
                            SockNetLogger.Log(SockNetLogger.LogLevel.WARN, this, "Potential resource leak found.");
                        }
                    }
                    rootChunk = currentChunk.next;
                    ReadPosition -= currentChunk.count;
                    WritePosition = Math.Max(0, WritePosition - currentChunk.count);
                    currentChunk = currentChunk.next;
                }
            }
        }

        /// <summary>
        /// Reads a single byte.
        /// </summary>
        /// <returns></returns>
        public int Read()
        {
            ValidateBuffer();

            byte[] buffer = new byte[1];

            if (Read(buffer, 0, 1) == 1)
            {
                return buffer[1];
            }

            return -1;
        }

        /// <summary>
        /// Reads data into the given buffer from the current position.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public int Read(byte[] buffer, int offset, int count)
        {
            lock (this)
            {
                ValidateBuffer();

                if (WritePosition == ReadPosition)
                {
                    return 0;
                }

                int bytesScanned = 0;
                int bytesRead = 0;

                MemoryChunkNode currentChunk = rootChunk;

                while (currentChunk != null && bytesRead < count)
                {
                    if (bytesScanned >= ReadPosition)
                    {
                        int bytesToCopy = Math.Min(currentChunk.count, count - bytesRead);

                        Buffer.BlockCopy(currentChunk.pooledBytes, currentChunk.offset, buffer, offset + bytesRead, bytesToCopy);

                        bytesRead += bytesToCopy;
                    }
                    else if (currentChunk.count + bytesScanned >= ReadPosition)
                    {
                        int sourceChunkOffset = (int)(ReadPosition - bytesScanned);
                        int bytesToCopy = Math.Min(currentChunk.count - sourceChunkOffset, count - bytesRead);

                        Buffer.BlockCopy(currentChunk.pooledBytes, currentChunk.offset + sourceChunkOffset, buffer, offset + bytesRead, bytesToCopy);

                        bytesRead += bytesToCopy;
                    }

                    bytesScanned += currentChunk.count;
                    currentChunk = currentChunk.next;
                }

                ReadPosition += bytesRead;

                return bytesRead;
            }
        }

        /// <summary>
        /// Offers the following raw buffer.
        /// </summary>
        /// <param name="data"></param>
        public ChunkedBuffer OfferRaw(byte[] data, int offset, int count)
        {
            ValidateBuffer();

            if (data == null)
            {
                throw new ArgumentNullException("'data' cannot be null");
            }

            MemoryChunkNode chunk = new MemoryChunkNode()
            {
                pooledObject = null,
                pooledBytes = data,
                offset = offset,
                count = count,
                next = null
            };

            AppendChunk(chunk);

            return this;
        }

        /// <summary>
        /// Affers a pooled memory chunk to this stream.
        /// </summary>
        /// <param name="pooledBytes"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public ChunkedBuffer OfferChunk(PooledObject<byte[]> pooledObject, int offset, int count)
        {
            ValidateBuffer();

            if (pooledObject == null)
            {
                throw new ArgumentNullException("'data' cannot be null");
            }

            if (pooledObject.Pool != pool)
            {
                throw new Exception("The given pooled object does not beong to ths pool that is assigned to this stream.");
            }

            MemoryChunkNode chunk = new MemoryChunkNode()
            {
                pooledObject = pooledObject,
                pooledBytes = pooledObject.Value,
                offset = offset,
                count = count,
                next = null
            };
            pooledObject.RefCount.Increment();

            AppendChunk(chunk);

            return this;
        }

        /// <summary>
        /// Reads at maximum maxBytes bytes from the given stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="maxBytes"></param>
        public ChunkedBuffer ReadFromStream(Stream stream)
        {
            lock (this)
            {
                PooledObject<byte[]> pooledObject = null;
                int bytesRead = 0;

                do
                {
                    pooledObject = pool.Borrow();
                    bytesRead = stream.Read(pooledObject.Value, 0, pooledObject.Value.Length);

                    if (bytesRead > 0)
                    {
                        OfferChunk(pooledObject, 0, bytesRead);
                    }
                    else
                    {
                        pooledObject.Return();
                        break;
                    }
                }
                while (true);
            }

            return this;
        }

        /// <summary>
        /// Synchronously drains the buffer into the stream.
        /// </summary>
        /// <param name="stream"></param>
        public ChunkedBuffer DrainToStreamSync(Stream stream)
        {
            lock (this)
            {
                while (rootChunk != null)
                {
                    stream.Write(rootChunk.pooledBytes, rootChunk.offset, rootChunk.count);

                    if (rootChunk.pooledObject != null && rootChunk.pooledObject.Pool != null && rootChunk.pooledObject.RefCount.Decrement() < 1)
                    {
                        rootChunk.pooledObject.Return();
                    }

                    rootChunk = rootChunk.next;
                }
            }

            return this;
        }

        /// <summary>
        /// Drains this ChunkedBuffer to the given stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public Promise<ChunkedBuffer> DrainToStream(Stream stream)
        {
            ValidateBuffer();

            Promise<ChunkedBuffer> promise = new Promise<ChunkedBuffer>();

            DrainToStream(stream, promise);

            return promise;
        }

        /// <summary>
        /// Drains chunks to the given stream and notifies the given promise when it's done.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="promise"></param>
        private void DrainToStream(Stream stream, Promise<ChunkedBuffer> promise)
        {
            MemoryChunkNode currentChunk = null;

            lock (this)
            {
                currentChunk = rootChunk;

                if (currentChunk == null)
                {
                    promise.CreateFulfiller().Fulfill(this);

                    return;
                }

                rootChunk = rootChunk.next;
            }

            stream.BeginWrite(currentChunk.pooledBytes, currentChunk.offset, currentChunk.count, new AsyncCallback(OnDrainToStreamWriteComplete),
                new DrainChunksState()
                {
                    currentChunk = currentChunk,
                    stream = stream,
                    promise = promise
                });
        }

        /// <summary>
        /// The async response to writing out this stream.
        /// </summary>
        /// <param name="result"></param>
        private void OnDrainToStreamWriteComplete(IAsyncResult result)
        {
            DrainChunksState state = (DrainChunksState)result.AsyncState;

            state.stream.EndWrite(result);

            if (state.currentChunk.pooledObject != null && state.currentChunk.pooledObject.Pool != null && state.currentChunk.pooledObject.RefCount.Decrement() < 1)
            {
                state.currentChunk.pooledObject.Return();
            }

            DrainToStream(state.stream, state.promise);
        }

        /// <summary>
        /// Writes the given data into this stream.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void Write(byte[] buffer, int offset, int count)
        {
            if (buffer.Length < offset + count)
            {
                throw new ArgumentOutOfRangeException("Offset + count must be less then the buffer length.");
            }

            lock (this)
            {
                ValidateBuffer();

                for (int i = offset; i < count; )
                {
                    PooledObject<byte[]> pooledObject = pool.Borrow();
                    pooledObject.RefCount.Increment();
                    int bytesToCopy = Math.Min(pooledObject.Value.Length, count - i);

                    Buffer.BlockCopy(buffer, i, pooledObject.Value, 0, bytesToCopy);

                    AppendChunk(new MemoryChunkNode()
                    {
                        pooledObject = pooledObject,
                        pooledBytes = pooledObject.Value,
                        offset = 0,
                        count = bytesToCopy,
                        next = null
                    });

                    i += bytesToCopy;
                }
            }
        }

        /// <summary>
        /// Appeds the given chunk to this stream.
        /// </summary>
        /// <param name="chunk"></param>
        private void AppendChunk(MemoryChunkNode chunk)
        {
            lock (this)
            {
                if (rootChunk == null)
                {
                    rootChunk = chunk;
                }
                else
                {
                    MemoryChunkNode currentChunk = rootChunk;

                    while (currentChunk != null)
                    {
                        if (currentChunk.next == null)
                        {
                            currentChunk.next = chunk;
                            break;
                        }
                        else
                        {
                            currentChunk = currentChunk.next;
                        }
                    }
                }

                WritePosition += chunk.count;
            }
        }

        /// <summary>
        /// Reads the contents of the buffer into a string.
        /// </summary>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public string ToString(Encoding encoding)
        {
            byte[] data = null;

            lock (this)
            {
                data = new byte[AvailableBytesToRead];
                Read(data, 0, data.Length);
            }

            return encoding.GetString(data);
        }

        /// <summary>
        /// Validates this buffer an throws exception as neede.
        /// </summary>
        private void ValidateBuffer()
        {
            if (IsClosed)
            {
                throw new ObjectDisposedException("ChunkedBuffer");
            }
        }

        /// <summary>
        /// Closes this buffer.
        /// </summary>
        public void Dispose()
        {
            lock (this)
            {
                MemoryChunkNode currentChunk = rootChunk;

                while (currentChunk != null)
                {
                    if (currentChunk.pooledObject != null && currentChunk.pooledObject.RefCount.Decrement() < 1)
                    {
                        if (currentChunk.pooledObject.State == PooledObject<byte[]>.PooledObjectState.USED)
                        {
                            currentChunk.pooledObject.Dispose();
                        }
                        else
                        {
                            SockNetLogger.Log(SockNetLogger.LogLevel.WARN, this, "Potential resource leak found.");
                        }
                    }

                    currentChunk = currentChunk.next;
                }

                rootChunk = null;
                IsClosed = true;
            }
        }

        /// <summary>
        /// Reads the given stream into a new buffer.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="bufferPool"></param>
        /// <returns></returns>
        public static ChunkedBuffer ReadFully(Stream stream, ObjectPool<byte[]> bufferPool = null)
        {
            if (bufferPool == null)
            {
                bufferPool = SockNetChannelGlobals.GlobalBufferPool;
            }

            return new ChunkedBuffer(bufferPool).ReadFromStream(stream);
        }

        /// <summary>
        /// Wraps the given byte array.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="bufferPool"></param>
        /// <returns></returns>
        public static ChunkedBuffer Wrap(byte[] data, int offset, int count, ObjectPool<byte[]> bufferPool = null)
        {
            if (bufferPool == null) 
            {
                bufferPool = SockNetChannelGlobals.GlobalBufferPool;
            }

            return new ChunkedBuffer(bufferPool).OfferRaw(data, offset, count);
        }

        /// <summary>
        /// Wraps the given string.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="encoding"></param>
        /// <param name="bufferPool"></param>
        /// <returns></returns>
        public static ChunkedBuffer Wrap(string data, Encoding encoding, ObjectPool<byte[]> bufferPool = null)
        {
            if (bufferPool == null)
            {
                bufferPool = SockNetChannelGlobals.GlobalBufferPool;
            }

            byte[] rawData = encoding.GetBytes(data);

            return new ChunkedBuffer(bufferPool).OfferRaw(rawData, 0, rawData.Length);
        }
    }
}
