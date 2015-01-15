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
        private static readonly string WebSocketAcceptHeader = "Sec-WebSocket-Accept";
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
            client.AddIncomingDataHandlerFirst<Stream>(new SockNetClient.OnDataDelegate<Stream>(HandleHandshake));
            byte[] bytes = Encoding.UTF8.GetBytes("GET " + path + " HTTP/1.1\r\nHost: " + hostname + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + secKey + "\r\nSec-WebSocket-Version: 13\r\n\r\n");
            client.Send((object)bytes);
        }

        /// <summary>
        /// Handles the WebSocket handshake.
        /// </summary>
        /// <param name="client"></param>
        /// <param name="data"></param>
        private void HandleHandshake(SockNetClient client, ref Stream data)
        {
            StreamReader headerReader = new StreamReader(data, Encoding.ASCII);

            long startingPosition = data.Position;

            string foundAccept = null;
            bool foundEndOfHeaders = false;
            string line = null;

            while ((line = headerReader.ReadLine()) != null)
            {
                line = line.Trim();

                if (line.StartsWith(WebSocketAcceptHeader))
                {
                    foundAccept = line.Split(new char[] { ':' }, 2)[1].Trim();
                }

                if (line.Equals(""))
                {
                    foundEndOfHeaders = true;
                }
            }

            if (!foundEndOfHeaders)
            {
                data.Position = startingPosition;
                return;
            }

            if (expectedAccept.Equals(foundAccept))
            {
                client.Logger(SockNetClient.LogLevel.INFO, "Established Web-Socket connection.");
                client.AddIncomingDataHandlerBefore<Stream, object>(new SockNetClient.OnDataDelegate<Stream>(HandleHandshake), new SockNetClient.OnDataDelegate<object>(HandleIncomingFrames));
                client.AddOutgoingDataHandlerLast<object>(new SockNetClient.OnDataDelegate<object>(HandleOutgoingFrames));
                client.RemoveIncomingDataHandler<Stream>(new SockNetClient.OnDataDelegate<Stream>(HandleHandshake));

                if (OnWebSocketEstablished != null)
                {
                    OnWebSocketEstablished(client);
                }
            }
            else
            {
                client.Logger(SockNetClient.LogLevel.ERROR, "Web-Socket handshake incomplete.");

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
            if (!(data is Stream))
            {
                return;
            }

            Stream stream = (Stream)data;
            long startingPosition = stream.Position;

            try
            {
                data = WebSocketFrame.ParseFrame(stream);
            }
            catch (ArgumentOutOfRangeException)
            {
                // websocket frame isn't done
                stream.Position = startingPosition;
            }
            catch (Exception e)
            {
                // otherwise we can't recover
                client.Logger(SockNetClient.LogLevel.ERROR, "Unexpected error: " + e.Message);

                client.Disconnect();
            }
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
