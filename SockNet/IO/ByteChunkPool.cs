using System;
using System.Collections.Generic;

namespace ArenaNet.SockNet.IO
{
    public class ByteChunkPool
    {
        public int ChunkSize { set; get; }
        private Queue<byte[]> pool = new Queue<byte[]>();

        public ByteChunkPool(int chunkSize = 1024)
        {
            this.ChunkSize = chunkSize;
        }

        public byte[] Borrow()
        {
            byte[] response = null;

            lock (this)
            {
                if (pool.Count > 0)
                {
                    response = pool.Dequeue();
                }
            }

            if (response == null)
            {
                response = new byte[ChunkSize];
            }

            return response;
        }

        public void Return(byte[] bytes)
        {
            if (bytes == null)
            {
                return;
            }

            lock (this)
            {
                pool.Enqueue(bytes);
            }
        }
    }
}
