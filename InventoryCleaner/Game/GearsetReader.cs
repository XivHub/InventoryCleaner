using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using InventoryCleaner.Models;

namespace InventoryCleaner.Game;

/// <summary>
/// Read-only iteration over the player's saved gearsets via
/// <see cref="RaptureGearsetModule"/>. All methods hit live game state; callers should consume
/// the results within a frame.
/// </summary>
internal static class GearsetReader
{
    /// <summary>
    /// Every existing saved gearset, skipping entries that lack
    /// <see cref="RaptureGearsetModule.GearsetFlag.Exists"/>. Each result's
    /// <see cref="GearsetEntry.Items"/> contains only slots whose <c>ItemId</c> is non-zero.
    /// </summary>
    internal static IEnumerable<GearsetEntry> All()
    {
        var result = new List<GearsetEntry>();
        AppendAll(result);
        return result;
    }

    private static unsafe void AppendAll(List<GearsetEntry> sink)
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null)
            return;
        foreach (ref var gs in module->Entries)
        {
            if (!gs.Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                continue;

            var items = new Dictionary<EquipSlot, uint>();
            var span = gs.Items;
            for (var i = 0; i < span.Length && i <= 13; i++)
            {
                // Gearset item ids carry the HQ flag as a +1,000,000 offset (base id =
                // ItemId % 1,000,000, HQ = ItemId >= 1,000,000), whereas inventory ItemIds are
                // the base id with HQ tracked separately. Normalize to the base id so gearset
                // refs match inventory/armoury items (clean keep-check, reverse, ilvl lookup).
                var itemId = span[i].ItemId % 1000000;
                if (itemId != 0)
                    items[(EquipSlot)i] = itemId;
            }

            sink.Add(new GearsetEntry(
                gs.Id,
                gs.NameString,
                gs.ClassJob,
                items));
        }
    }
}

/// <summary>Read-only view of one saved gearset and the items it references.</summary>
internal sealed record GearsetEntry
{
    /// <summary>Gearset id (matches <see cref="RaptureGearsetModule.GearsetEntry.Id"/>).</summary>
    public readonly uint Id;

    /// <summary>Gearset display name.</summary>
    public readonly string Name;

    /// <summary>Class/job id the gearset is bound to.</summary>
    public readonly uint ClassJob;

    /// <summary>
    /// Per-slot referenced item ids, HQ offset stripped (base id = raw % 1,000,000); only slots the
    /// gearset actually references (ItemId != 0). Base ids so they match inventory/armoury ItemIds.
    /// </summary>
    public readonly Dictionary<EquipSlot, uint> Items;

    internal GearsetEntry(uint id, string name, uint classJob, Dictionary<EquipSlot, uint> items)
    {
        this.Id = id;
        this.Name = name;
        this.ClassJob = classJob;
        this.Items = items;
    }
}