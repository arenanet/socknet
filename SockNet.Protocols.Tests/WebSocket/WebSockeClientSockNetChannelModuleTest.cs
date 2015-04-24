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

                return new Promise<ISockNetChannel>(this);
            }

            public Promise<ISockNetChannel> Close()
            {
                return new Promise<ISockNetChannel>(this);
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
            handshakeResponse.Header[WebSocketClientSockNetChannelModule.WebSocketAcceptHeader] = module.ExpectedAccept;
            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            WebSocketFrame continuation1 = WebSocketFrame.CreateTextFrame("This ", false, false, false);
            receiveResponse = ToBuffer(continuation1);
            channel.Receive(ref receiveResponse);

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            WebSocketFrame continuation2 = WebSocketFrame.CreateTextFrame("is ", false, true, false);
            receiveResponse = ToBuffer(continuation2);
            channel.Receive(ref receiveResponse);

            Assert.IsTrue(receiveResponse is ChunkedBuffer);

            WebSocketFrame continuation3 = WebSocketFrame.CreateTextFrame("awesome!", false, true, true);
            receiveResponse = ToBuffer(continuation3);
            channel.Receive(ref receiveResponse);

            Assert.IsTrue(receiveResponse is WebSocketFrame);
            Assert.AreEqual("This is awesome!", ((WebSocketFrame)receiveResponse).DataAsString);
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

            HttpResponse handshakeResponse = new HttpResponse(channel.BufferPool) { Code = "200", Reason = "OK", Version = "HTTP/1.1" };
            handshakeResponse.Header[WebSocketClientSockNetChannelModule.WebSocketAcceptHeader] = module.ExpectedAccept;
            object receiveResponse = handshakeResponse;
            channel.Receive(ref receiveResponse);

            Random random = new Random(this.GetHashCode() ^ (int)DateTime.Now.Subtract(new DateTime(2000, 1, 1)).TotalMilliseconds);

            for (int n = 0; n < 100; n++)
            {
                byte[] data = new byte[random.Next(50000, 150000)];
                random.NextBytes(data);

                WebSocketFrame frame = WebSocketFrame.CreateBinaryFrame(data, false);
                receiveResponse = ToBuffer(frame);
                channel.Receive(ref receiveResponse);

                Assert.IsTrue(receiveResponse is WebSocketFrame);
                Assert.AreEqual(data.Length, ((WebSocketFrame)receiveResponse).Data.Length);
                for (int i = 0; i < data.Length; i++)
                {
                    Assert.AreEqual(data[i], ((WebSocketFrame)receiveResponse).Data[i]);
                }
            }
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
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 80))
                .AddModule(new WebSocketClientSockNetChannelModule("/", "echo.websocket.org", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

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

        [TestMethod]
        public void TestEchoWithMaskWithSsl()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 443))
                .AddModule(new WebSocketClientSockNetChannelModule("/", "echo.websocket.org", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

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

        [TestMethod]
        public void TestEchoWithoutMask()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 80))
                .AddModule(new WebSocketClientSockNetChannelModule("/", "echo.websocket.org", (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); }));

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
    }
}
