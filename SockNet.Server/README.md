SockNet.Server
=====
A networking server implementation that uses SockNet channels.

Example
==========
The example bellow will echo all the packets back to the client.

```csharp
// create a server that will bind to any local address and a random available port
ServerSockNetChannel server = SockNetServer.Create(IPAddress.Any, 0);

try
{
	// attempts to bind the server
    server.Bind();

    // configure a incoming data listener that will echo the recived data back to the client
    server.Pipe.AddIncomingFirst<Stream>((ISockNetChannel channel, ref Stream data) => 
    {
    	// send the data back to the client
        channel.Send(data);
    });

    // do some things
}
finally
{
	// close the server
    server.Close();
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