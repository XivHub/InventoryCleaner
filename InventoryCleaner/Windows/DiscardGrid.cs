using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using InventoryCleaner.Game;
using InventoryCleaner.Models;
using InventoryItem = InventoryCleaner.Models.InventoryItem;

namespace InventoryCleaner.Windows;

/// <summary>
/// The discard picker's item grid: one square cell per candidate, painted with the item's game icon
/// over a frame tinted in its rarity color (white common, green uncommon, blue rare, purple relic,
/// pink aetherial), so rarity is readable at a glance without hovering. Left-click toggles a cell
/// into the caller's selection set, which is keyed by (container, slot) — the same identity the
/// discard executor re-verifies against live game state.
/// </summary>
internal static class DiscardGrid
{
    private const float SlotRounding = 3f;
    private const float Gap = 4f;

    /// <summary>
    /// Draws the grid of <paramref name="items"/> into the available content region, toggling
    /// membership in <paramref name="selection"/> on click. <paramref name="cellScale"/> is in text
    /// line heights (clamped 2-8), so cells track the user's font size.
    /// </summary>
    internal static void Draw(
        IReadOnlyList<InventoryItem> items,
        HashSet<(InventoryType Container, int Slot)> selection,
        float cellScale,
        Vector2 size)
    {
        using var child = ImRaii.Child("###InventoryCleanerDiscardGrid", size, true);
        if (!child)
            return;

        if (items.Count == 0)
        {
            ImGui.TextDisabled("Nothing discardable: every gear item in your bags is equipped, in a gearset, or allowlisted.");
            return;
        }

        var cell = ImGui.GetTextLineHeight() * Math.Clamp(cellScale, 2f, 8f);
        var perRow = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + Gap) / (cell + Gap)));

        for (var i = 0; i < items.Count; i++)
        {
            if (i % perRow != 0)
                ImGui.SameLine(0f, Gap);
            DrawCell(items[i], selection, cell);
        }
    }

    private static void DrawCell(
        InventoryItem item,
        HashSet<(InventoryType Container, int Slot)> selection,
        float cell)
    {
        var key = (item.Container, item.SlotIndex);
        var isSelected = selection.Contains(key);

        using var cellId = ImRaii.PushId((int)item.Container * 1000 + item.SlotIndex);

        ImGui.InvisibleButton("##cell", new Vector2(cell, cell));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var hovered = ImGui.IsItemHovered();

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isSelected)
                selection.Remove(key);
            else
                selection.Add(key);
            isSelected = !isSelected;
        }

        var dl = ImGui.GetWindowDrawList();
        var rarity = Sheets.RarityColorU32(item.ItemId);

        dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Panel), SlotRounding, ImDrawFlags.None);
        dl.AddRectFilled(min, max, WithAlpha(rarity, 0.20f), SlotRounding, ImDrawFlags.None);

        var iconId = Sheets.IconId(item.ItemId);
        if (iconId != 0)
        {
            var wrap = Plugin.Texture.GetFromGameIcon(new GameIconLookup(iconId, itemHq: item.IsHq)).GetWrapOrEmpty();
            if (!wrap.Handle.IsNull)
                dl.AddImage(wrap.Handle, min + new Vector2(1f, 1f), max - new Vector2(1f, 1f));
        }


        if (item.IsHq)
            CellText(dl, min + new Vector2(2f, 1f), SeIconChar.HighQuality.ToIconString());

        var ilvl = item.Ilvl.ToString();
        var ilvlSize = ImGui.CalcTextSize(ilvl);
        var ilvlPos = new Vector2(MathF.Max(min.X + 2f, max.X - ilvlSize.X - 3f), max.Y - ilvlSize.Y - 2f);
        dl.AddRectFilled(
            ilvlPos - new Vector2(2f, 1f),
            ilvlPos + ilvlSize + new Vector2(2f, 1f),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)),
            2f,
            ImDrawFlags.None);
        CellText(dl, ilvlPos, ilvl);

        // Discarding a fully spiritbonded piece throws away the materia it would have yielded.
        if (item.Spiritbond >= 100)
        {
            dl.AddCircleFilled(new Vector2(max.X - 6f, min.Y + 6f), 3.5f, ImGui.GetColorU32(Theme.Materia));
            dl.AddCircle(new Vector2(max.X - 6f, min.Y + 6f), 3.5f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f)), 0, 1f);
        }

        if (isSelected)
        {
            dl.AddRectFilled(min, max, ImGui.GetColorU32(Theme.Danger with { W = 0.35f }), SlotRounding, ImDrawFlags.None);
            dl.AddRect(min, max, ImGui.GetColorU32(Theme.Danger), SlotRounding, ImDrawFlags.None, 2.5f);
        }
        else if (hovered)
        {
            dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.65f)), SlotRounding, ImDrawFlags.None, 1.5f);
        }
        else
        {
            dl.AddRect(min, max, rarity, SlotRounding, ImDrawFlags.None, 1.5f);
            dl.AddRect(min, max, ImGui.GetColorU32(Theme.Rule), SlotRounding, ImDrawFlags.None, 0.5f);
        }

        if (hovered)
            DrawTooltip(item, isSelected);
    }

    private static void DrawTooltip(InventoryItem item, bool isSelected)
    {
        using var tooltip = ImRaii.Tooltip();

        var rarity = Sheets.RarityColorU32(item.ItemId);
        using (ImRaii.PushColor(ImGuiCol.Text, rarity))
            ImGui.TextUnformatted($"{item.Name}{(item.IsHq ? " " : string.Empty)}");

        ImGui.TextDisabled($"{RarityName(item.Rarity)} · ilvl {item.Ilvl} · {item.Slot}");
        ImGui.TextDisabled($"{item.Container}:{item.SlotIndex}");
        if (item.Spiritbond >= 100)
            ImGui.TextColored(Theme.Materia, "Spiritbond 100% — extract materia first");

        ImGui.Separator();
        ImGui.TextDisabled(isSelected ? "Click to unselect" : "Click to mark for discard");
    }

    private static void CellText(ImDrawListPtr dl, Vector2 pos, string text)
    {
        dl.AddText(pos + new Vector2(1f, 1f), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.9f)), text);
        dl.AddText(pos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), text);
    }

    private static string RarityName(byte rarity) => rarity switch
    {
        1 => "Common",
        2 => "Uncommon",
        3 => "Rare",
        4 => "Relic",
        7 => "Aetherial",
        _ => $"Rarity {rarity}",
    };

    /// <summary>Replaces the alpha byte of an ImGui-packed ABGR color.</summary>
    private static uint WithAlpha(uint abgr, float alpha)
        => (abgr & 0x00FFFFFFu) | ((uint)(Math.Clamp(alpha, 0f, 1f) * 255f) << 24);
}
