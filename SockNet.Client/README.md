SockNet.Client
=====
A networking client implementation that uses SockNet channels.

Example
==========
The example bellow will connect to "www.guildwars2.com", send a GET request, retrieve the first packet, print it out, and then disconnect.

```csharp
// blocking collection that will contain incoming HTTP response data
BlockingCollection<string> responseData = new BlockingCollection<string>();

// configure and create a channel
using (ClientSockNetChannel client = SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80)))
{
	// connect to the channel's endpoint
	if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null)
	{
	    throw new IOException(string.Format("Connection to [{0}] timed out.", client.RemoteEndpoint));
	}
	
	// add response collector
	client.Pipe.AddIncomingLast<ChunkedBuffer>((ISockNetChannel channel, ref ChunkedBuffer buffer) =>
	{
	    StreamReader reader = new StreamReader(buffer.Stream, Encoding.UTF8);
	
	    responseData.Add(reader.ReadToEnd());
	});
	
	// send a message
	if (client.Send(ChunkedBuffer.Wrap("GET / HTTP/1.1\r\nHost: www.guildwars2.com\r\n\r\n", Encoding.UTF8)).WaitForValue(TimeSpan.FromSeconds(5)) == null)
	{
	    throw new IOException(string.Format("HTTP message request to [{0}] timed out.", client.RemoteEndpoint));
	}
	
	// print out response data once we get it
	string firstResponseData;
	
	if (!responseData.TryTake(out firstResponseData, (int)TimeSpan.FromSeconds(5).TotalMilliseconds))
	{
	    throw new IOException(string.Format("Response timeout from [{0}].", client.RemoteEndpoint));
	}
	
	Console.Write(firstResponseData);
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
