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
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;
using ArenaNet.SockNet.Client;
using ArenaNet.SockNet.Protocols.Http;

namespace ArenaNet.SockNet.Protocols.Gds
{
    [TestClass]
    public class GdsSockNetChannelModuleTest
    {
        public class DummySockNetChannel : ISockNetChannel
        {
            public SockNetChannelPipe Pipe
            {
                get;
                set;
            }

            public ObjectPool<byte[]> BufferPool
            {
                get;
                set;
            }

            public IPEndPoint RemoteEndpoint
            {
                get;
                set;
            }

            public IPEndPoint LocalEndpoint
            {
                get;
                set;
            }

            public ISockNetChannel AddModule(ISockNetChannelModule module)
            {
                module.Install(this);

                return this;
            }

            public ISockNetChannel RemoveModule(ISockNetChannelModule module)
            {
                module.Uninstall(this);

                return this;
            }

            public bool HasModule(ISockNetChannelModule module)
            {
                return false;
            }

            public bool IsActive
            {
                get;
                set;
            }

            public Enum State
            {
                get;
                set;
            }

            public void Connect()
            {
                Pipe.HandleOpened();
            }

            public void Disconnect()
            {
                Pipe.HandleClosed();
            }

            public void Receive(ref object data)
            {
                Pipe.HandleIncomingData(ref data);
            }

            public Promise<ISockNetChannel> Send(object data)
            {
                Pipe.HandleOutgoingData(ref data);

                if (data is ChunkedBuffer)
                {
                    ((ChunkedBuffer)data).Close();
                }

                return new Promise<ISockNetChannel>(this);
            }

            public Promise<ISockNetChannel> Close()
            {
                return new Promise<ISockNetChannel>(this);
            }
        }

        [TestMethod]
        public void TestChunks()
        {
            DummySockNetChannel channel = new DummySockNetChannel()
            {
                State = null,
                IsActive = true,
                BufferPool = SockNetChannelGlobals.GlobalBufferPool
            };
            channel.Pipe = new SockNetChannelPipe(channel);

            GdsSockNetChannelModule module = new GdsSockNetChannelModule(true);
            channel.AddModule(module);
            channel.Connect();

            uint streamId = 1;

            GdsFrame chunk1 = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { "test1", new byte[] { 1 } } ,
                    { "test", new byte[] { 1 } } ,
                }, 
                false, Encoding.UTF8.GetBytes("This "), false);
            ChunkedBuffer buffer = ToBuffer(chunk1);
            object receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            GdsFrame chunk2 = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { "test2", new byte[] { 2 } } ,
                    { "test", new byte[] { 2 } } ,
                },
                false, Encoding.UTF8.GetBytes("is "), false);
            buffer = ToBuffer(chunk2);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            GdsFrame chunk3 = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { "test3", new byte[] { 3 } } ,
                    { "test", new byte[] { 3 } } ,
                },
                false, Encoding.UTF8.GetBytes("awesome!"), true);
            buffer = ToBuffer(chunk3);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is GdsFrame);
            Assert.AreEqual("This is awesome!", Encoding.UTF8.GetString(((GdsFrame)receiveResponse).Body));
            Assert.AreEqual(1, ((GdsFrame)receiveResponse).Headers["test1"][0]);
            Assert.AreEqual(2, ((GdsFrame)receiveResponse).Headers["test2"][0]);
            Assert.AreEqual(3, ((GdsFrame)receiveResponse).Headers["test3"][0]);
            Assert.AreEqual(3, ((GdsFrame)receiveResponse).Headers["test"][0]);

            Console.WriteLine("Pool stats: " + SockNetChannelGlobals.GlobalBufferPool.ObjectsInPool + "/" + SockNetChannelGlobals.GlobalBufferPool.TotalNumberOfObjects);
        }

        public ChunkedBuffer ToBuffer(GdsFrame frame)
        {
            ChunkedBuffer buffer = new ChunkedBuffer(SockNetChannelGlobals.GlobalBufferPool);
            frame.Write(buffer.Stream);
            return buffer;
        }
    }
}
