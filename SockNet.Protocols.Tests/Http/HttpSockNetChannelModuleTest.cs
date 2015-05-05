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
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Client;
using ArenaNet.SockNet.Server;
using ArenaNet.Medley.Concurrent;

namespace ArenaNet.SockNet.Protocols.Http
{
    [TestClass]
    public class HttpSockNetChannelModuleTest
    {
        public class HttpEchoServer
        {
            private ServerSockNetChannel server;

            public IPEndPoint Endpoint { get { return new IPEndPoint(GetLocalIpAddress(), server == null ? -1 : server.LocalEndpoint.Port); } }

            public void Start(bool isTls = false)
            {
                server = SockNetServer.Create(GetLocalIpAddress(), 0);

                try
                {
                    server.AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Server));

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
            HttpEchoServer server = new HttpEchoServer();

            try
            {
                server.Start();

                BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
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
            HttpEchoServer server = new HttpEchoServer();

            try
            {
                server.Start(true);

                BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

                ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(server.Endpoint)
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
            BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.httpwatch.com").AddressList[0], 80))
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

        [TestMethod]
        public void TestServer()
        {
            const String testString = "what a great test!";

            ServerSockNetChannel server = (ServerSockNetChannel)SockNetServer.Create(GetLocalIpAddress(), 0)
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
