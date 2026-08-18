using System.Collections.Generic;
using InventoryCleaner.Game;
using InventoryCleaner.Models;

namespace InventoryCleaner.Logic;

/// <summary>
/// Read-only planner that reverses <see cref="CleanPlanner"/>: for every main-inventory item that
/// a saved gearset references (slot, ItemId), emits a <see cref="MovePlan"/> moving it back into
/// the matching armoury container. Allowlisted item ids are skipped (the safety valve still
/// holds). No game writes.
/// </summary>
internal static class ReversePlanner
{
    /// <summary>
    /// Reads fresh main-inventory + gearset state and emits one move per gearset-referenced,
    /// non-allowlisted main-inventory item. Items not referenced by any gearset are skipped (not
    /// kept-as-a-plan; they simply do not appear).
    /// </summary>
    internal static List<MovePlan> Plan(Configuration c)
    {
        var gearsetRefs = BuildGearsetRefs(GearsetReader.All());
        var allowlist = c.AllowlistItemIds;

        var plans = new List<MovePlan>();
        foreach (var item in InventoryReader.MainInventoryItems())
        {
            var slot = item.Slot ?? ResolveSlot(item.ItemId);
            if (slot == null)
                continue;

            if (allowlist != null && allowlist.Contains(item.ItemId))
                continue;

            var slotsToCheck = SlotsForCategory(slot.Value);
            string? gearsetName = null;
            foreach (var s in slotsToCheck)
            {
                if (gearsetRefs.TryGetValue((s, item.ItemId), out var name))
                {
                    gearsetName = name;
                    break;
                }
            }

            if (gearsetName == null)
                continue;

            plans.Add(new MovePlan(
                item,
                slot.Value,
                DecisionReason.Moved_ReverseGearsetRef,
                $"reverse: in gearset '{gearsetName}'"));
        }

        return plans;
    }

    private static EquipSlot? ResolveSlot(uint itemId)
        => EquipSlotExtensions.FromEquipSlotCategory(Sheets.EquipSlotCategory(itemId));

    private static IReadOnlyList<EquipSlot> SlotsForCategory(EquipSlot slot)
        => slot == EquipSlot.Ring1 ? new[] { EquipSlot.Ring1, EquipSlot.Ring2 } : new[] { slot };

    private static Dictionary<(EquipSlot, uint), string> BuildGearsetRefs(IEnumerable<GearsetEntry> gearsets)
    {
        var dict = new Dictionary<(EquipSlot, uint), string>();
        foreach (var gs in gearsets)
        {
            foreach (var kv in gs.Items)
            {
                var key = (kv.Key, kv.Value);
                if (!dict.ContainsKey(key))
                    dict[key] = gs.Name;
            }
        }

        return dict;
    }
}