using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace naturalfertilizer
{
    public class Configs
    {
        public static ServerConfig SConfig { get; set; } = new();
        public static ClientConfig CConfig { get; set; } = new();

        private const string ConfigNameServer = "naturalfertilizer-server.json";
        private const string ConfigNameClient = "naturalfertilizer-client.json";

        #region ServerConfig
        public class ServerConfig
        {
            public bool EnableDebugLogging { get; set; } = false;
            public string[] ForbiddenDefecationBlocks { get; set; } = new[] { "egg", "dung", "smallpoop", "manure" };

            // --- Truth and Beauty: Detailed Animals Compatibility ---
            public bool EnableGenelibCompat { get; set; } = true;
            public double GenelibSatietyThreshold { get; set; } = -1.2;
            public double GenelibMinDefecationDelayHours { get; set; } = 18.0;
            public double GenelibMaxDefecationDelayHours { get; set; } = 28.0;

        }

        public static void TryLoadServerConfig(ICoreServerAPI api)
        {
            try
            {
                SConfig = api.LoadModConfig<ServerConfig>(ConfigNameServer);
                if (SConfig == null)
                {
                    SConfig = new ServerConfig();
                    api.Logger.VerboseDebug("[NaturalFertilizer] Config file not found, creating a new one...");
                }
                api.StoreModConfig(SConfig, ConfigNameServer);
                api.Logger.Event("[NaturalFertilizer] Server Config loaded");

                Core.DebugUtil.LogConfig(api, "Loaded server config values", Configs.SConfig);
            }
            catch (Exception ex)
            {
                api.Logger.Error("[NaturalFertilizer] Failed to load config, you probably made a typo:");
                api.Logger.Error(ex);
                SConfig = new ServerConfig();
            }
        }
        #endregion

        #region ClientConfig
        public class ClientConfig
        {
            public string[] FlyItemCodeRegexSelectors { get; set; } = new[] { "naturalfertilizer:manure" };
            public string[] FlyBlockCodeRegexSelectors { get; set; } = new[] { "naturalfertilizer:(manurepile|manurestack)" };
            public float FlySpawnMinTemperature { get; set; } = 10.0f;
            public float FlySpawnMaxTemperature { get; set; } = 50.0f;
            public int MaxExtraFlyCloudCount { get; set; } = 150;
            public int MaxFlyCloudSpawnPerSimTick { get; set; } = 6;
            public int MinFlyCountPerCloud { get; set; } = 5;
        }

        public static void TryLoadClientConfig(ICoreClientAPI api)
        {
            try
            {
                CConfig = api.LoadModConfig<ClientConfig>(ConfigNameClient);
                if (CConfig == null)
                {
                    CConfig = new ClientConfig();
                    api.Logger.VerboseDebug("[NaturalFertilizer] Config file not found, creating a new one...");
                }
                api.StoreModConfig(CConfig, ConfigNameClient);
                api.Logger.Event("[NaturalFertilizer] Client Config loaded");
                Core.DebugUtil.LogConfig(api, "Loaded server config values", Configs.CConfig);
            }
            catch (Exception ex)
            {
                api.Logger.Error("[NaturalFertilizer] Failed to load config, you probably made a typo:");
                api.Logger.Error(ex);
                CConfig = new ClientConfig();
            }
        }
        #endregion

    }
}
