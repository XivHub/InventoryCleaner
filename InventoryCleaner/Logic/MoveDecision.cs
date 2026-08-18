using InventoryCleaner.Models;

namespace InventoryCleaner.Logic;

/// <summary>
/// Why a given armoury item was kept or moved. Drives both the classifier decision and the
/// preview-list color/reason text in <see cref="Windows.MainWindow"/>.
/// </summary>
internal enum DecisionReason
{
    /// <summary>User allowlisted item id; never moved regardless of other rules. Short-circuits step 1.</summary>
    Kept_Allowlist,

    /// <summary>The item is currently equipped in the resolved slot; never moved.</summary>
    Kept_Equipped,

    /// <summary>At least one saved gearset references this (slot, ItemId); kept to preserve sets.</summary>
    Kept_GearsetRef,

    /// <summary>Item ilvl beats what some job-compatible gearset uses for that slot; kept as a per-job upgrade.</summary>
    Kept_Upgrade,

    /// <summary>No gearset references the slot at all; moved per the aggressive empty-slot policy.</summary>
    Moved_NoGearsetRef,

    /// <summary>Reserved for the Phase 5E cross-container duplicate pass; never produced by Phase 2.</summary>
    Moved_Duplicate,

    /// <summary>Slot is used by a gearset but the item upgrades none of them (for a job that can equip it); moved as inferior.</summary>
    Moved_LowerIlvl,

    /// <summary>Reverse-mode: main-inventory item referenced by a saved gearset; moved back to armoury.</summary>
    Moved_ReverseGearsetRef,
}

/// <summary>
/// One classifier decision for an armoury item: the resolved <see cref="EquipSlot"/>, the keep/move
/// <see cref="DecisionReason"/>, and a human-readable <see cref="WhyText"/> for the preview list
/// (e.g. "equipped", "in gearset 'PLD BiS'", "ilvl 590 > gearset's 530 for Body").
/// </summary>
internal sealed record MovePlan(InventoryItem Item, EquipSlot Slot, DecisionReason Reason, string WhyText);