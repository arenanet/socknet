using System;
using System.Collections.Generic;
using System.Text;

namespace ArenaNet.SockNet.Common
{
    /// <summary>
    /// A module for socknet.
    /// </summary>
    public interface ISockNetChannelModule
    {
        /// <summary>
        /// Installs this module into the given channel.
        /// </summary>
        /// <param name="channel"></param>
        void Install(ISockNetChannel channel);

        /// <summary>
        /// Uninstalls this module from the given channel.
        /// </summary>
        /// <param name="channel"></param>
        void Uninstall(ISockNetChannel channel);
    }
}
