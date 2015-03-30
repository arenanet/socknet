using System;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Client;

namespace ArenaNet.SockNet.Protocols.Http2
{
    [TestClass]
    public class Http2ClientSockNetChannelModuleTest
    {
        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        private const string TestUrl = "https://http2.golang.org/reqinfo";

        [TestMethod]
        public void TestSimple()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("twitter.com").AddressList[0], 443))
                .AddModule(new Http2ClientSockNetChannelModule(
                    "/",
                    "twitter.com",
                    (ISockNetChannel sockNetClient) => { blockingCollection.Add(true); },
                    new Dictionary<SettingsHttp2Frame.Parameter, uint>() { 
                        { SettingsHttp2Frame.Parameter.SETTINGS_ENABLE_PUSH, 1 }
                    }));

            client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                .WaitForValue(TimeSpan.FromSeconds(5));

            object currentObject;

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue((bool)currentObject);

            client.Pipe.AddIncomingLast<Http2Frame>((ISockNetChannel sockNetClient, ref Http2Frame data) => { Console.WriteLine(data); blockingCollection.Add(data); });

            System.Threading.Thread.Sleep(5000);

            client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
        }
    }
}
