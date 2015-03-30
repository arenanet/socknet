SockNet.Protocol.Http
=====
HTTP implementation on top of SockNet.

Example
==========
The example bellow shows a client connecting to "www.guildwars2.com", sending a GET request, retrieving the response, and finally disconnecting.

```csharp
// container for incomming response
BlockingCollection<HttpResponse> responses = new BlockingCollection<HttpResponse>();

// create the client, connect to guildwars2.com, and place all incomming responses into the above container
ClientSockNetChannel client = (ClientSockNetChannel)SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80))
    .AddModule(new HttpSockNetChannelModule(HttpSockNetChannelModule.ParsingMode.Client));
client.Pipe.AddIncomingLast<HttpResponse>((ISockNetChannel channel, ref HttpResponse data) => { responses.Add(data); });

// connect and wait for 5 seconds
if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null) 
{
    throw new Exception("Connection timed out.");
}

try {
    // create a simple GET request and send it
    HttpRequest request = new HttpRequest()
    {
        Action = "GET",
        Path = "/en/",
        Version = "HTTP/1.1"
    };
    request.Header["Host"] = "www.guildwars2.com";

    client.Send(request);

    // wait for response
    HttpResponse response = null;

    if (!responses.TryTake(out response, 5))
    {
        throw new Exception("Receive timeout.");
    }

    // do something with the response
} finally {
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