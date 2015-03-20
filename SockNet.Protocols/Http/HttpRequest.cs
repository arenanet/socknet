using System;
using System.Collections.Generic;
using System.Text;

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

        public string Action
        {
            set;
            get;
        }

        public string Path
        {
            set;
            get;
        }

        public string Version
        {
            set;
            get;
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
