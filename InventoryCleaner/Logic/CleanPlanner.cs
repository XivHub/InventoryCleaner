using System;
using System.Collections.Generic;
using System.Linq;
using InventoryCleaner.Game;
using InventoryCleaner.Models;

namespace InventoryCleaner.Logic;

/// <summary>
/// Pure read-only classifier over live armoury state. Produces a <see cref="MovePlan"/> per armoury
/// item: keep if it is equipped, referenced by a saved gearset, or a higher-ilvl upgrade for a slot
/// some gearset uses; otherwise move it to the main inventory. No game writes.
/// </summary>
internal static class CleanPlanner
{
    /// <summary>
    /// Reads fresh inventory + gearset state and classifies every armoury item. The returned list
    /// length equals the number of equippable armoury items; non-equippable items are skipped.
    /// </summary>
    internal static List<MovePlan> Plan() => Plan(Plugin.C);

    /// <summary>
    /// As <see cref="Plan()"/> but reads the keep/move toggles off <paramref name="c"/> so the UI
    /// can relax the classifier without code changes. When a keep toggle is off, the item falls
    /// through to the next decision step; when <see cref="Configuration.MoveNoGearsetRef"/> is off,
    /// items that would be <see cref="DecisionReason.Moved_NoGearsetRef"/> are skipped entirely.
    /// </summary>
    internal static List<MovePlan> Plan(Configuration c)
    {
        var equipped = InventoryReader.EquippedItems();
        var gearsets = GearsetReader.All();
        var armoury = InventoryReader.ArmouryItems();

        var equippedBySlot = BuildEquippedBySlot(equipped);
        var gearsetRefs = BuildGearsetRefs(gearsets);
        var referencedSlots = BuildReferencedSlots(gearsets);

        // Upgrades are only kept for gearset jobs the user left enabled; equipped items and exact
        // gearset references (above) ignore this filter. Disabling a job here makes its
        // higher-ilvl pieces eligible to move like any non-upgrade.
        var disabledJobs = new HashSet<uint>(c.DisabledUpgradeJobs ?? Enumerable.Empty<uint>());
        var gearsetSlots = BuildGearsetSlots(gearsets)
            .Where(g => !disabledJobs.Contains(g.ClassJob))
            .ToList();

        var plans = new List<MovePlan>();
        foreach (var item in armoury)
        {
            var slot = item.Slot ?? ResolveSlot(item.ItemId);
            if (slot == null)
                continue;

            var slotsToCheck = SlotsForCategory(slot.Value);
            var plan = Decide(item, slot.Value, slotsToCheck, equippedBySlot, gearsetRefs, gearsetSlots, referencedSlots, c);
            if (plan != null)
                plans.Add(plan);
        }

        return OrderByContainerFullness(plans);
    }

    /// <summary>
    /// Orders plans so the fullest armoury container is drained first, and within one container the
    /// lowest-ilvl piece goes first. The main inventory is the limiting resource: if it fills before
    /// the queue drains, the moves that did land are the ones that bought back space where the
    /// armoury was tightest.
    /// </summary>
    private static List<MovePlan> OrderByContainerFullness(List<MovePlan> plans)
    {
        var occupancy = InventoryReader.ArmouryOccupancy();
        return plans
            .OrderByDescending(p => occupancy.TryGetValue(p.Item.Container, out var fill) ? fill : 0f)
            .ThenBy(p => (int)p.Item.Container)
            .ThenBy(p => p.Item.Ilvl)
            .ThenBy(p => p.Item.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static EquipSlot? ResolveSlot(uint itemId)
        => EquipSlotExtensions.FromEquipSlotCategory(Sheets.EquipSlotCategory(itemId));

    /// <summary>
    /// Slots to consider for keep/move matching. Ring items (EquipSlotCategory 12) resolve to
    /// Ring1 but are valid in Ring2 as well, so we check both. Every other slot maps 1:1.
    /// </summary>
    private static IReadOnlyList<EquipSlot> SlotsForCategory(EquipSlot slot)
        => slot == EquipSlot.Ring1 ? new[] { EquipSlot.Ring1, EquipSlot.Ring2 } : new[] { slot };

    private static MovePlan? Decide(
        InventoryItem item,
        EquipSlot primarySlot,
        IReadOnlyList<EquipSlot> slotsToCheck,
        Dictionary<EquipSlot, uint> equippedBySlot,
        Dictionary<(EquipSlot, uint), string> gearsetRefs,
        IReadOnlyList<GearsetSlotIlvls> gearsetSlots,
        HashSet<EquipSlot> referencedSlots,
        Configuration c)
    {
        var allowlist = c.AllowlistItemIds;
        if (allowlist != null && allowlist.Contains(item.ItemId))
            return new MovePlan(item, primarySlot, DecisionReason.Kept_Allowlist, "allowlist");

        if (c.KeepEquipped)
        {
            foreach (var slot in slotsToCheck)
            {
                if (equippedBySlot.TryGetValue(slot, out var equippedId) && equippedId == item.ItemId)
                    return new MovePlan(item, primarySlot, DecisionReason.Kept_Equipped, "equipped");
            }
        }

        if (c.KeepGearsetRef)
        {
            foreach (var slot in slotsToCheck)
            {
                if (gearsetRefs.TryGetValue((slot, item.ItemId), out var gearsetName))
                    return new MovePlan(
                        item,
                        primarySlot,
                        DecisionReason.Kept_GearsetRef,
                        $"in gearset '{gearsetName}'");
            }
        }

        // Upgrade: keep if the item is a higher-ilvl piece than what at least one gearset already
        // uses in that slot, AND that gearset's job can actually equip the item. Compared per
        // gearset (not against a global max) so an upgrade for a lower-ilvl job is still kept.
        var margin = c.UpgradeMarginIlvl < 0 ? 0 : c.UpgradeMarginIlvl;
        if (c.KeepUpgrade)
        {
            foreach (var gs in gearsetSlots)
            {
                if (!Sheets.CanJobEquip(item.ItemId, gs.ClassJob))
                    continue;

                foreach (var slot in slotsToCheck)
                {
                    if (gs.SlotIlvls.TryGetValue(slot, out var gsIlvl) && item.Ilvl > gsIlvl + margin)
                        return new MovePlan(
                            item,
                            primarySlot,
                            DecisionReason.Kept_Upgrade,
                            $"ilvl {item.Ilvl} > {gs.Name}'s {gsIlvl} for {primarySlot}");
                }
            }
        }

        var slotReferenced = false;
        foreach (var slot in slotsToCheck)
        {
            if (referencedSlots.Contains(slot))
            {
                slotReferenced = true;
                break;
            }
        }

        if (!slotReferenced)
        {
            if (!c.MoveNoGearsetRef)
                return null;
            return new MovePlan(
                item,
                primarySlot,
                DecisionReason.Moved_NoGearsetRef,
                "no gearset uses this slot");
        }

        return new MovePlan(
            item,
            primarySlot,
            DecisionReason.Moved_LowerIlvl,
            $"ilvl {item.Ilvl}: not an upgrade for any gearset using {primarySlot}");
    }

    private static Dictionary<EquipSlot, uint> BuildEquippedBySlot(IEnumerable<InventoryItem> equipped)
    {
        var dict = new Dictionary<EquipSlot, uint>();
        foreach (var e in equipped)
        {
            if (e.Slot is EquipSlot s)
                dict[s] = e.ItemId;
        }

        return dict;
    }

    /// <summary>Maps (slot, ItemId) → the first gearset name that references that pair.</summary>
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

    /// <summary>
    /// Per-gearset slot ilvls: for each gearset, the ilvl of the item it references in each slot.
    /// Keeps gearsets separate (and tagged with their job) so the upgrade check can compare an
    /// armoury item against each job's gear individually rather than a global max.
    /// </summary>
    private static List<GearsetSlotIlvls> BuildGearsetSlots(IEnumerable<GearsetEntry> gearsets)
    {
        var list = new List<GearsetSlotIlvls>();
        foreach (var gs in gearsets)
        {
            var slotIlvls = new Dictionary<EquipSlot, ushort>();
            foreach (var kv in gs.Items)
                slotIlvls[kv.Key] = Sheets.Ilvl(kv.Value);
            list.Add(new GearsetSlotIlvls(gs.Name, gs.ClassJob, slotIlvls));
        }

        return list;
    }

    /// <summary>The set of slots referenced by at least one gearset (job-agnostic).</summary>
    private static HashSet<EquipSlot> BuildReferencedSlots(IEnumerable<GearsetEntry> gearsets)
    {
        var set = new HashSet<EquipSlot>();
        foreach (var gs in gearsets)
            foreach (var kv in gs.Items)
                set.Add(kv.Key);

        return set;
    }
}

/// <summary>One gearset's per-slot ilvls plus the job it is bound to, for the upgrade comparison.</summary>
internal sealed record GearsetSlotIlvls(string Name, uint ClassJob, Dictionary<EquipSlot, ushort> SlotIlvls);