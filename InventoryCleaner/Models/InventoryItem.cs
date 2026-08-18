using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using InventoryCleaner.Game;
using XivHubPluginKit.Inventory;
using GameInventoryItem = FFXIVClientStructs.FFXIV.Client.Game.InventoryItem;

namespace InventoryCleaner.Models;

/// <summary>
/// Lightweight managed view of a single inventory slot, built from a live
/// <see cref="FFXIVClientStructs.FFXIV.Client.Game.InventoryItem"/> plus the matching Lumina
/// <see cref="Item"/> row. Holds no game pointers; safe to retain across frames.
/// </summary>
internal sealed class InventoryItem
{
    /// <summary>The item's row id in the <c>Item</c> sheet; 0 means an empty slot was not yielded.</summary>
    public readonly uint ItemId;

    /// <summary>Equipment slot resolved from <see cref="Item.EquipSlotCategory"/>, null if non-equippable.</summary>
    public readonly EquipSlot? Slot;

    /// <summary>The container this item lives in (e.g. <see cref="InventoryType.ArmoryMainHand"/>).</summary>
    public readonly InventoryType Container;

    /// <summary>0-based index into the container's <c>Items</c> array.</summary>
    public readonly int SlotIndex;

    /// <summary>Item level from <see cref="Item.LevelItem"/> RowId.</summary>
    public readonly ushort Ilvl;

    /// <summary>Equip level from <see cref="Item.LevelEquip"/>.</summary>
    public readonly byte EquipLevel;

    /// <summary>Item name from the Lumina sheet.</summary>
    public readonly string Name;

    /// <summary>Rarity from <see cref="Item.Rarity"/>.</summary>
    public readonly byte Rarity;

    /// <summary>Whether the in-memory item is the high-quality variant.</summary>
    public readonly bool IsHq;

    /// <summary>Spiritbond percent (0-100) from <c>GameInventoryItem.SpiritbondOrCollectability</c>.</summary>
    public readonly byte Spiritbond;

    private InventoryItem(
        uint itemId,
        EquipSlot? slot,
        InventoryType container,
        int slotIndex,
        ushort ilvl,
        byte equipLevel,
        string name,
        byte rarity,
        bool isHq,
        byte spiritbond)
    {
        this.ItemId = itemId;
        this.Slot = slot;
        this.Container = container;
        this.SlotIndex = slotIndex;
        this.Ilvl = ilvl;
        this.EquipLevel = equipLevel;
        this.Name = name;
        this.Rarity = rarity;
        this.IsHq = isHq;
        this.Spiritbond = spiritbond;
    }

    /// <summary>
    /// Builds an <see cref="InventoryItem"/> from a live inventory slot pointer and its resolved
    /// Lumina <see cref="Item"/> row. Returns null if <paramref name="ptr"/>'s <c>ItemId</c> is 0
    /// (empty slot) — callers should skip nulls rather than enrolling empties.
    /// </summary>
    internal static unsafe InventoryItem? FromGameItem(GameInventoryItem* ptr, Item? itemRow)
    {
        if (ptr == null || ptr->ItemId == 0)
            return null;

        var ilvl = itemRow.HasValue ? (ushort)itemRow.Value.LevelItem.RowId : (ushort)0;
        var equipLevel = itemRow.HasValue ? itemRow.Value.LevelEquip : (byte)0;
        var name = itemRow.HasValue ? itemRow.Value.Name.ToString() : string.Empty;
        var rarity = itemRow.HasValue ? itemRow.Value.Rarity : (byte)0;
        var slot = itemRow.HasValue
            ? EquipSlotExtensions.FromEquipSlotCategory((byte)itemRow.Value.EquipSlotCategory.RowId)
            : null;
        var isHq = ptr->IsHighQuality();
        var spiritbond = (byte)ptr->SpiritbondOrCollectability;

        return new InventoryItem(
            ptr->ItemId,
            slot,
            ptr->Container,
            ptr->Slot,
            ilvl,
            equipLevel,
            name,
            rarity,
            isHq,
            spiritbond);
    }

    /// <summary>
    /// Builds an <see cref="InventoryItem"/> from a kit <see cref="SlotView"/> (produced by
    /// <see cref="XivHubPluginKit.Inventory.InventoryScan.ScanContainer"/>), used by
    /// <see cref="InventoryReader"/>'s thin wrappers over the kit scan. <see cref="SlotView"/> does
    /// not carry <see cref="EquipLevel"/>/<see cref="Rarity"/>/<see cref="Slot"/>, so those are
    /// resolved from the <c>Item</c> row via <see cref="Sheets.ById"/>, mirroring
    /// <see cref="FromGameItem"/>. Returns null if <paramref name="view"/>'s <c>ItemId</c> is 0.
    /// </summary>
    internal static InventoryItem? FromSlotView(SlotView view)
    {
        if (view.ItemId == 0)
            return null;

        Item? itemRow = Sheets.ById(view.ItemId);
        var equipLevel = itemRow.HasValue ? itemRow.Value.LevelEquip : (byte)0;
        var rarity = itemRow.HasValue ? itemRow.Value.Rarity : (byte)0;
        var slot = itemRow.HasValue
            ? EquipSlotExtensions.FromEquipSlotCategory((byte)itemRow.Value.EquipSlotCategory.RowId)
            : null;

        return new InventoryItem(
            view.ItemId,
            slot,
            view.Container,
            view.SlotIndex,
            view.Ilvl,
            equipLevel,
            view.Name,
            rarity,
            view.IsHq,
            view.Spiritbond);
    }
}