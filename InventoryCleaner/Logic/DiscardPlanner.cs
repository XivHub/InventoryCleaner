using System.Collections.Generic;
using InventoryCleaner.Game;
using InventoryCleaner.Models;

namespace InventoryCleaner.Logic;

/// <summary>An item withheld from the discard grid by <see cref="ItemProtection"/>, and why.</summary>
internal sealed record ProtectedItem(InventoryItem Item, string Reason);

/// <summary>
/// The discard grid's contents: what may be destroyed, and what was withheld for safety. The
/// withheld list is surfaced rather than silently dropped so a missing item is explainable.
/// </summary>
internal sealed record DiscardCandidates(List<InventoryItem> Discardable, List<ProtectedItem> Withheld);

/// <summary>
/// Read-only builder for the discard grid: every equippable main-inventory item that is safe to
/// offer for destruction. Four sets are excluded and can never reach the grid — allowlisted item
/// ids, items currently equipped, items any saved gearset references, and everything
/// <see cref="ItemProtection"/> covers — so the only way to lose a piece of gear you rely on is to
/// remove it from those first. No game writes.
/// </summary>
internal static class DiscardPlanner
{
    /// <summary>
    /// Reads fresh main-inventory, equipped and gearset state and returns the discardable items,
    /// ordered lowest ilvl first (the most likely junk sits at the top-left of the grid), alongside
    /// the items <see cref="ItemProtection"/> withheld.
    /// </summary>
    internal static DiscardCandidates Build(Configuration c)
    {
        var protectedIds = new HashSet<uint>(c.AllowlistItemIds ?? new List<uint>());

        foreach (var equipped in InventoryReader.EquippedItems())
            protectedIds.Add(equipped.ItemId);

        foreach (var gearset in GearsetReader.All())
        {
            foreach (var kv in gearset.Items)
                protectedIds.Add(kv.Value);
        }

        var discardable = new List<InventoryItem>();
        var withheld = new List<ProtectedItem>();
        foreach (var item in InventoryReader.MainInventoryItems())
        {
            if (protectedIds.Contains(item.ItemId))
                continue;

            var slot = item.Slot ?? EquipSlotExtensions.FromEquipSlotCategory(Sheets.EquipSlotCategory(item.ItemId));
            if (slot == null)
                continue;

            if (ItemProtection.IsProtected(item, out var reason))
            {
                withheld.Add(new ProtectedItem(item, reason));
                continue;
            }

            discardable.Add(item);
        }

        discardable.Sort(static (a, b) =>
        {
            var byIlvl = a.Ilvl.CompareTo(b.Ilvl);
            return byIlvl != 0 ? byIlvl : string.CompareOrdinal(a.Name, b.Name);
        });

        return new DiscardCandidates(discardable, withheld);
    }
}
