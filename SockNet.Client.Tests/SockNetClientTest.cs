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
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Server;
using ArenaNet.SockNet.Protocols.Http;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Client
{
    [TestClass]
    public class SockNetClientTest
    {
        public class HttpSimpleServer
        {
            private ObjectPool<byte[]> pool;
            private ServerSockNetChannel server;

            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }

            public HttpSimpleServer(ObjectPool<byte[]> pool)
            {
                this.pool = pool;
            }

            public void Start(bool isTls = false, string body = "")
            {
                server = SockNetServer.Create(GetLocalIpAddress(), 0, ServerSockNetChannel.DefaultBacklog, pool);

                try
                {
                    server.AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Server));

                    server.Pipe.AddIncomingLast<HttpRequest>((ISockNetChannel channel, ref HttpRequest data) =>
                    {
                        HttpResponse response = new HttpResponse(channel.BufferPool)
                        {
                            Version = data.Version,
                            Code = "200",
                            Reason = "OK"
                        };

                        response.Body = ChunkedBuffer.Wrap(body, Encoding.UTF8);

                        response.Header["Content-Length"] = "" + response.BodySize;

                        channel.Send(response);
                    });

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

        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        [TestMethod]
        public void TestConnectWithoutSsl()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            string text = "some great text here...";
            HttpSimpleServer server = new HttpSimpleServer(pool);

            try
            {
                server.Start(false, text);

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));

                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel sockNetClient, ref HttpResponse data) => { blockingCollection.Add(data); });

                client.Send(new HttpRequest(client.BufferPool) { Action = "GET", Path = "/", Version = "HTTP/1.0" });

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));

                Assert.IsTrue(currentObject is HttpResponse);

                Console.WriteLine("Got " + currentObject.GetType() + ": \n" + currentObject);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestConnectWithSsl()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            string text = "some great text here...";
            HttpSimpleServer server = new HttpSimpleServer(pool);

            try
            {
                server.Start(true, text);

                BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));

                client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                    .WaitForValue(TimeSpan.FromSeconds(5));

                object currentObject;

                client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel sockNetClient, ref HttpResponse data) => { blockingCollection.Add(data); });

                client.Send(new HttpRequest(client.BufferPool) { Action = "GET", Path = "/", Version = "HTTP/1.0" });

                Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));

                Assert.IsTrue(currentObject is HttpResponse);

                Console.WriteLine("Got " + currentObject.GetType() + ": \n" + currentObject);

                client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
