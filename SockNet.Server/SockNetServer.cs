using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Server
{
    /// <summary>
    /// A SockNet server.
    /// </summary>
    public static class SockNetServer
    {
        public const int DefaultBufferSize = 1024;

        private static readonly ObjectPool<byte[]> SharedPool = new ObjectPool<byte[]>(() => { return new byte[DefaultBufferSize]; });

        public static ServerSockNetChannel Create(IPAddress bindAddress, int bindPort, int backlog = ServerSockNetChannel.DefaultBacklog)
        {
            return Create(new IPEndPoint(bindAddress, bindPort), backlog);
        }

        public static ServerSockNetChannel Create(IPEndPoint bindEndpoint, int backlog = ServerSockNetChannel.DefaultBacklog)
        {
            // TODO possibly track?
            return new ServerSockNetChannel(bindEndpoint, SharedPool, backlog);
        }
    }
}
