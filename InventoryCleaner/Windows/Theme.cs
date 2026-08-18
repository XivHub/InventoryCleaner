using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using InventoryCleaner.Game;

namespace InventoryCleaner.Windows;

/// <summary>
/// The plugin's visual vocabulary: five semantic colors and the handful of primitives every window
/// draws with. Colors are deliberately few — an item is kept, moved, destroyed, pinned by the user,
/// or carries materia — and the nuance behind each decision lives in its reason text rather than in
/// more hues.
/// </summary>
internal static class Theme
{
    /// <summary>Brass — the Armoury Chest's own metal. Structure, headings, user pins.</summary>
    internal static readonly Vector4 Brass = new(0.851f, 0.741f, 0.420f, 1f);

    /// <summary>Gear staying where it is.</summary>
    internal static readonly Vector4 Keep = new(0.435f, 0.749f, 0.451f, 1f);

    /// <summary>Gear leaving the armoury.</summary>
    internal static readonly Vector4 Move = new(0.898f, 0.749f, 0.302f, 1f);

    /// <summary>Destruction, and anything that cannot be undone.</summary>
    internal static readonly Vector4 Danger = new(0.902f, 0.278f, 0.278f, 1f);

    /// <summary>Materia waiting to be extracted.</summary>
    internal static readonly Vector4 Materia = new(0.722f, 0.537f, 0.910f, 1f);

    internal static readonly Vector4 Panel = new(0.055f, 0.055f, 0.067f, 0.95f);
    internal static readonly Vector4 Track = new(0.157f, 0.157f, 0.184f, 1f);
    internal static readonly Vector4 Rule = new(0.227f, 0.227f, 0.267f, 1f);
    internal static readonly Vector4 Muted = new(0.549f, 0.549f, 0.588f, 1f);

    /// <summary>The spacing unit everything is measured in: half a line of text.</summary>
    internal static float Unit => ImGui.GetTextLineHeight() * 0.5f;

    /// <summary>Vertical breathing room between related blocks.</summary>
    internal static void Gap(float units = 1f) => ImGui.Dummy(new Vector2(0f, Unit * units));

    /// <summary>A FontAwesome glyph rendered inline at the cursor.</summary>
    internal static void Icon(FontAwesomeIcon icon, Vector4? color = null)
    {
        using var c = ImRaii.PushColor(ImGuiCol.Text, color ?? Brass, color.HasValue);
        using var font = Plugin.PluginInterface.UiBuilder.IconFontHandle.Push();
        ImGui.TextUnformatted(icon.ToIconString());
    }

    /// <summary>
    /// A count with a label on a tinted plate, e.g. "12 to move". Used instead of running prose so
    /// the numbers that drive a decision are the first thing read.
    /// </summary>
    internal static void Chip(string text, Vector4 color)
    {
        var padding = new Vector2(Unit, Unit * 0.4f);
        var size = ImGui.CalcTextSize(text);
        ImGui.Dummy(size + (padding * 2f));

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, ImGui.GetColorU32(color with { W = 0.18f }), 3f, ImDrawFlags.None);
        dl.AddText(min + padding, ImGui.GetColorU32(color), text);
    }

    /// <summary>A slim horizontal gauge; the fill is <paramref name="frac"/> of the full width.</summary>
    internal static void FillBar(float frac, Vector2 size, Vector4 color)
    {
        ImGui.Dummy(size);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, ImGui.GetColorU32(Track), 2f, ImDrawFlags.None);

        var width = (max.X - min.X) * Math.Clamp(frac, 0f, 1f);
        if (width > 0f)
            dl.AddRectFilled(min, new Vector2(min.X + width, max.Y), ImGui.GetColorU32(color), 2f, ImDrawFlags.None);
    }

    /// <summary>
    /// One segment of the mode switcher. The active segment is filled with its own mode color, so
    /// the destructive mode announces itself before it is entered rather than after.
    /// </summary>
    internal static bool Tab(string label, bool active, Vector4 color, float width)
    {
        var fill = active ? color with { W = 0.85f } : new Vector4(0.157f, 0.157f, 0.184f, 1f);
        var hover = active ? fill : new Vector4(0.220f, 0.220f, 0.259f, 1f);
        var text = active ? new Vector4(0.055f, 0.055f, 0.067f, 1f) : Muted;

        using var colors = ImRaii.PushColor(ImGuiCol.Button, fill)
            .Push(ImGuiCol.ButtonHovered, hover)
            .Push(ImGuiCol.ButtonActive, fill)
            .Push(ImGuiCol.Text, text);
        return ImGui.Button(label, new Vector2(width, 0f));
    }

    /// <summary>A button whose weight matches the consequence of pressing it.</summary>
    internal static bool ActionButton(FontAwesomeIcon icon, string label, Vector4 color, bool enabled = true)
    {
        // The glyph and label are painted through the draw list, which ImGui's disabled styling does
        // not reach, so the dimming is applied to the color itself.
        if (!enabled)
            color = color with { W = color.W * 0.4f };

        using var disabled = ImRaii.Disabled(!enabled);
        using var colors = ImRaii.PushColor(ImGuiCol.Button, color with { W = 0.22f })
            .Push(ImGuiCol.ButtonHovered, color with { W = 0.38f })
            .Push(ImGuiCol.ButtonActive, color with { W = 0.55f })
            .Push(ImGuiCol.Text, color);

        var height = ImGui.GetFrameHeight();
        var pressed = ImGui.Button($"##{label}", new Vector2(ImGui.CalcTextSize(label).X + (Unit * 5f), height));

        var min = ImGui.GetItemRectMin();
        var dl = ImGui.GetWindowDrawList();
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = icon.ToIconString();
            dl.AddText(min + new Vector2(Unit, (height - ImGui.CalcTextSize(glyph).Y) * 0.5f), ImGui.GetColorU32(color), glyph);
        }

        dl.AddText(
            min + new Vector2(Unit * 3.2f, (height - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(color),
            label);
        return pressed;
    }

    /// <summary>
    /// The item's game icon at <paramref name="size"/> pixels square, HQ variant where applicable.
    /// Reserves the space either way so rows stay aligned while textures are still loading.
    /// </summary>
    internal static void ItemIcon(uint itemId, bool hq, float size)
    {
        var iconId = Sheets.IconId(itemId);
        if (iconId != 0)
        {
            var wrap = Plugin.Texture.GetFromGameIcon(new GameIconLookup(iconId, itemHq: hq)).GetWrapOrEmpty();
            if (!wrap.Handle.IsNull)
            {
                ImGui.Image(wrap.Handle, new Vector2(size, size));
                return;
            }
        }

        ImGui.Dummy(new Vector2(size, size));
    }

    /// <summary>Vertically centers the following text against an icon of <paramref name="size"/>.</summary>
    internal static void AlignToIcon(float size)
    {
        var offset = (size - ImGui.GetTextLineHeight()) * 0.5f;
        if (offset > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
    }
}
