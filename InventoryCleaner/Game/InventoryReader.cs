using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using ZhyraPluginKit.Inventory;
using InventoryItem = InventoryCleaner.Models.InventoryItem;

namespace InventoryCleaner.Game;

/// <summary>
/// Read-only iteration over the player's inventory containers. Thin wrappers over the shared
/// <see cref="InventoryScan"/> (all methods ultimately hit live game state via
/// <see cref="InventoryManager"/>); callers should consume the results within a frame.
/// </summary>
internal static class InventoryReader
{
    private static readonly InventoryType[] ArmouryContainers =
    {
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryWaist,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
    };

    private static readonly InventoryType[] MainInventoryContainers =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    /// <summary>Every non-empty slot across all 12 <c>Armory*</c> containers.</summary>
    internal static IEnumerable<InventoryItem> ArmouryItems() => ScanContainers(ArmouryContainers);

    /// <summary>Every non-empty slot of <see cref="InventoryType.EquippedItems"/> (slots 0-13).</summary>
    internal static IEnumerable<InventoryItem> EquippedItems()
    {
        var result = new List<InventoryItem>();
        foreach (var view in InventoryScan.ScanContainer(InventoryType.EquippedItems))
        {
            if (view.SlotIndex >= 14)
                continue;
            var model = InventoryItem.FromSlotView(view);
            if (model != null)
                result.Add(model);
        }
        return result;
    }

    /// <summary>Every non-empty slot across <see cref="InventoryType.Inventory1"/>..<c>Inventory4</c>.</summary>
    internal static IEnumerable<InventoryItem> MainInventoryItems() => ScanContainers(MainInventoryContainers);

    /// <summary>
    /// Fill ratio (occupied slots / container size) of every armoury container, keyed by container.
    /// Containers that report no size are omitted. Drives the fullest-container-first move order:
    /// emptying the tightest container first is what actually buys back armoury space when the main
    /// inventory runs out mid-run.
    /// </summary>
    internal static Dictionary<InventoryType, float> ArmouryOccupancy()
    {
        var result = new Dictionary<InventoryType, float>();
        foreach (var type in ArmouryContainers)
        {
            var (used, size) = InventoryScan.ContainerUsage(type);
            if (size > 0)
                result[type] = (float)used / size;
        }

        return result;
    }

    /// <summary>Free slots in the main inventory. Uses <see cref="InventoryScan.FreeSlotsInBag"/>.</summary>
    internal static int FreeMainInventorySlots() => InventoryScan.FreeSlotsInBag();

    private static IEnumerable<InventoryItem> ScanContainers(InventoryType[] types)
    {
        var result = new List<InventoryItem>();
        foreach (var type in types)
        {
            foreach (var view in InventoryScan.ScanContainer(type))
            {
                var model = InventoryItem.FromSlotView(view);
                if (model != null)
                    result.Add(model);
            }
        }
        return result;
    }
}
