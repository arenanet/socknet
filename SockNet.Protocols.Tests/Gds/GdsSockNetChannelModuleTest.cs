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
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;
using ArenaNet.SockNet.Client;
using ArenaNet.SockNet.Server;
using ArenaNet.SockNet.Protocols.Http;

namespace ArenaNet.SockNet.Protocols.Gds
{
    [TestClass]
    public class GdsSockNetChannelModuleTest
    {
        public class GdsEchoServer
        {
            private ObjectPool<byte[]> pool;

            private ServerSockNetChannel server;

            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }

            public GdsEchoServer(ObjectPool<byte[]> pool)
            {
                this.pool = pool;
            }

            public void Start(bool isTls = false)
            {
                server = SockNetServer.Create(GetLocalIpAddress(), 0, ServerSockNetChannel.DefaultBacklog, pool);

                try
                {
                    server.AddModule(new GdsSockNetChannelModule(true));

                    if (isTls)
                    {
                        byte[] rawCert = CertificateUtil.CreateSelfSignCertificatePfx("CN=\"test\"; C=\"USA\"", DateTime.Today.AddDays(-10), DateTime.Today.AddDays(+10));

                        server.BindWithTLS(new X509Certificate2(rawCert),
                            (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; }).WaitForValue(TimeSpan.FromSeconds(5));
                    }
                    else
                    {
                        server.Bind().WaitForValue(TimeSpan.FromSeconds(5));
                    }

                    Assert.IsTrue(server.IsActive);

                    server.Pipe.AddIncomingLast<GdsFrame>((ISockNetChannel channel, ref GdsFrame data) =>
                    {
                        channel.Send(data);
                    });
                }
                catch (Exception)
                {
                    Stop();
                }
            }

            public void Stop()
            {
                if (server != null)
                {
                    server.Close();
                    server = null;
                }
            }

            private static IPAddress GetLocalIpAddress()
            {
                IPAddress response = null;

                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        response = address;
                        break;
                    }
                }

                return response;
            }
        }

        public class DummySockNetChannel : ISockNetChannel
        {
            public string Id { get { return "1";  } }

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

            public SockNetChannelProtocol Protocol
            {
                get { return SockNetChannelProtocol.Tcp; }
            }

            private ConcurrentDictionary<string, object> attributes = new ConcurrentDictionary<string, object>();

            public bool SetAttribute<T>(string name, T value, bool upsert = true)
            {
                return attributes.TryAdd(name, (object)value);
            }

            public bool RemoveAttribute(string name)
            {
                object ignore;
                return attributes.TryRemove(name, out ignore);
            }

            public bool TryGetAttribute<T>(string name, out T value)
            {
                object response;
                bool success = attributes.TryGetValue(name, out response);
                value = (T)response;

                return success;
            }
        }

        [TestMethod]
        public void TestSimpleContent()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            GdsEchoServer server = new GdsEchoServer(pool);

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new GdsSockNetChannelModule(true));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                client.Pipe.AddIncomingLast<GdsFrame>((ISockNetChannel sockNetClient, ref GdsFrame data) => { blockingCollection.Add(data); });

                ChunkedBuffer body = new ChunkedBuffer(pool);
                body.OfferRaw(Encoding.UTF8.GetBytes("some test"), 0, Encoding.UTF8.GetByteCount("some test"));

                client.Send(GdsFrame.NewContentFrame(1, null, false, body, true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, 5000));
                Assert.IsTrue(currentObject is GdsFrame);

                Assert.AreEqual("some test", ((GdsFrame)currentObject).Body.ToString(Encoding.UTF8));

                Console.WriteLine("Got response: \n" + ((GdsFrame)currentObject).Body);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestSimpleSslContent()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            GdsEchoServer server = new GdsEchoServer(pool);

            try
            {
                server.Start(true);

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new GdsSockNetChannelModule(true));

                client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                    .WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                client.Pipe.AddIncomingLast<GdsFrame>((ISockNetChannel sockNetClient, ref GdsFrame data) => { blockingCollection.Add(data); });

                ChunkedBuffer body = new ChunkedBuffer(pool);
                body.OfferRaw(Encoding.UTF8.GetBytes("some test"), 0, Encoding.UTF8.GetByteCount("some test"));

                client.Send(GdsFrame.NewContentFrame(1, null, false, body, true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, 5000));
                Assert.IsTrue(currentObject is GdsFrame);

                Assert.AreEqual("some test", ((GdsFrame)currentObject).Body.ToString(Encoding.UTF8));

                Console.WriteLine("Got response: \n" + ((GdsFrame)currentObject).Body);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestChunks()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            DummySockNetChannel channel = new DummySockNetChannel()
            {
                State = null,
                IsActive = true,
                BufferPool = pool
            };
            channel.Pipe = new SockNetChannelPipe(channel);

            GdsSockNetChannelModule module = new GdsSockNetChannelModule(true);
            channel.AddModule(module);
            channel.Connect();

            uint streamId = 1;

            ChunkedBuffer body = new ChunkedBuffer(pool);
            body.OfferRaw(Encoding.UTF8.GetBytes("This "), 0, Encoding.UTF8.GetByteCount("This "));

            GdsFrame chunk1 = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { "test1", new byte[] { 1 } } ,
                    { "test", new byte[] { 1 } } ,
                },
                false, body, false);
            ChunkedBuffer buffer = ToBuffer(chunk1);
            object receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            body = new ChunkedBuffer(pool);
            body.OfferRaw(Encoding.UTF8.GetBytes("is "), 0, Encoding.UTF8.GetByteCount("is "));

            GdsFrame chunk2 = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { "test2", new byte[] { 2 } } ,
                    { "test", new byte[] { 2 } } ,
                },
                false, body, false);
            buffer = ToBuffer(chunk2);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            body = new ChunkedBuffer(pool);
            body.OfferRaw(Encoding.UTF8.GetBytes("awesome!"), 0, Encoding.UTF8.GetByteCount("awesome!"));

            GdsFrame chunk3 = GdsFrame.NewContentFrame(streamId, new Dictionary<string, byte[]>() 
                { 
                    { "test3", new byte[] { 3 } } ,
                    { "test", new byte[] { 3 } } ,
                },
                false, body, true);
            buffer = ToBuffer(chunk3);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is GdsFrame);
            Assert.AreEqual("This is awesome!", ((GdsFrame)receiveResponse).Body.ToString(Encoding.UTF8));
            Assert.AreEqual(1, ((GdsFrame)receiveResponse).Headers["test1"][0]);
            Assert.AreEqual(2, ((GdsFrame)receiveResponse).Headers["test2"][0]);
            Assert.AreEqual(3, ((GdsFrame)receiveResponse).Headers["test3"][0]);
            Assert.AreEqual(3, ((GdsFrame)receiveResponse).Headers["test"][0]);

            body.Dispose();
            chunk1.Dispose();
            chunk2.Dispose();
            chunk3.Dispose();

            Console.WriteLine("Pool stats: " + pool.ObjectsInPool + "/" + pool.TotalNumberOfObjects);
        }

        public ChunkedBuffer ToBuffer(GdsFrame frame)
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            ChunkedBuffer buffer = new ChunkedBuffer(pool);
            frame.Write(buffer.Stream);
            return buffer;
        }
    }
}
