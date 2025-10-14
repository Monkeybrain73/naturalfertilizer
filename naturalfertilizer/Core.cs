[assembly: ModInfo("naturalfertilizer",
                    Authors = new string[] { "xXx_Ape_xXx" },
                    Description = "Adds natural fertilizer created from animal droppings",
                    Version = "1.3.0")]

namespace naturalfertilizer
{
    public sealed class Core : ModSystem
    {
        private Harmony HarmonyInstance => new Harmony("com.xxxapexxx.naturalferilizer");


        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
            HarmonyInstance.PatchAll();
        }

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterBlockClass("natfert.manurestack", typeof(BlockManureStack));
            api.RegisterBlockClass("natfert.manurepile", typeof(BlockManurePile));
            api.Logger.Event("... it poops");

            api.RegisterBlockEntityClass("natfert.bemanurestack", typeof(BlockEntityManureStack));
            api.RegisterBlockEntityClass("natfert.bemanurepile", typeof(BEManurePile));
            api.Logger.Event("... it smells");

            api.RegisterEntityBehaviorClass("natfert.defecate", typeof(EntityBehaviorDefecate));
            api.Logger.Event("... it reeks");

            api.RegisterCollectibleBehaviorClass("natfert.manurepile", typeof(CollectibleBehaviorManurePile));
            api.RegisterCollectibleBehaviorClass("natfert.manurestack", typeof(CollectibleBehaviorManureStack));

            api.Logger.Event("... the flies are buzzing");
            api.Logger.Event("[NaturalFertilizer] mod started");

            if (api.Side == EnumAppSide.Server)
            {
                Configs.TryLoadServerConfig(api as ICoreServerAPI);
            }
            if (api.Side == EnumAppSide.Client)
            {
                Configs.TryLoadClientConfig(api as ICoreClientAPI);
            }

        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
        }

        public static class DebugUtil
        {
            public static void Log(ICoreAPI api, string message, params object[] args)
            {
                if (Configs.SConfig.EnableDebugLogging)
                {
                    api.World.Logger.Debug("[NaturalFertilizer] " + message, args);
                }
            }

            public static void Verbose(ICoreAPI api, string message, params object[] args)
            {
                if (Configs.SConfig.EnableDebugLogging)
                {
                    api.World.Logger.VerboseDebug("[NaturalFertilizer] " + message, args);
                }
            }

            public static void Error(ICoreAPI api, string message, params object[] args)
            {
                api.World.Logger.Error("[NaturalFertilizer] " + message, args);
            }

            /// <summary>
            /// Logs all public config values from any object (e.g. ServerConfig, ClientConfig)
            /// </summary>
            public static void LogConfig(ICoreAPI api, string title, object config)
            {
                if (config == null)
                {
                    Verbose(api, $"{title}: (null)");
                    return;
                }

                var type = config.GetType();
                var sb = new StringBuilder();
                sb.Append($"{title}: ");

                var properties = type.GetProperties();
                foreach (var prop in properties)
                {
                    object value = prop.GetValue(config);

                    if (value is Array arr)
                    {
                        sb.AppendFormat("{0}=[{1}]; ", prop.Name, string.Join(", ", arr.Cast<object>()));
                    }
                    else
                    {
                        sb.AppendFormat("{0}={1}; ", prop.Name, value);
                    }
                }

                Verbose(api, sb.ToString());
            }
        }

        public override void Dispose()
        {
            HarmonyInstance.UnpatchAll("com.xxxapexxx.naturalferilizer");
            base.Dispose();
        }
    }
}
