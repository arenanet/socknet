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
using ArenaNet.SockNet.Common.IO;

namespace ArenaNet.SockNet.Protocols.Gds
{
    /// <summary>
    /// A module that can be applied to a ISockNetChannel to enable Gds support.
    /// </summary>
    public class GdsSockNetChannelModule
    {
        /// <summary>
        /// Installs the Gds module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            channel.Pipe.RemoveIncoming<object>(HandleIncoming);
            channel.Pipe.RemoveOutgoing<object>(HandleOutgoing);
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
