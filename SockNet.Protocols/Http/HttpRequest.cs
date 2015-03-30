using System;
using System.Collections.Generic;
using System.Text;
using ArenaNet.SockNet.Common.Pool;

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
