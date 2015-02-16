using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Net.Security;
using System.Net.Sockets;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Client
{
    /// <summary>
    /// A client implementation that creates ClientSockNetChannels.
    /// </summary>
    public static class SockNetClient
    {
        public static ClientSockNetChannel Create(IPAddress address, int port, bool noDelay = false, short ttl = 32)
        {
            return Create(new IPEndPoint(address, port), noDelay, ttl);
        }

        public static ClientSockNetChannel Create(IPEndPoint endpoint, bool noDelay = false, short ttl = 32)
        {
            // TODO client track channels
            return new ClientSockNetChannel(endpoint, SockNetChannelGlobals.GlobalBufferPool).WithNoDelay(noDelay).WithTtl(ttl);
        }
    }
}
