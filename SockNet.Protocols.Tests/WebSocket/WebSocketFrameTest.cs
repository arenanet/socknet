using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    [TestClass]
    public class WebSocketFrameTest
    {
        [TestMethod]
        public void TestTextFrameMasking()
        {
            WebSocketFrame frame = WebSocketFrame.CreateTextFrame("this is some really great text!", true, false, true);

            MemoryStream stream = new MemoryStream();
            frame.Write(stream, true);
            stream.Position = 0;

            WebSocketFrame readFrame = WebSocketFrame.ParseFrame(stream);

            Assert.AreEqual(frame.DataAsString, readFrame.DataAsString);
        }
    }
}
