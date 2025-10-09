using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

#nullable enable

namespace naturalfertilizer
{
    public class BuzzingFlies : ModSystem
    {
        private ILogger Logger => Mod.Logger;
        private ICoreClientAPI? Capi { get; set; }

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            Capi = api;
            _entityParticleSystem = api.ModLoader.GetModSystem<EntityParticleSystem>();
            if (_entityParticleSystem == null)
            {
                Logger.Error("[NaturalFertilizer] Couldn't find entity particle system.");
                return;
            }
            _entityParticleSystem.OnSimTick += OnSimTick;
        }

        //private List<string[]> _blockCodeSelectors = new();
        private List<Regex> _compiledBlockSelectors = new();
        private List<Regex> _compiledItemSelectors = new();

        public override void AssetsFinalize(ICoreAPI api)
        {
            // compile block selectors (config may contain wildcard patterns or regexes)
            _compiledBlockSelectors = Configs.CConfig.FlyBlockCodeRegexSelectors
                .Select(p => CompileSelector(p))
                .ToList();

            // build item-based list using compiled item selectors
            _compiledItemSelectors = Configs.CConfig.FlyItemCodeRegexSelectors
                .Select(p => CompileSelector(p))
                .ToList();

            // Build item list only for matching item collectibles
            foreach (var collObj in api.World.Collectibles.Where(c => c?.Code != null))
            {
                var codeString = collObj.Code.ToString();
                if (_compiledItemSelectors.Any(re => re.IsMatch(codeString)))
                {
                    _manureStacks.Add(new ItemStack(collObj));
                }
            }
        }

        private const string FlyEntityParticleKey = "matinggnats";
        private static readonly Random Random = new();
        private EntityParticleSystem? _entityParticleSystem;
        private float _accumulator;
        private void OnSimTick(float dt)
        {
            _accumulator += dt;
            if (Capi == null) return;
            if (_accumulator <= 1.0) return;
            _accumulator = 0.0f;
            var entityPos = Capi.World.Player.Entity.Pos;
            if (entityPos.Dimension != 0) return;
            var climateAt = Capi.World.BlockAccessor.GetClimateAt(entityPos.AsBlockPos);
            SpawnMatingGnatsSwarm(Capi, entityPos, climateAt);
        }
        private void SpawnMatingGnatsSwarm(ICoreClientAPI capi, EntityPos pos, ClimateCondition climate)
        {
            var entityParticleSystem = capi.ModLoader.GetModSystem<EntityParticleSystem>();
            if (climate.Temperature < Configs.CConfig.FlySpawnMinTemperature || climate.Temperature > Configs.CConfig.FlySpawnMaxTemperature)
                return;
            // Config.MaxExtraFlyCloudCount more than vanilla supported count (200), to ensure some always get spawned
            const int vanillaFlyCloudCap = 200;
            var flyCloudCap = vanillaFlyCloudCap + Configs.CConfig.MaxExtraFlyCloudCount;
            if (entityParticleSystem?.Count[FlyEntityParticleKey] > flyCloudCap) return;
            const int loopCountCap = 100;
            var found = 0;
            for (var i = 0; i < loopCountCap && found < Configs.CConfig.MaxFlyCloudSpawnPerSimTick; ++i)
            {
                var xPos = pos.X + (Random.NextDouble() - 0.5) * 24;
                var yPos = pos.Y + (Random.NextDouble() - 0.5) * 24;
                var zPos = pos.Z + (Random.NextDouble() - 0.5) * 24;
                if (pos.HorDistanceTo(xPos, zPos) < 2.0) continue;
                var blockPos = new BlockPos((int)xPos, (int)yPos, (int)zPos);
                if (!HasManureNearby(capi, blockPos)) continue;
                var cohesion = (float) GameMath.Max(Random.NextDouble() * 1.1, 0.20) / 2.5f;
                var spawnCount = Configs.CConfig.MinFlyCountPerCloud + Random.Next(21);
                for (var j = 0; j < spawnCount; ++j)
                {
                    entityParticleSystem?.SpawnParticle(new EntityParticleMatingGnats(capi, cohesion,
                        xPos + 0.5, yPos + Random.NextDouble() + 0.5, zPos + 0.5));
                }
                ++found;
            }
        }

        private readonly List<ItemStack> _manureStacks = [];
        private bool HasManureNearby(ICoreClientAPI capi, BlockPos blockPos)
        {
            var block = capi.World.BlockAccessor.GetBlock(blockPos);

            if (block?.Code != null)
            {
                string codeString = block.Code.ToString();
                if (_compiledBlockSelectors.Any(re => re.IsMatch(codeString)))
                {
                    return true;
                }
            }

            var be = capi.World.BlockAccessor.GetBlockEntity(blockPos) as IBlockEntityContainer;
            if (be != null) return HasManureNearby(be);

            return false;
        }
        private bool HasManureNearby(IBlockEntityContainer blockEntityContainer)
        {
            return blockEntityContainer.Inventory.Any(HasManureNearby);
        }
        private bool HasManureNearby(ItemSlot itemSlot)
        {
            if (itemSlot.Empty || itemSlot.Itemstack == null) return false;
            return _manureStacks.Any(stack => stack.Collectible.Satisfies(stack, itemSlot.Itemstack));
        }

        private Regex CompileSelector(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));

            // If it looks like a wildcard pattern, convert: * -> .*, ? -> .
            if (pattern.IndexOfAny(new char[] { '*', '?' }) >= 0)
            {
                // Escape the whole pattern then unescape wildcard chars
                string escaped = Regex.Escape(pattern);
                string regex = "^" + escaped.Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                return new Regex(regex, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }

            // Otherwise treat as a regex. Anchor it by default if it doesn't already have ^ or $
            // (optional) you can skip anchoring if you want partial matches.
            string anchored = pattern;
            if (!pattern.StartsWith("^") && !pattern.EndsWith("$"))
            {
                anchored = "^" + pattern + "$";
            }

            Core.DebugUtil.Log(Capi!, "Compiled selector pattern {1} to regex {2}", pattern, anchored);

            return new Regex(anchored, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }
        public override void Dispose()
        {
            if (_entityParticleSystem != null)
                _entityParticleSystem.OnSimTick -= OnSimTick;
            base.Dispose();
        }
    }
}