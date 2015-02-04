using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common.IO
{
    [TestClass]
    public class PooledMemoryStreamTest
    {
        private readonly byte[] TestData = Encoding.UTF8.GetBytes("why, what a wonderful test this is!");

        [TestMethod]
        public void TestWrite()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });
            PooledMemoryStream stream = new PooledMemoryStream(pool);

            Assert.AreEqual(0, stream.Position);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.Length);
            Assert.AreEqual(TestData.Length, stream.Position);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestWriteAndReadAndFlush()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });
            PooledMemoryStream stream = new PooledMemoryStream(pool);

            long startingPosition = stream.Position;
            
            Assert.AreEqual(0, startingPosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.Length);
            Assert.AreEqual(TestData.Length, stream.Position);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);

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
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });
            PooledMemoryStream stream = new PooledMemoryStream(pool);

            long startingPosition = stream.Position;

            Assert.AreEqual(0, startingPosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.Length);
            Assert.AreEqual(TestData.Length, stream.Position);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);

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
