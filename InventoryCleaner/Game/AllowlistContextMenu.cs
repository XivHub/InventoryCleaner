using System;
using Dalamud.Game.Gui.ContextMenu;
using InventoryCleaner.Models;

namespace InventoryCleaner.Game;

/// <summary>
/// Adds a right-click "InventoryCleaner allowlist" toggle to the inventory / Armoury Chest context
/// menu. Clicking adds the item to <see cref="Configuration.AllowlistItemIds"/> (never moved) or
/// removes it if already present. Only shown for equippable gear.
/// </summary>
internal sealed class AllowlistContextMenu : IDisposable
{
    private readonly Configuration cfg;

    internal AllowlistContextMenu(Configuration configuration)
    {
        cfg = configuration;
        Plugin.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    public void Dispose() => Plugin.ContextMenu.OnMenuOpened -= OnMenuOpened;

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Inventory
            || args.Target is not MenuTargetInventory inv
            || inv.TargetItem is null)
            return;

        // Strip the HQ +1,000,000 offset so the id matches our config / sheet lookups.
        var id = inv.TargetItem.Value.ItemId % 1_000_000;
        if (id == 0)
            return;

        // Only offer the toggle on actual gear — the allowlist exists for the slot/upgrade rules.
        if (EquipSlotExtensions.FromEquipSlotCategory(Sheets.EquipSlotCategory(id)) is null)
            return;

        var allowlisted = cfg.AllowlistItemIds.Contains(id);
        args.AddMenuItem(new MenuItem
        {
            Name = allowlisted
                ? "Remove from InventoryCleaner allowlist"
                : "Add to InventoryCleaner allowlist",
            PrefixChar = 'C',
            PrefixColor = 541,
            OnClicked = _ => Toggle(id),
        });
    }

    private void Toggle(uint itemId)
    {
        var name = Sheets.Name(itemId);
        var label = name.Length > 0 ? name : $"item {itemId}";
        if (cfg.AllowlistItemIds.Remove(itemId))
            Plugin.ChatGui.Print($"[InventoryCleaner] Removed {label} from the allowlist.");
        else
        {
            cfg.AllowlistItemIds.Add(itemId);
            Plugin.ChatGui.Print($"[InventoryCleaner] Added {label} to the allowlist (never moved).");
        }

        cfg.Save();
    }
}
