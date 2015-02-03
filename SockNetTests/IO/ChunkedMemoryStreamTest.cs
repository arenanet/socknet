using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.IO
{
    [TestClass]
    public class ChunkedMemoryStreamTest
    {
        private readonly byte[] TestData = Encoding.UTF8.GetBytes("why, what a wonderful test this is!");

        [TestMethod]
        public void TestWrite()
        {
            ByteChunkPool pool = new ByteChunkPool(10);
            ChunkedMemoryStream stream = new ChunkedMemoryStream(pool);

            Assert.AreEqual(0, stream.Position);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.Length);
            Assert.AreEqual(TestData.Length, stream.Position);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(Math.Round(((float)TestData.Length) / ((float)pool.ChunkSize), MidpointRounding.AwayFromZero), pool.TotalNumberOfChunks);
        }

        [TestMethod]
        public void TestWriteAndReadAndFlush()
        {
            ByteChunkPool pool = new ByteChunkPool(10);
            ChunkedMemoryStream stream = new ChunkedMemoryStream(pool);

            long startingPosition = stream.Position;
            
            Assert.AreEqual(0, startingPosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.Length);
            Assert.AreEqual(TestData.Length, stream.Position);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(Math.Round(((float)TestData.Length) / ((float)pool.ChunkSize), MidpointRounding.AwayFromZero), pool.TotalNumberOfChunks);

            stream.Position = startingPosition;

            StreamReader reader = new StreamReader(stream);

            Assert.AreEqual(Encoding.UTF8.GetString(TestData, 0, TestData.Length), reader.ReadToEnd());

            Assert.AreEqual(TestData.Length, stream.Position);

            stream.Flush();

            Assert.AreEqual(0, stream.Length);
            Assert.AreEqual(0, stream.Position);
        }

        [TestMethod]
        public void TestWriteAndReadAndClose()
        {
            ByteChunkPool pool = new ByteChunkPool(10);
            ChunkedMemoryStream stream = new ChunkedMemoryStream(pool);

            long startingPosition = stream.Position;

            Assert.AreEqual(0, startingPosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.Length);
            Assert.AreEqual(TestData.Length, stream.Position);

            Assert.AreEqual(0, pool.ChunksInPool);
            Assert.AreEqual(Math.Round(((float)TestData.Length) / ((float)pool.ChunkSize), MidpointRounding.AwayFromZero), pool.TotalNumberOfChunks);

            stream.Position = startingPosition;

            using (StreamReader reader = new StreamReader(stream))
            {
                Assert.AreEqual(Encoding.UTF8.GetString(TestData, 0, TestData.Length), reader.ReadToEnd());

                Assert.AreEqual(TestData.Length, stream.Position);
            }

            Assert.AreEqual(0, stream.Length);
            Assert.AreEqual(0, stream.Position);
        }
    }
}
