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
