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
            client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

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
    }
}
