# IoP Proximity Server - Introduction for Developers of Client Apps


## Similarity with Profile Server

The design of Proximity Server is very similar to the design of [IoP Profile Server](https://github.com/Fermat-ORG/iop-profile-server/). 
Please read [IoP Profile Server - Introduction for Developers of Client Apps](https://github.com/Fermat-ORG/iop-profile-server/blob/master/docs/CLIENT-APPS.md) 
as the following information only describes what is different for Proximity Server. Everything else you could read in the introduction for developers for Profile Server 
also applies for Proximity Server.


## Protocol Basics

The architecture is described in [Proximity Server Architecture](ARCHITECTURE.md) document and the proximity server network protocol, is described in its [.proto file](https://github.com/Internet-of-People/message-protocol/blob/master/IopProximityServer.proto).


## Setting Up Development Environment

The installation procedure is described here: [Proximity Server Installation](https://github.com/Fermat-ORG/iop-proximity-server/blob/master/docs/INSTALLATION.md).


### CAN and LOC Dependencies

Proximity Server also relies on both of CAN and LOC servers, but unlike Profile Server, Proximity Server can not be run without LOC server.


### Multiple Proximity Servers Instances

As LOC server is required, having multiple Proximity Server instances on a single machine is slightly more complicated than having multiple Profile Server instances.
You will need to make sure that each Proximity Server instance has its own dedicated LOC server.

## Tests and Logs

[Tests for Proximity Server protocol](https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#proximity-server-tests) are available in the same project as for Profile Server protocol.


