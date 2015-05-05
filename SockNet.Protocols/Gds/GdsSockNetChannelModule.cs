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
using System.Collections.Generic;
using System.Text;
using System.IO;
using ArenaNet.SockNet.Common;
using ArenaNet.Medley.Collections.Concurrent;
using ArenaNet.SockNet.Common.IO;

namespace ArenaNet.SockNet.Protocols.Gds
{
    /// <summary>
    /// A module that can be applied to a ISockNetChannel to enable Gds support.
    /// </summary>
    public class GdsSockNetChannelModule : ISockNetChannelModule
    {
        private bool combineChunks;

        private ConcurrentHashMap<string, GdsFrame> streamChunks = new ConcurrentHashMap<string, GdsFrame>(null, 1024, 128);

        /// <summary>
        /// Creates a new Gds module.
        /// </summary>
        /// <param name="combineChunks"></param>
        public GdsSockNetChannelModule(bool combineChunks = true)
        {
            this.combineChunks = combineChunks;
        }

        /// <summary>
        /// Installs the Gds module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            channel.Pipe.AddIncomingFirst<object>(HandleIncoming);
            channel.Pipe.AddOutgoingLast<object>(HandleOutgoing);
        }

        /// <summary>
        /// Uninstalls the Gds module
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            channel.Pipe.RemoveIncoming<object>(HandleIncoming);
            channel.Pipe.RemoveOutgoing<object>(HandleOutgoing);
        }

        /// <summary>
        /// Handles an incomming raw Gds message.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="obj"></param>
        private void HandleIncoming(ISockNetChannel channel, ref object obj)
        {
            if (!(obj is ChunkedBuffer))
            {
                return;
            }

            ChunkedBuffer stream = (ChunkedBuffer)obj;
            long startingPosition = stream.ReadPosition;

            try
            {
                GdsFrame frame = GdsFrame.ParseFrame(stream.Stream);

                if (combineChunks)
                {
                    GdsFrame chunkedFrame = null;

                    streamChunks.TryGetValue(channel.Id + "." + frame.StreamId, out chunkedFrame);

                    if (frame.IsComplete)
                    {
                        UpdateChunk(ref chunkedFrame, frame);

                        obj = chunkedFrame;
                        streamChunks.Remove(channel.Id + "." + frame.StreamId);
                    }
                    else
                    {
                        UpdateChunk(ref chunkedFrame, frame);

                        streamChunks[channel.Id + "." + frame.StreamId] = chunkedFrame;
                    }
                }
                else
                {
                    obj = frame;
                }

                if (SockNetLogger.DebugEnabled)
                {
                    SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Received Gds message. Body Size: {0}, Type: {1}, IsComplete: {2}", frame.Body.Length, Enum.GetName(typeof(GdsFrame.GdsFrameType), frame.Type), frame.IsComplete);
                }
            }
            catch (EndOfStreamException)
            {
                // frame isn't done
                stream.ReadPosition = startingPosition;
            }
            catch (ArgumentOutOfRangeException)
            {
                // frame isn't done
                stream.ReadPosition = startingPosition;
            }
            catch (Exception e)
            {
                SockNetLogger.Log(SockNetLogger.LogLevel.ERROR, this, "Unable to parse Gds message", e);
            }
        }

        /// <summary>
        /// Updates the given chunked frame with a new frame.
        /// </summary>
        /// <param name="frame"></param>
        private static void UpdateChunk(ref GdsFrame chunkedFrame, GdsFrame frame)
        {
            if (chunkedFrame == null)
            {
                chunkedFrame = frame;  // set initial frame
            }
            else
            {
                if (frame.Type == GdsFrame.GdsFrameType.Ping || frame.Type == GdsFrame.GdsFrameType.Pong || frame.Type == GdsFrame.GdsFrameType.Close)
                {
                    chunkedFrame = frame;
                }

                GdsFrame.GdsFrameType type = chunkedFrame.Type;

                byte[] body = null;

                if (frame.Type == GdsFrame.GdsFrameType.BodyOnly || frame.Type == GdsFrame.GdsFrameType.Full)
                {
                    if (type == GdsFrame.GdsFrameType.HeadersOnly)
                    {
                        type = GdsFrame.GdsFrameType.Full;
                    }

                    body = new byte[chunkedFrame.Body.Length + frame.Body.Length];
                    Buffer.BlockCopy(chunkedFrame.Body, 0, body, 0, chunkedFrame.Body.Length);
                    Buffer.BlockCopy(frame.Body, 0, body, chunkedFrame.Body.Length, frame.Body.Length);
                }

                Dictionary<string, byte[]> headers = null;

                if (frame.Type == GdsFrame.GdsFrameType.HeadersOnly || frame.Type == GdsFrame.GdsFrameType.Full)
                {
                    if (type == GdsFrame.GdsFrameType.BodyOnly)
                    {
                        type = GdsFrame.GdsFrameType.Full;
                    }

                    headers = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, byte[]> kvp in chunkedFrame.Headers)
                    {
                        headers[kvp.Key] = kvp.Value;
                    }
                    foreach (KeyValuePair<string, byte[]> kvp in frame.Headers)
                    {
                        headers[kvp.Key] = kvp.Value;
                    }
                }

                chunkedFrame = GdsFrame.NewContentFrame(frame.StreamId, headers, false, body, frame.IsComplete);
            }
        }

        /// <summary>
        /// Handles an outgoing HttpRequest and converts it to a raw buffer.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="obj"></param>
        private void HandleOutgoing(ISockNetChannel channel, ref object obj)
        {
            if (!(obj is GdsFrame))
            {
                return;
            }

            GdsFrame webSocketFrame = (GdsFrame)obj;
            ChunkedBuffer buffer = new ChunkedBuffer(channel.BufferPool);
            webSocketFrame.Write(buffer.Stream);
            obj = buffer;
        }
    }
}
