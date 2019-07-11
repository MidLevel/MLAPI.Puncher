# MLAPI.Puncher
MLAPI.Puncher is a lightweight, cross-platform, easy to use, tiny implementation (<500 lines) of NAT punchthrough. It has both a server and client.

## Features
* Supports Full Cone
* Supports Address-Restricted Cone
* Supports Port-Restricted Cone
* Supports Symmetric Cone (with port prediction if both parties have symmetric cones. Requires sequential port assignment)
* Server and Client implemented in <500 lines of code
* Dependency free
* Transport independent (can integrate into other transports to share a port without interfering)
* Runs on NET 3.5 and above (currently targets ``net35;net45;net471;netstandard2.0``)
* Tested on .NET Core, Mono and .NET Framework on Windows and Linux. Should work everywhere with socket access
* Listener allows multiple people to punch at once
* Token based to detect errors and missed punches
* Fast, Punches in 10-20 ms (on localhost, latency not included)
* Well commented code (read the flow below to get more understanding)
* Safe against routing attacks by validating addresses (optional)
* Multi server cluster supported (You can run multiple puncher servers, and clients will use all of them)

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
// Disposal stops everything and closes the connection.
using (PuncherClient listener = new PuncherClient("puncher.midlevel.io", 6776))
{
    // 1234 is the port where the other peer will connect and punch through.
    // That would be the port where your program is going to be listening after the punch is done.
    listener.ListenForPunches(new IPEndPoint(IPAddress.Any, 1234));
}
```

Note that this will not return as it will continue to listen for punches. Its recommended to be ran in a thread. (If you want a method that exits, you can use ``listener.ListenForSinglePunch`` which gives the EndPoint of the connector)

#### Connector
The second part is the connector. The party that wants to connect to a listener. It can be started with:

```csharp
// Get listener public IP address by means of a matchmaker or otherwise.
string listenerAddress = "46.51.179.90"

// Creates the connector with the address and port to the server.
// Disposal stops everything and closes the connection.
using (PuncherClient connector = new PuncherClient("puncher.midlevel.io", 6776))
{
    // Punches and returns the result
    if (connector.TryPunch(IPAddress.Parse(listenerAddress), out IPEndPoint remoteEndPoint);
    {
        // NAT Punchthrough was successful. It can now be connected to using your normal connection logic.
        Connect(remoteEndpoint);
    }
    else
    {
        // NAT Punchthrough failed.
    }
}
```

If the connector is successful in punching through, the remote address and the port that was punched through will be provided in the out endpoint from the StartConnect method. If it failed, it will return false and the endpoint will be null.

## Settings
The PuncherClient has a few settings that can be tweaked. They are listed below.


#### Transport
This is the transport you want to use, a transport is a class that inherits IUDPTransport and it's what handles all socket access. This allows you to integrate this into any networking library that has some sort of "Unconnected messages" functionality. It defaults to ``new SlimUDPTransport()`` which is just a wrapper around the Socket class.

#### PortPredictions
Port predictions are the amount of ports to be predicted, this is useful for solving symmetric NAT configurations that assigns ports in sequential order. What it actually does is, if you punch at port X and port prediction is set to 2. It will punch at X, X+1 and X+2.

#### PunchResponseTimeout
This is the time the Connector will wait for a punch response before assuming the punchthrough failed.

#### ServerRegisterResponseTimeout
This is the timeout for connecting to the Puncher server. If no response is received within this time, an exception will be thrown.

#### ServerRegisterInterval
This is the interval at which a listener will resend Register requests. This is because servers will clear their records if a client is not sending pings regularly. Default is 60 seconds, default server clear interval is 120 seconds.

#### SocketSendTimeout
This is the timeout sent to the Transport Send methods. The default value is 500 milliseconds.

#### SocketReceiveTimeout
This is the timeout sent to the Transport Receive methods. You want to keep this fairly low as all timeouts depend on the receive methods returning rather quickly. The default value is 500 milliseconds.

#### DropUnknownAddresses
If a connector sends a connect request to the Punch server, and gets a response that has a different address than the one we requested, and this option is turned on, the punch will be ignored. This could mean either that a proxy is used or that a routing attac is being performed. This defaults to true and is recommended to be set to true if you dont trust the punch server.

## Examples
Both client and server has example projects. See MLAPI.Puncher.Server.Console and MLAPI.Puncher.Client.Console.

## Public punch server
Currently, you can use the public punch server ``puncher.midlevel.io`` on port ``6776``. 

**This server has NO guaranteed uptime and is not recommended for production. Feel free to use it for testing**.

## Future Improvements
* Optimize socket code on the server, dont use single threaded blocking sockets. Minimal data has to be shared across threads anyways (only the listening clients lookup table)
* ~~Error handling, detect what went wrong~~
* Detect cone type
* ~~Improve the amount of simultaneous connectors that a listener can knock at a time~~
* Cryptographically secure (probably not going to be done, just implementing certificates requires reliability + some fragmentation because certificates are really large). Also, there is not too much value in incercepting this, all addresss are verified anyways to be correct. It's just the port that is resolved.

## Flow
Definitions:
```
LC = Listener Client
CC = Connector Client
PS = Puncher Server
Address = IPv4 Address WITHOUT Port
EndPoint = IPv4 Address AND Associated Port
```

1. LC sends Register packet to PS to inform that it's now ready to listen.
2. PS adds the listeners address to a lookup table. The key is the address (excluding port) and the value is is the endpoint (with port).
3. PS sends Registered packet to confirm registration to LC. If LC does not receive the Registered packet, it times out.
4. CC sends Register packet to PS. Included in the packet is the address of the listener he wishes to connect to.
5. PS looks up if the address is found. If it is not, it sends a Error packet back to CC. If it is found, it sends a ConnectTo packet to CC with LC's endpoint, and a ConnectTo packet to LC with CC's endpoint.
6. * LC receives ConnectTo packet and sends (PuncherClient.PortPredictions) amount of  Punch packets to CC. If the ConnectTo packet contains the address 10.10.10.10 and the port 2785, and PuncherClient.PortPredictions is set to 5. It sends 5 Punch packets, one on each of the following ports: 2785, 2786, 2787, 2788. (This is to try to trick symmetric NATs with sequential assignment)
   * CC receives ConnectTo packet and does the same as LC does in step 6.1, but sends them to LC instead of CC.
7. * If CC gets a punch request from a port that it has not yet punched on. It will send a new punch on that port.
   * If LC gets a punch request, it responds with a PunchSuccess packet to the sender of that punch request. (Method will exit if using ListenForSinglePunch)
   * If CC gets a PunchSuccess packet, it will return the **endpoint** of where the PunchSuccess packet came from.
