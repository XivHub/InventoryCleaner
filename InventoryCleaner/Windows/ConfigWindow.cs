using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace InventoryCleaner.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration cfg;

        public ConfigWindow(Configuration configuration) : base("InventoryCleaner Settings")
        {
            cfg = configuration;
        }

        public void Dispose() { }

        public override void Draw()
        {
            ImGui.TextDisabled("InventoryCleaner settings");
            ImGui.TextDisabled("Nothing configurable yet.");
        }
    }
}