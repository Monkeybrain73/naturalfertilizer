using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

#nullable disable

namespace naturalfertilizer
{
    public class BlockEntityManureStack : BlockEntityDisplay, IBlockEntityContainer, IRotatable
    {
        /*
        static SimpleParticleProperties smokeParticles;

        static BlockEntityManureStack()
        {
            smokeParticles = new SimpleParticleProperties(
                1, 1,
                ColorUtil.ToRgba(150, 40, 40, 40),
                new Vec3d(),
                new Vec3d(1, 0, 1),
                new Vec3f(-1 / 32f, 0.1f, -1 / 32f),
                new Vec3f(1 / 32f, 0.1f, 1 / 32f),
                2f,
                -0.025f / 4,
                0.2f,
                1f,
                EnumParticleModel.Quad
            );

            smokeParticles.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.25f);
            smokeParticles.SelfPropelled = true;
            smokeParticles.AddPos.Set(1, 0, 1);
        }
        */

        public object inventoryLock = new object(); // Because OnTesselation runs in another thread

        protected InventoryGeneric inventory;

        public ManureStackProperties ManureStackProps { get; protected set; }
        public bool forceManureStackProps = false;
        protected EnumManureStackLayout? overrideLayout;

        public int TransferQuantity => ManureStackProps?.TransferQuantity ?? 1;
        public int BulkTransferQuantity => ManureStackProps?.Layout == EnumManureStackLayout.Stacking ? ManureStackProps.BulkTransferQuantity : 1;

        protected virtual int invSlotCount => 4;
        protected Cuboidf[] colBoxes;
        protected Cuboidf[] selBoxes;

        ItemSlot isUsingSlot;

        public bool clientsideFirstPlacement = false;

        // private GroundStorageRenderer renderer;
        public bool UseRenderer;
        public bool NeedsRetesselation;

        public MultiTextureMeshRef[] MeshRefs = new MultiTextureMeshRef[4];

        public ModelTransform[] ModelTransformsRenderer = new ModelTransform[4];

        private Dictionary<string, MultiTextureMeshRef> UploadedMeshCache =>
            ObjectCacheUtil.GetOrCreate(Api, "manureStackUMC", () => new Dictionary<string, MultiTextureMeshRef>());

        private BlockFacing[] facings = (BlockFacing[])BlockFacing.ALLFACES.Clone();
        public int Layers => inventory[0].StackSize == 1 || ManureStackProps == null ? 1 : (int)(inventory[0].StackSize * ManureStackProps.ModelItemsToStackSizeRatio);


        public override int DisplayedItems
        {
            get
            {
                if (ManureStackProps == null) return 0;
                switch (ManureStackProps.Layout)
                {
                    case EnumManureStackLayout.Stacking: return 1;
                }

                return 0;
            }
        }

        public int TotalStackSize
        {
            get
            {
                int sum = 0;
                foreach (var slot in inventory) sum += slot.StackSize;
                return sum;
            }
        }

        public int Capacity
        {
            get
            {
                if (ManureStackProps == null) return 1;
                switch (ManureStackProps.Layout)
                {
                    case EnumManureStackLayout.Stacking: return ManureStackProps.StackingCapacity;
                    default: return 1;
                }
            }
        }


        public override InventoryBase Inventory
        {
            get { return inventory; }
        }

        public override string InventoryClassName
        {
            get { return "manurestack"; }
        }

        public override string AttributeTransformCode => "manureStackTransform";

        public float MeshAngle { get; set; }
        public BlockFacing AttachFace { get; set; }

        public override TextureAtlasPosition this[string textureCode]
        {
            get
            {
                // Prio 1: Get from list of explicility defined textures
                if (ManureStackProps?.Layout == EnumManureStackLayout.Stacking && ManureStackProps.StackingTextures != null)
                {
                    if (ManureStackProps.StackingTextures.TryGetValue(textureCode, out var texturePath))
                    {
                        return getOrCreateTexPos(texturePath);
                    }
                }

                // Prio 2: Try other texture sources
                return base[textureCode];
            }
        }

        public bool CanAttachBlockAt(BlockFacing blockFace, Cuboidi attachmentArea)
        {
            if (ManureStackProps == null) return false;
            return blockFace == BlockFacing.UP && ManureStackProps.Layout == EnumManureStackLayout.Stacking && inventory[0].StackSize == Capacity && ManureStackProps.UpSolid;
        }

        public BlockEntityManureStack() : base()
        {
            inventory = new InventoryGeneric(invSlotCount, null, null, (int slotId, InventoryGeneric inv) => new ItemSlot(inv));
            foreach (var slot in inventory)
            {
                slot.StorageType |= EnumItemStorageFlags.Backpack;
            }

            inventory.OnGetAutoPushIntoSlot = GetAutoPushIntoSlot;
            inventory.OnGetAutoPullFromSlot = GetAutoPullFromSlot;

            colBoxes = new Cuboidf[] { new Cuboidf(0, 0, 0, 1, 0.25f, 1) };
            selBoxes = new Cuboidf[] { new Cuboidf(0, 0, 0, 1, 0.25f, 1) };
        }

        private ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
        {
            return null;
        }

        private ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            return null;
        }

        public void ForceManureStackProps(ManureStackProperties storageProps)
        {
            ManureStackProps = storageProps;
            forceManureStackProps = true;
        }


        public override void Initialize(ICoreAPI api)
        {
            capi = api as ICoreClientAPI;
            base.Initialize(api);

            DetermineStorageProperties(null);

            if (capi != null)
            {
                updateMeshes();
            }
        }

        protected ItemStack GetShatteredStack(ItemStack contents)
        {
            var shatteredStack = contents.Collectible.Attributes?["shatteredStack"].AsObject<JsonItemStack>();
            if (shatteredStack != null)
            {
                shatteredStack.Resolve(Api.World, "shatteredStack for" + contents.Collectible.Code);
                if (shatteredStack.ResolvedItemstack != null)
                {
                    var stack = shatteredStack.ResolvedItemstack;
                    return stack;
                }
            }
            shatteredStack = Block.Attributes?["shatteredStack"].AsObject<JsonItemStack>();
            if (shatteredStack != null)
            {
                shatteredStack.Resolve(Api.World, "shatteredStack for" + contents.Collectible.Code);
                if (shatteredStack.ResolvedItemstack != null)
                {
                    var stack = shatteredStack.ResolvedItemstack;
                    return stack;
                }
            }
            return null;
        }

        public Cuboidf[] GetSelectionBoxes()
        {
            return selBoxes;
        }

        public Cuboidf[] GetCollisionBoxes()
        {
            return colBoxes;
        }

        public virtual bool OnPlayerInteractStart(IPlayer player, BlockSelection bs)
        {
            ItemSlot hotbarSlot = player.InventoryManager.ActiveHotbarSlot;

            if (!hotbarSlot.Empty && !hotbarSlot.Itemstack.Collectible.HasBehavior<CollectibleBehaviorManureStack>()) return false;

            if (!BlockBehaviorReinforcable.AllowRightClickPickup(Api.World, Pos, player)) return false;

            DetermineStorageProperties(hotbarSlot);

            bool ok = false;

            if (ManureStackProps != null)
            {
                if (!hotbarSlot.Empty && ManureStackProps.CtrlKey && !player.Entity.Controls.CtrlKey) return false;

                // fix RAD rotation being CCW - since n=0, e=-PiHalf, s=Pi, w=PiHalf so we swap east and west by inverting sign
                // changed since > 1.18.1 since east west on WE rotation was broken, to allow upgrading/downgrading without issues we invert the sign for all* usages instead of saving new value
                var hitPos = rotatedOffset(bs.HitPosition.ToVec3f(), -MeshAngle);


                switch (ManureStackProps.Layout)
                {
                    case EnumManureStackLayout.Stacking:
                        ok = putOrGetItemStacking(player, bs);
                        break;
                }
            }

            if (ok)
            {
                MarkDirty();    // Don't re-draw on client yet, that will be handled in FromTreeAttributes after we receive an updating packet from the server  (updating meshes here would have the wrong inventory contents, and also create a potential race condition)
            }

            if (inventory.Empty && !clientsideFirstPlacement)
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
            }

            return ok;
        }

        public bool OnPlayerInteractStep(float secondsUsed, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (isUsingSlot?.Itemstack?.Collectible is IContainedInteractable collIci)
            {
                return collIci.OnContainedInteractStep(secondsUsed, this, isUsingSlot, byPlayer, blockSel);
            }

            isUsingSlot = null;
            return false;
        }

        public void OnPlayerInteractStop(float secondsUsed, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (isUsingSlot?.Itemstack.Collectible is IContainedInteractable collIci)
            {
                collIci.OnContainedInteractStop(secondsUsed, this, isUsingSlot, byPlayer, blockSel);
            }

            isUsingSlot = null;
        }

        public ItemSlot GetSlotAt(BlockSelection bs)
        {
            if (ManureStackProps == null) return null;
            var hitPos = rotatedOffset(bs.HitPosition.ToVec3f(), -MeshAngle);

            switch (ManureStackProps.Layout)
            {
                case EnumManureStackLayout.Stacking:
                    return inventory[0];
            }

            return null;
        }


        public virtual void DetermineStorageProperties(ItemSlot sourceSlot)
        {
            ItemStack sourceStack = inventory.FirstNonEmptySlot?.Itemstack ?? sourceSlot?.Itemstack;

            var ManureStackProps = this.ManureStackProps;
            if (!forceManureStackProps)
            {
                if (ManureStackProps == null)
                {
                    if (sourceStack == null) return;

                    ManureStackProps = this.ManureStackProps = sourceStack.Collectible?.GetBehavior<CollectibleBehaviorManureStack>()?.ManureStackProps;
                }
            }

            if (ManureStackProps == null) return;  // Seems necessary to avoid crash with certain items placed in game version 1.15-pre.1?

            if (ManureStackProps.CollisionBox != null)
            {
                colBoxes[0] = selBoxes[0] = ManureStackProps.CollisionBox.Clone();
            }
            else
            {
                if (sourceStack?.Block != null)
                {
                    colBoxes[0] = selBoxes[0] = sourceStack.Block.CollisionBoxes[0].Clone();
                }
            }

            if (ManureStackProps.SelectionBox != null)
            {
                selBoxes[0] = ManureStackProps.SelectionBox.Clone();
            }

            if (ManureStackProps.CbScaleYByLayer != 0)
            {
                colBoxes[0] = colBoxes[0].Clone();
                colBoxes[0].Y2 *= ((int)Math.Ceiling(ManureStackProps.CbScaleYByLayer * inventory[0].StackSize) * 8) / 8;

                selBoxes[0] = colBoxes[0];
            }

            FixBrokenStorageLayout();

            if (overrideLayout != null)
            {
                this.ManureStackProps = ManureStackProps.Clone();
                this.ManureStackProps.Layout = (EnumManureStackLayout)overrideLayout;
            }
        }

        protected virtual void FixBrokenStorageLayout()
        {
            if (ManureStackProps == null) return;

            // Stacking and WallHalves are incompatible with other types so we want to make sure they don't mix
            if (ManureStackProps.Layout is EnumManureStackLayout.Stacking || overrideLayout is EnumManureStackLayout.Stacking)
            {
                overrideLayout = null;
            }

            var currentLayout = overrideLayout ?? ManureStackProps.Layout;
            int totalSlots = UsableSlots(currentLayout);
            if (totalSlots <= 0) return; // This should never happen, but just in case
            if (totalSlots >= 4) return; // Everything should be visible and interactable in this case

            ItemSlot[] fullSlots = [.. inventory.Where(slot => !slot.Empty)];
            if (fullSlots.Length <= 0) return; // Again, should never happen, but better safe than sorry

            // Everything should be visible and interactable in any of these cases
            if (fullSlots.Length == 1 && totalSlots == 1 && !inventory[0].Empty) return;
            if (fullSlots.Length == 2 && totalSlots == 2 && !inventory[0].Empty && !inventory[1].Empty) return;

            // Flip the items into the first slot if possible
            if (fullSlots.Length == 1)
            {
                if (totalSlots == 2 && (!inventory[0].Empty || !inventory[1].Empty)) return; // Item is already visible

                inventory[0].TryFlipWith(fullSlots[0]);
                if (!inventory[0].Empty) return;
            }

            // Try to collect all the items into the first slot if layout is stacking
            if (currentLayout is EnumManureStackLayout.Stacking &&
                inventory.All(slot => slot.Empty || slot.Itemstack.Equals(Api.World, fullSlots[0].Itemstack, GlobalConstants.IgnoredStackAttributes)))
            {
                for (int i = 0; i < fullSlots.Length; i++) fullSlots[i].TryPutInto(Api.World, inventory[0]);

                fullSlots = [.. inventory.Where(slot => !slot.Empty)];

                if (fullSlots.Length == 1) return;
            }

            // Try to move everything into the first two slots if they are all the layout displays
            if (totalSlots == 2 && fullSlots.Length == 2)
            {
                fullSlots[0].TryPutInto(Api.World, inventory[0]); // Shift the first item to the first slot

                fullSlots[1].TryPutInto(Api.World, inventory[1]); // Shift the second item to the second slot

                if (!inventory[0].Empty && !inventory[1].Empty) return; // Everything is moved into the first two visible slots
            }

        }

        public int UsableSlots(EnumManureStackLayout layout)
        {
            switch (layout)
            {
                case EnumManureStackLayout.Stacking: return 1;
                default: return 0;
            }
        }

        protected bool putOrGetItemStacking(IPlayer byPlayer, BlockSelection bs)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                (byPlayer as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
                return true;
            }

            ItemSlot hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            bool sneaking = byPlayer.Entity.Controls.ShiftKey;

            bool equalStack = inventory[0].Empty || !sneaking || (hotbarSlot.Itemstack != null && hotbarSlot.Itemstack.Equals(Api.World, inventory[0].Itemstack, GlobalConstants.IgnoredStackAttributes));

            BlockPos abovePos = Pos.UpCopy();
            var beg = Block.GetBlockEntity<BlockEntityManureStack>(abovePos);
            if (TotalStackSize >= Capacity && ((beg != null && equalStack) ||
                (hotbarSlot.Empty && beg?.inventory[0].Itemstack?.Equals(Api.World, inventory[0].Itemstack, GlobalConstants.IgnoredStackAttributes) == true)))
            {
                return beg.OnPlayerInteractStart(byPlayer, bs);
            }

            if (sneaking && hotbarSlot.Empty) return false;
            if (ManureStackProps == null) return false;

            if (sneaking && TotalStackSize >= Capacity)
            {
                Block pileblock = Api.World.BlockAccessor.GetBlock(Pos);
                Block aboveblock = Api.World.BlockAccessor.GetBlock(abovePos);

                if (abovePos.Y >= Api.World.BlockAccessor.MapSizeY) return false;

                if (aboveblock.IsReplacableBy(pileblock))
                {
                    if (!equalStack && bs.Face != BlockFacing.UP) return false;

                    int stackHeight = 1;
                    if (ManureStackProps.MaxStackingHeight > 0)
                    {
                        BlockPos tempPos = Pos.Copy();
                        while (Block.GetBlockEntity<BlockEntityManureStack>(tempPos.Down())?.inventory[0].Itemstack?.Equals(Api.World, inventory[0].Itemstack, GlobalConstants.IgnoredStackAttributes) == true)
                        {
                            stackHeight++;
                        }
                    }

                    if (ManureStackProps.MaxStackingHeight < 0 || stackHeight < ManureStackProps.MaxStackingHeight || !equalStack)
                    {
                        BlockManureStack bgs = pileblock as BlockManureStack;
                        var bsc = bs.Clone();
                        bsc.Position = Pos;
                        bsc.Face = BlockFacing.UP;
                        return bgs.CreateStorage(Api.World, bsc, byPlayer);
                    }
                }

                return false;
            }

            if (sneaking && !equalStack)
            {
                return false;
            }

            lock (inventoryLock)
            {
                if (sneaking)
                {
                    return TryPutItem(byPlayer);
                }
                else
                {
                    return TryTakeItem(byPlayer);
                }
            }
        }

        public virtual bool TryPutItem(IPlayer player)
        {
            lock (inventoryLock)
            {
                if (TotalStackSize >= Capacity) return false;

                ItemSlot hotbarSlot = player.InventoryManager.ActiveHotbarSlot;

                if (hotbarSlot.Itemstack == null) return false;

                ItemSlot invSlot = inventory[0];

                if (invSlot.Empty)
                {
                    bool putBulk = player.Entity.Controls.CtrlKey;

                    if (hotbarSlot.TryPutInto(Api.World, invSlot, putBulk ? BulkTransferQuantity : TransferQuantity) > 0)
                    {
                        Api.World.PlaySoundAt(ManureStackProps.PlaceRemoveSound.WithPathPrefixOnce("sounds/"), Pos.X + 0.5, Pos.InternalY, Pos.Z + 0.5, null, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);
                    }

                    Api.World.Logger.Audit("{0} Put {1}x{2} into new Manure stack at {3}.",
                        player.PlayerName,
                        TransferQuantity,
                        invSlot.Itemstack.Collectible.Code,
                        Pos
                    );

                    Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
                    return true;
                }

                if (invSlot.Itemstack.Equals(Api.World, hotbarSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
                {
                    bool putBulk = player.Entity.Controls.CtrlKey;

                    int q = GameMath.Min(hotbarSlot.StackSize, putBulk ? BulkTransferQuantity : TransferQuantity, Capacity - TotalStackSize);

                    // add to the pile and average item temperatures
                    int oldSize = invSlot.Itemstack.StackSize;
                    invSlot.Itemstack.StackSize += q;
                    if (oldSize + q > 0)
                    {
                        float tempPile = invSlot.Itemstack.Collectible.GetTemperature(Api.World, invSlot.Itemstack);
                        float tempAdded = hotbarSlot.Itemstack.Collectible.GetTemperature(Api.World, hotbarSlot.Itemstack);
                        invSlot.Itemstack.Collectible.SetTemperature(Api.World, invSlot.Itemstack, (tempPile * oldSize + tempAdded * q) / (oldSize + q), false);
                    }

                    if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
                    {
                        hotbarSlot.TakeOut(q);
                        hotbarSlot.OnItemSlotModified(null);
                    }

                    Api.World.PlaySoundAt(ManureStackProps.PlaceRemoveSound.WithPathPrefixOnce("sounds/"), Pos.X + 0.5, Pos.InternalY, Pos.Z + 0.5, null, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);

                    Api.World.Logger.Audit("{0} Put {1}x{2} into Manure stack at {3}.",
                        player.PlayerName,
                        q,
                        invSlot.Itemstack.Collectible.Code,
                        Pos
                    );

                    Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);

                    if (TotalStackSize >= Capacity)
                    {
                        int stackHeight = 1;
                        BlockPos tempPos = Pos.Copy();
                        while (Api.World.BlockAccessor.GetBlockEntity(tempPos.Down()) is BlockEntityManureStack below
                               && below.inventory[0].Itemstack?.Equals(Api.World, inventory[0].Itemstack, GlobalConstants.IgnoredStackAttributes) == true)
                        {
                            stackHeight++;
                            tempPos.Down();
                        }

                        if (stackHeight >= ManureStackProps.MaxStackingHeight)
                        {
                            ConvertToPile();
                            // Debug.WriteLine("Manure stack at {0} converted to pile after reaching max capacity of {1}", Pos, ManureStackProps.StackingCapacity);
                        }
                    }

                    MarkDirty(true);

                    Cuboidf[] collBoxes = Api.World.BlockAccessor.GetBlock(Pos).GetCollisionBoxes(Api.World.BlockAccessor, Pos);
                    if (collBoxes != null && collBoxes.Length > 0 && CollisionTester.AabbIntersect(collBoxes[0], Pos.X, Pos.Y, Pos.Z, player.Entity.SelectionBox, player.Entity.SidedPos.XYZ))
                    {
                        player.Entity.SidedPos.Y += collBoxes[0].Y2 - (player.Entity.SidedPos.Y - (int)player.Entity.SidedPos.Y);
                    }

                    return true;
                }

                return false;
            }
        }

        public bool TryTakeItem(IPlayer player)
        {
            lock (inventoryLock)
            {
                bool takeBulk = player.Entity.Controls.CtrlKey;
                int q = GameMath.Min(takeBulk ? BulkTransferQuantity : TransferQuantity, TotalStackSize);

                if (inventory[0]?.Itemstack != null)
                {
                    ItemStack stack = inventory[0].TakeOut(q);
                    player.InventoryManager.TryGiveItemstack(stack);

                    if (stack.StackSize > 0)
                    {
                        Api.World.SpawnItemEntity(stack, Pos);
                    }

                    Api.World.Logger.Audit("{0} Took {1}x{2} from Manure stack at {3}.",
                        player.PlayerName,
                        q,
                        stack.Collectible.Code,
                        Pos
                    );
                }

                if (TotalStackSize == 0)
                {
                    Api.World.BlockAccessor.SetBlock(0, Pos);
                }
                else Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);

                Api.World.PlaySoundAt(ManureStackProps.PlaceRemoveSound, Pos.X + 0.5, Pos.InternalY, Pos.Z + 0.5, null, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);

                MarkDirty(true);

                (player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);

                return true;
            }
        }

        public bool putOrGetItemSingle(ItemSlot ourSlot, IPlayer player, BlockSelection bs)
        {
            isUsingSlot = null;
            if (!ourSlot.Empty && ourSlot.Itemstack.Collectible is IContainedInteractable collIci)
            {
                if (collIci.OnContainedInteractStart(this, ourSlot, player, bs))
                {
                    BlockManureStack.IsUsingContainedBlock = true;
                    isUsingSlot = ourSlot;
                    return true;
                }
            }

            ItemSlot hotbarSlot = player.InventoryManager.ActiveHotbarSlot;

            if (ourSlot?.Itemstack?.Collectible is ILiquidInterface liquidCnt1 && hotbarSlot?.Itemstack?.Collectible is ILiquidInterface liquidCnt2)
            {
                BlockLiquidContainerBase heldLiquidContainer = hotbarSlot.Itemstack.Collectible as BlockLiquidContainerBase;

                CollectibleObject obj = hotbarSlot.Itemstack.Collectible;
                bool singleTake = player.WorldData.EntityControls.ShiftKey;
                bool singlePut = player.WorldData.EntityControls.CtrlKey;


                if (obj is ILiquidSource liquidSource && liquidSource.AllowHeldLiquidTransfer && !singleTake)
                {
                    ItemStack contentStackToMove = liquidSource.GetContent(hotbarSlot.Itemstack);

                    int moved = heldLiquidContainer.TryPutLiquid(
                        containerStack: ourSlot.Itemstack,
                        liquidStack: contentStackToMove,
                        desiredLitres: singlePut ? liquidSource.TransferSizeLitres : liquidSource.CapacityLitres);

                    if (moved > 0)
                    {
                        heldLiquidContainer.SplitStackAndPerformAction(player.Entity, hotbarSlot, delegate (ItemStack stack)
                        {
                            liquidSource.TryTakeContent(stack, moved);
                            return moved;
                        });
                        heldLiquidContainer.DoLiquidMovedEffects(player, contentStackToMove, moved, BlockLiquidContainerBase.EnumLiquidDirection.Pour);

                        BlockManureStack.IsUsingContainedBlock = true;
                        isUsingSlot = ourSlot;
                        return true;
                    }
                }

                if (obj is ILiquidSink liquidSink && liquidSink.AllowHeldLiquidTransfer && !singlePut)
                {
                    ItemStack owncontentStack = heldLiquidContainer.GetContent(ourSlot.Itemstack);
                    if (owncontentStack != null)
                    {
                        ItemStack liquidStackForParticles = owncontentStack.Clone();
                        float litres = (singleTake ? liquidSink.TransferSizeLitres : liquidSink.CapacityLitres);
                        int moved = heldLiquidContainer.SplitStackAndPerformAction(player.Entity, hotbarSlot, (ItemStack stack) => liquidSink.TryPutLiquid(stack, owncontentStack, litres));
                        if (moved > 0)
                        {
                            heldLiquidContainer.TryTakeContent(ourSlot.Itemstack, moved);
                            heldLiquidContainer.DoLiquidMovedEffects(player, liquidStackForParticles, moved, BlockLiquidContainerBase.EnumLiquidDirection.Fill);

                            BlockManureStack.IsUsingContainedBlock = true;
                            isUsingSlot = ourSlot;
                            return true;
                        }
                    }
                }
            }

            if (!hotbarSlot.Empty && !inventory.Empty)
            {
                var hotbarlayout = hotbarSlot.Itemstack.Collectible.GetBehavior<CollectibleBehaviorManureStack>()?.ManureStackProps.Layout;
                bool layoutEqual = ManureStackProps.Layout == hotbarlayout;

                if (!layoutEqual) return false;
            }

            lock (inventoryLock)
            {
                if (ourSlot.Empty)
                {
                    if (hotbarSlot.Empty) return false;

                    if (player.WorldData.CurrentGameMode == EnumGameMode.Creative)
                    {
                        ItemStack stack = hotbarSlot.Itemstack.Clone();
                        stack.StackSize = 1;
                        if (new DummySlot(stack).TryPutInto(Api.World, ourSlot, TransferQuantity) > 0)
                        {
                            Api.World.PlaySoundAt(ManureStackProps.PlaceRemoveSound, Pos.X + 0.5, Pos.InternalY, Pos.Z + 0.5, player, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);
                            Api.World.Logger.Audit("{0} Put 1x{1} into Manure stack at {2}.",
                                player.PlayerName,
                                ourSlot.Itemstack.Collectible.Code,
                                Pos
                            );
                        }
                    }
                    else
                    {
                        if (hotbarSlot.TryPutInto(Api.World, ourSlot, TransferQuantity) > 0)
                        {
                            Api.World.PlaySoundAt(ManureStackProps.PlaceRemoveSound, Pos.X + 0.5, Pos.InternalY, Pos.Z + 0.5, player, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);
                            Api.World.Logger.Audit("{0} Put 1x{1} into Manure stack at {2}.",
                                player.PlayerName,
                                ourSlot.Itemstack.Collectible.Code,
                                Pos
                            );
                        }
                    }
                }
                else
                {
                    if (!player.InventoryManager.TryGiveItemstack(ourSlot.Itemstack, true))
                    {
                        Api.World.SpawnItemEntity(ourSlot.Itemstack, Pos);
                    }

                    Api.World.PlaySoundAt(ManureStackProps.PlaceRemoveSound, Pos.X + 0.5, Pos.InternalY, Pos.Z + 0.5, player, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16);

                    Api.World.Logger.Audit("{0} Took 1x{1} from Manure stack at {2}.",
                        player.PlayerName,
                        ourSlot.Itemstack?.Collectible.Code,
                        Pos
                    );
                    ourSlot.Itemstack = null;
                    ourSlot.MarkDirty();
                }
            }

            return true;
        }

        private void ConvertToPile()
        {
            if (Api.Side != EnumAppSide.Server) return;  // Only run on server

            Block pileBlock = Api.World.GetBlock(new AssetLocation("naturalfertilizer:manurepile"));
            if (pileBlock == null) return;

            // Replace current stack with the manure pile block
            Api.World.BlockAccessor.SetBlock(pileBlock.BlockId, Pos);
            // Debug.WriteLine("Manure stack at {0} converted to pile", Pos);

        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            clientsideFirstPlacement = false;

            forceManureStackProps = tree.GetBool("forceManureStackProps");
            if (forceManureStackProps)
            {
                ManureStackProps = JsonUtil.FromString<ManureStackProperties>(tree.GetString("storageProps"));
            }

            overrideLayout = null;
            if (tree.HasAttribute("overrideLayout"))
            {
                overrideLayout = (EnumManureStackLayout)tree.GetInt("overrideLayout");
            }

            if (Api != null)
            {
                DetermineStorageProperties(null);
            }

            MeshAngle = tree.GetFloat("meshAngle");
            AttachFace = BlockFacing.ALLFACES[tree.GetInt("attachFace")];

            // Do this last!!!  Prevents bug where initially drawn with wrong rotation
            RedrawAfterReceivingTreeAttributes(worldForResolving);     // Redraw on client after we have completed receiving the update from server
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            
            tree.SetBool("forceManureStackProps", forceManureStackProps);
            if (forceManureStackProps && ManureStackProps != null)
            {
                tree.SetString("storageProps", JsonUtil.ToString(ManureStackProps));
            }
            if (overrideLayout != null)
            {
                tree.SetInt("overrideLayout", (int)overrideLayout);
            }
            

            tree.SetFloat("meshAngle", MeshAngle);
            tree.SetInt("attachFace", AttachFace?.Index ?? 0);
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            // Handled by block.GetDrops()
            /*if (Api.World.Side == EnumAppSide.Server)
            {
                inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5), 4);
            }*/
        }

        public virtual string GetBlockName()
        {
            var props = ManureStackProps;
            if (props == null || inventory.Empty) return Lang.Get("Empty pile");

            string[] contentSummary = getContentSummary();
            if (contentSummary.Length == 1)
            {
                var firstSlot = inventory.FirstNonEmptySlot;

                ItemStack stack = firstSlot.Itemstack;
                int sumQ = inventory.Sum(s => s.StackSize);

                string name = firstSlot.Itemstack.Collectible.GetCollectibleInterface<IContainedCustomName>()?.GetContainedName(firstSlot, sumQ);
                if (name != null) return name;

                if (sumQ == 1) return stack.GetName();
                return contentSummary[0];
            }

            return Lang.Get("Manure Pile");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            if (inventory.Empty) return;

            string[] contentSummary = getContentSummary();

            ItemStack stack = inventory.FirstNonEmptySlot.Itemstack;
            // Only add supplemental info for non-BlockEntities (otherwise it will be wrong or will get into a recursive loop, because right now this BEGroundStorage is the BlockEntity)
            if (contentSummary.Length == 1 && stack.Collectible.GetCollectibleInterface<IContainedCustomName>() == null && stack.Class == EnumItemClass.Block && ((Block)stack.Collectible).EntityClass == null)
            {
                string detailedInfo = stack.Block.GetPlacedBlockInfo(Api.World, Pos, forPlayer);
                if (detailedInfo != null && detailedInfo.Length > 0) dsc.Append(detailedInfo);
            }
            else
            {
                foreach (var line in contentSummary) dsc.AppendLine(line);
            }

            /*
            if (!inventory.Empty)
            {
                foreach (var slot in inventory)
                {
                    var temperature = slot.Itemstack?.Collectible.GetTemperature(Api.World, slot.Itemstack) ?? 0;

                    if (temperature > 20)
                    {
                        var f = slot.Itemstack?.Attributes.GetFloat("hoursHeatReceived") ?? 0;
                        dsc.AppendLine(Lang.Get("temperature-precise", temperature));
                        if (f > 0) dsc.AppendLine(Lang.Get("Fired for {0:0.##} hours", f));
                    }
                }
            }
            */
        }

        public virtual string[] getContentSummary()
        {
            OrderedDictionary<string, int> dict = new();

            foreach (var slot in inventory)
            {
                if (slot.Empty) continue;

                string stackName = slot.Itemstack.GetName();

                stackName = slot.Itemstack.Collectible.GetCollectibleInterface<IContainedCustomName>()?.GetContainedInfo(slot) ?? stackName;

                if (!dict.TryGetValue(stackName, out int cnt)) cnt = 0;

                dict[stackName] = cnt + slot.StackSize;
            }

            return dict.Select(elem => Lang.Get("{0}x {1}", elem.Value, elem.Key)).ToArray();
        }

        
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            NeedsRetesselation = false;
            lock (inventoryLock)
            {
                return base.OnTesselation(mesher, tesselator);
            }
        }

        Vec3f rotatedOffset(Vec3f offset, float radY)
        {
            Matrixf mat = new Matrixf();
            mat.Translate(0.5f, 0.5f, 0.5f).RotateY(radY).Translate(-0.5f, -0.5f, -0.5f);
            return mat.TransformVector(new Vec4f(offset.X, offset.Y, offset.Z, 1)).XYZ;
        }

        protected override float[][] genTransformationMatrices()
        {
            float[][] tfMatrices = new float[DisplayedItems][];

            Vec3f[] offs = new Vec3f[DisplayedItems];

            lock (inventoryLock)
            {
                GetLayoutOffset(offs);
            }

            for (int i = 0; i < tfMatrices.Length; i++)
            {
                Vec3f off = offs[i];
                off = new Matrixf().RotateY(MeshAngle).TransformVector(off.ToVec4f(0)).XYZ;

                tfMatrices[i] =
                    new Matrixf()
                    .Translate(off.X + 0.5f, off.Y, off.Z + 0.5f)
                    .RotateY(MeshAngle)
                    .Translate(-0.5f, 0, -0.5f)
                    .Values
                ;
            }

            return tfMatrices;
        }

        public void GetLayoutOffset(Vec3f[] offs)
        {
            if (ManureStackProps == null) return;
            switch (ManureStackProps.Layout)
            {
                case EnumManureStackLayout.Stacking:
                    offs[0] = new Vec3f();
                    break;
            }
        }

        protected override string getMeshCacheKey(ItemStack stack)
        {
            return (ManureStackProps?.ModelItemsToStackSizeRatio > 0 ? stack.StackSize : 1) + "x" + base.getMeshCacheKey(stack);
        }

        protected override MeshData getOrCreateMesh(ItemStack stack, int index)
        {
            if (stack.Class == EnumItemClass.Block)
            {
                    MeshRefs[index] = capi.TesselatorManager.GetDefaultBlockMeshRef(stack.Block);
            }
            // shingle/bricks are items but uses Stacking layout to get the mesh, so this should be not needed atm
            else if (stack.Class == EnumItemClass.Item && ManureStackProps != null && ManureStackProps.Layout != EnumManureStackLayout.Stacking)
            {
                MeshRefs[index] = capi.TesselatorManager.GetDefaultItemMeshRef(stack.Item);
            }


            if (ManureStackProps?.Layout == EnumManureStackLayout.Stacking)
            {
                var key = getMeshCacheKey(stack);
                var mesh = getMesh(stack);

                if (mesh != null)
                {
                    UploadedMeshCache.TryGetValue(key, out MeshRefs[index]);
                    return mesh;
                }

                var loc = ManureStackProps.StackingModel.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                nowTesselatingShape = Shape.TryGet(capi, loc);
                nowTesselatingObj = stack.Collectible;

                if (nowTesselatingShape == null)
                {
                    capi.Logger.Error("Stacking model shape for collectible " + stack.Collectible.Code + " not found. Block will be invisible!");
                    return null;
                }

                capi.Tesselator.TesselateShape("storagePile", nowTesselatingShape, out mesh, this, null, 0, 0, 0, (int)Math.Ceiling(ManureStackProps.ModelItemsToStackSizeRatio * stack.StackSize));

                MeshCache[key] = mesh;

                if (UploadedMeshCache.TryGetValue(key, out var mr)) mr.Dispose();
                UploadedMeshCache[key] = capi.Render.UploadMultiTextureMesh(mesh);
                MeshRefs[index] = UploadedMeshCache[key];
                return mesh;
            }

            var meshData = base.getOrCreateMesh(stack, index);
            if (stack.Collectible.Attributes?[AttributeTransformCode].Exists == true)
            {
                var transform = stack.Collectible.Attributes?[AttributeTransformCode].AsObject<ModelTransform>();
                ModelTransformsRenderer[index] = transform;
            }
            else
            {
                ModelTransformsRenderer[index] = null;
            }

            return meshData;
        }

        public void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
        {
            MeshAngle = tree.GetFloat("meshAngle");
            MeshAngle -= degreeRotation * GameMath.DEG2RAD;
            tree.SetFloat("meshAngle", MeshAngle);

            AttachFace = BlockFacing.ALLFACES[tree.GetInt("attachFace")];
            AttachFace = AttachFace.FaceWhenRotatedBy(0, -degreeRotation * GameMath.DEG2RAD, 0);
            tree.SetInt("attachFace", AttachFace?.Index ?? 0);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            Dispose();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            Dispose();
        }

        protected virtual void Dispose()
        {
            if (UploadedMeshCache != null)
            {
                foreach (var mesh in UploadedMeshCache.Values)
                {
                    mesh?.Dispose();
                }
            }
            // renderer?.Dispose();
        }

    }
}
