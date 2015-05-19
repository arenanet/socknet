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
using ArenaNet.SockNet.Common;

namespace ArenaNet.SockNet.Protocols
{
    /// <summary>
    /// A base class for multi channel socknet channel modules.
    /// </summary>
    public abstract class BaseMultiChannelSockNetChannelModule : ISockNetChannelModule
    {
        /// <summary>
        /// Installs a per channel module.
        /// </summary>
        /// <param name="channel"></param>
        public void Install(ISockNetChannel channel)
        {
            ISockNetChannelModule module = NewPerChannelModule();

            if (channel.SetAttribute(ModuleName, module, false))
            {
                module.Install(channel);
            }
        }

        /// <summary>
        /// Uninstalls a per channel module.
        /// </summary>
        /// <param name="channel"></param>
        public void Uninstall(ISockNetChannel channel)
        {
            object module;

            if (channel.TryGetAttribute(ModuleName, out module))
            {
                ((ISockNetChannelModule)module).Uninstall(channel);

                channel.RemoveAttribute(ModuleName);
            }
        }

        /// <summary>
        /// The module name that will be unique per channel.
        /// </summary>
        protected abstract string ModuleName { get; }

        /// <summary>
        /// Creates a new per channel module.
        /// </summary>
        /// <returns></returns>
        protected abstract ISockNetChannelModule NewPerChannelModule();
    }
}
