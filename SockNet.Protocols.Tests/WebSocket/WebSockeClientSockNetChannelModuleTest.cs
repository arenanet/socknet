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
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Server;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;
using ArenaNet.SockNet.Client;
using ArenaNet.SockNet.Protocols.Http;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    [TestClass]
    public class WebSockeClientSockNetChannelModuleTest
    {
        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        public class WebSocketEchoServer
        {
            private ServerSockNetChannel server;

            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }

            public void Start(bool isTls = false)
            {
                server = SockNetServer.Create(GetLocalIpAddress(), 0);

                try
                {
                    server.AddModule(new WebSocketServerSockNetChannelModule("/", "localhost"));

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

                    server.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel channel, ref WebSocketFrame data) =>
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
            public string Id { get { return "1"; } }

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
        }

        [TestMethod]
        public void TestContinuation()
        {
            DummySockNetChannel channel = new DummySockNetChannel()
            {
                State = null,
                IsActive = true,
                BufferPool = SockNetChannelGlobals.GlobalBufferPool
            };
            channel.Pipe = new SockNetChannelPipe(channel);

            WebSocketClientSockNetChannelModule module = new WebSocketClientSockNetChannelModule("/test", "test", null);
            channel.AddModule(module);
            channel.Connect();

            HttpResponse handshakeResponse = new HttpResponse(channel.BufferPool) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
            handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = module.ExpectedAccept;
            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            WebSocketFrame continuation1 = WebSocketFrame.CreateTextFrame("This ", false, false, false);
            ChunkedBuffer buffer = ToBuffer(continuation1);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            WebSocketFrame continuation2 = WebSocketFrame.CreateTextFrame("is ", false, true, false);
            buffer = ToBuffer(continuation2);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            WebSocketFrame continuation3 = WebSocketFrame.CreateTextFrame("awesome!", false, true, true);
            buffer = ToBuffer(continuation3);
            receiveResponse = buffer;
            channel.Receive(ref receiveResponse);
            buffer.Close();

            Assert.IsTrue(receiveResponse is WebSocketFrame);
            Assert.AreEqual("This is awesome!", ((WebSocketFrame)receiveResponse).DataAsString);

            Console.WriteLine("Pool stats: " + SockNetChannelGlobals.GlobalBufferPool.ObjectsInPool + "/" + SockNetChannelGlobals.GlobalBufferPool.TotalNumberOfObjects);
        }

        [TestMethod]
        public void TestLargeMessage()
        {
            DummySockNetChannel channel = new DummySockNetChannel()
            {
                State = null,
                IsActive = true,
                BufferPool = SockNetChannelGlobals.GlobalBufferPool
            };
            channel.Pipe = new SockNetChannelPipe(channel);

            WebSocketClientSockNetChannelModule module = new WebSocketClientSockNetChannelModule("/test", "test", null);
            channel.AddModule(module);
            channel.Connect();

            HttpResponse handshakeResponse = new HttpResponse(null) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
            handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = module.ExpectedAccept;
            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            Random random = new Random(this.GetHashCode() ^ (int)DateTime.Now.Subtract(new DateTime(2000, 1, 1)).TotalMilliseconds);

            for (int n = 0; n < 100; n++)
            {
                byte[] data = new byte[random.Next(50000, 150000)];
                random.NextBytes(data);

                WebSocketFrame frame = WebSocketFrame.CreateBinaryFrame(data, false);
                ChunkedBuffer buffer = ToBuffer(frame);
                receiveResponse = buffer;
                channel.Receive(ref receiveResponse);

                Assert.IsTrue(receiveResponse is WebSocketFrame);
                Assert.AreEqual(data.Length, ((WebSocketFrame)receiveResponse).Data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    Assert.AreEqual(data[i], ((WebSocketFrame)receiveResponse).Data[i]);
                }
                buffer.Close();
            }

            Console.WriteLine("Pool stats: " + SockNetChannelGlobals.GlobalBufferPool.ObjectsInPool + "/" + SockNetChannelGlobals.GlobalBufferPool.TotalNumberOfObjects);
        }

        public ChunkedBuffer ToBuffer(WebSocketFrame frame)
        {
            ChunkedBuffer buffer = new ChunkedBuffer(SockNetChannelGlobals.GlobalBufferPool);
            frame.Write(buffer.Stream, true);
            return buffer;
        }

        [TestMethod]
        public void TestEchoWithMask()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                client.Send(WebSocketFrame.CreateTextFrame("some test", true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestEchoWithMaskWithSsl()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start(true);

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                    .WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                client.Send(WebSocketFrame.CreateTextFrame("some test", true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestEchoWithoutMask()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                client.Send(WebSocketFrame.CreateTextFrame("some test", true));

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestLotsOfMessages()
        {
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                byte[] randomData = new byte[2000]; 
                new Random(GetHashCode() ^ DateTime.Now.Millisecond).NextBytes(randomData);

                for (int i = 0; i < 1000; i++)
                {
                    client.Send(WebSocketFrame.CreateBinaryFrame(randomData, false));
                }

                int receivedMessages = 0;

                for (int i = 0; i < 1000; i++)
                {
                    if (receivedMessages < 1000 && blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT))
                    {
                        receivedMessages++;
                    }
                    else
                    {
                        break;
                    }
                }

                Assert.AreEqual(1000, receivedMessages);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
