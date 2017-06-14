# IoP Proximity Server - Installation

In order to install and run the Proximity Server, you need to 

 * Install .NET Core,
 * install OpenSSL (needed only for TLS certificate generation),
 * download the latest release Proximity Server source codes from GitHub,
   * AND download IoP SDK for .NET Core sources,
   * AND build the Proximity Server binaries,
 * OR download the Proximity Server binaries and install dependencies,
 * configure the Proximity Server,
 * open ports on your firewall,
 * install and run IoP LOC Server,
 * run the Proximity Server.


## Install .NET Core

Note that this is not needed if you are going to use any of the binary releases. Simply go to [Microsoft .NET Core website](https://www.microsoft.com/net/core) and follow the instructions on how to install .NET Core SDK to your system.


## Install OpenSSL

Please visit [OpenSSL website](https://www.openssl.org/) and follow the instructions there. If you are running Windows OS, you can download OpenSSL from 
[Win32 OpenSSL Installation Project website](https://slproweb.com/products/Win32OpenSSL.html).


## Download Proximity Server Source Codes

The goal here is to download the latest release branch of the source code. If you are familiar with GIT and GitHub, you will probably know what to do.
If you are not familiar with them, simply go to the [Branches Page](https://github.com/Fermat-ORG/iop-proximity-server/branches/) of the repository and find a branch that starts with `release/` and click on its name. 
Then click the *Clone or download* green button on the right side. Then click the *Download ZIP* link and save the file to your disk. Unzip the file to any folder of your choice. This folder will be called `$InstDir` in the text below. 

## Download IoP SDK for .NET Core Source Codes

Proximity Server project has a dependency on [IoP SDK for .NET Core](https://github.com/Fermat-ORG/iop-sdk-netcore) project. In order to compile Proximity Server, you will need to download the source code 
of IoP SDK for .NET Core and have it in the right directory. Just as in case of Proximity Server, you need to find the latest release branch on the [Branches Page](https://github.com/Fermat-ORG/iop-sdk-netcore/branches/) 
of IoP SDK for .NET Core repository and download it or clone it. You need to put the source code of IoP SDK for .NET Core into a directory called `iop-sdk-netcore` on the same level as your `$InstDir`.


## Build Proximity Server

Go to `$InstDir\src\ProximityServer` and execute 

```
dotnet restore
```

Then depending on which operating system you are running, execute one of these lines (you can execute all of them if you want to create executables for all the supported platforms):

```
dotnet publish --configuration Release --runtime win7-x64
dotnet publish --configuration Release --runtime win81-x64
dotnet publish --configuration Release --runtime win10-x64
dotnet publish --configuration Release --runtime ubuntu.14.04-x64
dotnet publish --configuration Release --runtime ubuntu.16.04-x64
```

If you are using Windows Server software, please check list of [.NET Core Runtime Identifiers](https://github.com/dotnet/docs/blob/master/docs/core/rid-catalog.md) to see which runtime ID to use with which Windows Server version.

The publish command will create `$InstDir\src\ProximityServer\bin` folder with a subfolder structure that contains the compiled Proximity Server binaries. The actual name of the final binary folder 
differs with each operating system. The final folder with the binaries will be called `$BinDir` in the text below. 

Now you need to edit file `$InstDir\src\ProximityServer\ProximityServer.csproj` and change the value of `RuntimeIdentifier` inside `PropertyGroup` tag to contain runtime ID of your system. 
For example, if you are using 64-bit Windows 10, you need to have this line 

```
    <RuntimeIdentifier>win10-x64</RuntimeIdentifier>
```

if you are on 64-bit Ubuntu 14.04, you want to change it to 
```
    <RuntimeIdentifier>ubuntu.14.04-x64</RuntimeIdentifier>
```



Finally, in `$InstDir\src\ProximityServer` execute 

```
dotnet ef --configuration Release database update
```

to initialize the Proximity Server's database file `ProximityServer.db`, which should be created in `$BinDir`. If that does not work, execute 

```
dotnet ef database update
```

instead, which will create `ProximityServer.db` inside `Debug` directory structure. In this case, you need to copy the database file to `$BinDir`.


## Download Proximity Server Binaries

Go to [Releases Page](https://github.com/Fermat-ORG/iop-profile-server/releases) and download the latest release for your platform, if available.


### Dependencies for Windows 

Make sure your system is fully updated using Windows Update. Then you need to install [Visual C++ Redistributable for Visual Studio 2015 (64-bit)](https://www.microsoft.com/en-gb/download/details.aspx?id=48145).


### Dependencies for Linux

You need to have `libunwind8`, `libcurl4-openssl-dev` installed and on some systems (like Ubuntu 14.04) also `libicu52` is needed. If you do not have then, install then with the following command:

```
apt-get install libunwind8
apt-get install libcurl4-openssl-dev
apt-get install libicu52
```


## Configure Proximity Server

### Generate TLS Certificate

You will need to generate a TLS certificate in PFX format and then modify the configuration file.

To generate a PFX certificate, you can use OpenSSL as follows. Make sure that the final certificate is NOT password protected.
```
openssl req -x509 -newkey rsa:4096 -keyout ProximityServer.key -out ProximityServer.cer -days 365000
openssl pkcs12 -export -out ProximityServer.pfx -inkey ProximityServer.key -in ProximityServer.cer
```

The new file `ProximityServer.pfx` is your TLS certificate that you need to put it in `$BinDir`.


### Modify Configuration File

Next step is to find the configuration file `$BinDir\ProximityServer.conf` and modify it. If you want to use the Proximity Server in its default configuration, 
there is only one setting that you need to modify - `external_server_address`. 
You have to set its value to the static public IP address of your server. For example, if your server's IP address is `198.51.100.53`, change the relevant line of the configuration file as follows:

```
external_server_address = 198.51.100.53
```


## Open Firewall Ports 

By default, Proximity Server operates on TCP ports 16987 and 16988. These ports must be open and publicly accessible on the IP `external_server_address` address. However, if you decide to change the configuration 
of the Proximity Server, you will have to open all ports specified by 

 * `primary_interface_port`,
 * `neighbor_interface_port`, and
 * `client_interface_port`

settings in the configuration file.


## Run Proximity Server

Simply go to your `$BinDir` and execute:

```
ProximityServer
```

or 

```
./ProximityServer
```

depending on which operating system you use.


## Troubleshooting

If you use the default configuration, every time you run the Proximity Server, logs are going to be created in `$BinDir\Logs` folder. If there are any problems 
with your Proximity Server, the log file will contain detailed information about it and may help you solve the problems.


### CoreCLR Initialization Error On Linux

If you run Proximity Server on Linux and you see the following error

```
Failed to initialize CoreCLR, HRESULT: 0x8007001F
```
 
it is probably caused by access rights of the Proximity Server binaries. Make sure to add execution right to all binaries.


### Problem To Run Proximity Server On Background On Linux, Nohup Crashes

If you are having problems to run Proximity Server on background on Linux, run `screen` and run Proximity Server there and then detach the screen using CTRL+A+D.
