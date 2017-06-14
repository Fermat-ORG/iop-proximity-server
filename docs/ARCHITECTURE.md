# IoP Proximity Server - Architecture

## Similarity with Profile Server

The design of Proximity Server is very similar to the design of [IoP Profile Server](https://github.com/Fermat-ORG/iop-profile-server/). 
Please read [IoP Profile Server - Architecture](https://github.com/Fermat-ORG/iop-profile-server/blob/master/docs/ARCHITECTURE.md) 
as the following information only describes what is different for Proximity Server. Everything else you could read about architecture of Profile Server 
also applies for Proximity Server.



## Proximity Server in IoP Network

Unlike Profile Server, Proximity Server does not have any concept of user profiles. Proximity Server is a server that manages so called *activities*. 
But just as Profile Servers form neighborhood and share profiles they host among themselves, Proximity Servers form similar neighborhoods 
and share activities of their clients among themselves. The concept of *neighbors* and *followers* is the same.

An activity is an object with relatively short lifetime (from several minutes up to a few days) that represents an activity or an event in the real world. 
The role of Proximity Servers in the network is to allow clients to create their own activities and look for activities of other clients. 
An important characteristic of an activity is that it always lives on the Proximity Server that is nearest to its own geographical location.
Activities are allowed to be moved, which may cause them to be migrated to different Proximity Server. This concepts allows the clients to get a list 
of nearby activities just by asking the Proximity Server that is nearest to their location.

Each activity contains contact information to its client's hosting Profile Server. This makes it easy to contact the owner of the activity.


### Connections to Location Based Network and Content Address Network

These connections work in a same way as in case of Profile Servers. There just minor differences. One of them is that activities, unlike profiles, 
are not registered (and thus searchable) in CAN network. Unlike Profile Server, Proximity Server does not provide any functionality for the client 
to communicate with CAN.


### Connections to Client Applications

In a way, Proximity Server is much simpler than Profile Server as it provides only couple of services:

 * creating activities,
 * deleting activities,
 * getting information about specific activities,
 * searching for nearby activities.

And Proximity Server does recognize only one type of client.


## Proximity Server Fundamentals

The Proximity Server is based on the same fundamentals as Profile Server including the related projects to support development.



## Proximity Server Component Layers

Again, the design is pretty much the same as in case of Profile Server, with just a few changes described below.


### Data Layer

Proximity Server does not need Image manager component as activities do not come with any pictures. All bigger data are supposed 
to be stored in CAN network.

Proximity Server implements the following database tables:

 * Settings - Same as in Profile Server. Stores part of the configuration of the proximity server including the proximity server cryptographic identity.
 * PrimaryActivities - Similar to Profile Server's Identities table. Stores all activities that are managed by this proximity server. 
 * NeighborActivities - Similar to Profile Server's NeighborIdentities table. Stores all activities that are managed on neighbor proximity servers. 
 * NeighborhoodActions - Same as in Profile Server. Lists of pending actions related to the management of the proximity server's neighborhood are stored in this table. When a proximity server is informed about a change 
in its neighborhood, such as if a new server joined the neighborhood, or a server left the neighborhood, a new action is added. Similarly, in case of a change in the proximity server's  
activity database, the change has to be propagated to the followers, which is done through actions in this table.
 * Neighbors - Stores a list of proximity servers that the proximity server considers to be its neighbors, i.e. updates of their primary activities can come from them.
 * Followers - Stores a list of proximity servers that are following the proximity server, i.e. changes in primary activities has to be propagated to them.


### Network Layer

Proximity Server does have Relay Connection module and does not implement any similar concept.

Proximity Server's connection to CAN is limited to only propagating the server's contact information. Activities are not propagated in any way to CAN.
