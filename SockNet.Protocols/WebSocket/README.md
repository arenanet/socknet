SockNet.Protocol.WebSocket
=====
WebSocket implementation on top of SockNet.

Example
==========
The example bellow shows a client connecting to "echo.websocket.org", sending a text WebSocket frame, retrieving the echoed WebSocketFrame, printing it out, and then disconnecting.

```csharp
// blocking collection that holds flags for handshakes
BlockingCollection<bool> handshakes = new BlockingCollection<bool>();

// blocking collection that will contain incoming HTTP response data
BlockingCollection<WebSocketFrame> responseData = new BlockingCollection<WebSocketFrame>();

// configure and create a channel
using (ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 80))
    .AddModule(new WebSocketClientSockNetChannelModule("/", "echo.websocket.org", (ISockNetChannel channel) => { handshakes.Add(true); })))
{
    // connect to the channel's endpoint
    if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null)
    {
        throw new IOException(string.Format("Connection to [{0}] timed out.", client.RemoteEndpoint));
    }

    // wait for handshake
    bool handshakeFlag = false;
    if (!handshakes.TryTake(out handshakeFlag, TimeSpan.FromSeconds(5)) || !handshakeFlag)
    {
        throw new IOException(string.Format("Connection to [{0}] timed out.", client.RemoteEndpoint));
    }

    // add response collector
    client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel channel, ref WebSocketFrame response) =>
    {
        responseData.Add(response);
    });

    // send a message
    if (client.Send(WebSocketFrame.CreateTextFrame("Y u no echo!")).WaitForValue(TimeSpan.FromSeconds(5)) == null)
    {
        throw new IOException(string.Format("WebSocket frame send to [{0}] timed out.", client.RemoteEndpoint));
    }

    // print out response data once we get it
    WebSocketFrame httpResponse;

    if (!responseData.TryTake(out httpResponse, (int)TimeSpan.FromSeconds(5).TotalMilliseconds))
    {
        throw new IOException(string.Format("Response timeout from [{0}].", client.RemoteEndpoint));
    }

    Console.WriteLine(httpResponse.DataAsString);
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
