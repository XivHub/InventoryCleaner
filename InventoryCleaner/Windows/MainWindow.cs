using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using InventoryCleaner.Game;
using InventoryCleaner.Logic;
using InventoryCleaner.Models;
using ZhyraPluginKit.Inventory;
using InventoryItem = InventoryCleaner.Models.InventoryItem;

namespace InventoryCleaner.Windows
{
    public class MainWindow : Window, IDisposable
    {
        private enum UiState
        {
            Idle,
            Previewing,
            Running,
            Done,
        }

        private enum Mode
        {
            Clean,
            Reverse,
            Discard,
        }

        private readonly Plugin plugin;
        private readonly Configuration cfg;

        private UiState state = UiState.Idle;
        private int modeIndex;
        private int lastRunMoved;
        private int lastRunFailed;
        private int lastRunKept;

        /// <summary>
        /// Occupancy of every container the current preview touches, captured when the preview is
        /// built. The rack gauges must show the state the plan was made against, not a figure that
        /// drifts under the player while they read it.
        /// </summary>
        private Dictionary<InventoryType, (int Used, int Size)> previewUsage = new();

        private List<InventoryItem> discardCandidates = new();
        private readonly HashSet<(InventoryType Container, int Slot)> discardSelection = new();
        private int discardIlvlCutoff;
        private bool discardArmed;
        private bool discardRunActive;
        private (int Done, int Failed, int Skipped)? discardSummary;

        private Mode CurrentMode => (Mode)this.modeIndex;

        private static float RowIconSize => ImGui.GetTextLineHeight() * 1.6f;

        public MainWindow(Plugin plugin, Configuration configuration) : base("InventoryCleaner###InventoryCleanerMain")
        {
            this.plugin = plugin;
            cfg = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(460, 380),
                MaximumSize = new Vector2(1100, 1400),
            };
        }

        public void Dispose() { }

        /// <summary>Opens the window straight into the discard picker with a fresh candidate list.</summary>
        public void OpenDiscard()
        {
            this.modeIndex = (int)Mode.Discard;
            RefreshDiscardCandidates();
            IsOpen = true;
        }

        public override void Draw()
        {
            DrawTitleBar();
            DrawTabs();

            if (CurrentMode == Mode.Discard)
            {
                DrawDiscard();
                return;
            }

            if (state == UiState.Running && !plugin.MoveQueue.IsRunning)
                TransitionToDone();

            switch (state)
            {
                case UiState.Idle:
                    DrawIdle();
                    break;
                case UiState.Previewing:
                    DrawPreviewing();
                    break;
                case UiState.Running:
                    DrawRunning();
                    break;
                case UiState.Done:
                    DrawDone();
                    break;
            }
        }

        private void DrawTitleBar()
        {
            Theme.Icon(FontAwesomeIcon.Archive);
            ImGui.SameLine();
            ImGui.TextUnformatted("Armoury");
            ImGui.SameLine();
            ImGui.TextColored(Theme.Muted, "· keep what you use");

            var free = InventoryReader.FreeMainInventorySlots();
            var label = $"{free} bag slots free";
            var width = ImGui.CalcTextSize(label).X + (Theme.Unit * 2f);
            ImGui.SameLine(MathF.Max(
                ImGui.GetCursorPosX(),
                ImGui.GetWindowWidth() - width - ImGui.GetStyle().WindowPadding.X));
            Theme.Chip(label, free > 0 ? Theme.Muted : Theme.Danger);

            ImGui.Separator();
            Theme.Gap(0.5f);
        }

        private void DrawTabs()
        {
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var width = (ImGui.GetContentRegionAvail().X - (spacing * 2f)) / 3f;

            if (Theme.Tab("Clean", CurrentMode == Mode.Clean, Theme.Brass, width))
                SwitchMode(Mode.Clean);
            ImGui.SameLine();
            if (Theme.Tab("Restock", CurrentMode == Mode.Reverse, Theme.Keep, width))
                SwitchMode(Mode.Reverse);
            ImGui.SameLine();
            if (Theme.Tab("Discard", CurrentMode == Mode.Discard, Theme.Danger, width))
                SwitchMode(Mode.Discard);

            Theme.Gap(0.5f);
            ImGui.TextColored(Theme.Muted, CurrentMode switch
            {
                Mode.Clean => "Move gear no gearset needs out of the Armoury Chest and into your bags.",
                Mode.Reverse => "Pull gear your gearsets reference back out of your bags into the Armoury Chest.",
                _ => "Destroy leftover gear for good. Nothing here is recoverable.",
            });
            Theme.Gap(0.5f);
        }

        private void SwitchMode(Mode mode)
        {
            this.modeIndex = (int)mode;
            if (mode == Mode.Discard)
                RefreshDiscardCandidates();
        }

        private void DrawIdle()
        {
            var reverse = CurrentMode == Mode.Reverse;
            var free = InventoryReader.FreeMainInventorySlots();
            var blocked = !reverse && free <= 0;

            if (blocked)
                ImGui.TextColored(Theme.Danger, "Your bags are full, so there is nowhere to move gear to.");

            Theme.Gap();
            if (Theme.ActionButton(FontAwesomeIcon.Search, reverse ? "Plan restock" : "Plan clean", Theme.Brass, !blocked))
                BuildPreview(reverse ? ReversePlanner.Plan(cfg) : CleanPlanner.Plan(cfg), reverse);
        }

        private void BuildPreview(List<MovePlan> plans, bool reverse)
        {
            plugin.PendingPreview = plans;
            plugin.pendingReverse = reverse;
            previewUsage = plans
                .Select(p => p.Item.Container)
                .Distinct()
                .ToDictionary(c => c, InventoryScan.ContainerUsage);
            state = UiState.Previewing;
        }

        private void DrawPreviewing()
        {
            var plans = plugin.PendingPreview;
            if (plans == null)
            {
                state = UiState.Idle;
                DrawIdle();
                return;
            }

            var moveCount = plans.Count(p => !Plugin.IsKept(p.Reason));
            var keptCount = plans.Count - moveCount;

            Theme.Chip($"{moveCount} to move", Theme.Move);
            if (keptCount > 0)
            {
                ImGui.SameLine();
                Theme.Chip($"{keptCount} staying", Theme.Keep);
            }

            if (!plugin.pendingReverse)
            {
                var free = InventoryReader.FreeMainInventorySlots();
                if (moveCount > free)
                {
                    ImGui.SameLine();
                    Theme.Chip($"{moveCount - free} will not fit", Theme.Danger);
                    ImGui.TextColored(
                        Theme.Muted,
                        "The run stops when your bags fill. Fullest racks go first, so the space you get back is where it was tightest.");
                }
            }

            Theme.Gap();
            DrawPlanTable(plans);
            Theme.Gap();

            if (Theme.ActionButton(
                    FontAwesomeIcon.Check,
                    plugin.pendingReverse ? "Restock" : "Clean",
                    Theme.Brass,
                    !plugin.MoveQueue.IsRunning))
            {
                if (!plugin.pendingReverse && InventoryReader.FreeMainInventorySlots() <= 0)
                {
                    plugin.PendingPreview = null;
                    state = UiState.Idle;
                    return;
                }

                plugin.MoveQueue.Enqueue(plans.Where(p => !Plugin.IsKept(p.Reason)).ToList(), plugin.pendingReverse);
                lastRunKept = keptCount;
                plugin.PendingPreview = null;
                state = UiState.Running;
            }

            ImGui.SameLine();
            if (Theme.ActionButton(FontAwesomeIcon.Times, "Cancel", Theme.Muted))
            {
                plugin.PendingPreview = null;
                state = UiState.Idle;
            }
        }

        private void DrawRunning()
        {
            var mq = plugin.MoveQueue;
            var total = mq.Done + mq.Pending + mq.Failed;
            Theme.FillBar(
                total > 0 ? (float)mq.Done / total : 0f,
                new Vector2(ImGui.GetContentRegionAvail().X, Theme.Unit),
                Theme.Brass);

            Theme.Gap();
            var cur = mq.Current;
            if (cur != null)
            {
                Theme.ItemIcon(cur.Item.ItemId, cur.Item.IsHq, RowIconSize);
                ImGui.SameLine();
                Theme.AlignToIcon(RowIconSize);
                ImGui.TextUnformatted(ItemLabel(cur.Item));
            }
            else
            {
                ImGui.TextColored(Theme.Muted, "Finishing up");
            }

            ImGui.TextColored(Theme.Muted, $"{mq.Done} of {total} moved");

            Theme.Gap();
            if (Theme.ActionButton(FontAwesomeIcon.Stop, "Stop", Theme.Danger))
                plugin.MoveQueue.Abort();
        }

        private void DrawDone()
        {
            Theme.Chip($"{lastRunMoved} moved", Theme.Move);
            if (lastRunKept > 0)
            {
                ImGui.SameLine();
                Theme.Chip($"{lastRunKept} stayed", Theme.Keep);
            }

            if (lastRunFailed > 0)
            {
                ImGui.SameLine();
                Theme.Chip($"{lastRunFailed} failed", Theme.Danger);
            }

            Theme.Gap();
            if (Theme.ActionButton(FontAwesomeIcon.Undo, "Back", Theme.Muted))
                state = UiState.Idle;

            if (lastRunMoved > 0)
            {
                ImGui.SameLine();
                if (Theme.ActionButton(FontAwesomeIcon.TrashAlt, "Discard leftovers", Theme.Danger))
                {
                    state = UiState.Idle;
                    OpenDiscard();
                }
            }
        }

        private void TransitionToDone()
        {
            var mq = plugin.MoveQueue;
            lastRunMoved = mq.Done;
            lastRunFailed = mq.Failed;
            state = UiState.Done;
        }

        /// <summary>
        /// The plan, grouped into the racks it will be worked through. Each group carries the gauge
        /// that decided its position: fullest rack first, because that is the one whose space the run
        /// is trying to buy back.
        /// </summary>
        private void DrawPlanTable(IReadOnlyList<MovePlan> plans)
        {
            // No header row: the rack headers below carry the structure, and a second header band
            // above them would compete with it.
            const ImGuiTableFlags flags =
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp;

            var icon = RowIconSize;
            using var table = ImRaii.Table("###InventoryCleanerPreview", 4, flags, new Vector2(-1, 260));
            if (!table)
                return;

            ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, icon);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 4f);
            ImGui.TableSetupColumn("ilvl", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("0000").X);
            ImGui.TableSetupColumn("Why", ImGuiTableColumnFlags.WidthStretch, 5f);

            InventoryType? group = null;
            foreach (var plan in plans)
            {
                if (group != plan.Item.Container)
                {
                    group = plan.Item.Container;
                    DrawRackHeader(group.Value);
                }

                DrawPlanRow(plan, icon);
            }
        }

        private void DrawRackHeader(InventoryType container)
        {
            previewUsage.TryGetValue(container, out var usage);
            var fill = usage.Size > 0 ? (float)usage.Used / usage.Size : 0f;

            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(Theme.Brass with { W = 0.10f }));

            ImGui.TableSetColumnIndex(0);
            Theme.Icon(FontAwesomeIcon.Boxes);

            ImGui.TableSetColumnIndex(1);
            ImGui.TextColored(Theme.Brass, EquipSlotExtensions.ContainerName(container));

            ImGui.TableSetColumnIndex(2);
            if (usage.Size > 0)
                ImGui.TextColored(Theme.Muted, $"{usage.Used}/{usage.Size}");

            ImGui.TableSetColumnIndex(3);
            if (usage.Size > 0)
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (ImGui.GetTextLineHeight() * 0.35f));
                Theme.FillBar(
                    fill,
                    new Vector2(MathF.Max(24f, ImGui.GetContentRegionAvail().X - Theme.Unit), Theme.Unit * 0.8f),
                    fill >= 0.9f ? Theme.Danger : Theme.Brass);
            }
        }

        private static void DrawPlanRow(MovePlan plan, float icon)
        {
            var kept = Plugin.IsKept(plan.Reason);
            using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.6f, kept);

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            Theme.ItemIcon(plan.Item.ItemId, plan.Item.IsHq, icon);

            ImGui.TableSetColumnIndex(1);
            Theme.AlignToIcon(icon);
            using (ImRaii.PushColor(ImGuiCol.Text, Sheets.RarityColorU32(plan.Item.ItemId)))
                ImGui.TextUnformatted(ItemLabel(plan.Item));

            ImGui.TableSetColumnIndex(2);
            Theme.AlignToIcon(icon);
            ImGui.TextUnformatted(plan.Item.Ilvl.ToString());

            ImGui.TableSetColumnIndex(3);
            Theme.AlignToIcon(icon);
            ImGui.TextColored(ReasonColor(plan.Reason), plan.WhyText);
        }

        private void RefreshDiscardCandidates()
        {
            discardCandidates = DiscardPlanner.Candidates(cfg);
            discardSelection.Clear();
            discardArmed = false;
            discardSummary = null;
        }

        private void DrawDiscard()
        {
            var dq = plugin.DiscardQueue;
            if (discardRunActive && !dq.IsRunning)
            {
                discardSummary = (dq.Done, dq.Failed, dq.Skipped);
                discardRunActive = false;
            }

            if (dq.IsRunning)
                DrawDiscardRunning();
            else if (discardSummary != null)
                DrawDiscardDone();
            else
                DrawDiscardPicker();
        }

        private void DrawDiscardPicker()
        {
            Theme.Chip($"{discardCandidates.Count} eligible", Theme.Muted);
            if (discardSelection.Count > 0)
            {
                ImGui.SameLine();
                Theme.Chip($"{discardSelection.Count} marked", Theme.Danger);
            }

            ImGui.SameLine();
            if (Theme.ActionButton(FontAwesomeIcon.SyncAlt, "Rescan", Theme.Muted))
                RefreshDiscardCandidates();

            ImGui.TextColored(
                Theme.Muted,
                "Equipped gear, anything a gearset uses, your allowlist and Ultimate weapons are never listed here.");

            Theme.Gap();
            ImGui.TextUnformatted("Mark everything up to ilvl");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.CalcTextSize("00000").X + (Theme.Unit * 6f));
            ImGui.InputInt("###InventoryCleanerIlvlCutoff", ref discardIlvlCutoff);
            if (discardIlvlCutoff < 0)
                discardIlvlCutoff = 0;
            ImGui.SameLine();
            if (ImGui.Button("Mark"))
            {
                // Fully spiritbonded pieces are left out: bulk-marking them would silently throw away
                // the materia they are about to yield. They can still be picked by hand.
                foreach (var item in discardCandidates)
                {
                    if (item.Ilvl <= discardIlvlCutoff && item.Spiritbond < 100)
                        discardSelection.Add((item.Container, item.SlotIndex));
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Skips items at 100% spiritbond.");
            ImGui.SameLine();
            if (ImGui.Button("Clear marks"))
                discardSelection.Clear();

            Theme.Gap();
            DiscardGrid.Draw(discardCandidates, discardSelection, cfg.DiscardCellScale, new Vector2(-1, 300));
            Theme.Gap();

            if (!discardArmed)
            {
                if (Theme.ActionButton(
                        FontAwesomeIcon.TrashAlt,
                        $"Discard {discardSelection.Count}",
                        Theme.Danger,
                        discardSelection.Count > 0))
                    discardArmed = true;
                return;
            }

            ImGui.TextColored(Theme.Danger, $"Destroy {discardSelection.Count} items. This cannot be undone.");
            Theme.Gap(0.5f);
            if (Theme.ActionButton(FontAwesomeIcon.TrashAlt, "Destroy them", Theme.Danger))
                StartDiscard();
            ImGui.SameLine();
            if (Theme.ActionButton(FontAwesomeIcon.Times, "Keep them", Theme.Muted))
                discardArmed = false;
        }

        private void StartDiscard()
        {
            var selected = discardCandidates
                .Where(i => discardSelection.Contains((i.Container, i.SlotIndex)))
                .ToList();
            if (selected.Count == 0)
            {
                discardArmed = false;
                return;
            }

            plugin.DiscardQueue.Enqueue(selected);
            discardRunActive = true;
            discardArmed = false;
            discardSummary = null;
        }

        private void DrawDiscardRunning()
        {
            var dq = plugin.DiscardQueue;
            var handled = dq.Done + dq.Failed + dq.Skipped;
            var total = handled + dq.Pending;
            Theme.FillBar(
                total > 0 ? (float)handled / total : 0f,
                new Vector2(ImGui.GetContentRegionAvail().X, Theme.Unit),
                Theme.Danger);

            Theme.Gap();
            var cur = dq.Current;
            if (cur != null)
            {
                Theme.ItemIcon(cur.ItemId, cur.IsHq, RowIconSize);
                ImGui.SameLine();
                Theme.AlignToIcon(RowIconSize);
                ImGui.TextColored(Theme.Danger, ItemLabel(cur));
            }
            else
            {
                ImGui.TextColored(Theme.Muted, "Finishing up");
            }

            ImGui.TextColored(Theme.Muted, $"{handled} of {total} handled");

            Theme.Gap();
            if (Theme.ActionButton(FontAwesomeIcon.Stop, "Stop", Theme.Danger))
                plugin.DiscardQueue.Abort();
        }

        private void DrawDiscardDone()
        {
            var summary = discardSummary!.Value;
            Theme.Chip($"{summary.Done} destroyed", Theme.Danger);
            if (summary.Failed > 0)
            {
                ImGui.SameLine();
                Theme.Chip($"{summary.Failed} failed", Theme.Move);
            }

            if (summary.Skipped > 0)
            {
                ImGui.SameLine();
                Theme.Chip($"{summary.Skipped} skipped", Theme.Muted);
            }

            Theme.Gap();
            if (Theme.ActionButton(FontAwesomeIcon.Undo, "Back", Theme.Muted))
                RefreshDiscardCandidates();
        }

        private static string ItemLabel(InventoryItem item)
            => item.IsHq ? $"{item.Name} {SeIconChar.HighQuality.ToIconString()}" : item.Name;

        private static Vector4 ReasonColor(DecisionReason r) => r switch
        {
            DecisionReason.Kept_Allowlist => Theme.Brass,
            DecisionReason.Kept_Equipped => Theme.Keep,
            DecisionReason.Kept_GearsetRef => Theme.Keep,
            DecisionReason.Kept_Upgrade => Theme.Keep,
            DecisionReason.Moved_ReverseGearsetRef => Theme.Keep,
            _ => Theme.Move,
        };
    }
}
