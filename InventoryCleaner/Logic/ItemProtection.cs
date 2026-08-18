using System;
using InventoryCleaner.Game;
using InventoryCleaner.Models;

namespace InventoryCleaner.Logic;

/// <summary>
/// Items this plugin will never destroy, checked both when the discard grid is built and again
/// immediately before each discard is issued. Unlike the user allowlist these rules are not
/// configurable: they cover gear whose loss is unrecoverable.
/// </summary>
internal static class ItemProtection
{
    private const string UltimatePrefix = "Ultimate";

    /// <summary>
    /// Whether <paramref name="item"/> is protected from destruction, and why.
    ///
    /// Ultimate raid weapons are named "Ultimate ..." in most tiers (UCoB's Ultimate Dreadwyrm
    /// series, TEA's Ultimate Almace, TOP's Ultimate Omega), but not all: The Weapon's Refrain names
    /// its weapons "&lt;weapon&gt; Ultima". Those are caught structurally instead — a weapon that is
    /// both unique and untradable at rare quality or better is by definition not re-obtainable
    /// through any market or duplicate drop. Rare quality alone is deliberately *not* enough: plenty
    /// of ordinary junk carries it.
    /// </summary>
    internal static bool IsProtected(InventoryItem item, out string reason)
    {
        if (item.Name.StartsWith(UltimatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Ultimate raid weapon";
            return true;
        }

        var row = Sheets.ById(item.ItemId);
        if (row == null)
        {
            reason = "unknown item";
            return true;
        }

        // The game's own undiscardable flag. Attempting these would fail anyway; refusing up front
        // keeps them out of the grid instead of burning a queue slot on a dialog that never appears.
        if (row.Value.IsIndisposable)
        {
            reason = "the game does not allow discarding this";
            return true;
        }

        if (IsWeapon(item.Slot) && item.Rarity >= 3 && row.Value.IsUnique && row.Value.IsUntradable)
        {
            reason = "unique untradable weapon";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsWeapon(EquipSlot? slot)
        => slot is EquipSlot.MainHand or EquipSlot.OffHand;
}
