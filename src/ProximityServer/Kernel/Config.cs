using ProximityServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using IopCrypto;
using ProximityServer.Data.Models;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.IO;
using ProximityServer.Network;
using System.Globalization;
using IopCommon.Multiformats;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Network.CAN;
using IopProtocol;

namespace ProximityServer.Kernel
{
  /// <summary>
  /// Provides access to the global configuration. The configuration is partly stored in the configuration file 
  /// and partly in the database. Other modules access the configuration via Kernel.Config.Configuration, which 
  /// is a static instance of this class.
  /// </summary>
  /// <remarks>
  /// Loading configuration is essential for the proximity server's startup. If any part of it fails, the proximity server will refuse to start.
  /// </remarks>
  public class Config : ConfigBase
  {
    /// <summary>Instance logger.</summary>
    protected new Logger log = new Logger("ProximityServer." + ComponentName);

    /// <summary>Default name of the configuration file.</summary>
    public const string ConfigFileName = "ProximityServer.conf";

    /// <summary>Instance of the configuration component to be easily referenced by other components.</summary>
    public static Config Configuration;

    /// <summary>Certificate to be used for TCP TLS server. </summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public X509Certificate TcpServerTlsCertificate;

    /// <summary>Description of role servers.</summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public ConfigServerRoles ServerRoles;

    /// <summary>Cryptographic keys of the server that can be used for signing messages and verifying signatures.</summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public KeysEd25519 Keys;

    /// <summary>Specification of a machine's network interface, on which the proximity server will listen.</summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public IPAddress BindToInterface;

    /// <summary>External IP address of the server from its network peers' point of view.</summary>
    public IPAddress ExternalServerAddress;

    /// <summary>Name of the database file.</summary>
    public string DatabaseFileName;

    /// <summary>Maximal number of activities this server is willing to maintain.</summary>
    public int MaxActivities;

    /// <summary>Maximum number of parallel neighborhood initialization processes that the proximity server is willing to process.</summary>
    public int NeighborhoodInitializationParallelism;

    /// <summary>End point of the Location Based Network server.</summary>
    public IPEndPoint LocEndPoint;

    /// <summary>End point of the Content Address Network server.</summary>
    public IPEndPoint CanEndPoint;

    /// <summary>Time in seconds between the last contact from a neighbor server up to the point when the proximity server considers the neighbor as dead.</summary>
    public int NeighborExpirationTimeSeconds;

    /// <summary>Time in seconds between the last refresh request sent by the proximity server to its Follower server.</summary>
    public int FollowerRefreshTimeSeconds;

    /// <summary>Test mode allows the server to violate protocol as some of the limitations are not enforced.</summary>
    public bool TestModeEnabled;

    /// <summary>Maximum number of neighbors that the proximity server is going to accept.</summary>
    public int MaxNeighborhoodSize;

    /// <summary>Maximum number of follower servers the proximity server is willing to share its database with.</summary>
    public int MaxFollowerServersCount;


    /// <summary>Last sequence number used for IPNS record.</summary>
    public UInt64 CanIpnsLastSequenceNumber;

    /// <summary>
    /// CAN hash of the proximity server's contact information object in CAN loaded from the database.
    /// This information may not reflect the current contact information hash. That one is stored in ContentAddressNetwork.canContactInformationHash.
    /// This is used for initialization only.
    /// </summary>
    public byte[] CanProximityServerContactInformationHash;

    /// <summary>True if the proximity server's contact information loaded from the database differs from the one loaded from the configuration file.</summary>
    public bool CanProximityServerContactInformationChanged;


    /// <summary>GPS location loaded from database.</summary>
    public GpsLocation LocLocation;



    public override bool Init()
    {
      log.Trace("()");

      bool res = false;
      Configuration = this;

      try
      {
        if (LoadConfigurationFromFile(ConfigFileName))
        {
          if (InitializeDbSettings())
          {
            res = true;
            Initialized = true;
          }
          else log.Error("Database initialization failed.");
        }
        else log.Error("Loading configuration file failed.");
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    public override void Shutdown()
    {
      log.Trace("()");

      log.Trace("(-)");
    }


    /// <summary>
    /// <para>Loads global configuration from a string array that corresponds to lines of configuration file.</para>
    /// <seealso cref="LoadConfigurationFromFile"/>
    /// </summary>
    /// <param name="Lines">Proximity server configuration as a string array.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public override bool LoadConfigurationFromStringArray(string[] Lines)
    {
      log.Trace("()");

      bool error = false;
      if ((Lines != null) && (Lines.Length > 0))
      {
        bool testModeEnabled = false;
        string tcpServerTlsCertificateFileName = null;
        X509Certificate tcpServerTlsCertificate = null;
        ConfigServerRoles serverRoles = null;
        IPAddress externalServerAddress = null;
        IPAddress bindToInterface = null;
        string databaseFileName = null;
        int maxActivities = 0;
        int neighborhoodInitializationParallelism = 0;
        int locPort = 0;
        int canPort = 0;
        IPEndPoint locEndPoint = null;
        IPEndPoint canEndPoint = null;
        int neighborExpirationTimeSeconds = 0;
        int followerRefreshTimeSeconds = 0;
        int maxNeighborhoodSize = 0;
        int maxFollowerServersCount = 0;

        Dictionary<string, object> nameVal = new Dictionary<string, object>(StringComparer.Ordinal);

        // Definition of all supported values in configuration file together with their types.
        Dictionary<string, ConfigValueType> namesDefinition = new Dictionary<string, ConfigValueType>(StringComparer.Ordinal)
        {
          { "test_mode",                               ConfigValueType.OnOffSwitch    },
          { "external_server_address",                 ConfigValueType.IpAddress      },
          { "bind_to_interface",                       ConfigValueType.IpAddress      },
          { "primary_interface_port",                  ConfigValueType.Port           },
          { "neighbor_interface_port",                 ConfigValueType.Port           },
          { "client_interface_port",                   ConfigValueType.Port           },
          { "db_file_name",                            ConfigValueType.StringNonEmpty },
          { "tls_server_certificate",                  ConfigValueType.StringNonEmpty },
          { "max_activities",                          ConfigValueType.Int            },
          { "neighborhood_initialization_parallelism", ConfigValueType.Int            },
          { "loc_port",                                ConfigValueType.Port           },
          { "can_api_port",                            ConfigValueType.Port           },
          { "max_neighborhood_size",                   ConfigValueType.Int            },
          { "max_follower_servers_count",              ConfigValueType.Int            },
          { "neighbor_expiration_time",                ConfigValueType.Int            },
          { "follower_refresh_time",                   ConfigValueType.Int            },
        };

        error = !LinesToNameValueDictionary(Lines, namesDefinition, nameVal);
        if (!error)
        {
          testModeEnabled = (bool)nameVal["test_mode"];
          externalServerAddress = (IPAddress)nameVal["external_server_address"];
          bindToInterface = (IPAddress)nameVal["bind_to_interface"];
          databaseFileName = (string)nameVal["db_file_name"];
          int primaryInterfacePort = (int)nameVal["primary_interface_port"];
          int neighborInterfacePort = (int)nameVal["neighbor_interface_port"];
          int clientInterfacePort = (int)nameVal["client_interface_port"];

          tcpServerTlsCertificateFileName = (string)nameVal["tls_server_certificate"];
          maxActivities = (int)nameVal["max_activities"];
          neighborhoodInitializationParallelism = (int)nameVal["neighborhood_initialization_parallelism"];

          locPort = (int)nameVal["loc_port"];
          canPort = (int)nameVal["can_api_port"];

          maxNeighborhoodSize = (int)nameVal["max_neighborhood_size"];
          maxFollowerServersCount = (int)nameVal["max_follower_servers_count"];

          neighborExpirationTimeSeconds = (int)nameVal["neighbor_expiration_time"];
          followerRefreshTimeSeconds = (int)nameVal["follower_refresh_time"];

          serverRoles = new ConfigServerRoles();
          error = !(serverRoles.AddRoleServer(primaryInterfacePort, (uint)ServerRole.Primary, false, Server.ServerKeepAliveIntervalMs)
                 && serverRoles.AddRoleServer(neighborInterfacePort, (uint)ServerRole.Neighbor, true, Server.ServerKeepAliveIntervalMs)
                 && serverRoles.AddRoleServer(clientInterfacePort, (uint)ServerRole.Client, true, Server.ClientKeepAliveIntervalMs));
        }

        if (!error)
        {
          if (!testModeEnabled && externalServerAddress.IsReservedOrLocal())
          {
            log.Error("external_server_address must be an IP address of external, publicly accessible interface.");
            error = true;
          }
        }

        if (!error)
        {
          if (!File.Exists(databaseFileName))
          {
            log.Error("Database file '{0}' does not exist.", databaseFileName);
            error = true;
          }
        }

        if (!error)
        {
          string finalTlsCertFileName;
          if (FileHelper.FindFile(tcpServerTlsCertificateFileName, out finalTlsCertFileName))
          {
            tcpServerTlsCertificateFileName = finalTlsCertFileName;
          }
          else
          {
            log.Error("File '{0}' not found.", tcpServerTlsCertificateFileName);
            error = true;
          }
        }

        if (!error)
        {
          try
          {
            tcpServerTlsCertificate = new X509Certificate2(tcpServerTlsCertificateFileName);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while loading certificate file '{0}': {1}", tcpServerTlsCertificateFileName, e.ToString());
            error = true;
          }
        }

        if (!error)
        {
          if ((maxActivities <= 0) || (maxActivities > ActivityBase.MaxPrimaryActivities))
          {
            log.Error("max_activities must be an integer between 1 and {0}.", ActivityBase.MaxPrimaryActivities);
            error = true;
          }
        }

        if (!error)
        {
          if (neighborhoodInitializationParallelism <= 0)
          {
            log.Error("neighborhood_initialization_parallelism must be a positive integer.");
            error = true;
          }
        }

        if (!error)
        {
          foreach (RoleServerConfiguration rsc in serverRoles.RoleServers.Values)
          {
            if (locPort == rsc.Port)
            {
              log.Error("loc_port {0} collides with port of server role {1}.", locPort, rsc.Roles);
              error = true;
              break;
            }
          }

          if (!error)
            locEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), locPort);
        }

        if (!error)
        {
          foreach (RoleServerConfiguration rsc in serverRoles.RoleServers.Values)
          {
            if (canPort == rsc.Port)
            {
              log.Error("can_api_port {0} collides with port of server role {1}.", canPort, rsc.Roles);
              error = true;
              break;
            }
          }

          if (canPort == locPort)
          {
            log.Error("can_api_port {0} collides with loc_port.", canPort);
            error = true;
          }

          if (!error)
            canEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), canPort);
        }

        if (!error)
        {
          if (!testModeEnabled && (neighborExpirationTimeSeconds < Neighbor.MinNeighborhoodExpirationTimeSeconds))
          {
            log.Error("neighbor_expiration_time must be an integer number greater or equal to {0}.", Neighbor.MinNeighborhoodExpirationTimeSeconds);
            error = true;
          }
        }

        if (!error)
        {
          bool followerRefreshTimeSecondsValid = (0 < followerRefreshTimeSeconds) && (followerRefreshTimeSeconds < Neighbor.MinNeighborhoodExpirationTimeSeconds);
          if (!testModeEnabled && !followerRefreshTimeSecondsValid)
          {
            log.Error("follower_refresh_time must be an integer number between 1 and {0}.", Neighbor.MinNeighborhoodExpirationTimeSeconds - 1);
            error = true;
          }
        }

        if (!error)
        {
          if (!testModeEnabled && (maxNeighborhoodSize < Neighbor.MinMaxNeighborhoodSize))
          {
            log.Error("max_neighborhood_size must be an integer number greater or equal to {0}.", Neighbor.MinMaxNeighborhoodSize);
            error = true;
          }
        }

        if (!error)
        {
          if (!testModeEnabled && (maxFollowerServersCount < maxNeighborhoodSize))
          {
            log.Error("max_follower_servers_count must be an integer greater or equal to max_neighborhood_size.");
            error = true;
          }
        }

        // Finally, if everything is OK, change the actual configuration.
        if (!error)
        {
          TestModeEnabled = testModeEnabled;
          Settings["TestModeEnabled"] = TestModeEnabled;

          ExternalServerAddress = externalServerAddress;
          Settings["ExternalServerAddress"] = ExternalServerAddress;

          BindToInterface = bindToInterface;
          Settings["BindToInterface"] = BindToInterface;

          ServerRoles = serverRoles;
          Settings["ServerRoles"] = ServerRoles;

          DatabaseFileName = databaseFileName;
          Settings["DatabaseFileName"] = DatabaseFileName;

          TcpServerTlsCertificate = tcpServerTlsCertificate;
          Settings["TcpServerTlsCertificate"] = TcpServerTlsCertificate;

          MaxActivities = maxActivities;
          Settings["MaxActivities"] = MaxActivities;

          NeighborhoodInitializationParallelism = neighborhoodInitializationParallelism;
          Settings["NeighborhoodInitializationParallelism"] = NeighborhoodInitializationParallelism;

          LocEndPoint = locEndPoint;
          Settings["LocEndPoint"] = LocEndPoint;

          CanEndPoint = canEndPoint;
          Settings["CanEndPoint"] = CanEndPoint;

          NeighborExpirationTimeSeconds = neighborExpirationTimeSeconds;
          Settings["NeighborExpirationTimeSeconds"] = NeighborExpirationTimeSeconds;

          FollowerRefreshTimeSeconds = followerRefreshTimeSeconds;
          Settings["FollowerRefreshTimeSeconds"] = FollowerRefreshTimeSeconds;

          MaxNeighborhoodSize = maxNeighborhoodSize;
          Settings["MaxNeighborhoodSize"] = MaxNeighborhoodSize;

          MaxFollowerServersCount = maxFollowerServersCount;
          Settings["MaxFollowerServersCount"] = MaxFollowerServersCount;


          log.Info("New configuration loaded successfully.");
        }
      }
      else log.Error("Configuration file is empty.");

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Initializes the database, or loads database configuration if the database already exists.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool InitializeDbSettings()
    {
      log.Trace("()");

      bool res = false;

      CanIpnsLastSequenceNumber = 0;
      CanProximityServerContactInformationChanged = false;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        log.Trace("Loading database settings.");
        Setting initialized = unitOfWork.SettingsRepository.Get(s => s.Name == "Initialized").FirstOrDefault();
        if ((initialized != null) && (!string.IsNullOrEmpty(initialized.Value)) && (initialized.Value == "true"))
        {
          Setting privateKeyHex = unitOfWork.SettingsRepository.Get(s => s.Name == "PrivateKeyHex").FirstOrDefault();
          Setting publicKeyHex = unitOfWork.SettingsRepository.Get(s => s.Name == "PublicKeyHex").FirstOrDefault();
          Setting expandedPrivateKeyHex = unitOfWork.SettingsRepository.Get(s => s.Name == "ExpandedPrivateKeyHex").FirstOrDefault();
          Setting externalServerAddress = unitOfWork.SettingsRepository.Get(s => s.Name == "ExternalServerAddress").FirstOrDefault();
          Setting primaryPort = unitOfWork.SettingsRepository.Get(s => s.Name == "PrimaryPort").FirstOrDefault();
          Setting canIpnsLastSequenceNumber = unitOfWork.SettingsRepository.Get(s => s.Name == "CanIpnsLastSequenceNumber").FirstOrDefault();
          Setting canProximityServerContactInformationHash = unitOfWork.SettingsRepository.Get(s => s.Name == "CanProximityServerContactInformationHash").FirstOrDefault();
          Setting locLocationLatitude = unitOfWork.SettingsRepository.Get(s => s.Name == "LocLocationLatitude").FirstOrDefault();
          Setting locLocationLongitude = unitOfWork.SettingsRepository.Get(s => s.Name == "LocLocationLongitude").FirstOrDefault();

          bool havePrivateKey = (privateKeyHex != null) && !string.IsNullOrEmpty(privateKeyHex.Value);
          bool havePublicKey = (publicKeyHex != null) && !string.IsNullOrEmpty(publicKeyHex.Value);
          bool haveExpandedPrivateKey = (expandedPrivateKeyHex != null) && !string.IsNullOrEmpty(expandedPrivateKeyHex.Value);
          bool havePrimaryPort = primaryPort != null;
          bool haveExternalServerAddress = (externalServerAddress != null) && !string.IsNullOrEmpty(externalServerAddress.Value);
          bool haveCanIpnsLastSequenceNumber = canIpnsLastSequenceNumber != null;
          bool haveCanContactInformationHash = (canProximityServerContactInformationHash != null) && !string.IsNullOrEmpty(canProximityServerContactInformationHash.Value);

          if (havePrivateKey
            && havePublicKey
            && haveExpandedPrivateKey
            && havePrimaryPort
            && haveExternalServerAddress
            && haveCanIpnsLastSequenceNumber)
          {
            Keys = new KeysEd25519();
            Keys.PrivateKeyHex = privateKeyHex.Value;
            Keys.PrivateKey = Keys.PrivateKeyHex.FromHex();

            Keys.PublicKeyHex = publicKeyHex.Value;
            Keys.PublicKey = Keys.PublicKeyHex.FromHex();

            Keys.ExpandedPrivateKeyHex = expandedPrivateKeyHex.Value;
            Keys.ExpandedPrivateKey = Keys.ExpandedPrivateKeyHex.FromHex();

            bool error = false;
            if (!UInt64.TryParse(canIpnsLastSequenceNumber.Value, out CanIpnsLastSequenceNumber))
            {
              log.Error("Invalid CanIpnsLastSequenceNumber value '{0}' in the database.", canIpnsLastSequenceNumber.Value);
              error = true;
            }

            if (!error)
            {
              if (haveCanContactInformationHash)
              {
                CanProximityServerContactInformationHash = Base58Encoding.Encoder.DecodeRaw(canProximityServerContactInformationHash.Value);
                if (CanProximityServerContactInformationHash == null)
                {
                  log.Error("Invalid CanProximityServerContactInformationHash value '{0}' in the database.", canProximityServerContactInformationHash.Value);
                  error = true;
                }
              }
              else CanProximityServerContactInformationChanged = true;
            }

            if (!error)
            {
              // Database settings contain information on previous external network address and primary port values.
              // If they are different to what was found in the configuration file, it means the contact 
              // information of the proximity server changed. Such a change must be propagated to proximity server's 
              // CAN records.
              string configExternalServerAddress = ExternalServerAddress.ToString();
              if (configExternalServerAddress != externalServerAddress.Value)
              {
                log.Info("Network interface address in configuration file is different from the database value.");

                CanProximityServerContactInformationChanged = true;
              }

              string configPrimaryPort = ServerRoles.GetRolePort((uint)ServerRole.Primary).ToString();
              if (configPrimaryPort != primaryPort.Value)
              {
                log.Info("Primary port in configuration file is different from the database value.");

                CanProximityServerContactInformationChanged = true;
              }
            }

            if (!error)
            {
              if ((locLocationLatitude != null) && (locLocationLongitude != null))
              {
                decimal lat;
                decimal lon;
                if (decimal.TryParse(locLocationLatitude.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out lat)
                  && decimal.TryParse(locLocationLongitude.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out lon))
                {
                  LocLocation = new GpsLocation(lat, lon);
                  log.Info("Server GPS location is [{0}].", LocLocation);
                }
              }
            }

            res = !error;
          }
          else
          {
            log.Error("Database settings are corrupted, DB has to be reinitialized.");
            if (!havePrivateKey) log.Debug("Private key is missing.");
            if (!havePublicKey) log.Debug("Public key is missing.");
            if (!haveExpandedPrivateKey) log.Debug("Expanded private key is missing.");
            if (!havePrimaryPort) log.Debug("Primary port is missing.");
            if (!haveExternalServerAddress) log.Debug("External server address is missing.");
            if (!haveCanIpnsLastSequenceNumber) log.Debug("Last CAN IPNS sequence number is missing.");
            if (!haveCanContactInformationHash) log.Debug("CAN contact information hash is missing.");
          }
        }

        if (!res)
        {
          log.Info("Database settings are not initialized, initializing now ...");

          unitOfWork.SettingsRepository.Clear();
          unitOfWork.Save();

          Keys = Ed25519.GenerateKeys();

          Setting privateKey = new Setting("PrivateKeyHex", Keys.PrivateKeyHex);
          unitOfWork.SettingsRepository.Insert(privateKey);

          Setting publicKey = new Setting("PublicKeyHex", Keys.PublicKeyHex);
          unitOfWork.SettingsRepository.Insert(publicKey);

          Setting expandedPrivateKey = new Setting("ExpandedPrivateKeyHex", Keys.ExpandedPrivateKeyHex);
          unitOfWork.SettingsRepository.Insert(expandedPrivateKey);

          Setting externalServerAddress = new Setting("ExternalServerAddress", ExternalServerAddress.ToString());
          unitOfWork.SettingsRepository.Insert(externalServerAddress);

          Setting primaryPort = new Setting("PrimaryPort", ServerRoles.GetRolePort((uint)ServerRole.Primary).ToString());
          unitOfWork.SettingsRepository.Insert(primaryPort);

          Setting canIpnsLastSequenceNumber = new Setting("CanIpnsLastSequenceNumber", "0");
          unitOfWork.SettingsRepository.Insert(canIpnsLastSequenceNumber);

          initialized = new Setting("Initialized", "true");
          unitOfWork.SettingsRepository.Insert(initialized);


          if (unitOfWork.Save())
          {
            log.Info("Database initialized successfully.");

            CanProximityServerContactInformationChanged = true;
            res = true;
          }
          else log.Error("Unable to save settings to DB.");
        }
      }

      if (res)
      {
        Settings["Keys"] = Keys;
        Settings["CanIpnsLastSequenceNumber"] = CanIpnsLastSequenceNumber;
        Settings["CanProximityServerContactInformationHash"] = CanProximityServerContactInformationHash;
        Settings["CanProximityServerContactInformationChanged"] = CanProximityServerContactInformationChanged;

        log.Debug("Server public key hex is '{0}'.", Keys.PublicKeyHex);
        log.Debug("Server network ID is '{0}'.", Crypto.Sha256(Keys.PublicKey).ToHex());
        log.Debug("Server network ID in CAN encoding is '{0}'.", CanApi.PublicKeyToId(Keys.PublicKey).ToBase58());
        log.Debug("Server primary external contact is '{0}:{1}'.", ExternalServerAddress, ServerRoles.GetRolePort((uint)ServerRole.Primary));
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
