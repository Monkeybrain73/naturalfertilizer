using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace naturalfertilizer
{
    public class EntityBehaviorDefecate : EntityBehavior
    {
        public override string PropertyName() => "defecate";

        public readonly ICoreAPI api;
        public readonly Random rand;
        private string dungBase = "naturalfertilizer:dung";
        private int maxStages = 4;
        private string[] variants = new[] { "a", "b", "c", "d" };
        private int minHours = 12;
        private int maxHours = 24;

        public double nextDefecationHour = -1;
        public bool canDefecate = false;

        public EntityBehaviorDefecate(Entity entity) : base(entity)
        {
            rand = new Random(entity.EntityId.GetHashCode());
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            JsonObject props = attributes["properties"];

            dungBase = new AssetLocation(props["dungBase"].AsString("naturalfertilizer:dung"));
            maxStages = props["stages"].AsInt(maxStages);
            variants = props["variants"].AsArray<string>(new string[] { "a", "b", "c" });
            minHours = props["minHours"].AsInt(minHours);
            maxHours = props["maxHours"].AsInt(maxHours);

            // entity.World.Logger.Debug("[Defecate] Initialized for {0}, dungBase={1}, stages={2}, variants={3}, minHours={4}, maxHours={5}",
            // entity.Code, dungBase, maxStages, string.Join(",", variants), minHours, maxHours
            // );
        }

        public override void Notify(string key, object data)
        {
            IWorldAccessor world = entity.World;

            if (!canDefecate)
            {
                if (key == "mealEaten")
                {
                    double currentHour = entity.World.Calendar.TotalHours;
                    nextDefecationHour = currentHour + minHours + rand.NextDouble() * maxHours;
                    canDefecate = true;

                    // string displayName = entity.GetName();
                    // world.Logger.VerboseDebug("{0} ate a meal at {1}, next defecation scheduled at {2}", displayName, FormatTime(currentHour), FormatTime(nextDefecationHour));
                    // world.Logger.Debug("[Defecate] {0} can now defecate", entity.GetName());
                }
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
            if (!canDefecate) return;

            IWorldAccessor world = entity.World;
            BlockPos pos = entity.ServerPos.AsBlockPos.DownCopy();

            Block dungBlockTemplate = world.GetBlock(new AssetLocation($"{dungBase}-1{variants[0]}"));
            Block targetBlock = world.BlockAccessor.GetBlock(pos);

            // If the block below is not replaceable enough, try current pos instead
            if (targetBlock.Replaceable < 6000 && !IsDungBlock(targetBlock))
            {
                pos = entity.ServerPos.AsBlockPos;
                targetBlock = world.BlockAccessor.GetBlock(pos);
                // Debug.WriteLine("[Defecate] {0} trying to defecate at own position {1}", entity.GetName(), pos);
                // Debug.WriteLine("[Defecate] {0} block at own position is {1} (replaceable {2})", entity.GetName(), targetBlock.Code?.ToShortString(), targetBlock.Replaceable);
            }

            // Abort if still not replaceable
            if (targetBlock.Replaceable < 6000 && !IsDungBlock(targetBlock))
            {
                entity.World.Logger.Debug("[Defecate] {0} could not defecate at {1}, block not replaceable ({2})",
                    entity.GetName(), pos, targetBlock.Code?.ToShortString());
                return;
            }

            if (IsBlockedBySpecialCase(targetBlock))
            {
                // entity.World.Logger.Debug("[Defecate] {0} skipped defecating at {1}, special block detected", entity.GetName(), pos);
                return;
            }

            // Stage/variant handling
            int newStage = 1;
            string chosenVariant = variants[rand.Next(variants.Length)];

            if (IsDungBlock(targetBlock))
            {
                string[] parts = targetBlock.Code.Path.Split('-');
                if (parts.Length > 1)
                {
                    string stagePart = new string(parts[1].TakeWhile(char.IsDigit).ToArray());
                    string variantPart = new string(parts[1].SkipWhile(char.IsDigit).ToArray());

                    if (int.TryParse(stagePart, out int parsed))
                    {
                        newStage = Math.Min(parsed + 1, maxStages);
                    }

                    if (!string.IsNullOrEmpty(variantPart))
                    {
                        chosenVariant = variantPart;
                    }
                }
            }

            string newBlockCode = $"{dungBase}-{newStage}{chosenVariant}";
            Block newBlock = world.GetBlock(new AssetLocation(newBlockCode));

            if (newBlock != null)
            {
                world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
                // entity.World.Logger.Debug("[Defecate] {0} defecated at {1}, placed block {2}", entity.GetName(), pos, newBlockCode);
            }

            canDefecate = false;
        }

        private bool IsBlockedBySpecialCase(Block block)
        {
            if (block?.Code == null) return false;

            string path = block.Code.Path;
            return Core.Config.ForbiddenDefecationBlocks
                .Any(forbidden => path.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsDungBlock(Block block)
        {
            return block?.Code != null && block.Code.Path.StartsWith(dungBase.Split(':')[1]);
        }

        private string FormatTime(double totalHours)
        {
            int totalMinutes = (int)(totalHours * 60);
            int hours = (totalMinutes / 60) % 24;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }
    }
}