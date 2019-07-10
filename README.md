# MLAPI.Puncher
MLAPI.Puncher is a lightweight, cross-platform, easy to use, tiny implementation (<500 lines) of NAT punchthrough. It has both a server and client.

## Features
* Supports Full Cone
* Supports Address Restricted Cone
* Supports Port-Restricted Cone
* Supports Symmetric Cone (with port prediction if both parties have symmetric cones. Requires sequential port assignment)
* Server and Client implemented in <500 lines of code
* Dependency free
* Transport independent (can integrate into other transports to share a port without interfering)
* Runs on NET 3.5 and above (currently targets ``net35;net45;net471;netstandard2.0``)
* Tested on .NET Core, Mono and .NET Framework on Windows and Linux. Should work everywhere with socket access
* Listener allows multiple people to punch at once
* Token based to detect errors and missed punches

## Usage

### Server
To start a server, use the MLAPI.Puncher.Server library. This library can run anywhere, for example as a console application (example in MLAPI.Puncher.Server.Console).
To start the server, simply use:

```csharp
PuncherServer server = new PuncherServer();
// 6776 is the port of the NAT server. Can be changed.
server.Start(new IPEndPoint(IPAddress.Any, 6776));
```

### Client
To use the client, you need the MLAPI.Puncher.Client library. This is what you use in your applications. An example of a console application can be found in MLAPI.Puncher.Client.Console.

#### Listener
The client has two parts, one part that is used by anyone who wants to allow other people to connect to them. This can be done like this:

```csharp
// Creates the listener with the address and port of the server.
PuncherClient listener = new PuncherClient("puncher.midlevel.io", 6776);

// 1234 is the port where the other peer will connect and punch through.
listener.ListenForPunches(new IPEndPoint(IPAddress.Any, 1234));
```

Note that this will not return as it will continue to listen for punches. Its recommended to be ran in a thread. (If you want a method that exits, you can use ``listener.ListenForSinglePunch`` which gives the EndPoint of the connector)

#### Connector
The second part is the connector. The party that wants to connect to a listener. It can be started with:

```csharp
// Get listener public IP address by means of a matchmaker or otherwise.
string listenerPublicIP = "46.51.179.90"

// Creates the connector with the address and port to the server.
PuncherClient connector = new PuncherClient("puncher.midlevel.io", 6776);

// Punches and returns the result
IPEndPoint remoteEndpoint = connector.Punch(IPAddress.Parse(listenerPublicIP));

// Check if NAT punchthrough was successful.
if (remoteEndpoint != null)
{
    // NAT Punchthrough was successful. It can now be connected to.
    Connect(remoteEndpoint);
}
else
{
    // NAT Punchthrough failed.
}
```

If the connector is successful in punching through, the remote address and the port that was punched through will be returned by the StartConnect method. If it failed, it will return null.

## Examples
Both client and server has example projects. See MLAPI.Puncher.Server.Console and MLAPI.Puncher.Client.Console. Start the server project, then the console project to attempt a punch.

## Public punch servers
Currently, you can use the public punch server ``puncher.midlevel.io`` on port ``6776``. 

**This server has NO guaranteed uptime and is not recommended for production. Feel free to use it for testing**.

## Future Improvements
* Optimize socket code on the server, dont use single threaded blocking sockets. Minimal data has to be shared across threads anyways (only the listening clients lookup table)
* Error handling, detect what went wrong
* Detect cone type
* ~~Improve the amount of simultaneous connectors that a listener can knock at a time~~
* Cryptographically secure