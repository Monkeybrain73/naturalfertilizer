using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

[assembly: ModInfo("naturalfertilizer",
                    Authors = new string[] { "xXx_Ape_xXx" },
                    Description = "Adds natural fertilizer created from animal droppings",
                    Version = "1.0.0")]

namespace naturalfertilizer
{
    public sealed class Core : ModSystem
    {
        public static NaturalFertilizerConfig Config;

        private Harmony HarmonyInstance => new Harmony("com.xxxapexxx.naturalferilizer");

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
            HarmonyInstance.PatchAll();
        }


        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("natfert.manurestack", typeof(BlockManureStack));
            api.RegisterBlockClass("natfert.manurepile", typeof(BlockManurePile));
            api.Logger.Event("...it poops");

            api.RegisterBlockEntityClass("natfert.bemanurestack", typeof(BlockEntityManureStack));
            api.RegisterBlockEntityClass("natfert.bemanurepile", typeof(BEManurePile));
            api.Logger.Event("...it smells");

            api.RegisterEntityBehaviorClass("natfert.defecate", typeof(EntityBehaviorDefecate));
            api.Logger.Event("...it reeks");

            api.RegisterCollectibleBehaviorClass("natfert.manurepile", typeof(CollectibleBehaviorManurePile));
            api.RegisterCollectibleBehaviorClass("natfert.manurestack", typeof(CollectibleBehaviorManureStack));

            api.Logger.Event("'Natural Fertilizer' mod started");

            Config = api.LoadModConfig<NaturalFertilizerConfig>("naturalfertilizer.json");
            if (Config == null)
            {
                Config = new NaturalFertilizerConfig();
                api.StoreModConfig(Config, "naturalfertilizer.json");
            }
            api.Logger.Event("'Natural Fertilizer' Config loaded");
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
        }

        public override void Dispose()
        {
            HarmonyInstance.UnpatchAll("com.xxxapexxx.naturalferilizer");
            base.Dispose();
        }

        public class NaturalFertilizerConfig
        {
            public string[] ForbiddenDefecationBlocks { get; set; } = new string[0];
        }
    }
}
