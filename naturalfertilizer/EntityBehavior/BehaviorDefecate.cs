using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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

        // Attribute keys for persistent storage
        private const string CanDefecateKey = "naturalfertilizer:canDefecate";
        private const string NextDefecationHourKey = "naturalfertilizer:nextDefecationHour";

        public EntityBehaviorDefecate(Entity entity) : base(entity)
        {
            rand = new Random(entity.EntityId.GetHashCode());
        }

        // --- Persistent properties ---
        private bool CanDefecate
        {
            get => entity.Attributes.GetBool(CanDefecateKey, false);
            set => entity.Attributes.SetBool(CanDefecateKey, value);
        }

        private double NextDefecationHour
        {
            get => entity.Attributes.GetDouble(NextDefecationHourKey, -1);
            set => entity.Attributes.SetDouble(NextDefecationHourKey, value);
        }
        // -----------------------------

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            JsonObject props = attributes["properties"];

            dungBase = new AssetLocation(props["dungBase"].AsString("naturalfertilizer:dung"));
            maxStages = props["stages"].AsInt(maxStages);
            variants = props["variants"].AsArray<string>(new string[] { "a", "b", "c" });
            minHours = props["minHours"].AsInt(minHours);
            maxHours = props["maxHours"].AsInt(maxHours);

            Core.DebugUtil.Log(entity.Api, "Initialized for {0}, dungBase={1}, stages={2}, variants={3}, minHours={4}, maxHours={5}",
                entity.Code, dungBase, maxStages, string.Join(",", variants), minHours, maxHours);
        }

        public override void Notify(string key, object data)
        {
            IWorldAccessor world = entity.World;

            if (!CanDefecate && key == "mealEaten")
            {
                double currentHour = world.Calendar.TotalHours;
                NextDefecationHour = minHours + currentHour + rand.NextDouble() * (maxHours - minHours);
                CanDefecate = true;

                string displayName = entity.GetName();
                Core.DebugUtil.Log(world.Api, "{0} ate a meal at {1}, next defecation scheduled at {2}",
                    displayName, FormatTime(currentHour), FormatTime(NextDefecationHour));
                Core.DebugUtil.Log(world.Api, "{0} can now defecate", displayName);
            }
        }

        public override void OnGameTick(float deltaTime)
        {
            double now = entity.World.Calendar.TotalHours;

            if (Configs.SConfig.EnableGenelibCompat)
            {
                TryScheduleDefecation_Genelib();

                if (CanDefecate && NextDefecationHour > 0 && now >= NextDefecationHour)
                {
                    Defecate();
                }
            }
            else
            {
                if (CanDefecate && NextDefecationHour > 0 && now >= NextDefecationHour)
                {
                    Defecate();
                }
            }
        }

        #region Defecation Logic
        private void Defecate()
        {
            if (!CanDefecate) return;

            IWorldAccessor world = entity.World;
            BlockPos pos = entity.ServerPos.AsBlockPos.DownCopy();

            Block targetBlock = world.BlockAccessor.GetBlock(pos);

            // If the block below is not replaceable enough, try current pos instead
            if (targetBlock.Replaceable < 6000 && !IsDungBlock(targetBlock))
            {
                pos = entity.ServerPos.AsBlockPos;
                targetBlock = world.BlockAccessor.GetBlock(pos);
                Core.DebugUtil.Log(entity.Api, "{0} trying to defecate at own position {1}", entity.GetName(), pos);
                Core.DebugUtil.Log(entity.Api, "{0} block at own position is {1} (replaceable {2})",
                    entity.GetName(), targetBlock.Code?.ToShortString(), targetBlock.Replaceable);
            }

            // Abort if still not replaceable
            if (targetBlock.Replaceable < 6000 && !IsDungBlock(targetBlock))
            {
                Core.DebugUtil.Log(entity.Api, "{0} could not defecate at {1}, block not replaceable ({2})",
                    entity.GetName(), pos, targetBlock.Code?.ToShortString());

                // Retry after 1 in-game hour
                NextDefecationHour = entity.World.Calendar.TotalHours + 1;

                Core.DebugUtil.Log(entity.Api, "{0} will retry defecation at {1}. CanDefecate = {2}",
                    entity.GetName(), FormatTime(NextDefecationHour), CanDefecate);
                return;
            }

            if (IsBlockedBySpecialCase(targetBlock) && !IsDungBlock(targetBlock))
            {
                Core.DebugUtil.Log(entity.Api, "{0} skipped defecating at {1}, special block detected: {2}",
                    entity.GetName(), pos, targetBlock.Code?.ToShortString());

                // Retry after 1 in-game hour
                NextDefecationHour = entity.World.Calendar.TotalHours + 1;

                Core.DebugUtil.Log(entity.Api, "{0} will retry defecation at {1}",
                    entity.GetName(), FormatTime(NextDefecationHour));
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
                Core.DebugUtil.Log(entity.Api, "{0} defecated at {1}, placed block {2}",
                    entity.GetName(), pos, newBlockCode);
            }

            CanDefecate = false;
            NextDefecationHour = -1;
        }

        private bool IsBlockedBySpecialCase(Block block)
        {
            if (block?.Code == null) return false;

            string path = block.Code.Path;
            return Configs.SConfig.ForbiddenDefecationBlocks
                .Any(forbidden => path.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsDungBlock(Block block)
        {
            if (block?.Code == null) return false;
            string path = block.Code.Path.ToLowerInvariant();

            return path.StartsWith(dungBase.Split(':')[1]) ||
                   path.Contains("poop") ||
                   path.Contains("dung");
        }
        #endregion

        #region Compatibility Modules
        private void TryScheduleDefecation_Genelib()
        {
            var hungerTree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (hungerTree == null) return; // Compatibility: Genelib not installed or not initialized

            // Try to get satiety, or saturation fallback
            double satiety = hungerTree?.TryGetDouble("genelib.satiety") ??
                             hungerTree?.GetFloat("saturation") ?? 0;

            double threshold = Configs.SConfig.GenelibSatietyThreshold;
            double minDelay = Configs.SConfig.GenelibMinDefecationDelayHours;
            double maxDelay = Configs.SConfig.GenelibMaxDefecationDelayHours;
            double now = entity.World.Calendar.TotalHours;

            if (satiety < threshold)
            {
                // If satiety drops below threshold, cancel pending defecation (optional)
                if (NextDefecationHour > 0 && now < NextDefecationHour)
                {
                    Core.DebugUtil.Verbose(entity.Api,
                        "{0} cancelling Genelib defecation (satiety dropped below {1:0.00})",
                        entity.GetName(), threshold);
                    NextDefecationHour = -1;
                    CanDefecate = false;
                }
                return;
            }

            if (NextDefecationHour <= 0)
            {
                double hoursUntilNext = minDelay + rand.NextDouble() * (maxDelay - minDelay);
                NextDefecationHour = now + hoursUntilNext;
                CanDefecate = true;

                Core.DebugUtil.Log(entity.Api,
                    "{0} scheduled Genelib defecation in {1:0.0} hours (satiety {2:0.00})",
                    entity.GetName(), hoursUntilNext, satiety);
            }
        }
        #endregion

        #region Helper Methods
        public override void GetInfoText(StringBuilder infotext)
        {
            if (!entity.Alive)
            {
                return;
            }
            base.GetInfoText(infotext);

            infotext.AppendLine($"• CanDefecate: {CanDefecate}");

            if (NextDefecationHour > 0)
            {
                double hoursLeft = NextDefecationHour - entity.World.Calendar.TotalHours;
                infotext.AppendLine($"• Next in: {Math.Max(0, hoursLeft):0.0}h");
            }
            else
            {
                infotext.AppendLine("• Next defecation not scheduled");
            }
        }

        private string FormatTime(double totalHours)
        {
            int totalMinutes = (int)(totalHours * 60);
            int hours = (totalMinutes / 60) % 24;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }
        #endregion
    }
}
