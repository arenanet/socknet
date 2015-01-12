using System;
using System.Collections.Generic;

namespace ArenaNet.SockNet.Buffer
{
    public class ByteBufferPool
    {
        public static readonly ByteBufferPool DEFAULT = new ByteBufferPool(1024 * 64, 1024 * 256);
        
        private readonly int minBufferSize;
        private readonly int maxBufferSize;

        private Queue<byte[]> bufferCache = new Queue<byte[]>();

        public ByteBufferPool(int minBufferSize, int maxBufferSize)
        {
            this.minBufferSize = minBufferSize;
            this.minBufferSize = maxBufferSize;
        }

        /// <summary>
        /// Retrieves a list of buffers that should cover the needed necessary bytes.
        /// </summary>
        /// <param name="bytesNeeded"></param>
        /// <returns></returns>
        public LinkedList<byte[]> Retrieve(int bytesNeeded)
        {
            LinkedList<byte[]> byteBuffers = new LinkedList<byte[]>();

            lock (bufferCache)
            {
                while (bytesNeeded > 0)
                {
                    byte[] buffer = bufferCache.Dequeue();
                    if (buffer == null)
                    {
                        buffer = new byte[Math.Min(Math.Max(bytesNeeded, maxBufferSize), minBufferSize)];
                        bytesNeeded -= buffer.Length;
                    }
                    else
                    {
                        bytesNeeded -= buffer.Length;
                    }

                    byteBuffers.AddLast(buffer);
                }
            }

            return byteBuffers;
        }

        /// <summary>
        /// Releases the given buffers to the pool so they can be reused.
        /// </summary>
        /// <param name="buffers"></param>
        public void Release(LinkedList<byte[]> buffers)
        {
            if (buffers.Count > 0)
            {
                lock (bufferCache)
                {
                    foreach (byte[] buffer in buffers)
                    {
                        bufferCache.Enqueue(buffer);
                    }
                }
            }
        }

        /// <summary>
        /// Releases the given buffer to the pool so they can be reused.
        /// </summary>
        /// <param name="buffer"></param>
        public void Release(byte[] buffer)
        {
            if (buffer != null)
            {
                lock (bufferCache)
                {
                    bufferCache.Enqueue(buffer);
                }
            }
        }
    }
}
