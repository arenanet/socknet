using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    /// <summary>
    /// A module that can be applied to a ISockNetChannel to enable WebSocket support.
    /// </summary>
    public class WebSocketClientSockNetChannelModule : ISockNetChannelModule
    {
        private static readonly byte[] HeaderNewLine = Encoding.ASCII.GetBytes("\r\n");
        private static readonly string WebSocketAcceptHeader = "Sec-WebSocket-Accept";
        private const string MAGIC = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private string path;
        private string hostname;
        private OnWebSocketEstablishedDelegate onWebSocketEstablished;
        private string secKey;
        private string expectedAccept;

        public WebSocketClientSockNetChannelModule(string path, string hostname, OnWebSocketEstablishedDelegate onWebSocketEstablished)
        {
            this.path = path;
            this.hostname = hostname;
            this.onWebSocketEstablished = onWebSocketEstablished;

            this.secKey = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0, 16)));
            this.expectedAccept = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(this.secKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        }

        /// <summary>
        /// Installs this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            channel.Pipe.AddOpenedLast(OnConnected);
        }

        /// <summary>
        /// Uninstalls this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            channel.Pipe.RemoveIncoming<Stream>(HandleHandshake);
            channel.Pipe.RemoveIncoming<object>(HandleIncomingFrames);
            channel.Pipe.RemoveOutgoing<object>(HandleOutgoingFrames);
            channel.Pipe.RemoveOpened(OnConnected);
        }

        /// <summary>
        /// Invoked on channel connect.
        /// </summary>
        /// <param name="channel"></param>
        public void OnConnected(ISockNetChannel channel)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Sending WebSocket upgrade request.");

            channel.Pipe.AddIncomingFirst<Stream>(new OnDataDelegate<Stream>(HandleHandshake));
            byte[] bytes = Encoding.UTF8.GetBytes("GET " + path + " HTTP/1.1\r\nHost: " + hostname + "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + secKey + "\r\nSec-WebSocket-Version: 13\r\n\r\n");
            channel.Send((object)bytes);
        }

        /// <summary>
        /// Handles the WebSocket handshake.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleHandshake(ISockNetChannel channel, ref Stream data)
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
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Established Web-Socket connection.");
                channel.Pipe.AddIncomingBefore<Stream, object>(new OnDataDelegate<Stream>(HandleHandshake), new OnDataDelegate<object>(HandleIncomingFrames));
                channel.Pipe.AddOutgoingLast<object>(new OnDataDelegate<object>(HandleOutgoingFrames));
                channel.Pipe.RemoveIncoming<Stream>(new OnDataDelegate<Stream>(HandleHandshake));

                if (onWebSocketEstablished != null)
                {
                    onWebSocketEstablished(channel);
                }
            }
            else
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Web-Socket handshake incomplete.");

                channel.Close();
            }
        }

        /// <summary>
        /// Handles incoming raw frames and translates them into WebSocketFrame(s)
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleIncomingFrames(ISockNetChannel channel, ref object data)
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
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, "Unexpected error: {0}",  e.Message);

                channel.Close();
            }
        }

        /// <summary>
        /// Handles WebSocketFrame(s) and translates them into raw frames.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleOutgoingFrames(ISockNetChannel channel, ref object data)
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
        /// <param name="channel"></param>
        public delegate void OnWebSocketEstablishedDelegate(ISockNetChannel channel);
    }
}
