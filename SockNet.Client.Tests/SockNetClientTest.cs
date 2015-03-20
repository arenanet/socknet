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

namespace ArenaNet.SockNet.Client
{
    [TestClass]
    public class SockNetClientTest
    {
        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        [TestMethod]
        public void TestConnectWithoutSsl()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80));

            client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

            object currentObject;

            client.Pipe.AddIncomingFirst<ChunkedBuffer>((ISockNetChannel sockNetClient, ref ChunkedBuffer data) => { blockingCollection.Add(data); });

            client.Send(Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost: www.guildwars2.com\r\n\r\n"));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is ChunkedBuffer);

            Console.WriteLine("Got response: \n" + new StreamReader(((ChunkedBuffer)currentObject).Stream, Encoding.UTF8).ReadToEnd());

            client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public void TestConnectWithoutSslWithStreamSending()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80), false, 32);

            client.Connect().WaitForValue(TimeSpan.FromSeconds(5));

            object currentObject;

            client.Pipe.AddIncomingFirst<ChunkedBuffer>((ISockNetChannel sockNetClient, ref ChunkedBuffer data) => { blockingCollection.Add(data); });

            ChunkedBuffer buffer = new ChunkedBuffer(client.BufferPool);
            byte[] sendData = Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost: www.guildwars2.com\r\n\r\n");
            buffer.Write(sendData, 0, sendData.Length);

            client.Send(buffer);

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is ChunkedBuffer);

            Console.WriteLine("Got response: \n" + new StreamReader(((ChunkedBuffer)currentObject).Stream, Encoding.UTF8).ReadToEnd());

            client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public void TestConnectWithSsl()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            ClientSockNetChannel client = SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 443));

            client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                .WaitForValue(TimeSpan.FromSeconds(5));

            object currentObject;

            client.Pipe.AddIncomingFirst<ChunkedBuffer>((ISockNetChannel sockNetClient, ref ChunkedBuffer data) => { blockingCollection.Add(data); });

            client.Send(Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost: www.guildwars2.com\r\n\r\n"));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is ChunkedBuffer);

            Console.WriteLine("Got response: \n" + new StreamReader(((ChunkedBuffer)currentObject).Stream, Encoding.UTF8).ReadToEnd());

            client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5));
        }
    }
}
