SockNet.Protocol.WebSocket
=====
WebSocket implementation on top of SockNet.

Example
==========
The example bellow will shows a client connecting to "echo.websocket.org", send a text WebSocket frame, retrieve the echoed WebSocketFrame, print it out, and then disconnect.

```csharp
// this collection will hold the incoming frames
BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

// create a promise that will be set once we perform the websocket handshake
Promise<ISockNetChannel> webSocketAuthPromise = new Promise<ISockNetChannel>();

// create a SockNetClient that will connect to "echo.websocket.org" on port 80 and install the WebSocketClient module
ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("echo.websocket.org").AddressList[0], 80))
    .AddModule(new WebSocketClientSockNetChannelModule("/", "echo.websocket.org", (ISockNetChannel sockNetClient) => { webSocketAuthPromise.CreateFulfiller().Fulfill(sockNetClient); }));

// connect and wait for 5 seconds
if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null) 
{
    throw new Exception("Connection timed out.");
}

try
{
    // wait for handshake to complete
    if (webSocketAuthPromise.WaitForValue(TimeSpan.FromSeconds(5)) == null)
    {
        throw new Exception("Handshake timed out.")
    }

    // register my WebSocket frame listener
    client.Pipe.AddIncomingLast<WebSocketFrame>((ISockNetChannel sockNetClient, ref WebSocketFrame data) => { blockingCollection.Add(data); });

    // send a WebSocket frame
    client.Send(WebSocketFrame.CreateTextFrame("some test", true));

    // wait for a response
    object currentObject;

    if (blockingCollection.TryTake(out currentObject, 5))
    {
        throw new Exception("Receive timeout.");
    }

    if(!(currentObject is WebSocketFrame))
    {
        throw new Exception("Unexpected error.");
    }

    if (!"some test".equals(((WebSocketFrame)currentObject).DataAsString))
    {
        throw new Exception("Echo failed.");
    }

    // print out the response
    Console.WriteLine("Got response: \n" + ((WebSocketFrame)currentObject).DataAsString);
} 
finally
{
    // disconnect
    if (client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5)) == null)
    {
        throw new Exception("Internal socket error.");
    }
}
```

## Bugs and Feedback

For bugs, questions and discussions please use the [GitHub Issues](https://github.com/ArenaNet/SockNet/issues).

## LICENSE

Copyright 2015 ArenaNet, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

<http://www.apache.org/licenses/LICENSE-2.0>

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.