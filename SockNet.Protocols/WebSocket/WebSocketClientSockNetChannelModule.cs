/*
 * Copyright 2015 ArenaNet, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this 
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * 	 http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under 
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF 
 * ANY KIND, either express or implied. See the License for the specific language governing 
 * permissions and limitations under the License.
 */
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Protocols.Http;

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

        private HttpSockNetChannelModule httpModule = new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client);

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
            channel.AddModule(httpModule);
            channel.Pipe.AddOpenedLast(OnConnected);
        }

        /// <summary>
        /// Uninstalls this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            channel.Pipe.RemoveIncoming<HttpResponse>(HandleHandshake);
            channel.Pipe.RemoveIncoming<object>(HandleIncomingFrames);
            channel.Pipe.RemoveOutgoing<object>(HandleOutgoingFrames);
            channel.Pipe.RemoveOpened(OnConnected);
            channel.RemoveModule(httpModule);
        }

        /// <summary>
        /// Invoked on channel connect.
        /// </summary>
        /// <param name="channel"></param>
        public void OnConnected(ISockNetChannel channel)
        {
            SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Sending WebSocket upgrade request.");

            channel.Pipe.AddIncomingLast<HttpResponse>(HandleHandshake);

            HttpRequest request = new HttpRequest(channel.BufferPool)
            {
                Action = "GET",
                Path = path,
                Version = "HTTP/1.1"
            };
            request.Header["Host"] = hostname;
            request.Header["Upgrade"] = "websocket";
            request.Header["Connection"] = "Upgrade";
            request.Header["Sec-WebSocket-Key"] = secKey;
            request.Header["Sec-WebSocket-Version"] = "13";

            channel.Send(request);
        }

        /// <summary>
        /// Handles the WebSocket handshake.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleHandshake(ISockNetChannel channel, ref HttpResponse data)
        {
            if (expectedAccept.Equals(data.Header[WebSocketAcceptHeader]))
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.INFO, this, "Established Web-Socket connection.");
                channel.Pipe.RemoveIncoming<HttpResponse>(HandleHandshake);
                channel.Pipe.AddIncomingFirst<object>(HandleIncomingFrames);
                channel.Pipe.AddOutgoingLast<object>(HandleOutgoingFrames);

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
            if (!(data is ChunkedBuffer))
            {
                return;
            }

            ChunkedBuffer stream = (ChunkedBuffer)data;
            long startingPosition = stream.ReadPosition;

            try
            {
                data = WebSocketFrame.ParseFrame(stream.Stream);
            }
            catch (ArgumentOutOfRangeException)
            {
                // websocket frame isn't done
                stream.ReadPosition = startingPosition;
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
            ChunkedBuffer buffer = new ChunkedBuffer(channel.BufferPool);
            webSocketFrame.Write(buffer.Stream);
            data = buffer;
        }

        /// <summary>
        /// A delegates that is used for websocket establishment notifications.
        /// </summary>
        /// <param name="channel"></param>
        public delegate void OnWebSocketEstablishedDelegate(ISockNetChannel channel);
    }
}