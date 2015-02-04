using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Common.Pool;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// State of the SockNetClient
    /// </summary>
    public enum SockNetState
    {
        DISCONNECTING,
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
    }

    /// <summary>
    /// A delegate that is used for connect notifications.
    /// </summary>
    /// <param name="sockNet"></param>
    public delegate void OnConnectedDelegate(ISockNetChannel sockNet);

    /// <summary>
    /// A delegate that is used for disconnect notification.
    /// </summary>
    /// <param name="sockNet"></param>
    public delegate void OnDisconnectedDelegate(ISockNetChannel sockNet);

    /// <summary>
    /// A delegate that is used for incoming and outgoing data.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="channel"></param>
    /// <param name="data"></param>
    public delegate void OnDataDelegate<T>(ISockNetChannel channel, ref T data);

    public interface ISockNetChannel
    {
        /// <summary>
        /// The SockNetPipe for incoming data
        /// </summary>
        SockNetChannelPipe InPipe { get; }

        /// <summary>
        /// The SockNetPipe for outgoing data
        /// </summary>
        SockNetChannelPipe OutPipe { get; }

        /// <summary>
        /// The buffer pool
        /// </summary>
        ObjectPool<byte[]> BufferPool { get; }

        /// <summary>
        /// The remote endpoint
        /// </summary>
        IPEndPoint RemoteEndpoint { get; }

        /// <summary>
        /// The local endpoint
        /// </summary>
        IPEndPoint LocalEndpoint { get; }

        /// <summary>
        /// Adds a module into this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        ISockNetChannel AddModule(ISockNetChannelModule module);

        /// <summary>
        /// Removes a module from this channel.
        /// </summary>
        /// <param name="module"></param>
        /// <returns></returns>
        ISockNetChannel RemoveModule(ISockNetChannelModule module);

        /// <summary>
        /// Gets the current state
        /// </summary>
        SockNetState State { get; }

        /// <summary>
        /// Returns true if this channel thinks it's connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Sends the given message to this channel
        /// </summary>
        /// <param name="data"></param>
        void Send(object data);

        /// <summary>
        /// An event that will be triggered when this client is connected to its IPEndpoint.
        /// </summary>
        event OnConnectedDelegate OnConnect;

        /// <summary>
        /// An event that will be triggered when this client is disconnected from its IPEndpoint.
        /// </summary>
        event OnDisconnectedDelegate OnDisconnect;

        /// <summary>
        /// Disconnects this channel from the remote endpoint
        /// </summary>
        /// <returns></returns>
        WaitHandle Disconnect();
    }
}
