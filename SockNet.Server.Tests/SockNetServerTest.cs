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
using ArenaNet.SockNet.Client;

namespace ArenaNet.SockNet.Server
{
    [TestClass]
    public class SockNetServerTest
    {
        [TestMethod]
        public void TestBindWithoutSsl()
        {
            ServerSockNetChannel server = SockNetServer.Create(GetLocalIpAddress(), 0);

            try
            {
                server.Bind();

                server.Pipe.AddIncomingFirst<Stream>((ISockNetChannel channel, ref Stream data) => 
                {
                    channel.Send(data);
                });

                BlockingCollection<string> incomingData = new BlockingCollection<string>();

                ClientSockNetChannel client = SockNetClient.Create(GetLocalIpAddress(), server.LocalEndpoint.Port);
                Assert.IsNotNull(client.Connect().WaitForValue(TimeSpan.FromSeconds(5)));

                client.Pipe.AddIncomingFirst((ISockNetChannel channel, ref Stream data) =>
                {
                    StreamReader reader = new StreamReader(data);

                    incomingData.Add(reader.ReadToEnd());
                });

                client.Send(Encoding.UTF8.GetBytes("a test!"));

                string incomingValue = null;

                Assert.IsTrue(incomingData.TryTake(out incomingValue, TimeSpan.FromSeconds(5)));
                Assert.AreEqual("a test!", incomingValue);
            }
            finally
            {
                server.Close();
            }
        }

        public static IPAddress GetLocalIpAddress()
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
