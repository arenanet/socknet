using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.IO
{
    [TestClass]
    public class ByteChunkPoolTest
    {
        [TestMethod]
        public void TestBorrow()
        {
            ByteChunkPool pool = new ByteChunkPool(100);
            Assert.AreEqual(100, pool.ChunkSize);

            byte[] chunk1 = pool.Borrow();
            byte[] chunk2 = pool.Borrow();

            Assert.AreEqual(100, chunk1.Length);
            Assert.AreEqual(100, chunk2.Length);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(2, pool.TotalNumberOfChunks);
        }

        [TestMethod]
        public void TestBorrowAndReturn()
        {
            ByteChunkPool pool = new ByteChunkPool(100);
            Assert.AreEqual(100, pool.ChunkSize);

            byte[] chunk = pool.Borrow();

            Assert.AreEqual(100, chunk.Length);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(1, pool.TotalNumberOfChunks);

            pool.Return(chunk);

            Assert.AreEqual(1, pool.ChunksInPool);
            Assert.AreEqual(1, pool.TotalNumberOfChunks);
        }

        [TestMethod]
        public void TestBorrowAndReturnAndBorrow()
        {
            ByteChunkPool pool = new ByteChunkPool(100);
            Assert.AreEqual(100, pool.ChunkSize);

            byte[] chunk = pool.Borrow();

            Assert.AreEqual(100, chunk.Length);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(1, pool.TotalNumberOfChunks);

            pool.Return(chunk);

            Assert.AreEqual(1, pool.ChunksInPool);
            Assert.AreEqual(1, pool.TotalNumberOfChunks);

            chunk = pool.Borrow();

            Assert.AreEqual(100, chunk.Length);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(1, pool.TotalNumberOfChunks);
        }
    }
}
