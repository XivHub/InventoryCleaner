using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace InventoryCleaner.Models
{
    /// <summary>
    /// Equipment slot index, mirroring <see cref="RaptureGearsetModule.GearsetItemIndex"/>
    /// (MainHand=0 ... SoulStone=13). Matches the <see cref="FFXIVClientStructs.FFXIV.Client.Game.InventoryType.EquippedItems"/>
    /// slot layout and the per-slot index into <see cref="RaptureGearsetModule.GearsetEntry.Items"/>.
    /// </summary>
    public enum EquipSlot : byte
    {
        MainHand = 0,
        OffHand = 1,
        Head = 2,
        Body = 3,
        Hands = 4,
        Waist = 5,
        Legs = 6,
        Feet = 7,
        Ear = 8,
        Neck = 9,
        Wrist = 10,
        Ring1 = 11,
        Ring2 = 12,
        SoulStone = 13,
    }

    /// <summary>Helpers for <see cref="EquipSlot"/>.</summary>
    public static class EquipSlotExtensions
    {
        /// <summary>
        /// Maps an <see cref="Lumina.Excel.Sheets.Item.EquipSlotCategory"/> RowId to the equipment slot it
        /// goes into, per the in-game category convention (verified against Questionable
        /// <c>EquipItem.GetEquipSlot</c>): 1-11 → RowId-1 (head/body/.../wrist), 12 → rings (Ring1 first),
        /// 13 → MainHand, 17 → SoulStone. Returns null for non-equippable categories (0, 14-16, 18+).
        /// </summary>
        /// <remarks>
        /// For rings (RowId 12) two slots are valid; this returns <see cref="EquipSlot.Ring1"/> as the
        /// primary. Callers that need to consider both ring slots (the keep-check, the move planner)
        /// must handle Ring2 explicitly.
        /// </remarks>
        public static EquipSlot? FromEquipSlotCategory(byte rowId) => rowId switch
        {
            >= 1 and <= 11 => (EquipSlot)(rowId - 1),
            12 => EquipSlot.Ring1,
            13 => EquipSlot.MainHand,
            17 => EquipSlot.SoulStone,
            _ => null,
        };

        /// <summary>Readable name for a container, for headings and row provenance.</summary>
        public static string ContainerName(InventoryType container) => container switch
        {
            InventoryType.ArmoryMainHand => "Main Hand",
            InventoryType.ArmoryOffHand => "Off Hand",
            InventoryType.ArmoryHead => "Head",
            InventoryType.ArmoryBody => "Body",
            InventoryType.ArmoryHands => "Hands",
            InventoryType.ArmoryLegs => "Legs",
            InventoryType.ArmoryFeets => "Feet",
            InventoryType.ArmoryEar => "Earrings",
            InventoryType.ArmoryNeck => "Necklaces",
            InventoryType.ArmoryWrist => "Bracelets",
            InventoryType.ArmoryRings => "Rings",
            InventoryType.ArmorySoulCrystal => "Soul Crystals",
            InventoryType.Inventory1 => "Bag 1",
            InventoryType.Inventory2 => "Bag 2",
            InventoryType.Inventory3 => "Bag 3",
            InventoryType.Inventory4 => "Bag 4",
            _ => container.ToString(),
        };

        /// <summary>
        /// The armoury container that holds items for <paramref name="slot"/>. Ring1 and Ring2 both
        /// map to <see cref="InventoryType.ArmoryRings"/> (the ring container is shared).
        /// </summary>
        public static InventoryType ToArmouryContainer(EquipSlot slot) => slot switch
        {
            EquipSlot.MainHand => InventoryType.ArmoryMainHand,
            EquipSlot.OffHand => InventoryType.ArmoryOffHand,
            EquipSlot.Head => InventoryType.ArmoryHead,
            EquipSlot.Body => InventoryType.ArmoryBody,
            EquipSlot.Hands => InventoryType.ArmoryHands,
            EquipSlot.Waist => InventoryType.ArmoryWaist,
            EquipSlot.Legs => InventoryType.ArmoryLegs,
            EquipSlot.Feet => InventoryType.ArmoryFeets,
            EquipSlot.Ear => InventoryType.ArmoryEar,
            EquipSlot.Neck => InventoryType.ArmoryNeck,
            EquipSlot.Wrist => InventoryType.ArmoryWrist,
            EquipSlot.Ring1 => InventoryType.ArmoryRings,
            EquipSlot.Ring2 => InventoryType.ArmoryRings,
            EquipSlot.SoulStone => InventoryType.ArmorySoulCrystal,
            _ => throw new System.ArgumentOutOfRangeException(nameof(slot), slot, "Unknown EquipSlot"),
        };
    }
}