using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ArenaNet.SockNet.Protocols.WebSocket
{
    public static class WebSocketUtil
    {
        public static readonly string WebSocketKeyHeader = "Sec-WebSocket-Key";
        public static readonly string WebSocketVersionHeader = "Sec-WebSocket-Version";
        public static readonly string WebSocketAcceptHeader = "Sec-WebSocket-Accept";
        public static readonly string WebSocketProtocolHeader = "Sec-WebSocket-Protocol";

        public const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        /// <summary>
        /// Generates a security key.
        /// </summary>
        /// <returns></returns>
        public static string GenerateSecurityKey()
        {
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString().Substring(0, 16)));
        }

        /// <summary>
        /// Generates the accept key for a security key.
        /// </summary>
        /// <param name="securityKey"></param>
        /// <returns></returns>
        public static string GenerateAccept(string securityKey)
        {
            return Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(securityKey + Magic)));
        }
    }
}
