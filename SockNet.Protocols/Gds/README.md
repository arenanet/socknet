SockNet.Protocol.Gds
=====
GDS implementation on top of SockNet.

Example
==========
The example below creates a echo GDS server and has a client connect, send a frame, and read the echo frame back.

```csharp
// configure and create server channel
using (ServerSockNetChannel server = (ServerSockNetChannel)SockNetServer.Create(new IPEndPoint(IPAddress.Any, 0))
    .AddModule(new GdsSockNetChannelModule()))
{
    // add data echo handler
    server.Pipe.AddIncomingFirst<GdsFrame>((ISockNetChannel channel, ref GdsFrame data) =>
    {
        channel.Send(data);
    });

    // bind to the port
    server.Bind();

    // blocking collection that will contain incoming WebSocket frame data
    BlockingCollection<GdsFrame> responseData = new BlockingCollection<GdsFrame>();

    // configure and create client channel
    using (ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(IPAddress.Parse("127.0.0.1"), server.LocalEndpoint.Port))
        .AddModule(new GdsSockNetChannelModule()))
    {
        // connect to the channel's endpoint
        if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null)
        {
            throw new IOException(string.Format("Connection to [{0}] timed out.", client.RemoteEndpoint));
        }

        // add response collector
        client.Pipe.AddIncomingLast<GdsFrame>((ISockNetChannel channel, ref GdsFrame response) =>
        {
            responseData.Add(response);
        });

        // send a message
        if (client.Send(GdsFrame.NewContentFrame(1, null, false, ChunkedBuffer.Wrap("Y u no echo?", Encoding.UTF8), true)).WaitForValue(TimeSpan.FromSeconds(5)) == null)
        {
            throw new IOException(string.Format("Gds frame send to [{0}] timed out.", client.RemoteEndpoint));
        }

        // print out response data once we get it
        GdsFrame gdsResponse;

        if (!responseData.TryTake(out gdsResponse, (int)TimeSpan.FromSeconds(5).TotalMilliseconds))
        {
            throw new IOException(string.Format("Response timeout from [{0}].", client.RemoteEndpoint));
        }

        StreamReader reader = new StreamReader(gdsResponse.Body.Stream, Encoding.UTF8);

        Console.WriteLine(reader.ReadToEnd());

        gdsResponse.Dispose();
    }
}
```

## Bugs and Feedback

For bugs, questions and discussions please use the [GitHub Issues](https://github.com/ArenaNet/SockNet/issues).

## LICENSE

Copyright 2015 ArenaNet, LLC.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
