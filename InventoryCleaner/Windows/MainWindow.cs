using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace InventoryCleaner.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private static readonly Vector4 Accent = new(0.85f, 0.74f, 0.42f, 1f);

        private readonly Configuration cfg;

        public MainWindow(Configuration configuration) : base("InventoryCleaner###InventoryCleanerMain")
        {
            cfg = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(380, 320),
                MaximumSize = new Vector2(900, 1400),
            };
        }

        public void Dispose() { }

        public override void Draw()
        {
            DrawHeader();
            DrawControlBar();
            ImGui.Spacing();
            DrawStatus();
        }

        private void DrawHeader()
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            {
                Icon(FontAwesomeIcon.Broom);
                ImGui.SameLine();
                ImGui.TextUnformatted("InventoryCleaner");
            }
            ImGui.SameLine();
            ImGui.TextDisabled("· tidy your bags");
            ImGui.Separator();
        }

        private void DrawControlBar()
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, "Start"))
                Plugin.ChatGui.Print("[InventoryCleaner] start");
            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, "Stop"))
                Plugin.ChatGui.Print("[InventoryCleaner] stop");
        }

        private void DrawStatus()
        {
            ImGui.TextDisabled("State");
            ImGui.SameLine(110);
            ImGui.TextUnformatted("Idle");
        }

        private static void Icon(FontAwesomeIcon icon)
        {
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                ImGui.TextUnformatted(icon.ToIconString());
        }
    }
}