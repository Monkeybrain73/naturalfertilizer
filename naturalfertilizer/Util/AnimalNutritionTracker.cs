using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace naturalfertilizer
{
    public static class AnimalNutritionTracker
    {
        private const string LastAteAttr = "naturalfertilizer:lastAteFromTroughHours";

        public static void SetAteFromTrough(Entity entity)
        {
            entity.WatchedAttributes.SetDouble(LastAteAttr, entity.World.Calendar.TotalHours);
            entity.WatchedAttributes.MarkPathDirty(LastAteAttr);
        }

        public static bool AteRecently(Entity entity, double withinHours = 1)
        {
            double lastEat = entity.WatchedAttributes.GetDouble(LastAteAttr, -1);
            if (lastEat < 0) return false;
            return (entity.World.Calendar.TotalHours - lastEat) < withinHours;
        }
    }
}
