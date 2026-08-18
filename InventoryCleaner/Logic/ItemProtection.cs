using System;
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
    /// Whether <paramref name="item"/> is protected from destruction, and why. Two rules apply:
    ///
    /// Ultimate raid weapons are named "Ultimate ..." in most tiers (UCoB's Ultimate Dreadwyrm
    /// series, TEA's Ultimate Almace, DSR's ... of the Heavens, TOP's Ultimate Omega), but not all:
    /// The Weapon's Refrain names its weapons "&lt;weapon&gt; Ultima" (Bravura Ultima, Curtana
    /// Ultima). A name prefix alone would leave those unprotected, so any weapon of rare (blue)
    /// quality or better is protected structurally as well — that quality tier is scarce enough on
    /// weapons that over-protecting costs nothing, and it holds in every client language.
    /// </summary>
    internal static bool IsProtected(InventoryItem item, out string reason)
    {
        if (item.Name.StartsWith(UltimatePrefix, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Ultimate raid weapon";
            return true;
        }

        if (IsWeapon(item.Slot) && item.Rarity >= 3)
        {
            reason = "rare-quality weapon";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsWeapon(EquipSlot? slot)
        => slot is EquipSlot.MainHand or EquipSlot.OffHand;
}
