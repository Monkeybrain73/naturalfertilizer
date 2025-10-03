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
    public class BEManurePile : BlockEntityGroundStorage, IRotatable
    {
        new InventoryGeneric inventory;
        BlockManurePile ownBlock;

        public ManureFermentationProps fermentationProps { get; private set; }

        public double fermentationStartTotalHours = -1; // -1 = not fermenting
        public double fermentationDurationHours;

        public bool IsFermenting;

        public long listenerId;

        public string type = "manure-fresh";
        public string preferredFillState = "empty";
        public int quantitySlots = 1;
        float rotAngleY;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "manurepile";

        static Vec3f origin = new Vec3f(0.5f, 0f, 0.5f);
        float rndScale => 1 + (GameMath.MurmurHash3Mod(Pos.X, Pos.Y, Pos.Z, 100) - 50) / 1000f;

        MeshData ownMesh;

        Cuboidf selBoxManurePile;

        public new virtual float MeshAngle
        {
            get { return rotAngleY; }
            set
            {
                rotAngleY = value;
            }
        }

        public string FillState
        {
            get
            {
                if (inventory.Empty) return "empty";

                foreach (var slot in inventory)
                {
                    if (!slot.Empty && slot.Itemstack.Collectible is ItemDryGrass)
                    {
                        return "filled";
                    }
                }

                return "empty";
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            ownBlock = (BlockManurePile)Block;

            bool isNewlyplaced = inventory == null;
            if (isNewlyplaced)
            {
                InitInventory(Block, api);
            }

            base.Initialize(api);

            if (api.Side == EnumAppSide.Client && !isNewlyplaced)
            {
                loadOrCreateMesh();
            }
            if (Api.Side == EnumAppSide.Server)
            {
                listenerId = RegisterGameTickListener(CheckFermentationProgress, 5000); // every 5 seconds
            }
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            if (byItemStack?.Attributes != null)
            {
                string nowType = byItemStack.Attributes.GetString("type", ownBlock.Props.DefaultType);
                string nowFillState = byItemStack.Attributes.GetString("fillState", "empty");

                if (nowType != type || nowFillState != preferredFillState)
                {
                    this.type = nowType;
                    this.preferredFillState = nowFillState;
                    InitInventory(Block, Api);
                    Inventory.LateInitialize(InventoryClassName + "-" + Pos, Api);
                    Inventory.ResolveBlocksOrItems();
                    container.LateInit();
                    MarkDirty();
                }
            }

            base.OnBlockPlaced();
        }

        public bool OnBlockInteractStart(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (IsFermenting) return false;
            
            bool put = byPlayer.Entity.Controls.ShiftKey;
            bool take = !put;
            bool bulk = byPlayer.Entity.Controls.CtrlKey;

            ItemSlot hotbarslot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (hotbarslot == null) throw new Exception("Interact called when byPlayer has null ActiveHotbarSlot");

            if (put && !hotbarslot.Empty)
            {
                if (!IsGrass(hotbarslot.Itemstack))
                {
                    (Api as ICoreClientAPI)?.TriggerIngameError(this, "onlygrass", Lang.Get("Only grass can be added to the manure."));
                    return true;
                }

                ItemSlot ownSlot = inventory.FirstNonEmptySlot;
                var quantity = bulk ? hotbarslot.StackSize : 1;
                if (ownSlot == null)
                {
                    if (hotbarslot.TryPutInto(Api.World, inventory[0], quantity) > 0)
                    {
                        didMoveItems(inventory[0].Itemstack, byPlayer);
                        if (Api.Side == EnumAppSide.Client)
                        {
                            loadOrCreateMesh();
                        }
                        MarkDirty(true);
                        Api.World.Logger.Audit("{0} Put {1}x{2} into Manure pile at {3}.",
                            byPlayer.PlayerName,
                            quantity,
                            inventory[0].Itemstack?.Collectible.Code,
                            Pos
                        );
                    }
                }
                else
                {
                    if (hotbarslot.Itemstack.Equals(Api.World, ownSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
                    {
                        List<ItemSlot> skipSlots = new List<ItemSlot>();
                        while (hotbarslot.StackSize > 0 && skipSlots.Count < inventory.Count)
                        {
                            var wslot = inventory.GetBestSuitedSlot(hotbarslot, null, skipSlots);
                            if (wslot.slot == null) break;

                            if (hotbarslot.TryPutInto(Api.World, wslot.slot, quantity) > 0)
                            {
                                didMoveItems(wslot.slot.Itemstack, byPlayer);
                                Api.World.Logger.Audit("{0} Put {1}x{2} into Manure pile at {3}.",
                                    byPlayer.PlayerName,
                                    quantity,
                                    wslot.slot.Itemstack?.Collectible.Code,
                                    Pos
                                );
                                if (!bulk) break;
                            }

                            skipSlots.Add(wslot.slot);
                        }
                    }
                }

                hotbarslot.MarkDirty();
                MarkDirty();
            }

            return true;
        }

        protected void didMoveItems(ItemStack stack, IPlayer byPlayer)
        {
            if (Api.Side == EnumAppSide.Client) loadOrCreateMesh();

            (Api as ICoreClientAPI)?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            AssetLocation sound = stack?.Block?.Sounds?.Place;
            Api.World.PlaySoundAt(sound != null ? sound : new AssetLocation("game:sounds/walk/sludge1"), byPlayer.Entity, byPlayer, true, 16);
        }

        protected virtual void InitInventory(Block block, ICoreAPI api)
        {
            if (block?.Attributes != null)
            {
                var props = block.Attributes["properties"][type];
                if (!props.Exists) props = block.Attributes["properties"]["*"];
                quantitySlots = props["quantitySlots"].AsInt(quantitySlots);
            }

            inventory = new InventoryGeneric(quantitySlots, null, null, null);
            inventory.BaseWeight = 1f;
            inventory.OnGetSuitability = (sourceSlot, targetSlot, isMerge) => (isMerge ? (inventory.BaseWeight + 3) : (inventory.BaseWeight + 1)) + (sourceSlot.Inventory is InventoryBasePlayer ? 1 : 0);
            inventory.OnGetAutoPushIntoSlot = GetAutoPushIntoSlot;

            // inventory.OnInventoryClosed += OnInvClosed;
            // inventory.OnInventoryOpened += OnInvOpened;

            if (api.Side == EnumAppSide.Server)
            {
                inventory.SlotModified += Inventory_SlotModified;
            }

            if (inventory != null)
            {
                inventory.SlotModified += (slotIndex) =>
                {
                    if (Api.Side == EnumAppSide.Client)
                    {
                        loadOrCreateMesh();
                    }
                    MarkDirty(true); // Mark block dirty so client updates properly
                };
            }

            container.Reset();

            var manifoldBlock = block as BlockManurePile;
            var ferProps = manifoldBlock?.FermentationProps;
            if (ferProps != null)
            {
                this.fermentationProps = ferProps;
            }
        }


        private void Inventory_SlotModified(int obj)
        {
            TryStartFermentation();
            MarkDirty(false);
        }

        public new Cuboidf[] GetSelectionBoxes()
        {
            if (selBoxManurePile == null)
            {
                selBoxManurePile = Block.SelectionBoxes[0].RotatedCopy(0, ((int)Math.Round(rotAngleY * GameMath.RAD2DEG / 90)) * 90, 0, new Vec3d(0.5, 0, 0.5));
            }

            if (Api.Side == EnumAppSide.Client)
            {
                ItemSlot hotbarslot = ((ICoreClientAPI)Api).World.Player.InventoryManager.ActiveHotbarSlot;
            }

            return new Cuboidf[] { selBoxManurePile };
        }

        #region Load/Store

        private void TryStartFermentation()
        {
            if (IsFermenting) return;

            var props = ownBlock._fermentationProps;
            if (props == null) return;

            int grassCount = 0;
            foreach (var slot in inventory)
            {
                if (!slot.Empty && IsGrass(slot.Itemstack))
                {
                    grassCount += slot.StackSize;
                }
                else if (!slot.Empty)
                {
                    return;
                }
            }

            if (grassCount >= fermentationProps.RequiredGrass && !IsFermenting)
            {
                var fprops = (Block as BlockManurePile)?.FermentationProps;
                if (fprops == null) return;

                IsFermenting = true;
                fermentationStartTotalHours = Api.World.Calendar.TotalHours;
                fermentationDurationHours = fprops.FermentationHours;
                // Debug.WriteLine("Start:" + fermentationStartTotalHours.ToString() + "End:" + fermentationDurationHours.ToString() + "Ferment hrs:" + fermentationProps.FermentationHours.ToString());

                MarkDirty(true);
            }
        }

        private void CheckFermentationProgress(float dt)
        {
            if (Api.Side == EnumAppSide.Server)
            {
                if (!IsFermenting) return;

                var props = ownBlock._fermentationProps;
                double elapsed = Api.World.Calendar.TotalHours - fermentationStartTotalHours;
                // Debug.WriteLine("Elapsed hours:" + elapsed);

                if (elapsed >= fermentationProps.FermentationHours)
                {
                    CompleteFermentation();
                }
                else
                {
                    MarkDirty(true);
                }
            }
        }

        private void CompleteFermentation()
        {
            var props = ownBlock._fermentationProps;
            if (props == null) return;

            fermentationStartTotalHours = -1;
            IsFermenting = false;

            Block result = Api.World.GetBlock(fermentationProps.ResultBlock);
            if (result != null)
            {
                Api.World.BlockAccessor.SetBlock(result.BlockId, Pos);
            }
            if (listenerId != 0)
            {
                UnregisterGameTickListener(listenerId);
                listenerId = 0;
            }

            MarkDirty(true);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            var block = worldForResolving.GetBlock(new AssetLocation(tree.GetString("blockCode"))) as BlockManurePile;

            type = tree.GetString("type", block?.Props.DefaultType);
            MeshAngle = tree.GetFloat("meshAngle", MeshAngle);

            preferredFillState = tree.GetString("fillState");

            if (inventory == null)
            {
                if (tree.HasAttribute("blockCode"))
                {
                    InitInventory(block, worldForResolving.Api);
                }
                else
                {
                    InitInventory(null, worldForResolving.Api);
                }
            }

            if (Api != null && Api.Side == EnumAppSide.Client)
            {
                loadOrCreateMesh();
                MarkDirty(true);
            }

            base.FromTreeAttributes(tree, worldForResolving);

            IsFermenting = tree.GetBool("isFermenting", false);
            fermentationStartTotalHours = tree.GetDouble("fermentStartHours", -1);
            fermentationDurationHours = tree.GetDouble("fermentDurationHours", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            if (Block != null) tree.SetString("forBlockCode", Block.Code.ToShortString());

            if (type == null) type = ownBlock.Props.DefaultType;

            tree.SetString("type", type);
            tree.SetFloat("meshAngle", MeshAngle);
            tree.SetString("fillState", preferredFillState);
            tree.SetBool("isFermenting", IsFermenting);
            tree.SetDouble("fermentStartHours", fermentationStartTotalHours);
            tree.SetDouble("fermentDurationHours", fermentationDurationHours);

        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
        {
            base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        }

        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        }

        #endregion

        private ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            if (!IsGrass(fromSlot?.Itemstack)) return null;

            var slotNonEmpty = inventory.FirstNonEmptySlot;
            if (slotNonEmpty == null) return inventory[0];

            if (slotNonEmpty.Itemstack.Equals(Api.World, fromSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
            {
                foreach (var slot in inventory)
                {
                    if (slot.Itemstack == null || slot.StackSize < slot.Itemstack.Collectible.MaxStackSize)
                        return slot;
                }
                return null;
            }

            return null;
        }


        #region Meshing

        private void loadOrCreateMesh()
        {
            Block ??= Api.World.BlockAccessor.GetBlock(Pos) as BlockManurePile;
            BlockManurePile block = Block as BlockManurePile;
            if (block == null) return;

            string cacheKey = "manurepileMeshes" + block.FirstCodePart();
            Dictionary<string, MeshData> meshes = ObjectCacheUtil.GetOrCreate(Api, cacheKey, () => new Dictionary<string, MeshData>());


            CompositeShape cshape = ownBlock.Props[type].Shape;
            if (cshape?.Base == null)
            {
                return;
            }

            var firstStack = inventory.FirstNonEmptySlot?.Itemstack;

            string stage = ComputeFillStage();

            string meshKey = type + block.Subtype + "-" + stage;

            if (!meshes.TryGetValue(meshKey, out MeshData mesh))
            {
                mesh = block.GenMesh(Api as ICoreClientAPI, firstStack, type, stage, cshape, new Vec3f(cshape.rotateX, cshape.rotateY, cshape.rotateZ));
                meshes[meshKey] = mesh;
            }

            ownMesh = mesh.Clone().Rotate(origin, 0, MeshAngle, 0).Scale(origin, rndScale, rndScale, rndScale);
        }

        private string ComputeFillStage()
        {
            int totalCount = 0;
            for (int i = 0; i < inventory.Count; i++)
            {
                totalCount += inventory[i].StackSize;
            }

            if (totalCount <= 0) return "empty";

            int perSlotMax = inventory.FirstNonEmptySlot?.Itemstack?.Collectible?.MaxStackSize ?? 64;

            // Capacity = slots * per-slot max
            int capacity = quantitySlots * perSlotMax;
            if (capacity <= 0) return "stage1";

            float ratio = (float)totalCount / capacity;

            // Divide into 4 slices (each = 25% capacity)
            if (ratio <= 0.25f) return "stage1";
            if (ratio <= 0.50f) return "stage2";
            if (ratio <= 0.75f) return "stage3";
            return "stage4";
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            bool skipmesh = base.OnTesselation(mesher, tesselator);
            if (skipmesh) return true;


            if (ownMesh == null)
            {
                return true;
            }


            mesher.AddMeshData(ownMesh);

            return true;
        }

        #endregion


        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(Pos) as BEManurePile;
            if (be == null) return;

            if (be.IsFermenting)
            {
                double hoursPassed = Api.World.Calendar.TotalHours - fermentationStartTotalHours;
                double hoursLeft = fermentationDurationHours - hoursPassed;
                if (hoursLeft > 0)

                dsc.AppendLine(Lang.Get("Fermenting: {0} hours remaining", (int)Math.Ceiling(hoursLeft)));
            }
            else
            {
                int stacksize = 0;
                foreach (var slot in inventory) stacksize += slot.StackSize;

                if (stacksize > 0)
                {
                    dsc.AppendLine(Lang.Get("Grass added: {0}/{1}", stacksize, fermentationProps.RequiredGrass));
                }
                else
                {
                    dsc.AppendLine(Lang.Get("Add grass to start fermenting"));
                }
            }

            // base.GetBlockInfo(forPlayer, dsc);
        }


        public override void OnBlockUnloaded()
        {
            Cleanup();
            base.OnBlockUnloaded();
        }

        public override void OnBlockRemoved()
        {
            Cleanup();
            base.OnBlockRemoved();
        }

        private void FreeAtlasSpace()
        {
            if (Api is ICoreClientAPI capi)
            {
                string cacheKey = "manurepileMeshes" + Block.FirstCodePart();
                var meshes = ObjectCacheUtil.TryGet<Dictionary<string, MeshData>>(capi, cacheKey);

                if (meshes != null)
                {
                    // Remove only this BE's meshes
                    var keysToRemove = meshes.Keys.Where(k => k.StartsWith(type)).ToList();
                    foreach (var key in keysToRemove)
                    {
                        meshes.Remove(key);
                    }
                }
            }

            ownMesh = null;
        }

        private void Cleanup()
        {
            if (listenerId != 0)
            {
                UnregisterGameTickListener(listenerId);
                listenerId = 0;
            }

            FreeAtlasSpace();
            ownMesh = null;

            if (inventory != null)
            {
                inventory.SlotModified -= Inventory_SlotModified;
            }
        }

        public new void OnTransformed(IWorldAccessor worldAccessor, ITreeAttribute tree, int degreeRotation,
            Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, EnumAxis? flipAxis)
        {
            ownMesh = null;
            MeshAngle = tree.GetFloat("meshAngle");
            MeshAngle -= degreeRotation * GameMath.DEG2RAD;
            tree.SetFloat("meshAngle", MeshAngle);
        }

        private static bool IsGrass(ItemStack stack)
        {
            if (stack == null) return false;

            var path = stack.Collectible?.Code?.Path ?? "";
            if (path.StartsWith("drygrass")) return true;
            if (path.StartsWith("thatch")) return true;

            // Optional: allow a JSON flag to mark custom arrows
            if (stack.Collectible?.Attributes?["isGrass"].AsBool(false) == true) return true;

            return false;
        }
    }
}