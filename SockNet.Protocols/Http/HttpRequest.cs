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
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Protocols.Http
{
    /// <summary>
    /// Represents an HTTP request.
    /// </summary>
    public class HttpRequest : HttpPayload
    {
        public override string CommandLine
        {
            get {
                return Action + " " + Path + " " + Version;
            }
        }

        /// <summary>
        /// The action of this request, i.e. GET, POST, etc.
        /// </summary>
        public string Action
        {
            set;
            get;
        }

        /// <summary>
        /// The path of this request, i.e. /index.html
        /// </summary>
        public string Path
        {
            set;
            get;
        }

        /// <summary>
        /// The version of this request pyload, i.e. HTTP/1.1
        /// </summary>
        public string Version
        {
            set;
            get;
        }

        public HttpRequest(ObjectPool<byte[]> bufferPool) : base(bufferPool)
        {

        }

        /// <summary>
        /// Handles a request line.
        /// </summary>
        /// <param name="commandLine"></param>
        /// <returns></returns>
        protected override bool HandleCommandLine(string commandLine)
        {
            string[] split = commandLine.Split(new string[] { " " }, 3, StringSplitOptions.None);

            if (split.Length == 3)
            {
                Action = split[0].Trim();
                Path = split[1].Trim();
                Version = split[2].Trim();

                return true;
            } 
            else
            {
                return false;
            }
        }
    }
}
