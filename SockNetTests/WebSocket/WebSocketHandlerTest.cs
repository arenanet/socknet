using System;
using System.Text;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.WebSocket
{
    [TestClass]
    public class WebSocketHandlerTest
    {
        private const int DEFAULT_ASYNC_TIMEOUT = 5000;

        [TestMethod]
        public void TestEchoWithMask()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            SockNetClient client = new SockNetClient(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 80));

            client.Connect().WaitOne(TimeSpan.FromSeconds(5));

            object currentObject;

            WebSocketHandler handler = new WebSocketHandler();
            handler.Apply(client, "/", "echo.websocket.org", (SockNetClient sockNetClient) => { blockingCollection.Add(true); });

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue((bool)currentObject);

            client.InPipe.AddLast<WebSocketFrame>((SockNetClient sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

            client.Send(WebSocketFrame.CreateTextFrame("some test", true));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is WebSocketFrame);

            Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

            Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

            client.Disconnect().WaitOne(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public void TestEchoWithMaskWithSsl()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            SockNetClient client = new SockNetClient(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 443));

            client.ConnectWithTLS((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => { return true; })
                .WaitOne(TimeSpan.FromSeconds(5));

            object currentObject;

            WebSocketHandler handler = new WebSocketHandler();
            handler.Apply(client, "/", "echo.websocket.org", (SockNetClient sockNetClient) => { blockingCollection.Add(true); });

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue((bool)currentObject);

            client.InPipe.AddLast<WebSocketFrame>((SockNetClient sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

            client.Send(WebSocketFrame.CreateTextFrame("some test", true));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is WebSocketFrame);

            Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

            Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

            client.Disconnect().WaitOne(TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public void TestEchoWithoutMask()
        {
            BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

            SockNetClient client = new SockNetClient(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 80));

            client.Connect().WaitOne(TimeSpan.FromSeconds(5));

            object currentObject;

            WebSocketHandler handler = new WebSocketHandler();
            handler.Apply(client, "/", "echo.websocket.org", (SockNetClient sockNetClient) => { blockingCollection.Add(true); });

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue((bool)currentObject);

            client.InPipe.AddLast<WebSocketFrame>((SockNetClient sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

            client.Send(WebSocketFrame.CreateTextFrame("some test", true));

            Assert.IsTrue(blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT));
            Assert.IsTrue(currentObject is WebSocketFrame);

            Assert.AreEqual("some test", ((WebSocketFrame)currentObject).DataAsString);

            Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);

            client.Disconnect().WaitOne(TimeSpan.FromSeconds(5));
        }
    }
}
