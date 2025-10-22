#nullable disable

namespace naturalfertilizer
{
    public class ManureFermentationProps
    {
        public int RequiredGrass { get; set; }
        // public int SlotStackSize { get; set; }
        public double FermentationHours { get; set; }
        public AssetLocation ResultBlock { get; set; } = new AssetLocation("naturalfertilizer:weakfertilizer");

        [System.NonSerialized] public AssetLocation ResultBlockAsset;
    }

    public class ManurePileTypeProperties
    {
        public int QuantitySlots;
        public CompositeShape Shape;
        public string RotatatableInterval;
        public EnumItemStorageFlags StorageType;
    }

    public class ManurePileProperties
    {
        public Dictionary<string, ManurePileTypeProperties> Properties;
        public string[] Types;
        public string DefaultType = "manure-fresh";
        public string VariantByGroup;
        public string VariantByGroupInventory;
        public string InventoryClassName = "manurepile";

        public ManurePileTypeProperties this[string type]
        {
            get
            {
                if (!Properties.TryGetValue(type, out var props))
                {
                    return Properties["*"];
                }

                return props;
            }
        }
    }

    public class ItemStackRenderCacheItem
    {
        public int TextureSubId;
        public HashSet<int> UsedCounter;
    }

    public class CollectibleBehaviorManurePile : CollectibleBehaviorHeldBag, IAttachedInteractions
    {
        ICoreAPI Api;
        public CollectibleBehaviorManurePile(CollectibleObject collObj) : base(collObj)
        {
        }

        public override void OnLoaded(ICoreAPI api)
        {
            this.Api = api;
            base.OnLoaded(api);
        }

        public override bool IsEmpty(ItemStack bagstack)
        {
            bool empty = base.IsEmpty(bagstack);
            return empty;
        }

        public override int GetQuantitySlots(ItemStack bagstack)
        {
            if (collObj is not BlockManurePile barrel) return 0;

            string type = bagstack.Attributes.GetString("type") ?? barrel.Props.DefaultType;
            int quantity = barrel.Props[type].QuantitySlots;
            return quantity;
        }

        public override void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave)
        {
            var controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
            if (controls.Sprint) return;


            bool put = byEntity.Controls.ShiftKey;
            bool take = !put;
            bool bulk = byEntity.Controls.CtrlKey;

            var byPlayer = (byEntity as EntityPlayer).Player;

            var ws = getOrCreateContainerWorkspace(slotIndex, onEntity, onRequireSave);

            var face = BlockFacing.UP;
            var Pos = byEntity.Pos.XYZ;

            if (!ws.TryLoadInv(bagSlot, slotIndex, onEntity))
            {
                return;
            }

            ItemSlot ownSlot = ws.WrapperInv.FirstNonEmptySlot;
            var hotbarslot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (put && !hotbarslot.Empty)
            {
                var quantity = bulk ? hotbarslot.StackSize : 1;
                if (ownSlot == null)
                {
                    if (hotbarslot.TryPutInto(Api.World, ws.WrapperInv[0], quantity) > 0)
                    {
                        didMoveItems(ws.WrapperInv[0].Itemstack, byPlayer);
                        Api.World.Logger.Audit("{0} Put {1}x{2} into Manure pile at {3}.",
                            byPlayer.PlayerName,
                            quantity,
                            ws.WrapperInv[0].Itemstack.Collectible.Code,
                            Pos
                        );
                    }

                    ws.BagInventory.SaveSlotIntoBag((ItemSlotBagContent)ws.WrapperInv[0]);
                }
                else
                {
                    if (hotbarslot.Itemstack.Equals(Api.World, ownSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
                    {
                        List<ItemSlot> skipSlots = new List<ItemSlot>();
                        while (hotbarslot.StackSize > 0 && skipSlots.Count < ws.WrapperInv.Count)
                        {
                            WeightedSlot wslot = ws.WrapperInv.GetBestSuitedSlot(hotbarslot, null, skipSlots);
                            if (wslot.slot == null) break;

                            if (hotbarslot.TryPutInto(Api.World, wslot.slot, quantity) > 0)
                            {
                                didMoveItems(wslot.slot.Itemstack, byPlayer);

                                ws.BagInventory.SaveSlotIntoBag((ItemSlotBagContent)wslot.slot);

                                Api.World.Logger.Audit("{0} Put {1}x{2} into Manure pile at {3}.",
                                    byPlayer.PlayerName,
                                    quantity,
                                    wslot.slot.Itemstack.Collectible.Code,
                                    Pos
                                );
                                if (!bulk) break;
                            }

                            skipSlots.Add(wslot.slot);
                        }
                    }
                }

                hotbarslot.MarkDirty();
            }
        }

        protected void didMoveItems(ItemStack stack, IPlayer byPlayer)
        {
            (Api as ICoreClientAPI)?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            AssetLocation sound = stack?.Block?.Sounds?.Place;
            Api.World.PlaySoundAt(sound != null ? sound : new AssetLocation("game:sounds/walk/sludge1"), byPlayer.Entity, byPlayer, true, 16);
        }
    }

    public class BlockManurePile : BlockContainer, ITexPositionSource, IWearableShapeSupplier
    {
        public Size2i AtlasSize { get { return tmpTextureSource.AtlasSize; } }

        string curType;
        ITexPositionSource tmpTextureSource;

        public string Subtype => Props.VariantByGroup == null ? "" : Variant[Props.VariantByGroup];
        public string SubtypeInventory => Props?.VariantByGroupInventory == null ? "" : Variant[Props.VariantByGroupInventory];

        public int RequiresBehindSlots { get; set; } = 0;

        private Vec3f origin = new Vec3f(0.5f, 0.5f, 0.5f);

        #region IAttachableToEntity

        public void CollectTextures(ItemStack stack, Shape shape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict)
        {
            string type = stack.Attributes.GetString("type");
            foreach (var key in shape.Textures.Keys)
            {
                this.Textures.TryGetValue(type + "-" + key, out var ctex);
                if (ctex != null)
                {
                    intoDict[texturePrefixCode + key] = ctex;
                }
                else
                {
                    Textures.TryGetValue(key, out var ctex2);
                    intoDict[texturePrefixCode + key] = ctex2;
                }

            }
        }

        public string GetCategoryCode(ItemStack stack)
        {
            return "manurepile";
        }

        public Shape GetShape(ItemStack stack, Entity forEntity, string texturePrefixCode)
        {
            string type = stack.Attributes.GetString("type", Props.DefaultType);
            string isFilled = stack.Attributes.GetString("fillState", "empty");

            CompositeShape cshape = Props[type].Shape;
            var rot = ShapeInventory == null ? null : new Vec3f(ShapeInventory.rotateX, ShapeInventory.rotateY, ShapeInventory.rotateZ);

            var contentStacks = GetNonEmptyContents(api.World, stack);
            var contentStack = contentStacks == null || contentStacks.Length == 0 ? null : contentStacks[0];

            if (isFilled == "filled")
            {
                cshape = cshape.Clone();
                cshape.Base.Path = cshape.Base.Path.Replace("empty", "filled");
            }

            AssetLocation shapeloc = cshape.Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");
            Shape shape = Vintagestory.API.Common.Shape.TryGet(api, shapeloc);

            shape.SubclassForStepParenting(texturePrefixCode, 0);

            return shape;
        }

        public CompositeShape GetAttachedShape(ItemStack stack, string slotCode)
        {
            return null;
        }

        public string[] GetDisableElements(ItemStack stack)
        {
            return null;
        }

        public string[] GetKeepElements(ItemStack stack)
        {
            return null;
        }

        public string GetTexturePrefixCode(ItemStack stack)
        {
            var key = GetKey(stack);
            return key;
        }
        #endregion


        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                TextureAtlasPosition pos = tmpTextureSource[curType + "-" + textureCode];
                if (pos == null) pos = tmpTextureSource[textureCode];
                if (pos == null)
                {
                    pos = (api as ICoreClientAPI).BlockTextureAtlas.UnknownTexturePosition;
                }
                return pos;
            }
        }


        public override bool DoParticalSelection(IWorldAccessor world, BlockPos pos)
        {
            return true;
        }

        public ManureFermentationProps _fermentationProps;

        public ManureFermentationProps FermentationProps
        {
            get
            {
                if (_fermentationProps == null)
                {
                    var node = Attributes?["fermentation"];
                    if (node != null && node.Exists)
                    {
                        _fermentationProps = node.AsObject<ManureFermentationProps>(null, Code.Domain) ?? new ManureFermentationProps();
                    }
                    else
                    {
                        _fermentationProps = new ManureFermentationProps();
                    }

                    try
                    {
                        if (!string.IsNullOrEmpty(_fermentationProps.ResultBlock))
                        {
                            _fermentationProps.ResultBlockAsset = new AssetLocation(_fermentationProps.ResultBlock);
                        }
                    }
                    catch
                    {
                        _fermentationProps.ResultBlockAsset = null;
                    }

                    PlacedPriorityInteract = true;
                }

                /*
                Debug.WriteLine("Accessed manure pile fermentation props: " + _fermentationProps?.RequiredGrass + " grass for " + _fermentationProps?.ResultBlock);
                Debug.WriteLine("Accessed manure pile fermentation propes: " + _fermentationProps?.FermentationHours + " hours to ferment.");
                */


                return _fermentationProps;
            }
        }

        ManurePileProperties _props;
        public ManurePileProperties Props => _props ??= Attributes.AsObject<ManurePileProperties>(null, Code.Domain);


        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            PlacedPriorityInteract = true;
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool val = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

            if (val)
            {
                BEManurePile bect = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEManurePile;
                if (bect != null)
                {
                    BlockPos targetPos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
                    double dx = byPlayer.Entity.Pos.X - (targetPos.X + blockSel.HitPosition.X);
                    double dz = (float)byPlayer.Entity.Pos.Z - (targetPos.Z + blockSel.HitPosition.Z);
                    float angleHor = (float)Math.Atan2(dx, dz);


                    string type = bect.type;
                    string rotatatableInterval = Props[type].RotatatableInterval;

                    if (rotatatableInterval == "22.5degnot45deg")
                    {
                        float rounded90degRad = ((int)Math.Round(angleHor / GameMath.PIHALF)) * GameMath.PIHALF;
                        float deg45rad = GameMath.PIHALF / 4;


                        if (Math.Abs(angleHor - rounded90degRad) >= deg45rad)
                        {
                            bect.MeshAngle = rounded90degRad + 22.5f * GameMath.DEG2RAD * Math.Sign(angleHor - rounded90degRad);
                        }
                        else
                        {
                            bect.MeshAngle = rounded90degRad;
                        }
                    }
                    if (rotatatableInterval == "22.5deg")
                    {
                        float deg22dot5rad = GameMath.PIHALF / 4;
                        float roundRad = ((int)Math.Round(angleHor / deg22dot5rad)) * deg22dot5rad;
                        bect.MeshAngle = roundRad;
                    }
                }
            }

            Core.DebugUtil.Verbose(api, "Placed manure pile of type {0} at {1}", byItemStack.Attributes.GetString("type", Props.DefaultType), blockSel.Position);
            return val;
        }

        public string GetKey(ItemStack itemstack)
        {
            string type = itemstack.Attributes.GetString("type", Props.DefaultType);
            string isFilled = itemstack.Attributes.GetString("fillState", "empty");
            string key = type + "-" + isFilled;

            return key;
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            string cacheKey = "manurepileMeshRefs" + FirstCodePart() + SubtypeInventory;
            var meshrefs = ObjectCacheUtil.GetOrCreate(capi, cacheKey, () => new Dictionary<string, MultiTextureMeshRef>());

            string key = GetKey(itemstack);

            if (!meshrefs.TryGetValue(key, out renderinfo.ModelRef))
            {
                string type = itemstack.Attributes.GetString("type", Props.DefaultType);
                string isFilled = itemstack.Attributes.GetString("fillState", "empty");

                CompositeShape cshape = Props[type].Shape;
                var rot = ShapeInventory == null ? null : new Vec3f(ShapeInventory.rotateX, ShapeInventory.rotateY, ShapeInventory.rotateZ);

                var contentStacks = GetNonEmptyContents(capi.World, itemstack);
                var contentStack = contentStacks == null || contentStacks.Length == 0 ? null : contentStacks[0];

                var mesh = GenMesh(capi, contentStack, type, isFilled, cshape, rot);
                meshrefs[key] = renderinfo.ModelRef = capi.Render.UploadMultiTextureMesh(mesh);
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;
            if (capi == null) return;

            string cacheKey = "manurepileMeshRefs" + FirstCodePart() + SubtypeInventory;
            var meshrefs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, cacheKey);

            if (meshrefs != null)
            {
                foreach (var key in meshrefs.Keys.ToList())
                {
                    if (key.StartsWith(Props.DefaultType))
                    {
                        meshrefs[key]?.Dispose();
                        meshrefs.Remove(key);
                    }
                }
            }
        }

        public Shape GetShape(ICoreClientAPI capi, string type, CompositeShape cshape)
        {
            if (cshape?.Base == null) return null;
            var tesselator = capi.Tesselator;

            tmpTextureSource = tesselator.GetTextureSource(this, 0, true);

            AssetLocation shapeloc = cshape.Base.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");
            Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, shapeloc);
            curType = type;
            return shape;
        }

        public MeshData GenMesh(ICoreClientAPI capi, ItemStack contentStack, string type, string fillStage, CompositeShape cshape, Vec3f rotation = null)
        {
            if (fillStage != "empty")
            {
                cshape = cshape.Clone();

                if (cshape.Base.Path.Contains("empty"))
                {
                    cshape.Base.Path = cshape.Base.Path.Replace("empty", fillStage);
                }
                else
                {
                    cshape.Base.Path = cshape.Base.Path.Replace("filled", fillStage);
                }
            }

            Shape shape = GetShape(capi, type, cshape);
            var tesselator = capi.Tesselator;
            if (shape == null) return new MeshData();

            curType = type;
            tesselator.TesselateShape("manurepile", shape, out MeshData mesh, this, rotation == null ? new Vec3f(Shape.rotateX, Shape.rotateY, Shape.rotateZ) : rotation);

            return mesh;
        }

        public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
        {
            BEManurePile be = world.BlockAccessor.GetBlockEntity(pos) as BEManurePile;
            if (be != null)
            {

                decalModelData.Rotate(origin, 0, be.MeshAngle, 0);
                decalModelData.Scale(origin, 15f / 16, 1f, 15f / 16);
                return;
            }

            base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack stack = new ItemStack(this);

            BEManurePile be = world.BlockAccessor.GetBlockEntity(pos) as BEManurePile;
            if (be != null)
            {
                stack.Attributes.SetString("type", be.type);
                stack.Attributes.SetString("fillState", be.preferredFillState);
            }
            else
            {
                stack.Attributes.SetString("type", Props.DefaultType);
            }

            return stack;
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            if (world.Side != EnumAppSide.Server) return;

            world.BlockAccessor.SetBlock(0, pos);

            ItemStack manureStack = new ItemStack(world.GetItem(new AssetLocation("naturalfertilizer:manure")), 32);
            world.SpawnItemEntity(manureStack, pos.ToVec3d().Add(0.5, 0.5, 0.5));

            Core.DebugUtil.Log(world.Api, "[ManurePile] Dropped 32 manure items at {0}", pos);
        }

        public override BlockDropItemStack[] GetDropsForHandbook(ItemStack handbookStack, IPlayer forPlayer)
        {
            if (Drops != null)
            {
                IEnumerable<BlockDropItemStack> drops = Array.Empty<BlockDropItemStack>();

                foreach (BlockDropItemStack drop in Drops)
                {
                    if (drop.ResolvedItemstack.Collectible is IResolvableCollectible resolvable)
                    {
                        BlockDropItemStack[] resolvableStacks = resolvable.GetDropsForHandbook(handbookStack, forPlayer);

                        drops = drops.Concat(resolvableStacks);
                    }
                    else
                    {
                        drops = drops.Append(drop);
                    }
                }
                return drops.ToArray();
            }

            return Drops;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            BEManurePile be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEManurePile;
            if (be != null && !be.IsFermenting) return be.OnBlockInteractStart(byPlayer, blockSel);

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            string type = itemStack.Attributes.GetString("type", Props.DefaultType);
            string isFilled = itemStack.Attributes.GetString("fillState", "empty");
            if (isFilled.Length == 0) isFilled = "empty";

            return Lang.GetMatching(Code?.Domain + AssetLocation.LocationSeparator + "block-" + type + "-" + Code?.Path, Lang.Get("manurepilefillstate-" + isFilled, "empty"));
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        }

        public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is BEManurePile be)
            {
                if (be.IsFermenting) return Lang.Get("Fermenting Manure Pile");
            }
            return base.GetPlacedBlockName(world, pos);
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public string GetType(IBlockAccessor blockAccessor, BlockPos pos)
        {
            BlockEntityGenericTypedContainer be = blockAccessor.GetBlockEntity(pos) as BlockEntityGenericTypedContainer;
            if (be != null)
            {
                return be.type;
            }

            return Props.DefaultType;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            BEManurePile be = world.BlockAccessor.GetBlockEntity(selection.Position) as BEManurePile;
            if (be != null && be.IsFermenting)
            {
                return new WorldInteraction[0];
            }

            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer).Append(new WorldInteraction[] {
            new WorldInteraction()
            {
                ActionLangCode = "blockhelp-manurepile-add",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "shift"
            },
            new WorldInteraction()
            {
                ActionLangCode = "blockhelp-manurepile-addall",
                MouseButton = EnumMouseButton.Right,
                HotKeyCodes = new string[] { "shift", "ctrl" }
            }
            });
        }
    }
}