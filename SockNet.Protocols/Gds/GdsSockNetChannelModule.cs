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
    public class GdsSockNetChannelModule : BaseMultiChannelSockNetChannelModule
    {
        private bool combineChunks;

        /// <summary>
        /// A per channel module.
        /// </summary>
        private class PerChannelGdsSockNetChannelModule : ISockNetChannelModule
        {
            private GdsFrame chunkedFrame;
            private bool combineChunks;

            public PerChannelGdsSockNetChannelModule(bool combineChunks)
            {
                this.combineChunks = combineChunks;
            }

            /// <summary>
            /// Handles an incomming raw Gds message.
            /// </summary>
            /// <param name="channel"></param>
            /// <param name="obj"></param>
            public void HandleIncoming(ISockNetChannel channel, ref object obj)
            {
                if (!(obj is ChunkedBuffer))
                {
                    return;
                }

                ChunkedBuffer stream = (ChunkedBuffer)obj;
                long startingPosition = stream.ReadPosition;

                try
                {
                    GdsFrame frame = GdsFrame.ParseFrame(stream.Stream, channel.BufferPool);

                    if (combineChunks)
                    {
                        if (frame.IsComplete)
                        {
                            UpdateChunk(ref chunkedFrame, frame, channel);

                            obj = chunkedFrame;
                            chunkedFrame = null;
                        }
                        else
                        {
                            UpdateChunk(ref chunkedFrame, frame, channel);
                        }
                    }
                    else
                    {
                        obj = frame;
                    }

                    if (SockNetLogger.DebugEnabled)
                    {
                        SockNetLogger.Log(SockNetLogger.LogLevel.DEBUG, this, "Received Gds message. Body Size: {0}, Type: {1}, IsComplete: {2}", frame.Body.AvailableBytesToRead, Enum.GetName(typeof(GdsFrame.GdsFrameType), frame.Type), frame.IsComplete);
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
            private static void UpdateChunk(ref GdsFrame chunkedFrame, GdsFrame frame, ISockNetChannel channel)
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

                    ChunkedBuffer body = null;

                    if (frame.Type == GdsFrame.GdsFrameType.BodyOnly || frame.Type == GdsFrame.GdsFrameType.Full)
                    {
                        if (type == GdsFrame.GdsFrameType.HeadersOnly)
                        {
                            type = GdsFrame.GdsFrameType.Full;
                        }

                        body = new ChunkedBuffer(channel.BufferPool);
                        chunkedFrame.Body.DrainToStreamSync(body.Stream).Close();
                        frame.Body.DrainToStreamSync(body.Stream).Close();
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

                    chunkedFrame.Dispose();
                    frame.Dispose();

                    chunkedFrame = GdsFrame.NewContentFrame(frame.StreamId, headers, false, body, frame.IsComplete);
                }
            }

            /// <summary>
            /// Handles an outgoing HttpRequest and converts it to a raw buffer.
            /// </summary>
            /// <param name="channel"></param>
            /// <param name="obj"></param>
            public void HandleOutgoing(ISockNetChannel channel, ref object obj)
            {
                if (!(obj is GdsFrame))
                {
                    return;
                }

                GdsFrame gdsFrame = (GdsFrame)obj;
                ChunkedBuffer buffer = new ChunkedBuffer(channel.BufferPool);
                gdsFrame.Write(buffer.Stream);

                gdsFrame.Dispose();

                obj = buffer;
            }

            public void Install(ISockNetChannel channel)
            {
                channel.Pipe.AddIncomingFirst<object>(HandleIncoming);
                channel.Pipe.AddOutgoingLast<object>(HandleOutgoing);
            }

            public void Uninstall(ISockNetChannel channel)
            {
                channel.Pipe.RemoveIncoming<object>(HandleIncoming);
                channel.Pipe.RemoveOutgoing<object>(HandleOutgoing);
            }
        }

        /// <summary>
        /// Creates a new Gds module.
        /// </summary>
        /// <param name="combineChunks"></param>
        public GdsSockNetChannelModule(bool combineChunks = true)
        {
            this.combineChunks = combineChunks;
        }

        /// <summary>
        /// Returns the module name.
        /// </summary>
        protected override string ModuleName
        {
            get { return "GdsModule"; }
        }

        /// <summary>
        /// Creates a new per channel module.
        /// </summary>
        /// <returns></returns>
        protected override ISockNetChannelModule NewPerChannelModule()
        {
            return new PerChannelGdsSockNetChannelModule(combineChunks);
        }
    }
}
