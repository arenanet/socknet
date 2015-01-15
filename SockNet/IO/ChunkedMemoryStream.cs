using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ArenaNet.SockNet.IO
{
    public class ChunkedMemoryStream : Stream
    {
        public delegate byte[] OnChunkNeededDelegate();
        public delegate void OnChunkRemovedDelegate(byte[] bytes);

        public OnChunkNeededDelegate OnChunkNeeded { set; get; }
        public OnChunkRemovedDelegate OnChunkRemoved { set; get; }

        private bool isClosed = false;

        private MemoryChunk rootChunk;
        private class MemoryChunk
        {
            public byte[] bytes;
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

        public ChunkedMemoryStream(OnChunkNeededDelegate onChunkNeeded, OnChunkRemovedDelegate onChunkRemoved)
        {
            this.OnChunkNeeded = onChunkNeeded;
            this.OnChunkRemoved = onChunkRemoved;
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
                int bytesScanned = 0;

                MemoryChunk currentChunk = rootChunk;

                while (currentChunk != null && bytesScanned < position)
                {
                    if (position - bytesScanned > currentChunk.count)
                    {
                        OnChunkRemoved(currentChunk.bytes);

                        rootChunk = currentChunk.next;
                        length -= currentChunk.count;
                        position -= currentChunk.count;
                    }

                    bytesScanned += currentChunk.count;
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

                        Buffer.BlockCopy(currentChunk.bytes, 0, buffer, bytesRead, bytesToCopy);

                        bytesRead += bytesToCopy;
                    }
                    else
                    {
                        if (currentChunk.count + bytesScanned >= position)
                        {
                            int sourceChunkOffset = (int)(position - bytesScanned);
                            int bytesToCopy = Math.Min(currentChunk.count - sourceChunkOffset, count - bytesRead);

                            Buffer.BlockCopy(currentChunk.bytes, sourceChunkOffset, buffer, bytesRead, bytesToCopy);

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

        public void OfferChunk(byte[] bytes, int offset, int count)
        {
            MemoryChunk chunk = new MemoryChunk()
            {
                bytes = bytes,
                offset = offset,
                count = count,
                next = null
            };

            AppendChunk(chunk);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            MemoryChunk chunk = new MemoryChunk()
            {
                bytes = new byte[count],
                offset = 0,
                count = count,
                next = null
            };

            Buffer.BlockCopy(buffer, offset, chunk.bytes, chunk.offset, count);

            AppendChunk(chunk);
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
