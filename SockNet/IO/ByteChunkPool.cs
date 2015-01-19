using System;
using System.Collections.Generic;
using ArenaNet.SockNet.Collections;

namespace ArenaNet.SockNet.IO
{
    public class ByteChunkPool
    {
        public int ChunkSize { set; get; }

        public int ChunksInPool { get { return pool.Count; } }

        public int TotalNumberOfChunks { get { return totalPoolSize; } }

        private ConcurrentQueue<byte[]> pool = new ConcurrentQueue<byte[]>();
        private int totalPoolSize = 0;

        public ByteChunkPool(int chunkSize = 1024)
        {
            this.ChunkSize = chunkSize;
        }

        public byte[] Borrow()
        {
            byte[] response = null;

            if (!pool.TryDequeue(out response))
            {
                response = new byte[ChunkSize];
                totalPoolSize++;
            }

            return response;
        }

        public void Return(byte[] bytes)
        {
            if (bytes == null)
            {
                return;
            }

            pool.Enqueue(bytes);
        }
    }
}
