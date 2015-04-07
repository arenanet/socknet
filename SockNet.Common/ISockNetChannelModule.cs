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
