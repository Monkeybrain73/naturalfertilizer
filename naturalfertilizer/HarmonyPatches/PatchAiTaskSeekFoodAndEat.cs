using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace naturalfertilizer
{
    [HarmonyPatch(typeof(AiTaskSeekFoodAndEat), "ContinueExecute")]
    public static class Patch_AiTaskSeekFoodAndEat
    {

        [HarmonyPostfix]
        static void Postfix(AiTaskSeekFoodAndEat __instance, EntityAgent ___entity, ref bool __result)
        {
            if (!__result && __instance.QuantityEatenLastMeal() > 0)
            {
                var defecateBh = ___entity.GetBehavior<EntityBehaviorDefecate>();
                defecateBh?.Notify("mealEaten", __instance.TargetPoi());
                // Debug.WriteLine("DEBUG: Notified defecate behavior of meal eaten");
            }
        }
    }

    public static class EatTaskAccess
    {
        static readonly System.Reflection.FieldInfo qtyField =
            AccessTools.Field(typeof(AiTaskSeekFoodAndEat), "quantityEaten");
        static readonly System.Reflection.FieldInfo targetPoiField =
            AccessTools.Field(typeof(AiTaskSeekFoodAndEat), "targetPoi");

        public static float QuantityEatenLastMeal(this AiTaskSeekFoodAndEat task)
        {
            return (float)qtyField.GetValue(task);
        }

        public static object TargetPoi(this AiTaskSeekFoodAndEat task)
        {
            return targetPoiField.GetValue(task);
        }
    }


}
