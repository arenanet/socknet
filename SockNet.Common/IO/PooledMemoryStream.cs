using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common.IO
{
    public class PooledMemoryStream : Stream
    {
        private ObjectPool<byte[]> pool = null;

        private bool isClosed = false;

        private MemoryChunk rootChunk;
        private class MemoryChunk
        {
            public PooledObject<byte[]> pooledBytes;
            public int offset;
            public int count;

            public MemoryChunk next;
        }

        public override bool CanRead
        {
            get { return !isClosed; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return !isClosed; }
        }

        private long length = 0;
        public override long Length
        {
            get { return length; }
        }

        private long position = 0;
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

        public PooledMemoryStream(ObjectPool<byte[]> pool)
        {
            this.pool = pool;
        }

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

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public void OfferChunk(PooledObject<byte[]> pooledBytes, int offset, int count)
        {
            MemoryChunk chunk = new MemoryChunk()
            {
                pooledBytes = pooledBytes,
                offset = offset,
                count = count,
                next = null
            };

            AppendChunk(chunk);
        }

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
