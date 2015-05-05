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
using System.Text;
using ArenaNet.SockNet.Common;
using ArenaNet.Medley.Collections.Concurrent;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Protocols.Http;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    /// <summary>
    /// A module that can be applied to a ISockNetChannel to enable WebSocket support.
    /// </summary>
    public class WebSocketServerSockNetChannelModule : ISockNetChannelModule
    {
        private static readonly byte[] HeaderNewLine = Encoding.ASCII.GetBytes("\r\n");

        private bool combineContinuations;
        private string path;
        private string hostname;
        private HttpSockNetChannelModule httpModule = new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Server);

        private ConcurrentHashMap<string, WebSocketFrame> continuationFrames = new ConcurrentHashMap<string, WebSocketFrame>(null, 1024, 128);

        public WebSocketServerSockNetChannelModule(string path, string hostname, bool combineContinuations = true)
        {
            this.path = path;
            this.hostname = hostname;
            this.combineContinuations = combineContinuations;
        }

        /// <summary>
        /// Installs this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            channel.AddModule(httpModule);

            channel.Pipe.AddIncomingLast<HttpRequest>(HandleHandshake);
        }

        /// <summary>
        /// Uninstalls this module.
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            channel.Pipe.RemoveIncoming<HttpRequest>(HandleHandshake);
            channel.Pipe.RemoveIncoming<object>(HandleIncomingFrames);
            channel.Pipe.RemoveOutgoing<object>(HandleOutgoingFrames);

            if (channel.HasModule(httpModule))
            {
                channel.RemoveModule(httpModule);
            }
        }

        /// <summary>
        /// Handles the WebSocket handshake.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="data"></param>
        private void HandleHandshake(ISockNetChannel channel, ref HttpRequest data)
        {
            string connection = data.Header["Connection"];
            string upgrade = data.Header["Upgrade"];
            string securityKey = data.Header["Sec-WebSocket-Key"];

            if (connection != null && upgrade != null && securityKey != null && "websocket".Equals(upgrade.Trim().ToLower()) && "upgrade".Equals(connection.Trim().ToLower()))
            {
                HttpResponse request = new HttpResponse(channel.BufferPool)
                {
                    Version = "HTTP/1.1",
                    Code = "101",
                    Reason = "Switching Protocols"
                };
                request.Header["Upgrade"] = "websocket";
                request.Header["Connection"] = "Upgrade";
                request.Header[WebSocketUtil.WebSocketAcceptHeader] = WebSocketUtil.GenerateAccept(securityKey);
                request.Header["Sec-WebSocket-Protocol"] = "";

                channel.Send(request);

                channel.RemoveModule(httpModule);
                channel.Pipe.AddIncomingFirst<object>(HandleIncomingFrames);
                channel.Pipe.AddOutgoingLast<object>(HandleOutgoingFrames);
            }
            else
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Expecting upgrade request.");

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
                WebSocketFrame frame = WebSocketFrame.ParseFrame(stream.Stream);

                if (combineContinuations)
                {
                    WebSocketFrame continuationFrame = null;

                    continuationFrames.TryGetValue(channel.Id, out continuationFrame);

                    if (frame.IsFinished)
                    {
                        UpdateContinuation(ref continuationFrame, frame);

                        data = continuationFrame;
                        continuationFrames.Remove(channel.Id);
                    }
                    else
                    {
                        UpdateContinuation(ref continuationFrame, frame);

                        continuationFrames[channel.Id] = continuationFrame;
                    }
                }
                else
                {
                    data = frame;
                }

                if (SockNetLogger.DebugEnabled)
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Received WebSocket message. Size: {0}, Type: {1}, IsFinished: {2}", frame.Data.Length, Enum.GetName(typeof(WebSocketFrame.WebSocketFrameOperation), frame.Operation), frame.IsFinished);
                }
            }
            catch (EndOfStreamException)
            {
                // websocket frame isn't done
                stream.ReadPosition = startingPosition;
            }
            catch (ArgumentOutOfRangeException)
            {
                // websocket frame isn't done
                stream.ReadPosition = startingPosition;
            }
            catch (Exception e)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to parse web-socket request", e);

                channel.Close();
            }
        }

        /// Updates the local continuation.
        /// </summary>
        /// <param name="frame"></param>
        private void UpdateContinuation(ref WebSocketFrame continuationFrame, WebSocketFrame frame)
        {
            if (continuationFrame == null)
            {
                continuationFrame = frame;  // set initial frame
            }
            else
            {
                byte[] frameData = new byte[continuationFrame.Data.Length + frame.Data.Length];
                Buffer.BlockCopy(continuationFrame.Data, 0, frameData, 0, continuationFrame.Data.Length);
                Buffer.BlockCopy(frame.Data, 0, frameData, continuationFrame.Data.Length, frame.Data.Length);

                switch (continuationFrame.Operation)
                {
                    case WebSocketFrame.WebSocketFrameOperation.BinaryFrame:
                        continuationFrame = WebSocketFrame.CreateBinaryFrame(frameData, false, false);
                        break;
                    case WebSocketFrame.WebSocketFrameOperation.TextFrame:
                        continuationFrame = WebSocketFrame.CreateTextFrame(frameData, false, false);
                        break;
                }
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
    }
}