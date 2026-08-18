using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using InventoryCleaner.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace InventoryCleaner.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private const int MaxSuggestions = 10;

        private readonly Configuration cfg;

        private string allowlistSearch = string.Empty;
        private string lastSearched = "###init###";
        private List<(uint ItemId, string Name)> suggestions = new();
        private ExcelSheet<Item>? itemSheet;

        // Distinct ClassJob ids that currently have a saved gearset; refreshed when the window opens.
        private List<uint> gearsetJobs = new();

        public ConfigWindow(Configuration configuration) : base("InventoryCleaner Settings")
        {
            cfg = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(360, 240),
                MaximumSize = new Vector2(800, 800),
            };
        }

        public override void OnOpen() => RefreshGearsetJobs();

        private void RefreshGearsetJobs()
            => gearsetJobs = GearsetReader.All()
                .Select(g => g.ClassJob)
                .Distinct()
                .OrderBy(j => j)
                .ToList();

        public void Dispose() { }

        public override void Draw()
        {
            ImGui.TextDisabled("InventoryCleaner settings");
            ImGui.Separator();
            ImGui.Spacing();

            DrawToggle("Keep equipped items", FontAwesomeIcon.CheckCircle,
                "Items currently equipped in the resolved slot are never moved.",
                v => cfg.KeepEquipped = v, cfg.KeepEquipped);

            DrawToggle("Keep items referenced by a gearset", FontAwesomeIcon.ListAlt,
                "Items any saved gearset references for the slot are kept.",
                v => cfg.KeepGearsetRef = v, cfg.KeepGearsetRef);

            DrawToggle("Keep upgrades", FontAwesomeIcon.ArrowUp,
                "Items that out-ilvl what an enabled job's gearset uses for the slot are kept as upgrades.",
                v => cfg.KeepUpgrade = v, cfg.KeepUpgrade);

            ImGui.Indent();
            ImGui.BeginDisabled(!cfg.KeepUpgrade);
            ImGui.TextUnformatted("Upgrade margin (ilvl):");
            ImGui.SameLine();
            var margin = cfg.UpgradeMarginIlvl;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("###UpgradeMarginIlvl", ref margin, 1, 5))
            {
                if (margin < 0)
                    margin = 0;
                cfg.UpgradeMarginIlvl = margin;
            }
            ImGui.SameLine();
            ImGui.TextDisabled("keep if ilvl > gearset max + margin");

            DrawUpgradeJobs();
            ImGui.EndDisabled();
            ImGui.Unindent();

            DrawToggle("Move items in unreferenced slots", FontAwesomeIcon.TrashAlt,
                "When off, items in slots no gearset uses are left in place (skipped from the plan).",
                v => cfg.MoveNoGearsetRef = v, cfg.MoveNoGearsetRef);

            ImGui.Spacing();
            ImGui.Separator();
            DrawAllowlist();

            ImGui.Spacing();
            ImGui.Separator();
            DrawDiscardSettings();

            ImGui.Spacing();
            ImGui.Separator();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Save, "Save"))
                cfg.Save();
        }

        private void DrawDiscardSettings()
        {
            ImGui.TextDisabled("Discard picker");
            ImGui.Spacing();

            ImGui.TextUnformatted("Icon size:");
            ImGui.SameLine();
            var scale = cfg.DiscardCellScale;
            ImGui.SetNextItemWidth(200);
            if (ImGui.SliderFloat("###DiscardCellScale", ref scale, 2f, 8f, "%.1f lines"))
                cfg.DiscardCellScale = scale;

            ImGui.TextUnformatted("Confirmation delay:");
            ImGui.SameLine();
            var delay = cfg.DiscardConfirmDelayMs;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("###DiscardConfirmDelayMs", ref delay, 50, 250))
                cfg.DiscardConfirmDelayMs = Math.Clamp(delay, 200, 5000);
            ImGui.SameLine();
            ImGui.TextDisabled("ms between discards; raise if confirmations are missed");
        }

        private void DrawUpgradeJobs()
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Keep upgrades for jobs:");
            ImGui.SameLine();
            ImGui.TextDisabled("click to toggle; dimmed = upgrades not kept");

            if (gearsetJobs.Count == 0)
            {
                ImGui.TextDisabled("(no saved gearsets found — open the Gear Set list, then reopen this window)");
                if (ImGui.SmallButton("Refresh"))
                    RefreshGearsetJobs();
                return;
            }

            if (ImGui.SmallButton("All"))
            {
                cfg.DisabledUpgradeJobs.Clear();
                cfg.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("None"))
            {
                cfg.DisabledUpgradeJobs = gearsetJobs.Distinct().ToList();
                cfg.Save();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Refresh##jobs"))
                RefreshGearsetJobs();

            var iconSize = ImGui.GetTextLineHeight() * 1.9f;
            var style = ImGui.GetStyle();
            var perCell = iconSize + (style.FramePadding.X * 2) + style.ItemSpacing.X;
            var avail = ImGui.GetContentRegionAvail().X;
            var perRow = Math.Max(1, (int)(avail / perCell));

            for (var i = 0; i < gearsetJobs.Count; i++)
            {
                var job = gearsetJobs[i];
                var enabled = !cfg.DisabledUpgradeJobs.Contains(job);

                using (ImRaii.PushId((int)job))
                {
                    var tint = enabled ? Vector4.One : new Vector4(0.3f, 0.3f, 0.3f, 1f);
                    var wrap = Plugin.Texture.GetFromGameIcon(Sheets.JobIconId(job)).GetWrapOrEmpty();
                    var clicked = wrap.Size.X > 0
                        ? ImGui.ImageButton(wrap.Handle, new Vector2(iconSize, iconSize), 1, Vector4.Zero, tint)
                        : DrawAbbrevButton(job, enabled, iconSize);

                    if (clicked)
                        ToggleUpgradeJob(job);

                    if (ImGui.IsItemHovered())
                    {
                        var name = Sheets.JobName(job);
                        ImGui.SetTooltip($"{(name.Length > 0 ? name : Sheets.JobAbbrev(job))} — upgrades {(enabled ? "kept" : "NOT kept")}");
                    }
                }

                if ((i + 1) % perRow != 0 && i + 1 < gearsetJobs.Count)
                    ImGui.SameLine();
            }
        }

        private static bool DrawAbbrevButton(uint job, bool enabled, float size)
        {
            var abbrev = Sheets.JobAbbrev(job);
            if (abbrev.Length == 0)
                abbrev = job.ToString();
            using var color = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1f), !enabled);
            return ImGui.Button(abbrev, new Vector2(size, size));
        }

        private void ToggleUpgradeJob(uint job)
        {
            if (!cfg.DisabledUpgradeJobs.Remove(job))
                cfg.DisabledUpgradeJobs.Add(job);
            cfg.Save();
        }

        private void DrawAllowlist()
        {
            ImGui.TextUnformatted("Allowlist (always-keep)");
            ImGui.TextDisabled("Items here are never moved, regardless of any other rule.");
            ImGui.Spacing();

            if (itemSheet == null)
                itemSheet = Sheets.ItemSheetOrNull;

            ImGui.TextUnformatted("Add by name:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(220);
            if (ImGui.InputText("###AllowlistSearch", ref allowlistSearch, 64))
                RefreshSuggestions();

            if (allowlistSearch.Length > 0 && suggestions.Count > 0)
            {
                ImGui.Spacing();
                using var list = ImRaii.Child("###AllowlistSuggestions", new Vector2(-1, 0), false);
                for (var i = 0; i < suggestions.Count; i++)
                {
                    var (id, name) = suggestions[i];
                    if (ImGui.Selectable($"{name} (ItemId: {id})###sug_{id}_{i}"))
                    {
                        AddAllowlist(id);
                        allowlistSearch = string.Empty;
                        lastSearched = "###clear###";
                        suggestions.Clear();
                    }
                }
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Current allowlist:");
            if (cfg.AllowlistItemIds == null || cfg.AllowlistItemIds.Count == 0)
            {
                ImGui.TextDisabled("(empty)");
            }
            else
            {
                var snapshot = cfg.AllowlistItemIds.ToList();
                foreach (var id in snapshot)
                {
                    var name = Sheets.Name(id);
                    var label = name.Length > 0 ? $"{name} (ItemId: {id})" : $"Unknown (ItemId: {id})";
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, label))
                        RemoveAllowlist(id);
                }
            }
        }

        private void RefreshSuggestions()
        {
            if (itemSheet == null)
            {
                suggestions.Clear();
                lastSearched = allowlistSearch;
                return;
            }

            if (allowlistSearch == lastSearched)
                return;
            lastSearched = allowlistSearch;

            if (allowlistSearch.Length == 0)
            {
                suggestions.Clear();
                return;
            }

            var needle = allowlistSearch;
            var existing = cfg.AllowlistItemIds ?? new List<uint>();
            var results = new List<(uint, string)>(MaxSuggestions);
            foreach (var row in itemSheet)
            {
                var rowName = row.Name.ToString();
                if (rowName.Length == 0 || !rowName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (existing.Contains(row.RowId))
                    continue;
                results.Add((row.RowId, rowName));
                if (results.Count >= MaxSuggestions)
                    break;
            }
            suggestions = results;
        }

        private void AddAllowlist(uint itemId)
        {
            cfg.AllowlistItemIds ??= new List<uint>();
            if (cfg.AllowlistItemIds.Contains(itemId))
                return;
            cfg.AllowlistItemIds.Add(itemId);
            cfg.Save();
        }

        private void RemoveAllowlist(uint itemId)
        {
            if (cfg.AllowlistItemIds == null)
                return;
            if (!cfg.AllowlistItemIds.Remove(itemId))
                return;
            cfg.Save();
        }

        private void DrawToggle(string label, FontAwesomeIcon icon, string help, Action<bool> set, bool current)
        {
            var v = current;
            if (ImGui.Checkbox($"###toggle_{label}", ref v))
            {
                set(v);
            }
            ImGui.SameLine();
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                ImGui.TextUnformatted(icon.ToIconString());
            ImGui.SameLine();
            ImGui.TextUnformatted(label);
            ImGui.SameLine();
            ImGui.TextDisabled(help);
        }
    }
}