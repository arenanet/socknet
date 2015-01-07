using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ArenaNet.SockNet;

namespace ArenaNet.SockNet.WebSocket
{
    /// <summary>
    /// A handler that can be applied to a SockNetClient to enable WebSocket support.
    /// </summary>
    public class WebSocketHandler
    {
        private static readonly byte[] WebSocketAcceptHeader = Encoding.ASCII.GetBytes("Sec-WebSocket-Accept:");
        private static readonly byte[] HeaderEnd = Encoding.ASCII.GetBytes("\r\n");
        private const string MAGIC = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private string secKey;
        private string expectedAccept;

        private WebSocketHandler.OnWebSocketEstablishedDelegate OnWebSocketEstablished;

        public WebSocketHandler()
        {
            this.secKey = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0, 16)));
            this.expectedAccept = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(this.secKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        }

        /// <summary>
        /// Applies this WebSocketHandler to the given client.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="path"></param>
        /// <param name="hostname"></param>
        /// <param name="establishedNotification"></param>
        public void Apply(SockNetClient client, string path, string hostname, WebSocketHandler.OnWebSocketEstablishedDelegate establishedNotification)
        {
            OnWebSocketEstablished = establishedNotification;
            client.AddIncomingDataHandlerFirst<byte[]>(new SockNetClient.OnDataDelegate<byte[]>(HandleHandshake));
            byte[] bytes = Encoding.UTF8.GetBytes("GET " + path + " HTTP/1.1\r\nHost: " + hostname + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + secKey + "\r\nSec-WebSocket-Version: 13\r\n\r\n");
            client.Send((object)bytes);
        }

        /// <summary>
        /// Handles the WebSocket handshake.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="data"></param>
        private void HandleHandshake(SockNetClient client, ref byte[] data)
        {
            string str = (string)null;

            for (int i = 0; i < data.Length; ++i)
            {
                if (str != null)
                {
                    if (data[i] == WebSocketHandler.HeaderEnd[0])
                    {
                        str = str.Trim();
                        break;
                    }

                    str += (char)data[i];
                }
                else if (i + WebSocketHandler.WebSocketAcceptHeader.Length < data.Length)
                {
                    bool flag = false;

                    for (int j = 0; j < WebSocketHandler.WebSocketAcceptHeader.Length; ++j)
                    {
                        flag = WebSocketHandler.WebSocketAcceptHeader[j] == data[i + j];

                        if (!flag)
                        {
                            break;
                        }
                    }

                    if (flag)
                    {
                        i += WebSocketHandler.WebSocketAcceptHeader.Length - 1;

                        str = "";
                    }
                }
            }

            if (expectedAccept.Equals(str))
            {
                client.Logger(SockNetClient.LogLevel.INFO, "Established Web-Socket connection.");
                client.AddIncomingDataHandlerBefore<byte[], object>(new SockNetClient.OnDataDelegate<byte[]>(HandleHandshake), new SockNetClient.OnDataDelegate<object>(HandleIncomingFrames));
                client.AddOutgoingDataHandlerLast<object>(new SockNetClient.OnDataDelegate<object>(HandleOutgoingFrames));
                client.RemoveIncomingDataHandler<byte[]>(new SockNetClient.OnDataDelegate<byte[]>(HandleHandshake));

                if (OnWebSocketEstablished != null)
                {
                    OnWebSocketEstablished(client);
                }
            }
            else
            {
                client.Logger(SockNetClient.LogLevel.ERROR, "Web-Socket handshake incomplete: " + str);
                client.Disconnect();
            }
        }

        /// <summary>
        /// Handles incoming raw frames and translates them into WebSocketFrame(s)
        /// </summary>
        /// <param name="client"></param>
        /// <param name="data"></param>
        private void HandleIncomingFrames(SockNetClient client, ref object data)
        {
            if (!(data is byte[]))
            {
                return;
            }

            byte[] buffer = (byte[])data;
            data = (object)WebSocketFrame.ParseFrame((Stream)new MemoryStream(buffer));
        }

        /// <summary>
        /// Handles WebSocketFrame(s) and translates them into raw frames.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="data"></param>
        private void HandleOutgoingFrames(SockNetClient client, ref object data)
        {
            if (!(data is WebSocketFrame))
            {
                return;
            }

            WebSocketFrame webSocketFrame = (WebSocketFrame)data;
            MemoryStream memoryStream = new MemoryStream();
            webSocketFrame.Write((Stream)memoryStream);
            data = (object)memoryStream.ToArray();
        }

        /// <summary>
        /// A delegates that is used for websocket establishment notifications.
        /// </summary>
        /// <param name="client"></param>
        public delegate void OnWebSocketEstablishedDelegate(SockNetClient client);
    }
}
