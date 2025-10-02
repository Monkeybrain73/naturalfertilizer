using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace fubar
{
    public class ManurePileProperties
    {
        public AssetLocation PlaceRemoveSound = new AssetLocation("sounds/walk/sludge1");
        public bool RandomizeSoundPitch;

        public string[] AcceptedAdditives = new[] { "game:grassdry", "game:thatch" };
        public int NeededAdditives = 8;
        public int FermentDuration = 36;
        public int TransferQuantity = 1;
        public int BulkTransferQuantity = 4;
        public bool CtrlKey;

        public Cuboidf CollisionBox;
        public Cuboidf SelectionBox;


        public ManurePileProperties Clone()
        {
            return new ManurePileProperties()
            {
                PlaceRemoveSound = PlaceRemoveSound,
                RandomizeSoundPitch = RandomizeSoundPitch,
                AcceptedAdditives = AcceptedAdditives,
                NeededAdditives = NeededAdditives,
                FermentDuration = FermentDuration,
                TransferQuantity = TransferQuantity,
                BulkTransferQuantity = BulkTransferQuantity,
                CollisionBox = CollisionBox,
                SelectionBox = SelectionBox,
                CtrlKey = CtrlKey,
            };
        }
    }

    public class CollectibleBehaviorManurePile : CollectibleBehavior
    {

        private int maxAdditives;

        public ManurePileProperties ManurePileProps
        {
            get;
            protected set;
        }

        public CollectibleBehaviorManurePile(CollectibleObject collObj) : base(collObj) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            maxAdditives = properties["neededAdditives"].AsInt(8);  // default 8 if not set
            ManurePileProps = properties.AsObject<ManurePileProperties>(null, collObj.Code.Domain);
        }

        public override void OnHeldInteractStart(
            ItemSlot itemslot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handHandling,
            ref EnumHandling handling
        )
        {
            if (blockSel == null || !byEntity.Controls.ShiftKey) return;

            IWorldAccessor world = byEntity.World;
            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
            if (byPlayer == null) return;

            // Check land claim
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return;
            }

            BlockPos pos = blockSel.Position.AddCopy(blockSel.Face);
            Block blockAt = world.BlockAccessor.GetBlock(pos);

            // If space is not empty, don't place
            if (blockAt.Replaceable < 6000) return;

            // Place the manure pile block
            AssetLocation pileLoc = new AssetLocation("naturalfertilizer:manurepile-0");
            Block pileBlock = world.GetBlock(pileLoc);
            if (pileBlock == null) return;

            world.BlockAccessor.SetBlock(pileBlock.BlockId, pos);

            // Initialize block entity
            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityManurePile;
            if (be != null)
            {
                int transfer = Math.Min(itemslot.StackSize, maxAdditives);
                be.SetInitialManure(transfer);

                itemslot.TakeOut(transfer);
                itemslot.MarkDirty();
            }

            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventSubsequent;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            handling = EnumHandling.PassThrough;
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    HotKeyCodes = new string[] { "shift" },
                    ActionLangCode = "heldhelp-place",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }
    }
}
