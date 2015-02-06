SockNet.Client
=====
A networking client implementation that uses SockNet channels.

Example
==========
The example bellow will connect to "www.guildwars2.com", send a GET request, retrieve the first packet, print it out, and then disconnect.

```csharp
// this collection will hold the incoming packets
BlockingCollection<object> blockingCollection = new BlockingCollection<object>();

// create a SockNetClient that will connect to "www.guildwars2.com" on port 80
ClientSockNetChannel client = SockNetClient.Create(new IPEndPoint(Dns.GetHostEntry("www.guildwars2.com").AddressList[0], 80));

// connect and wait for 5 seconds
if (client.Connect().WaitForValue(TimeSpan.FromSeconds(5)) == null) 
{
	throw new Exception("Connection timed out.");
}

try
{
	// add a handler that will take the server data an place it into the blocking collection
	client.Pipe.AddIncomingFirst<Stream>((ISockNetChannel sockNetClient, ref Stream data) => { blockingCollection.Add(data); });

	// send our initial GET request
	client.Send(Encoding.UTF8.GetBytes("GET / HTTP/1.1\r\nHost: www.guildwars2.com\r\n\r\n"));

	// wait for server data
	object currentObject;
	blockingCollection.TryTake(out currentObject, DEFAULT_ASYNC_TIMEOUT);

	if (currentObject != null) 
	{
		throw new Exception("Receive timeout.")
	}

	// print the server response
	Console.WriteLine("Got response: \n" + new StreamReader((Stream)currentObject, Encoding.UTF8).ReadToEnd());
}
finally
{
	// disconnect
	if (client.Disconnect().WaitForValue(TimeSpan.FromSeconds(5)) != null)
	{
		throw new Exception("Internal socket error.");
	}
}
```

## Bugs and Feedback

For bugs, questions and discussions please use the [Github Issues](https://github.com/ArenaNet/SockNet/issues).

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