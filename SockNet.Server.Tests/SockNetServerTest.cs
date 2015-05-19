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
using System.Text;
using System.Net;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ArenaNet.SockNet.Common;
using ArenaNet.SockNet.Common.IO;
using ArenaNet.SockNet.Client;
using ArenaNet.Medley.Pool;

namespace ArenaNet.SockNet.Server
{
    [TestClass]
    public class SockNetServerTest
    {
        [TestMethod]
        public void TestBindWithoutSsl()
        {
            ObjectPool<byte[]> pool = new ObjectPool<byte[]>(() => { return new byte[1024]; });

            ServerSockNetChannel server = SockNetServer.Create(GetLocalIpAddress(), 0, ServerSockNetChannel.DefaultBacklog, pool);

            try
            {
                server.Bind();

                server.Pipe.AddIncomingFirst<ChunkedBuffer>((ISockNetChannel channel, ref ChunkedBuffer data) => 
                {
                    channel.Send(data);
                });

                BlockingCollection<string> incomingData = new BlockingCollection<string>();

                ClientSockNetChannel client = SockNetClient.Create(GetLocalIpAddress(), server.LocalEndpoint.Port, ClientSockNetChannel.DefaultNoDelay, ClientSockNetChannel.DefaultTtl, pool);
                Assert.IsNotNull(client.Connect().WaitForValue(TimeSpan.FromSeconds(5)));

                client.Pipe.AddIncomingFirst((ISockNetChannel channel, ref ChunkedBuffer data) =>
                {
                    StreamReader reader = new StreamReader(data.Stream);

                    incomingData.Add(reader.ReadToEnd());
                });

                client.Send(Encoding.UTF8.GetBytes("a test!"));

                string incomingValue = null;

                Assert.IsTrue(incomingData.TryTake(out incomingValue, TimeSpan.FromSeconds(5)));
                Assert.AreEqual("a test!", incomingValue);
            }
            finally
            {
                server.Close();
            }
        }

        public static IPAddress GetLocalIpAddress()
        {
            IPAddress response = null;

            foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    response = address;
                    break;
                }
            }

            return response;
        }
    }
}
