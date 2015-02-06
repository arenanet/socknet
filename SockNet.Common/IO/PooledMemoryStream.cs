using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common.IO
{
    /// <summary>
    /// A memory stream that uses an ObjectPool of byte arrays to chain data blobs.
    /// </summary>
    public class PooledMemoryStream : Stream
    {
        private ObjectPool<byte[]> pool = null;

        private bool isClosed = false;

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

        /// <summary>
        /// Returns true if this stream is readable
        /// </summary>
        public override bool CanRead
        {
            get { return !isClosed; }
        }

        /// <summary>
        /// Returns true if this stream can stream.
        /// </summary>
        public override bool CanSeek
        {
            get { return !isClosed; }
        }

        /// <summary>
        /// Returns true if this stream is writable.
        /// </summary>
        public override bool CanWrite
        {
            get { return !isClosed; }
        }

        private long length = 0;

        /// <summary>
        /// The length of data in this tream
        /// </summary>
        public override long Length
        {
            get { return length; }
        }

        private long position = 0;

        /// <summary>
        /// The position the stream is in
        /// </summary>
        public override long Position
        {
            get
            {
                return position;
            }
            set
            {
                this.position = value;
            }
        }

        /// <summary>
        /// Creates a pooled memory stream with the given pool.
        /// </summary>
        /// <param name="pool"></param>
        public PooledMemoryStream(ObjectPool<byte[]> pool)
        {
            this.pool = pool;
        }

        /// <summary>
        /// Closes this stream and returns all pooled memory chunks into the pool.
        /// </summary>
        public override void Close()
        {
            base.Close();

            lock (this)
            {
                position = length;

                Flush();

                isClosed = true;
            }
        }

        /// <summary>
        /// Flushes this stream and clears any read pooled memory chunks.
        /// </summary>
        public override void Flush()
        {
            lock (this)
            {
                MemoryChunk currentChunk = rootChunk;

                while (currentChunk != null)
                {
                    if (position < currentChunk.count)
                    {
                        break;
                    }

                    currentChunk.pooledBytes.Return();

                    rootChunk = currentChunk.next;
                    length -= currentChunk.count;
                    position -= currentChunk.count;

                    currentChunk = currentChunk.next;
                }
            }
        }

        /// <summary>
        /// Reads data into the given buffer from the current position.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (this)
            {
                if (Length == Position)
                {
                    return 0;
                }
                
                int bytesScanned = 0;
                int bytesRead = 0;

                MemoryChunk currentChunk = rootChunk;

                while (currentChunk != null && bytesRead < count)
                {
                    if (bytesScanned > position)
                    {
                        int bytesToCopy = Math.Min(currentChunk.count, count - bytesRead);

                        Buffer.BlockCopy(currentChunk.pooledBytes.Value, 0, buffer, bytesRead, bytesToCopy);

                        bytesRead += bytesToCopy;
                    }
                    else
                    {
                        if (currentChunk.count + bytesScanned >= position)
                        {
                            int sourceChunkOffset = (int)(position - bytesScanned);
                            int bytesToCopy = Math.Min(currentChunk.count - sourceChunkOffset, count - bytesRead);

                            Buffer.BlockCopy(currentChunk.pooledBytes.Value, sourceChunkOffset, buffer, bytesRead, bytesToCopy);

                            bytesRead += bytesToCopy;
                        } // else keep scanning
                    }

                    bytesScanned += currentChunk.count;
                    currentChunk = currentChunk.next;
                }

                position += bytesRead;

                return bytesRead;
            }
        }

        /// <summary>
        /// Seeks the current position
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="origin"></param>
        /// <returns></returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (this)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                    case SeekOrigin.End:
                        long newPosition = Length + offset;

                        if (newPosition > Length)
                        {
                            throw new Exception("Applied offset to position exceeds length.");
                        }

                        Position = newPosition;
                        break;
                }
            }

            return Position;
        }

        /// <summary>
        /// Sets the length of the stream. Note: Trucation is not supported.
        /// </summary>
        /// <param name="value"></param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
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

            AppendChunk(chunk);
        }

        /// <summary>
        /// Writes the given data into this stream.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (this)
            {
                for (int i = 0; i < count;)
                {
                    PooledObject<byte[]> pooledBytes = pool.Borrow();
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

                position += chunk.count;
                length += chunk.count;
            }
        }
    }
}
