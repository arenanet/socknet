/*
 * Copyright 2015 ArenaNet, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this 
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * 	 http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under 
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF 
 * ANY KIND, either express or implied. See the License for the specific language governing 
 * permissions and limitations under the License.
 */
using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Common.IO
{
    [TestClass]
    public class ChunkedBufferTest
    {
        private static readonly string TestDataString = "why, what a wonderful test this is!";
        private static readonly byte[] TestData = Encoding.UTF8.GetBytes(TestDataString);

        [TestMethod]
        public void TestWrapString()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            byte[] randomUtf8StringBytes = new byte[rand.Next(2000, 5000)];
            for (int i = 0; i < randomUtf8StringBytes.Length; i++)
            {
                randomUtf8StringBytes[i] = (byte)rand.Next(0x0020, 0x007F);
            }

            string randomUtf8String = Encoding.UTF8.GetString(randomUtf8StringBytes);

            Console.WriteLine("Generated random string: " + randomUtf8String);

            using (ChunkedBuffer buffer = ChunkedBuffer.Wrap(randomUtf8String, Encoding.UTF8, SockNetChannelGlobals.GlobalBufferPool))
            {
                using (StreamReader reader = new StreamReader(buffer.Stream, Encoding.UTF8))
                {
                    Assert.AreEqual(randomUtf8String, reader.ReadToEnd());
                }
            }
        }

        [TestMethod]
        public void TestReadFromStream()
        {
            MemoryStream stream = new MemoryStream();
            stream.Write(TestData, 0, TestData.Length);
            stream.Position = 0;

            MemoryStream newStream = new MemoryStream();

            new ChunkedBuffer(SockNetChannelGlobals.GlobalBufferPool)
                .ReadFromStream(stream)
                .DrainToStreamSync(newStream)
                .Close();

            newStream.Position = 0;

            Assert.AreEqual(TestDataString, new StreamReader(newStream).ReadToEnd());

            stream.Close();
            newStream.Close();
        }

        [TestMethod]
        public void TestDrainToStreamSync()
        {
            using (ChunkedBuffer buffer = new ChunkedBuffer(new ObjectPool<byte[]>(() => { return new byte[10]; })))
            {
                buffer.Write(TestData, 0, TestData.Length);

                MemoryStream stream = new MemoryStream();

                buffer.DrainToStreamSync(stream);

                stream.Position = 0;

                using (StreamReader reader = new StreamReader(stream))
                {
                    Assert.AreEqual(TestDataString, reader.ReadToEnd());
                }
            }
        }

        [TestMethod]
        public void TestDrainToStream()
        {
            using (ChunkedBuffer buffer = new ChunkedBuffer(new ObjectPool<byte[]>(() => { return new byte[10]; })))
            {
                buffer.Write(TestData, 0, TestData.Length);

                MemoryStream stream = new MemoryStream();

                buffer.DrainToStream(stream).WaitForValue(TimeSpan.FromSeconds(5));

                stream.Position = 0;

                using (StreamReader reader = new StreamReader(stream))
                {
                    Assert.AreEqual(TestDataString, reader.ReadToEnd());
                }
            }
        }

        [TestMethod]
        public void TestWrite()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });

            using (ChunkedBuffer stream = new ChunkedBuffer(pool))
            {
                Assert.AreEqual(0, stream.ReadPosition);
                Assert.AreEqual(0, stream.WritePosition);

                stream.Write(TestData, 0, TestData.Length);

                Assert.AreEqual(TestData.Length, stream.WritePosition);
                Assert.AreEqual(0, stream.ReadPosition);

                Assert.AreEqual(0, pool.ObjectsInPool);
                Assert.AreEqual(Math.Round((float)TestData.Length / 10f, MidpointRounding.AwayFromZero), pool.TotalNumberOfObjects);
            }
        }

        [TestMethod]
        public void TestWriteAndReadAndFlush()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });

            using (ChunkedBuffer stream = new ChunkedBuffer(pool))
            {
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
        }

        [TestMethod]
        public void TestWriteAndReadAndClose()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[10]; });

            using (ChunkedBuffer stream = new ChunkedBuffer(pool))
            {
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
}
