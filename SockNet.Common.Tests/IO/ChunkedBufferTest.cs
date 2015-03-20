using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common.IO
{
    [TestClass]
    public class ChunkedBufferTest
    {
        private readonly byte[] TestData = Encoding.UTF8.GetBytes("why, what a wonderful test this is!");

        [TestMethod]
        public void TestWrite()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });
            ChunkedBuffer stream = new ChunkedBuffer(pool);

            Assert.AreEqual(0, stream.ReadPosition);
            Assert.AreEqual(0, stream.WritePosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.WritePosition);
            Assert.AreEqual(0, stream.ReadPosition);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestWriteAndReadAndFlush()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });
            ChunkedBuffer stream = new ChunkedBuffer(pool);

            Assert.AreEqual(0, stream.ReadPosition);
            Assert.AreEqual(0, stream.WritePosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.WritePosition);
            Assert.AreEqual(0, stream.ReadPosition);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);

            StreamReader reader = new StreamReader(stream.Stream);

            Assert.AreEqual(Encoding.UTF8.GetString(TestData, 0, TestData.Length), reader.ReadToEnd());

            Assert.AreEqual(TestData.Length, stream.ReadPosition);
            Assert.AreEqual(TestData.Length, stream.WritePosition);

            stream.Flush();

            Assert.AreEqual(0, stream.ReadPosition);
            Assert.AreEqual(0, stream.WritePosition);
        }

        [TestMethod]
        public void TestWriteAndReadAndClose()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });
            ChunkedBuffer stream = new ChunkedBuffer(pool);

            Assert.AreEqual(0, stream.ReadPosition);
            Assert.AreEqual(0, stream.WritePosition);

            stream.Write(TestData, 0, TestData.Length);

            Assert.AreEqual(TestData.Length, stream.WritePosition);
            Assert.AreEqual(0, stream.ReadPosition);

            Assert.AreEqual(0, pool.ObjectsInPool);
            Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);

            using (StreamReader reader = new StreamReader(stream.Stream))
            {
                Assert.AreEqual(Encoding.UTF8.GetString(TestData, 0, TestData.Length), reader.ReadToEnd());

                Assert.AreEqual(TestData.Length, stream.ReadPosition);
                Assert.AreEqual(TestData.Length, stream.WritePosition);
            }

            Assert.AreEqual(0, stream.ReadPosition);
            Assert.AreEqual(0, stream.WritePosition);
        }
    }
}
