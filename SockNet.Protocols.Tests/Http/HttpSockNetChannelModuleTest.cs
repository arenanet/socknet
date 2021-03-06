﻿/*
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
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Client;
using ArenaNet.SockNet.Server;
using ArenaNet.Medley.Concurrent;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Protocols.Http
{
    [TestClass]
    public class HttpSockNetChannelModuleTest
    {
        public class HttpChunkedServer
        {
            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }

            private ObjectPool<byte[]> pool;
            private ServerSockNetChannel server;

            public HttpChunkedServer(ObjectPool<byte[]> pool)
            {
                this.pool = pool;
            }

            public void Start(bool isTls = false)
            {
                string sampleContent = "<test><val>hello</val></test>";

                int sampleContentLength = Encoding.UTF8.GetByteCount(sampleContent);

                string chunk1Content = "<test><val>";
                string chunk2Content = "hello</val>";
                string chunk3Content = "</test>";

                int chunk1ContentLength = Encoding.UTF8.GetByteCount(chunk1Content);
                int chunk2ContentLength = Encoding.UTF8.GetByteCount(chunk2Content);
                int chunk3ContentLength = Encoding.UTF8.GetByteCount(chunk3Content);

                string chunk1HttpContent = "HTTP/1.0 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" + string.Format("{0:X}", chunk1ContentLength) + "\r\n" + chunk1Content + "\r\n";
                string chunk2HttpContent = string.Format("{0:X}", chunk2ContentLength) + "\r\n" + chunk2Content + "\r\n";
                string chunk3HttpContent = string.Format("{0:X}", chunk3ContentLength) + "\r\n" + chunk3Content + "\r\n";
                string chunk4HttpContent = "0\r\n\r\n";

                server = SockNetServer.Create(GetLocalIpAddress(), 0, ServerSockNetChannel.DefaultBacklog, pool);

                try
                {
                    server.AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Server));

                    server.Pipe.AddIncomingLast<HttpRequest>((ISockNetChannel channel, ref HttpRequest data) =>
                    {
                        ChunkedBuffer buffer1 = new ChunkedBuffer(channel.BufferPool);
                        buffer1.Write(Encoding.ASCII.GetBytes(chunk1HttpContent), 0, Encoding.ASCII.GetByteCount(chunk1HttpContent));
                        channel.Send(buffer1);

                        ChunkedBuffer buffer2 = new ChunkedBuffer(channel.BufferPool);
                        buffer2.Write(Encoding.ASCII.GetBytes(chunk2HttpContent), 0, Encoding.ASCII.GetByteCount(chunk2HttpContent));
                        channel.Send(buffer2);

                        ChunkedBuffer buffer3 = new ChunkedBuffer(channel.BufferPool);
                        buffer3.Write(Encoding.ASCII.GetBytes(chunk3HttpContent), 0, Encoding.ASCII.GetByteCount(chunk3HttpContent));
                        channel.Send(buffer3);

                        ChunkedBuffer buffer4 = new ChunkedBuffer(channel.BufferPool);
                        buffer4.Write(Encoding.ASCII.GetBytes(chunk4HttpContent), 0, Encoding.ASCII.GetByteCount(chunk4HttpContent));
                        channel.Send(buffer4);
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
        }

        public class HttpEchoServer
        {
            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }
            
            private ObjectPool<byte[]> pool;
            private ServerSockNetChannel server;

            public HttpEchoServer(ObjectPool<byte[]> pool)
            {
                this.pool = pool;
            }

            public void Start(bool isTls = false)
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

                        foreach (string headerName in data.Headers.Names)
                        {
                            response.Headers[headerName] = data.Headers[headerName];
                        }

                        response.Body = data.Body;

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
        }

        [TestMethod]
        public void TestSimpleGet()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            HttpEchoServer server = new HttpEchoServer(pool);

            try
            {
                server.Start();

                BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
                client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });
                client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

                HttpRequest request = new HttpRequest(client.BufferPool)
                {
                    Action = "GET",
                    Path = "/en/",
                    Version = "HTTP/1.1"
                };
                request.Header["Host"] = "localhost";

                client.Send(request);

                HttpResponse response = null;
                responses.TryTake(out response, 5000);

                Assert.IsNotNull(response);

                Assert.IsNotNull(response.Version);
                Assert.IsNotNull(response.Code);
                Assert.IsNotNull(response.Reason);

                MemoryStream stream = new MemoryStream();
                response.Write(stream, false);
                stream.Position = 0;

                using (StreamReader reader = new StreamReader(stream))
                {
                    Console.WriteLine("Got response: " + reader.ReadToEnd());
                }
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestSimpleGetHttps()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            HttpEchoServer server = new HttpEchoServer(pool);

            try
            {
                server.Start(true);

                BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
                client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });
                client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                    .WaitForValue(TimeSpan.FromSeconds(5));

                HttpRequest request = new HttpRequest(client.BufferPool)
                {
                    Action = "GET",
                    Path = "/",
                    Version = "HTTP/1.1"
                };
                request.Header["Host"] = "localhost";
                request.Header["Connection"] = "Close";

                client.Send(request);

                HttpResponse response = null;
                responses.TryTake(out response, 5000);

                Assert.IsNotNull(response);

                Assert.IsNotNull(response.Version);
                Assert.IsNotNull(response.Code);
                Assert.IsNotNull(response.Reason);

                MemoryStream stream = new MemoryStream();
                response.Write(stream, false);
                stream.Position = 0;

                using (StreamReader reader = new StreamReader(stream))
                {
                    Console.WriteLine("Got response: " + reader.ReadToEnd());
                }
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestChunked()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            HttpChunkedServer server = new HttpChunkedServer(pool);

            try
            {
                server.Start(false);

                BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool)
                    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
                client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });
                Assert.IsNotNull(client.Connect().WaitForValue(TimeSpan.FromSeconds(5)));

                HttpRequest request = new HttpRequest(client.BufferPool)
                {
                    Action = "GET",
                    Path = "/httpgallery/chunked/chunkedimage.aspx",
                    Version = "HTTP/1.1"
                };
                request.Header["Host"] = "www.httpwatch.com";

                client.Send(request);

                HttpResponse response = null;
                responses.TryTake(out response, 10000);

                Assert.IsNotNull(response);

                Assert.IsNotNull(response.Version);
                Assert.IsNotNull(response.Code);
                Assert.IsNotNull(response.Reason);

                MemoryStream stream = new MemoryStream();
                response.Write(stream, false);
                stream.Position = 0;

                using (StreamReader reader = new StreamReader(stream))
                {
                    Console.WriteLine("Got response: " + reader.ReadToEnd());
                }
            }
            finally
            {
                server.Stop();
            }
        }

        [TestMethod]
        public void TestServer()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            const String testString = "what a great test!";

            ServerSockNetChannel server = (ServerSockNetChannel)SockNetServer.Create(GetLocalIpAddress(), 0, ServerSockNetChannel.DefaultBacklog, pool)
                .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Server));

            try
            {
                Assert.IsNotNull(server.Bind().WaitForValue(TimeSpan.FromSeconds(5)));

                server.Pipe.AddIncomingLast<HttpRequest>((ISockNetChannel channel, ref HttpRequest data) =>
                {
                    HttpResponse response = new HttpResponse(channel.BufferPool)
                    {
                        Version = data.Version,
                        Code = "200",
                        Reason = "OK"
                    };
                    response.Header["Content-Length"] = "" + data.BodySize;
                    Copy(data.Body.Stream, response.Body.Stream, data.BodySize);

                    Promise<ISockNetChannel> sendPromise = channel.Send(response);
                    if (("http/1.0".Equals(data.Version) && !"keep-alive".Equals(data.Header["connection"], StringComparison.CurrentCultureIgnoreCase)) ||
                        "close".Equals(data.Header["connection"], StringComparison.CurrentCultureIgnoreCase))
                    {
                        sendPromise.OnFulfilled = (ISockNetChannel value, Exception e, Promise<ISockNetChannel> promise) =>
                        {
                            value.Close();
                        };
                    }
                });

                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create("http://" + GetLocalIpAddress() + ":" + server.LocalEndpoint.Port);
                request.KeepAlive = false;
                request.Timeout = 5000;
                request.Method = "POST";

                using (StreamWriter writer = new StreamWriter(request.GetRequestStream(), Encoding.Default))
                {
                    writer.Write(testString);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    Assert.AreEqual(200, (int)response.StatusCode);
                    Assert.AreEqual("OK", response.StatusDescription);

                    Assert.AreEqual(Encoding.Default.GetByteCount(testString), response.ContentLength);

                    using (StreamReader responseReader = new StreamReader(response.GetResponseStream(), Encoding.Default))
                    {
                        Assert.AreEqual(testString, responseReader.ReadToEnd());
                    }
                }
            }
            finally
            {
                server.Close();
            }
        }

        private static int Copy(Stream input, Stream output, int count)
        {
            byte[] buffer = new byte[1024];
            int totalRead = 0;
            int read;

            while (totalRead < count && (read = input.Read(buffer, 0, Math.Min(buffer.Length, count - totalRead))) > 0)
            {
                output.Write(buffer, 0, read);
                totalRead += read;
            }

            return totalRead;
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
}
