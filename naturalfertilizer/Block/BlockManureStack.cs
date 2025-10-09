using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

#nullable disable

namespace naturalfertilizer
{

    public class BlockManureStack : Block
    {
        ItemStack[] groundStorablesQuadrants;
        ItemStack[] groundStorablesHalves;

        public static bool IsUsingContainedBlock;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            ItemStack[][] stacks = ObjectCacheUtil.GetOrCreate(api, "groundStorablesQuadrands", () =>
            {
                List<ItemStack> qstacks = new List<ItemStack>();
                List<ItemStack> hstacks = new List<ItemStack>();

                return new ItemStack[][] { qstacks.ToArray(), hstacks.ToArray() };
            });

            groundStorablesQuadrants = stacks[0];
            groundStorablesHalves = stacks[1];

            if (api.Side == EnumAppSide.Client)
            {
                ICoreClientAPI capi = api as ICoreClientAPI;
                capi.Event.MouseUp += Event_MouseUp;
            }
        }

        private void Event_MouseUp(MouseEvent e)
        {
            IsUsingContainedBlock = false;
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntity be = blockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                return beg.GetCollisionBoxes();
            }

            return base.GetCollisionBoxes(blockAccessor, pos);
        }

        public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntity be = blockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                return beg.GetCollisionBoxes();
            }

            return base.GetCollisionBoxes(blockAccessor, pos);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            var be = blockAccessor.GetBlockEntity<BlockEntityManureStack>(pos);
            if (be != null)
            {
                return be.GetSelectionBoxes();
            }

            return base.GetSelectionBoxes(blockAccessor, pos);
        }

        public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null)
        {
            var be = blockAccessor.GetBlockEntity<BlockEntityManureStack>(pos);
            if (be != null)
            {
                return be.CanAttachBlockAt(blockFace, attachmentArea);
            }
            return base.CanAttachBlockAt(blockAccessor, block, pos, blockFace, attachmentArea);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (api.Side == EnumAppSide.Client && IsUsingContainedBlock) return false;

            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                byPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return false;
            }

            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be is BlockEntityManureStack beg)
            {
                return beg.OnPlayerInteractStart(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be is BlockEntityManureStack beg)
            {
                return beg.OnPlayerInteractStep(secondsUsed, byPlayer, blockSel);
            }

            return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be is BlockEntityManureStack beg)
            {
                beg.OnPlayerInteractStop(secondsUsed, byPlayer, blockSel);
                return;
            }

            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        public override EnumBlockMaterial GetBlockMaterial(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
        {
            return base.GetBlockMaterial(blockAccessor, pos, stack);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                List<ItemStack> stacks = new List<ItemStack>();
                foreach (var slot in beg.Inventory)
                {
                    if (slot.Empty) continue;
                    stacks.Add(slot.Itemstack);
                }

                return stacks.ToArray();
            }

            return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public float FillLevel(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntity be = blockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                return (int)Math.Ceiling((float)beg.TotalStackSize / beg.Capacity);
            }

            return 1;
        }

        public bool CreateStorage(IWorldAccessor world, BlockSelection blockSel, IPlayer player)
        {
            if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                player.InventoryManager.ActiveHotbarSlot.MarkDirty();
                return false;
            }

            BlockPos pos = blockSel.Position;
            if (blockSel.Face != null)
            {
                pos = pos.AddCopy(blockSel.Face);
            }
            BlockPos posBelow = pos.DownCopy();
            Block belowBlock = world.BlockAccessor.GetBlock(posBelow);
            if (!belowBlock.CanAttachBlockAt(world.BlockAccessor, this, posBelow, BlockFacing.UP)) return false;

            var storageProps = player.InventoryManager.ActiveHotbarSlot.Itemstack.Collectible.GetBehavior<CollectibleBehaviorManureStack>()?.ManureStackProps;
            if (storageProps != null && storageProps.CtrlKey && !player.Entity.Controls.CtrlKey)
            {
                return false;
            }

            BlockPos targetPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
            double dx = player.Entity.Pos.X - (targetPos.X + blockSel.HitPosition.X);
            double dz = (float)player.Entity.Pos.Z - (targetPos.Z + blockSel.HitPosition.Z);
            float angleHor = (float)Math.Atan2(dx, dz);

            float deg90 = GameMath.PIHALF;
            float roundRad = ((int)Math.Round(angleHor / deg90)) * deg90;
            BlockFacing attachFace = null;

            world.BlockAccessor.SetBlock(BlockId, pos);

            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                beg.MeshAngle = roundRad;
                beg.AttachFace = attachFace;
                beg.clientsideFirstPlacement = (world.Side == EnumAppSide.Client);
                beg.OnPlayerInteractStart(player, blockSel);
            }

            if (CollisionTester.AabbIntersect(
                GetCollisionBoxes(world.BlockAccessor, pos)[0],
                pos.X, pos.Y, pos.Z,
                player.Entity.SelectionBox,
                player.Entity.SidedPos.XYZ
            ))
            {
                player.Entity.SidedPos.Y += GetCollisionBoxes(world.BlockAccessor, pos)[0].Y2;
            }

            (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

            return true;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            BlockEntityManureStack beg = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityManureStack;

        }

        public override int GetColorWithoutTint(ICoreClientAPI capi, BlockPos pos)
        {
            BlockEntity be = capi.World.BlockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                ItemSlot slot = beg.Inventory.ToArray().Shuffle(capi.World.Rand).FirstOrDefault(s => !s.Empty);
                if (slot != null)
                {
                    return slot.Itemstack.Collectible.GetRandomColor(capi, slot.Itemstack);
                }
            }

            return base.GetColorWithoutTint(capi, pos);
        }

        public override int GetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex = -1)
        {
            BlockEntity be = capi.World.BlockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                ItemSlot slot = beg.Inventory.ToArray().Shuffle(capi.World.Rand).FirstOrDefault(s => !s.Empty);
                if (slot != null)
                {
                    return slot.Itemstack.Collectible.GetRandomColor(capi, slot.Itemstack);
                }
                else
                {
                    return 0;
                }
            }

            return base.GetRandomColor(capi, pos, facing, rndIndex);
        }

        public override int GetRandomColor(ICoreClientAPI capi, ItemStack stack)
        {
            return base.GetRandomColor(capi, stack);
        }

        public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
        {
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityManureStack beg)
            {
                return beg.GetBlockName();
            }
            else return OnPickBlock(world, pos)?.GetName();
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            var beg = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityManureStack;
            if (beg != null)
            {
                return beg.Inventory.FirstNonEmptySlot?.Itemstack.Clone();
            }

            return null;
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            var beg = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityManureStack;
            if (beg?.ManureStackProps != null)
            {
                WorldInteraction[] liquidInteractions = (beg.Inventory.FirstOrDefault(slot => !slot.Empty && slot.Itemstack.Collectible is BlockLiquidContainerBase)?
                                                                      .Itemstack.Collectible as BlockLiquidContainerBase)?.interactions ?? [];

                int bulkquantity = beg.ManureStackProps.BulkTransferQuantity;

                if (beg.ManureStackProps.Layout == EnumManureStackLayout.Stacking && !beg.Inventory.Empty)
                {

                    var collObj = beg.Inventory[0].Itemstack?.Collectible;
                    if (collObj == null) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer).Append(liquidInteractions);

                    return new WorldInteraction[]
                    {
                        new WorldInteraction()
                        {
                            ActionLangCode = "blockhelp-groundstorage-addone",
                            MouseButton = EnumMouseButton.Right,
                            HotKeyCode = "shift",
                            Itemstacks = [new (collObj, 1)]
                        },
                        new WorldInteraction()
                        {
                            ActionLangCode = "blockhelp-groundstorage-removeone",
                            MouseButton = EnumMouseButton.Right,
                            HotKeyCode = null
                        },

                        new WorldInteraction()
                        {
                            ActionLangCode = "blockhelp-groundstorage-addbulk",
                            MouseButton = EnumMouseButton.Right,
                            HotKeyCodes = ["ctrl", "shift"],
                            Itemstacks = [new (collObj, bulkquantity)]
                        },
                        new WorldInteraction()
                        {
                            ActionLangCode = "blockhelp-groundstorage-removebulk",
                            HotKeyCode = "ctrl",
                            MouseButton = EnumMouseButton.Right
                        }

                    }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)).Append(liquidInteractions);
                }
            }

            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            var groundStorageMeshRefs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, "manureStackUMC");
            if (groundStorageMeshRefs != null)
            {
                foreach (var meshRef in groundStorageMeshRefs.Values)
                {
                    if (meshRef?.Disposed == false)
                        meshRef.Dispose();
                }
                ObjectCacheUtil.Delete(api, "manureStackUMC");
            }
        }


        public override bool ShouldReceiveClientParticleTicks(IWorldAccessor world, IPlayer player, BlockPos pos, out bool isWindAffected)
        {
            return base.ShouldReceiveClientParticleTicks(world, player, pos, out isWindAffected) ||
                   (world.BlockAccessor.GetBlockEntity<BlockEntityManureStack>(pos)?.Inventory.Any(slot => slot.Itemstack?.Collectible?.GetCollectibleInterface<IGroundStoredParticleEmitter>() != null) ?? false);
        }

        public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
        {
            if (manager.BlockAccess.GetBlockEntity(pos) is BlockEntityManureStack begs && begs.ManureStackProps != null && !begs.Inventory.Empty)
            {
                Vec3f[] offs = new Vec3f[begs.DisplayedItems];
                begs.GetLayoutOffset(offs);

                foreach (ItemSlot slot in begs.Inventory)
                {
                    if (slot?.Itemstack?.Collectible.GetCollectibleInterface<IGroundStoredParticleEmitter>() is IGroundStoredParticleEmitter gsParticleEmitter)
                    {
                        int slotId = begs.Inventory.GetSlotId(slot);
                        if (slotId < 0 || slotId >= offs.Length)
                        {
                            continue;
                        }
                        Vec3f offset = new Matrixf().RotateY(begs.MeshAngle).TransformVector(new Vec4f(offs[slotId].X, offs[slotId].Y, offs[slotId].Z, 1)).XYZ;

                        if (gsParticleEmitter.ShouldSpawnGSParticles(begs.Api.World, slot.Itemstack)) gsParticleEmitter.DoSpawnGSParticles(manager, pos, offset);
                    }
                }
            }

            base.OnAsyncClientParticleTick(manager, pos, windAffectednessAtPos, secondsTicking);
        }

    }
}
