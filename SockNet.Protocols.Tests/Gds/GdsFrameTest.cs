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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;

namespace ArenaNet.SockNet.Protocols.Gds
{
    [TestClass]
    public class GdsFrameTest
    {
        [TestMethod]
        public void TestCompression()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            // deflate works great on text - it is horrible with random byte arrays
            string header1Key = "Some key";
            byte[] header1Value = Encoding.UTF8.GetBytes("Well here is a great value for some key. We're really great at making keys and value.");

            string header2Key = "Another key";
            byte[] header2Value = Encoding.UTF8.GetBytes("Yet another great value for another key. This is just getting absurd.");

            GdsFrame frame = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { header1Key, header1Value }, 
                    { header2Key, header2Value } 
                },
                true, null, true);
            
            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.HeadersOnly, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);
            Assert.AreEqual(2, frame.Headers.Count);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            int uncompressedSize = (4           // frame definition
                + 2         // header definition
                + (4 * 2)   // header sizes for two headers
                + header1Key.Length + header1Value.Length + header2Key.Length + header2Value.Length);

            Console.WriteLine("Compressed: " + stream.Position + ", Uncompressed: " + uncompressedSize);

            Assert.IsTrue(stream.Position < uncompressedSize);
        }

        [TestMethod]
        public void TestPing()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            GdsFrame frame = GdsFrame.NewPingFrame(streamId);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Ping, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Assert.AreEqual(4, stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Ping, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
        }

        [TestMethod]
        public void TestPong()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            GdsFrame frame = GdsFrame.NewPongFrame(streamId);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Pong, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Assert.AreEqual(4, stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Pong, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
        }

        [TestMethod]
        public void TestClose()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            GdsFrame frame = GdsFrame.NewCloseFrame(streamId);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Close, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Assert.AreEqual(4, stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Close, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
        }

        [TestMethod]
        public void TestHeaderOnlyNotCompressed()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            string header1Key = "the first key";
            byte[] header1Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header1Value);

            string header2Key = "the second key";
            byte[] header2Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header2Value);

            GdsFrame frame = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { header1Key, header1Value }, 
                    { header2Key, header2Value } 
                }, 
                false, null, true);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.HeadersOnly, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);
            Assert.AreEqual(2, frame.Headers.Count);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Assert.AreEqual(
                4           // frame definition
                + 2         // header definition
                + (4 * 2)   // header sizes for two headers
                + header1Key.Length + header1Value.Length + header2Key.Length + header2Value.Length
                , stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.HeadersOnly, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
            Assert.AreEqual(2, readFrame.Headers.Count);

            AssertEquals(header1Value, readFrame.Headers[header1Key]);
            AssertEquals(header2Value, readFrame.Headers[header2Key]);
        }

        [TestMethod]
        public void TestHeaderOnlyCompressed()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            string header1Key = "the first key";
            byte[] header1Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header1Value);

            string header2Key = "the second key";
            byte[] header2Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header2Value);

            GdsFrame frame = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { header1Key, header1Value }, 
                    { header2Key, header2Value } 
                },
                true, null, true);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.HeadersOnly, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);
            Assert.AreEqual(2, frame.Headers.Count);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Console.WriteLine(stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.HeadersOnly, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
            Assert.AreEqual(2, readFrame.Headers.Count);

            AssertEquals(header1Value, readFrame.Headers[header1Key]);
            AssertEquals(header2Value, readFrame.Headers[header2Key]);
        }

        [TestMethod]
        public void TestBodyOnly()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            byte[] body = new byte[rand.Next(1024, 1024*64)];
            rand.NextBytes(body);

            GdsFrame frame = GdsFrame.NewContentFrame(streamId, null, false, body, true);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.BodyOnly, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);
            Assert.AreEqual(0, frame.Headers.Count);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Assert.AreEqual(
                4           // frame definition
                + 4         // body definition
                + body.Length
                , stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.BodyOnly, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
            Assert.AreEqual(0, readFrame.Headers.Count);

            AssertEquals(body, readFrame.Body);
        }

        [TestMethod]
        public void TestFull()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            string header1Key = "the first key";
            byte[] header1Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header1Value);

            string header2Key = "the second key";
            byte[] header2Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header2Value);

            byte[] body = new byte[rand.Next(1024, 1024 * 64)];
            rand.NextBytes(body);

            GdsFrame frame = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { header1Key, header1Value }, 
                    { header2Key, header2Value } 
                }, false, body, true);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Full, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);
            Assert.AreEqual(2, frame.Headers.Count);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            Assert.AreEqual(
                4           // frame definition
                + 2         // header definition
                + (4 * 2)   // header sizes for two headers
                + header1Key.Length + header1Value.Length + header2Key.Length + header2Value.Length
                + 4         // body definition
                + body.Length
                , stream.Position);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Full, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
            Assert.AreEqual(2, readFrame.Headers.Count);

            AssertEquals(body, readFrame.Body);
        }

        [TestMethod]
        public void TestFullCompressed()
        {
            Random rand = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            uint streamId = (uint)rand.Next(0, (int)(Math.Pow(2, 24) - 1));

            string header1Key = "the first key";
            byte[] header1Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header1Value);

            string header2Key = "the second key";
            byte[] header2Value = new byte[rand.Next(32, 1024 * 64)];
            rand.NextBytes(header2Value);

            byte[] body = new byte[rand.Next(1024, 1024 * 64)];
            rand.NextBytes(body);

            GdsFrame frame = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { header1Key, header1Value }, 
                    { header2Key, header2Value } 
                }, true, body, true);

            Assert.AreEqual(true, frame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Full, frame.Type);
            Assert.AreEqual(streamId, frame.StreamId);
            Assert.AreEqual(2, frame.Headers.Count);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream);

            stream.Position = 0;

            GdsFrame readFrame = GdsFrame.ParseFrame(stream);

            Assert.AreEqual(true, readFrame.IsComplete);
            Assert.AreEqual(GdsFrame.GdsFrameType.Full, readFrame.Type);
            Assert.AreEqual(streamId, readFrame.StreamId);
            Assert.AreEqual(2, readFrame.Headers.Count);

            AssertEquals(body, readFrame.Body);
        }

        private static void AssertEquals(byte[] l, byte[] r)
        {
            Assert.IsNotNull(l);
            Assert.IsNotNull(r);
            Assert.AreEqual(l.Length, r.Length);

            for (int i = 0; i < l.Length; i++)
            {
                Assert.AreEqual(l[i], r[i]);
            }
        }

        private static void PrintBitStream(Stream stream, int count)
        {
            stream.Position = 0;

            byte[] data = new BinaryReader(stream).ReadBytes(count);

            foreach (byte byt in data)
            {
                Console.Write(Convert.ToString(byt, 2).PadLeft(8, '0'));
            }

            Console.WriteLine();
        }
    }
}
