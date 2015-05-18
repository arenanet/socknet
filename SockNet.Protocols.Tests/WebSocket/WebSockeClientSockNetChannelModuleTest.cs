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
using System.Threading;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Server;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.Medley.Pool;
using ArenaNet.Medley.Concurrent;
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

            public BlockingCollection<object> outgoing = new BlockingCollection<object>();
            public BlockingCollection<object> incoming = new BlockingCollection<object>();

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
                incoming.Add(data);

                Pipe.HandleIncomingData(ref data);
            }

            public Promise<ISockNetChannel> Send(object data)
            {
                outgoing.Add(data);

                Pipe.HandleOutgoingData(ref data);

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

            object sent = null;
            channel.outgoing.TryTake(out sent, 5000);
            Assert.IsTrue(sent is HttpRequest);

            HttpRequest request = (HttpRequest)sent;

            HttpResponse handshakeResponse = new HttpResponse(channel.BufferPool) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
            handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = WebSocketUtil.GenerateAccept(request.Header[WebSocketUtil.WebSocketKeyHeader]);
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
        public void TestSmallMessage()
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

            object sent = null;
            channel.outgoing.TryTake(out sent, 5000);
            Assert.IsTrue(sent is HttpRequest);

            HttpResponse handshakeResponse = null;

            using (HttpRequest request = (HttpRequest)sent)
            {
                handshakeResponse = new HttpResponse(null) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
                handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = WebSocketUtil.GenerateAccept(request.Header[WebSocketUtil.WebSocketKeyHeader]);
            }

            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            Random random = new Random(this.GetHashCode() ^ (int)DateTime.Now.Subtract(new DateTime(2000, 1, 1)).TotalMilliseconds);

            for (int n = 0; n < 1000; n++)
            {
                byte[] data = new byte[random.Next(50, 150)];
                random.NextBytes(data);

                WebSocketFrame frame = WebSocketFrame.CreateBinaryFrame(data, false);
                using (ChunkedBuffer buffer = ToBuffer(frame))
                {
                    receiveResponse = buffer;
                    channel.Receive(ref receiveResponse);

                    Assert.IsTrue(receiveResponse is WebSocketFrame);
                    Assert.AreEqual(data.Length, ((WebSocketFrame)receiveResponse).Data.Length);
                    for (int i = 0; i < data.Length; i++)
                    {
                        Assert.AreEqual(data[i], ((WebSocketFrame)receiveResponse).Data[i]);
                    }
                }
            }

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

            object sent = null;
            channel.outgoing.TryTake(out sent, 5000);
            Assert.IsTrue(sent is HttpRequest);

            HttpResponse handshakeResponse = null;

            using (HttpRequest request = (HttpRequest)sent)
            {
                handshakeResponse = new HttpResponse(null) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
                handshakeResponse.Header[WebSocketUtil.WebSocketAcceptHeader] = WebSocketUtil.GenerateAccept(request.Header[WebSocketUtil.WebSocketKeyHeader]);
            }

            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            Random random = new Random(this.GetHashCode() ^ (int)DateTime.Now.Subtract(new DateTime(2000, 1, 1)).TotalMilliseconds);

            for (int n = 0; n < 1000; n++)
            {
                byte[] data = new byte[random.Next(50000, 150000)];
                random.NextBytes(data);

                WebSocketFrame frame = WebSocketFrame.CreateBinaryFrame(data, false);
                using (ChunkedBuffer buffer = ToBuffer(frame))
                {
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
        public void TestLargeMessagesInParallel()
        {
            Random random = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

            ClientSockNetChannel client = null;
            WebSocketEchoServer server = new WebSocketEchoServer();

            try
            {
                server.Start();

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
                    .AddModule(new WebSocketClientSockNetChannelModule("/", "localhost", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue((bool)currentObject);

                client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

                int numberOfMessages = 20;

                byte[][] expectedResults = new byte[numberOfMessages][];

                for (int i = 0; i < numberOfMessages; i++)
                {
                    ThreadPool.QueueUserWorkItem((object state) =>
                    {
                        int index = (int)state;

                        byte[] messageData = new byte[random.Next(75000, 125000)];
                        random.NextBytes(messageData);
                        messageData[0] = (byte)(index >> 0);
                        messageData[1] = (byte)(index >> 8);
                        messageData[2] = (byte)(index >> 16);
                        messageData[3] = (byte)(index >> 24);

                        expectedResults[index] = messageData;

                        client.Send(WebSocketFrame.CreateBinaryFrame(messageData));

                        // simulate GC
                        if (index % Math.Max(numberOfMessages / 10, 1) == 0)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    }, i);
                }

                for (int i = 0; i < numberOfMessages; i++)
                {
                    Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                    Assert.IsTrue(currentObject is WebSocketFrame);

                    // simulate GC
                    if (i % Math.Max(numberOfMessages / 10, 1) == 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    byte[] incomingData = ((WebSocketFrame)currentObject).Data;
                    int index = 0;

                    index |= incomingData[0] << 0;
                    index |= incomingData[1] << 8;
                    index |= incomingData[2] << 16;
                    index |= incomingData[3] << 24;

                    Console.WriteLine(index);

                    AreArraysEqual(expectedResults[index], incomingData);
                }

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                try
                {
                    if (client != null)
                    {
                        client.Close();
                    }
                }
                finally
                {
                    server.Stop();
                }
            }
        }

        [TestMethod]
        public void TestSmallMessagesInParallel()
        {
            Random random = new Random(this.GetHashCode() ^ DateTime.Now.Millisecond);

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

                int numberOfMessages = 100;

                byte[][] expectedResults = new byte[numberOfMessages][];

                for (int i = 0; i < numberOfMessages; i++)
                {
                    ThreadPool.QueueUserWorkItem((object state) =>
                    {
                        int index = (int)state;

                        byte[] messageData = new byte[random.Next(50, 100)];
                        random.NextBytes(messageData);
                        messageData[0] = (byte)(index >> 0);
                        messageData[1] = (byte)(index >> 8);
                        messageData[2] = (byte)(index >> 16);
                        messageData[3] = (byte)(index >> 24);

                        expectedResults[index] = messageData;

                        client.Send(WebSocketFrame.CreateBinaryFrame(messageData));

                        // simulate GC
                        if (index % Math.Max(numberOfMessages / 10, 1) == 0)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    }, i);
                }

                for (int i = 0; i < numberOfMessages; i++)
                {
                    Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                    Assert.IsTrue(currentObject is WebSocketFrame);

                    // simulate GC
                    if (i % Math.Max(numberOfMessages / 10, 1) == 0)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    byte[] incomingData = ((WebSocketFrame)currentObject).Data;
                    int index = 0;

                    index |= incomingData[0] << 0;
                    index |= incomingData[1] << 8;
                    index |= incomingData[2] << 16;
                    index |= incomingData[3] << 24;

                    Console.WriteLine(index);

                    AreArraysEqual(expectedResults[index], incomingData);
                }

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        public static void AreArraysEqual<T>(T[] l, T[] r)
        {
            Assert.IsNotNull(l);
            Assert.IsNotNull(r);
            Assert.AreEqual(l.Length, r.Length);

            for (int i = 0; i < l.Length; i++)
            {
                Assert.AreEqual(l[i], r[i]);
            }
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
        public void TestIncompleteBufferParsing()
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

                string body1 = new string('A', 4913) + "X";

                WebSocketFrame frame1 = WebSocketFrame.CreateTextFrame(
                    "STS/1.0 200 OK" + "\r\n" +
                    "s:7R" + "\r\n" +
                    "n:bytes 0-4915/5000" + "\r\n" +
                    "l:4916" + "\r\n" +
                    "" + "\r\n" +
                    body1 + "\r\n",
                    false);
                client.Send(frame1);

                string body2 = new string('B', 81) + "Y";

                WebSocketFrame frame2 = WebSocketFrame.CreateTextFrame(
                    "STS/1.0 200 OK" + "\r\n" +
                    "s:7R" + "\r\n" +
                    "n:bytes 4916-4999/5000" + "\r\n" +
                    "l:84" + "\r\n" +
                    "" + "\r\n" +
                    body2 + "\r\n",
                    false);

                client.Send(frame2);

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual(frame1.DataAsString, ((WebSocketFrame)currentObject).DataAsString);

                Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
                Assert.IsTrue(currentObject is WebSocketFrame);

                Assert.AreEqual(frame2.DataAsString, ((WebSocketFrame)currentObject).DataAsString);

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
