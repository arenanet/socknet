using System;
using System.Collections.Generic;
using System.Text;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Protocols.Http
{
    /// <summary>
    /// Represents an HTTP response.
    /// </summary>
    public class HttpResponse : HttpPayload
    {
        public override string CommandLine
        {
            get {
                return Version + " " + Code + " " + Reason;
            }
        }

        /// <summary>
        /// The version of this response payload, i.e. HTTP/1.1
        /// </summary>
        public string Version
        {
            set;
            get;
        }

        /// <summary>
        /// The response code, i.e. 200, 404, etc.
        /// </summary>
        public string Code
        {
            set;
            get;
        }

        /// <summary>
        /// The reason for the above response, i.e. Bad Request
        /// </summary>
        public string Reason
        {
            set;
            get;
        }

        public HttpResponse(ObjectPool<byte[]> bufferPool) : base(bufferPool)
        {

        }

        /// <summary>
        /// Handles a status line.
        /// </summary>
        /// <param name="commandLine"></param>
        /// <returns></returns>
        protected override bool HandleCommandLine(string commandLine)
        {
            string[] split = commandLine.Split(new string[] { " " }, 3, StringSplitOptions.None);

            if (split.Length > 1)
            {
                Version = split[0].Trim();
                Code = split[1].Trim();

                if (split.Length > 2)
                {
                    Reason = split[2].Trim();
                }

                return true;
            } 
            else
            {
                return false;
            }
        }
    }
}
