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
using System.Collections.Generic;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A chunked buffer.
    /// </summary>
    public class ChunkedBuffer
    {
        private ObjectPool<byte[]> pool = null;

        public bool IsClosed { private set; get; }

        /// <summary>
        /// The root in the memory chunk chain
        /// </summary>
        private MemoryChunk rootChunk;

        /// <summary>
        /// Represents a memory chunk in the chain.
        /// </summary>
        private class MemoryChunk
        {
            public PooledObject<byte[]> pooledBytes;
            public int offset;
            public int count;

            public MemoryChunk next;
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
                ReadPosition = WritePosition;

                Flush();

                IsClosed = true;
            }
        }

        /// <summary>
        /// Flushes this stream and clears any read pooled memory chunks.
        /// </summary>
        public void Flush()
        {
            lock (this)
            {
                MemoryChunk currentChunk = rootChunk;

                while (currentChunk != null)
                {
                    if (currentChunk.count > ReadPosition)
                    {
                        break;
                    }

                    if (currentChunk.pooledBytes.RefCount.Decrement() < 1)
                    {
                        currentChunk.pooledBytes.Return();
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
                if (WritePosition == ReadPosition)
                {
                    return 0;
                }

                int bytesScanned = 0;
                int bytesRead = 0;

                MemoryChunk currentChunk = rootChunk;

                while (currentChunk != null && bytesRead < count)
                {
                    if (bytesScanned >= ReadPosition)
                    {
                        int bytesToCopy = Math.Min(currentChunk.count, count - bytesRead);

                        Buffer.BlockCopy(currentChunk.pooledBytes.Value, currentChunk.offset, buffer, offset + bytesRead, bytesToCopy);

                        bytesRead += bytesToCopy;
                    }
                    else if (currentChunk.count + bytesScanned >= ReadPosition)
                    {
                        int sourceChunkOffset = (int)(ReadPosition - bytesScanned);
                        int bytesToCopy = Math.Min(currentChunk.count - sourceChunkOffset, count - bytesRead);

                        Buffer.BlockCopy(currentChunk.pooledBytes.Value, currentChunk.offset + sourceChunkOffset, buffer, offset + bytesRead, bytesToCopy);

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
        /// Affers a pooled memory chunk to this stream.
        /// </summary>
        /// <param name="pooledBytes"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public void OfferChunk(PooledObject<byte[]> pooledBytes, int offset, int count)
        {
            if (pooledBytes.Pool != pool)
            {
                throw new Exception("The given pooled object does not beong to ths pool that is assigned to this stream.");
            }

            MemoryChunk chunk = new MemoryChunk()
            {
                pooledBytes = pooledBytes,
                offset = offset,
                count = count,
                next = null
            };
            pooledBytes.RefCount.Increment();

            AppendChunk(chunk);
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
                for (int i = offset; i < count; )
                {
                    PooledObject<byte[]> pooledBytes = pool.Borrow();
                    pooledBytes.RefCount.Increment();
                    int bytesToCopy = Math.Min(pooledBytes.Value.Length, count - i);

                    Buffer.BlockCopy(buffer, i, pooledBytes.Value, 0, bytesToCopy);

                    AppendChunk(new MemoryChunk()
                    {
                        pooledBytes = pooledBytes,
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
        private void AppendChunk(MemoryChunk chunk)
        {
            lock (this)
            {
                if (rootChunk == null)
                {
                    rootChunk = chunk;
                }
                else
                {
                    MemoryChunk currentChunk = rootChunk;

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
    }
}
