using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace naturalfertilizer
{
    public class EntityBehaviorDefecate : EntityBehavior
    {
        public readonly ICoreAPI api;
        public double nextDefecationHour = -1;
        public readonly Random rand;

        public EntityBehaviorDefecate(Entity entity) : base(entity)
        {
            rand = new Random(entity.EntityId.GetHashCode());
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
        }


        public override void Notify(string key, object data)
        {
            IWorldAccessor world = entity.World;

            if (key == "mealEaten")
            {
                double currentHour = entity.World.Calendar.TotalHours;
                nextDefecationHour = currentHour + 2 + rand.NextDouble() * 2; // 2–4 hours
                // string displayName = entity.GetName();
                // world.Logger.VerboseDebug("{0} ate a meal at {1}, next defecation scheduled at {2}", displayName, FormatTime(currentHour), FormatTime(nextDefecationHour));
            }
        }

        public override void OnGameTick(float deltaTime)
        {
            if (nextDefecationHour > 0 && entity.World.Calendar.TotalHours >= nextDefecationHour)
            {
                
                Defecate();
                nextDefecationHour = -1;
            }
        }

        private void Defecate()
        {
            IWorldAccessor world = entity.World;

            int variant = rand.Next(1, 5);
            Block dungBlock = world.GetBlock(new AssetLocation($"naturalfertilizer:dung-{variant}"));
            if (dungBlock == null) return;

            BlockPos pos = entity.ServerPos.AsBlockPos.DownCopy();

            if (world.BlockAccessor.GetBlock(pos).IsReplacableBy(dungBlock))
            {
                world.BlockAccessor.SetBlock(dungBlock.BlockId, pos);
            }
            else
            {
                pos = entity.ServerPos.AsBlockPos;
                if (world.BlockAccessor.GetBlock(pos).IsReplacableBy(dungBlock))
                {
                    world.BlockAccessor.SetBlock(dungBlock.BlockId, pos);
                }
            }
            // string displayName = entity.GetName();
            // world.Logger.VerboseDebug("{0} defecated at {1},{2},{3}", displayName, pos.X - 512000, pos.Y, pos.Z -512000);
            // Reset next defecation time
            nextDefecationHour = -1;
        }
        public override string PropertyName() => "defecate";

        private string FormatTime(double totalHours)
        {
            int totalMinutes = (int)(totalHours * 60);
            int hours = (totalMinutes / 60) % 24;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }

    }
}