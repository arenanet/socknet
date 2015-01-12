using System;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet
{
    [TestClass]
    public class SockNetClientTest
    {
        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        [TestMethod]
        public void TestConnectWithoutSsl()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            SockNetClient client = new SockNetClient(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80), false);
            client.OnConnect += (SockNetClient sockNet) => { blockingCollection.Add(true); };
            client.OnDisconnect += (SockNetClient sockNet) => { blockingCollection.Add(false); };

            client.Connect();

            object currentObject;

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue((bool)currentObject);

            client.AddIncomingDataHandlerFirst<ArraySegment<byte>>((SockNetClient sockNetClient, ref ArraySegment<byte> data) => { blockingCollection.Add(data); });

            client.Send(Encoding.UTF8.GetBytes("GET / HTTP/1.1\nHost: www.guildwars2.com\n\n"));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is ArraySegment<byte>);

            Console.WriteLine("Got response: \n" + Encoding.UTF8.GetString(((ArraySegment<byte>)currentObject).Array, ((ArraySegment<byte>)currentObject).Offset, ((ArraySegment<byte>)currentObject).Count));

            client.Disconnect();

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsFalse((bool)currentObject);
        }

        [TestMethod]
        public void TestConnectWithSsl()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            SockNetClient client = new SockNetClient(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 443), true);
            client.CertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; }; // accept  all server certs
            client.OnConnect += (SockNetClient sockNet) => { blockingCollection.Add(true); };
            client.OnDisconnect += (SockNetClient sockNet) => { blockingCollection.Add(false); };

            client.Connect();

            object currentObject;

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue((bool)currentObject);

            client.AddIncomingDataHandlerFirst<ArraySegment<byte>>((SockNetClient sockNetClient, ref ArraySegment<byte> data) => { blockingCollection.Add(data); });

            client.Send(Encoding.UTF8.GetBytes("GET / HTTP/1.1\nHost: www.guildwars2.com\n\n"));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is ArraySegment<byte>);

            Console.WriteLine("Got response: \n" + Encoding.UTF8.GetString(((ArraySegment<byte>)currentObject).Array, ((ArraySegment<byte>)currentObject).Offset, ((ArraySegment<byte>)currentObject).Count));

            client.Disconnect();

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsFalse((bool)currentObject);
        }
    }
}
