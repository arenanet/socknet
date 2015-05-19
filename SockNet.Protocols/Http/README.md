SockNet.Protocol.Http
=====
HTTP implementation on top of SockNet.

Example
==========
The example bellow shows a client connecting to "www.guildwars2.com", sending a GET request, retrieving the response, and finally disconnecting.

```csharp
// blocking collection that will contain incoming HTTP response data
BlockingCollection<HttpResponse> responseData = new BlockingCollection<HttpResponse>();

// configure and create a channel
using (ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80))
    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client)))
{
    // connect to the channel's endpoint
    if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null)
    {
        throw new IOException(string.Format("Connection to [{0}] timed out.", client.RemoteEndpoint));
    }

    // add response collector
    client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse response) =>
    {
        responseData.Add(response);
    });

    // send a message
    if (client.Send(new HttpRequest(client.BufferPool) { Action = "GET", Path = "/", Version = "HTTP/1.1" }).WaitForValue(TimeSpan.FromSeconds(5)) == null)
    {
        throw new IOException(string.Format("HTTP message request to [{0}] timed out.", client.RemoteEndpoint));
    }

    // print out response data once we get it
    HttpResponse httpResponse;

    if (!responseData.TryTake(out httpResponse, (int)TimeSpan.FromSeconds(5).TotalMilliseconds))
    {
        throw new IOException(string.Format("Response timeout from [{0}].", client.RemoteEndpoint));
    }

    using (ChunkedBuffer buffer = new ChunkedBuffer(client.BufferPool))
    {
        httpResponse.Write(buffer.Stream, false);

        StreamReader reader = new StreamReader(buffer.Stream, Encoding.UTF8);

        Console.WriteLine(reader.ReadToEnd());
    }

    httpResponse.Dispose();
}
```

The example below create a SockNet echo server and has a client connect to it, send a message, and read the response.

```csharp
// configure and create server channel
using (ServerSockNetChannel server = (ServerSockNetChannel)SockNetServer.Create(new IPEndPoint(IPAddress.Any, 0))
    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Server)))
{
    // add data echo handler
    server.Pipe.AddIncomingFirst<HttpRequest>((ISockNetChannel channel, ref HttpRequest data) =>
    {
        HttpResponse response = new HttpResponse(channel.BufferPool)
        {
            Version = "HTTP/1.1",
            Code = "200",
            Reason = "OK"
        };
        response.Body = data.Body;
        response.Header["Content-Length"] = "" + data.Body.AvailableBytesToRead;

        channel.Send(response);
    });

    // bind to the port
    server.Bind();

    // blocking collection that will contain incoming HTTP response data
    BlockingCollection<HttpResponse> responseData = new BlockingCollection<HttpResponse>();

    // configure and create client channel
    using (ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(IPAddress.Parse("127.0.0.1"), server.LocalEndpoint.Port))
        .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client)))
    {
        // connect to the channel's endpoint
        if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null)
        {
            throw new IOException(string.Format("Connection to [{0}] timed out.", client.RemoteEndpoint));
        }

        // add response collector
        client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse response) =>
        {
            responseData.Add(response);
        });

        // send a message
        HttpRequest httpRequest = new HttpRequest(client.BufferPool) { Action = "POST", Path = "/", Version = "HTTP/1.1" };
        httpRequest.Body = ChunkedBuffer.Wrap("Y u no echo?", Encoding.UTF8);
        httpRequest.Header["Content-Length"] = "" + httpRequest.Body.AvailableBytesToRead;

        if (client.Send(httpRequest).WaitForValue(TimeSpan.FromSeconds(5)) == null)
        {
            throw new IOException(string.Format("HTTP message request to [{0}] timed out.", client.RemoteEndpoint));
        }

        // print out response data once we get it
        HttpResponse httpResponse;

        if (!responseData.TryTake(out httpResponse, (int)TimeSpan.FromSeconds(5).TotalMilliseconds))
        {
            throw new IOException(string.Format("Response timeout from [{0}].", client.RemoteEndpoint));
        }

        using (ChunkedBuffer buffer = new ChunkedBuffer(client.BufferPool))
        {
            httpResponse.Write(buffer.Stream, false);

            StreamReader reader = new StreamReader(buffer.Stream, Encoding.UTF8);

            Console.WriteLine(reader.ReadToEnd());
        }

        httpResponse.Dispose();
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
