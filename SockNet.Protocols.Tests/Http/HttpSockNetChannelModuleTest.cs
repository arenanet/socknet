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

namespace ArenaNet.SockNet.Protocols.Http
{
    [TestClass]
    public class HttpSockNetChannelModuleTest
    {
        [TestMethod]
        public void TestSimpleGet()
        {
            BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80))
                .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
            client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });
            client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

            HttpRequest request = new HttpRequest()
            {
                Action = "GET",
                Path = "/en/",
                Version = "HTTP/1.1"
            };
            request.Header["Host"] = "www.guildwars2.com";

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

        [TestMethod]
        public void TestSimpleGetHttps()
        {
            BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 443))
                .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
            client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });
            client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                .WaitForValue(TimeSpan.FromSeconds(5));

            HttpRequest request = new HttpRequest()
            {
                Action = "GET",
                Path = "/en/",
                Version = "HTTP/1.1"
            };
            request.Header["Host"] = "www.guildwars2.com";

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

        [TestMethod]
        public void TestChunked()
        {
            BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.httpwatch.com").AddressList[0], 80))
                .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
            client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });
            Assert.IsNotNull(client.Connect().WaitForValue(TimeSpan.FromSeconds(5)));

            HttpRequest request = new HttpRequest()
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

                server.Pipe.AddIncomingFirst<HttpRequest>((ISockNetChannel channel, ref HttpRequest data) =>
                {
                    HttpResponse response = new HttpResponse()
                    {
                        Version = data.Version,
                        Code = "200",
                        Reason = "OK"
                    };
                    response.Header["Content-Length"] = "" + data.BodySize;
                    data.Body.Position = 0;
                    Copy(data.Body, response.Body, data.BodySize);
                    response.BodySize = data.BodySize;

                    channel.Send(response).OnFulfilled = (ISockNetChannel value, Exception e, Promise<ISockNetChannel> promise) =>
                    {
                        value.Close();
                    };
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
