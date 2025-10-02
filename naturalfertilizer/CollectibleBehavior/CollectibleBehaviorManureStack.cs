﻿using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace naturalfertilizer
{
    public enum EnumManureStackLayout
    {
        /// <summary>
        /// A generic stack of items
        /// </summary>
        Stacking
    }


    public class ManureStackProperties
    {
        public EnumManureStackLayout Layout = EnumManureStackLayout.Stacking;
        public AssetLocation PlaceRemoveSound = new AssetLocation("sounds/walk/sludge1");
        public bool RandomizeSoundPitch;
        public bool RandomizeCenterRotation;
        public AssetLocation StackingModel;

        public float ModelItemsToStackSizeRatio = 1;
        public Dictionary<string, AssetLocation> StackingTextures;
        public int MaxStackingHeight = -1;
        public int StackingCapacity = 1;
        public int TransferQuantity = 1;
        public int BulkTransferQuantity = 4;
        public bool CtrlKey;
        public bool UpSolid = false;

        public Cuboidf CollisionBox;
        public Cuboidf SelectionBox;
        public float CbScaleYByLayer = 0;


        public ManureStackProperties Clone()
        {
            return new ManureStackProperties()
            {
                Layout = Layout,
                PlaceRemoveSound = PlaceRemoveSound,
                RandomizeSoundPitch = RandomizeSoundPitch,
                RandomizeCenterRotation = RandomizeCenterRotation,
                StackingCapacity = StackingCapacity,
                StackingModel = StackingModel,
                StackingTextures = StackingTextures,
                MaxStackingHeight = MaxStackingHeight,
                TransferQuantity = TransferQuantity,
                BulkTransferQuantity = BulkTransferQuantity,
                CollisionBox = CollisionBox,
                SelectionBox = SelectionBox,
                CbScaleYByLayer = CbScaleYByLayer,
                CtrlKey = CtrlKey,
                UpSolid = UpSolid
            };
        }
    }


    public class CollectibleBehaviorManureStack : CollectibleBehavior
    {
        public ManureStackProperties ManureStackProps
        {
            get;
            protected set;
        }

        public CollectibleBehaviorManureStack(CollectibleObject collObj) : base(collObj)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            ManureStackProps = properties.AsObject<ManureStackProperties>(null, collObj.Code.Domain);
        }


        public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            Interact(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);
        }


        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    HotKeyCodes = ManureStackProps.CtrlKey ? new string[] {"ctrl", "shift" } : new string[] {"shift"},
                    ActionLangCode = "heldhelp-place",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }



        public static void Interact(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            IWorldAccessor world = byEntity?.World;

            if (blockSel == null || world == null || !byEntity.Controls.ShiftKey) return;


            IPlayer byPlayer = null;
            if (byEntity is EntityPlayer) byPlayer = world.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
            if (byPlayer == null) return;

            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                itemslot.MarkDirty();
                world.BlockAccessor.MarkBlockDirty(blockSel.Position.UpCopy());
                return;
            }

            BlockManureStack blockgs = world.GetBlock(new AssetLocation("naturalfertilizer:manurestack")) as BlockManureStack;
            if (blockgs == null) return;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            BlockEntity beAbove = world.BlockAccessor.GetBlockEntity(blockSel.Position.UpCopy());
            if (be is BlockEntityManureStack || beAbove is BlockEntityManureStack)
            {
                if (((be as BlockEntityManureStack) ?? (beAbove as BlockEntityManureStack)).OnPlayerInteractStart(byPlayer, blockSel))
                {
                    handHandling = EnumHandHandling.PreventDefault;
                }
                return;
            }

            // Must be aiming at the up face
            if (blockSel.Face != BlockFacing.UP) return;
            Block onBlock = world.BlockAccessor.GetBlock(blockSel.Position);

            // Must have a support below
            if (!onBlock.CanAttachBlockAt(world.BlockAccessor, blockgs, blockSel.Position, BlockFacing.UP))
            {
                return;
            }

            // Must have empty space above
            BlockPos pos = blockSel.Position.AddCopy(blockSel.Face);
            if (world.BlockAccessor.GetBlock(pos).Replaceable < 6000) return;


            if (blockgs.CreateStorage(byEntity.World, blockSel, byPlayer))
            {
                handHandling = EnumHandHandling.PreventDefault;
                handling = EnumHandling.PreventSubsequent;
            }
        }



    }
}
