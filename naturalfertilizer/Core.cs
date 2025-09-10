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

        private Harmony HarmonyInstance => new Harmony("com.xxxapexxx.naturalferilizer");

        public override void StartPre(ICoreAPI api)
        {
            base.StartPre(api);
            HarmonyInstance.PatchAll();
        }


        public override void Start(ICoreAPI api)
        {
            api.RegisterEntityBehaviorClass("defecate", typeof(EntityBehaviorDefecate));
            api.Logger.Event("started 'Natural Fertilizer' mod");
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
    }
}
