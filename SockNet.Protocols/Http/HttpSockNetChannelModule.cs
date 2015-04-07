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
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;

namespace ArenaNet.SockNet.Protocols.Http
{
    /// <summary>
    /// HTTP module which enables support for HTTP 1.x and HTTP 1.x-like protocols.
    /// </summary>
    public class HttpSockNetChannelModule : ISockNetChannelModule
    {
        public ParsingMode Mode { private set; get; }
        public enum ParsingMode
        {
            Client,
            Server
        }

        /// <summary>
        /// Creates a Client or Server HTTP module.
        /// </summary>
        /// <param name="mode"></param>
        public HttpSockNetChannelModule(ParsingMode mode)
        {
            this.Mode = mode;
        }

        private HttpPayload currentIncoming = null;

        /// <summary>
        /// Installs the HTTP module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            switch (Mode)
            {
                case ParsingMode.Client:
                    channel.Pipe.AddIncomingFirst<object>(HandleIncomingResponse);
                    channel.Pipe.AddOutgoingLast<object>(HandleOutgoingRequest);
                    break;
                case ParsingMode.Server:
                    channel.Pipe.AddIncomingFirst<object>(HandleIncomingRequest);
                    channel.Pipe.AddOutgoingLast<object>(HandleOutgoingResponse);
                    break;
            }
        }

        /// <summary>
        /// Uninstalls the HTTP module/
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            switch (Mode)
            {
                case ParsingMode.Client:
                    channel.Pipe.RemoveIncoming<object>(HandleIncomingResponse);
                    channel.Pipe.RemoveOutgoing<object>(HandleOutgoingRequest);
                    break;
                case ParsingMode.Server:
                    channel.Pipe.RemoveIncoming<object>(HandleIncomingRequest);
                    channel.Pipe.RemoveOutgoing<object>(HandleOutgoingResponse);
                    break;
            }
        }

        /// <summary>
        /// Handles an incomming raw HTTP request.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="obj"></param>
        private void HandleIncomingRequest(ISockNetChannel channel, ref object obj)
        {
            if (!(obj is ChunkedBuffer))
            {
                return;
            }

            ChunkedBuffer data = (ChunkedBuffer)obj;

            if (currentIncoming == null)
            {
                currentIncoming = new HttpRequest(channel.BufferPool);
            }

            if (currentIncoming.Parse(data.Stream, channel.IsActive))
            {
                obj = currentIncoming;
                currentIncoming = null;
            }
        }

        /// <summary>
        /// Handles an incoming raw HTTP response.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="obj"></param>
        private void HandleIncomingResponse(ISockNetChannel channel, ref object obj)
        {
            if (!(obj is ChunkedBuffer))
            {
                return;
            }

            ChunkedBuffer data = (ChunkedBuffer)obj;

            if (currentIncoming == null)
            {
                currentIncoming = new HttpResponse(channel.BufferPool);
            }

            if (currentIncoming.Parse(data.Stream, channel.IsActive))
            {
                obj = currentIncoming;
                currentIncoming = null;
            }
        }

        /// <summary>
        /// Handles an outgoing HttpRequest and converts it to a raw buffer.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="obj"></param>
        private void HandleOutgoingRequest(ISockNetChannel channel, ref object obj)
        {
            if (!(obj is HttpRequest))
            {
                return;
            }

            HttpRequest data = (HttpRequest)obj;

            ChunkedBuffer buffer = new ChunkedBuffer(channel.BufferPool);

            data.Write(buffer.Stream);

            obj = buffer;
        }

        /// <summary>
        /// Handles an outgoing HttpResponse and converts it into a raw buffer.
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="obj"></param>
        private void HandleOutgoingResponse(ISockNetChannel channel, ref object obj)
        {
            if (!(obj is HttpResponse))
            {
                return;
            }

            HttpResponse data = (HttpResponse)obj;

            ChunkedBuffer buffer = new ChunkedBuffer(channel.BufferPool);

            data.Write(buffer.Stream);

            obj = buffer;
        }
    }
}
