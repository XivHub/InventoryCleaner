using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using InventoryCleaner.Game;
using InventoryCleaner.Logic;
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

        private static readonly Vector4 Accent = new(0.85f, 0.74f, 0.42f, 1f);
        private static readonly Vector4 KeptColor = new(0.45f, 0.75f, 0.45f, 1f);
        private static readonly Vector4 KeptDimColor = new(0.40f, 0.40f, 0.40f, 1f);
        private static readonly Vector4 MovedNoRefColor = new(0.90f, 0.75f, 0.30f, 1f);
        private static readonly Vector4 MovedLowerIlvlColor = new(0.90f, 0.45f, 0.30f, 1f);
        private static readonly Vector4 MovedDuplicateColor = new(0.90f, 0.30f, 0.30f, 1f);
        private static readonly Vector4 KeptAllowlistColor = new(0.30f, 0.80f, 0.85f, 1f);
        private static readonly Vector4 DangerColor = new(0.90f, 0.28f, 0.28f, 1f);

        private readonly Plugin plugin;
        private readonly Configuration cfg;

        private UiState state = UiState.Idle;
        private int modeIndex;
        private int lastRunMoved;
        private int lastRunFailed;
        private int lastRunKept;

        private List<InventoryItem> discardCandidates = new();
        private readonly HashSet<(InventoryType Container, int Slot)> discardSelection = new();
        private int discardIlvlCutoff;
        private bool discardArmed;
        private bool discardRunActive;
        private (int Done, int Failed, int Skipped)? discardSummary;

        private Mode CurrentMode => (Mode)this.modeIndex;

        public MainWindow(Plugin plugin, Configuration configuration) : base("InventoryCleaner###InventoryCleanerMain")
        {
            this.plugin = plugin;
            cfg = configuration;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(380, 320),
                MaximumSize = new Vector2(900, 1400),
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
            DrawHeader();
            DrawModeToggle();

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

        private void DrawModeToggle()
        {
            ImGui.TextUnformatted("Mode:");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            {
                ImGui.RadioButton("Clean", ref this.modeIndex, (int)Mode.Clean);
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            {
                ImGui.RadioButton("Reverse", ref this.modeIndex, (int)Mode.Reverse);
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, DangerColor))
            {
                if (ImGui.RadioButton("Discard", ref this.modeIndex, (int)Mode.Discard))
                    RefreshDiscardCandidates();
            }
            if (CurrentMode == Mode.Reverse)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(moves gearset items to armoury; aborts on full container)");
            }
            else if (CurrentMode == Mode.Discard)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(destroys items; equipped / gearset / allowlisted gear is never listed)");
            }
            ImGui.Separator();
        }

        private void DrawIdle()
        {
            if (CurrentMode == Mode.Clean)
                DrawIdleClean();
            else
                DrawIdleReverse();
        }

        private void DrawIdleClean()
        {
            var free = InventoryReader.FreeMainInventorySlots();
            ImGui.TextUnformatted("Main inventory free slots:");
            ImGui.SameLine();
            if (free <= 0)
                ImGui.TextColored(new Vector4(0.90f, 0.30f, 0.30f, 1f), free.ToString());
            else
                ImGui.TextUnformatted(free.ToString());

            ImGui.Spacing();
            ImGui.BeginDisabled(free <= 0);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, "Start"))
            {
                var plans = CleanPlanner.Plan(cfg);
                plugin.PendingPreview = plans;
                plugin.pendingReverse = false;
                state = UiState.Previewing;
            }
            ImGui.EndDisabled();

            if (free <= 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(need free main inventory slots to move into)");
            }
        }

        private void DrawIdleReverse()
        {
            ImGui.TextUnformatted("Reverse mode:");
            ImGui.SameLine();
            ImGui.TextDisabled("moves main-inventory items referenced by a gearset back to armoury");

            ImGui.Spacing();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Play, "Start"))
            {
                var plans = ReversePlanner.Plan(cfg);
                plugin.PendingPreview = plans;
                plugin.pendingReverse = true;
                state = UiState.Previewing;
            }
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

            var moveCount = plans.Count(p => !IsKept(p.Reason));
            var keptCount = plans.Count - moveCount;
            ImGui.TextUnformatted($"{moveCount} items to move");
            ImGui.SameLine();
            ImGui.TextDisabled($" · {keptCount} kept");

            if (!plugin.pendingReverse)
            {
                var free = InventoryReader.FreeMainInventorySlots();
                if (moveCount > free)
                    ImGui.TextColored(
                        MovedLowerIlvlColor,
                        $"Only {free} free main inventory slots: {moveCount - free} will not fit and the run stops there. " +
                        "The fullest armoury containers are emptied first.");
            }

            ImGui.Spacing();
            DrawPlanTable(plans);
            ImGui.Spacing();

            ImGui.BeginDisabled(plugin.MoveQueue.IsRunning);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Check, "Confirm"))
            {
                if (!plugin.pendingReverse && InventoryReader.FreeMainInventorySlots() <= 0)
                {
                    plugin.PendingPreview = null;
                    state = UiState.Idle;
                    return;
                }
                var movePlans = plans.Where(p => !IsKept(p.Reason)).ToList();
                plugin.MoveQueue.Enqueue(movePlans, plugin.pendingReverse);
                lastRunKept = keptCount;
                plugin.PendingPreview = null;
                state = UiState.Running;
            }
            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, "Cancel"))
            {
                plugin.PendingPreview = null;
                state = UiState.Idle;
            }
        }

        private void DrawRunning()
        {
            var mq = plugin.MoveQueue;
            var total = mq.Done + mq.Pending + mq.Failed;
            var frac = total > 0 ? (float)mq.Done / total : 0f;
            ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{mq.Done} / {total}");

            ImGui.Spacing();
            ImGui.TextUnformatted("Current:");
            ImGui.SameLine();
            var cur = mq.Current;
            if (cur != null)
                ImGui.TextUnformatted($"{cur.Item.Name}{(cur.Item.IsHq ? " \uE03c" : string.Empty)}");
            else
                ImGui.TextDisabled("(finishing)");

            ImGui.Spacing();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, "Stop"))
            {
                plugin.MoveQueue.Abort();
            }
        }

        private void DrawDone()
        {
            ImGui.TextUnformatted($"Moved {lastRunMoved}");
            ImGui.SameLine();
            ImGui.TextDisabled($" · failed {lastRunFailed} · kept {lastRunKept}");

            ImGui.Spacing();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, "Back"))
            {
                state = UiState.Idle;
            }

            if (lastRunMoved > 0)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, DangerColor))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.TrashAlt, "Discard leftovers"))
                    {
                        state = UiState.Idle;
                        OpenDiscard();
                    }
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
            ImGui.TextUnformatted($"{discardCandidates.Count} discardable");
            ImGui.SameLine();
            ImGui.TextDisabled($" · {discardSelection.Count} selected");
            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh"))
                RefreshDiscardCandidates();

            ImGui.SetNextItemWidth(110f);
            ImGui.InputInt("###InventoryCleanerIlvlCutoff", ref discardIlvlCutoff);
            if (discardIlvlCutoff < 0)
                discardIlvlCutoff = 0;
            ImGui.SameLine();
            if (ImGui.Button($"Select ilvl <= {discardIlvlCutoff}"))
            {
                // Fully spiritbonded pieces are left out: bulk-selecting them would silently throw
                // away the materia they are about to yield. They can still be picked by hand.
                foreach (var item in discardCandidates)
                {
                    if (item.Ilvl <= discardIlvlCutoff && item.Spiritbond < 100)
                        discardSelection.Add((item.Container, item.SlotIndex));
                }
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Skips items at 100% spiritbond.");
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
                discardSelection.Clear();

            ImGui.Spacing();
            DiscardGrid.Draw(discardCandidates, discardSelection, cfg.DiscardCellScale, new Vector2(-1, 320));
            ImGui.Spacing();

            if (!discardArmed)
            {
                ImGui.BeginDisabled(discardSelection.Count == 0);
                using (ImRaii.PushColor(ImGuiCol.Text, DangerColor))
                {
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.TrashAlt, $"Discard {discardSelection.Count}"))
                        discardArmed = true;
                }
                ImGui.EndDisabled();
                return;
            }

            ImGui.TextColored(DangerColor, $"Destroy {discardSelection.Count} items? This cannot be undone.");
            using (ImRaii.PushColor(ImGuiCol.Text, DangerColor))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.TrashAlt, "Yes, discard"))
                    StartDiscard();
            }
            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Times, "Cancel"))
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
            var total = dq.Done + dq.Failed + dq.Skipped + dq.Pending;
            var frac = total > 0 ? (float)(dq.Done + dq.Failed + dq.Skipped) / total : 0f;
            ImGui.ProgressBar(frac, new Vector2(-1, 0), $"{dq.Done + dq.Failed + dq.Skipped} / {total}");

            ImGui.Spacing();
            ImGui.TextUnformatted("Discarding:");
            ImGui.SameLine();
            var cur = dq.Current;
            if (cur != null)
                ImGui.TextColored(DangerColor, cur.Name);
            else
                ImGui.TextDisabled("(finishing)");

            ImGui.Spacing();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Stop, "Stop"))
                plugin.DiscardQueue.Abort();
        }

        private void DrawDiscardDone()
        {
            var summary = discardSummary!.Value;
            ImGui.TextUnformatted($"Discarded {summary.Done}");
            ImGui.SameLine();
            ImGui.TextDisabled($" · failed {summary.Failed} · skipped {summary.Skipped}");

            ImGui.Spacing();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, "Back"))
                RefreshDiscardCandidates();
        }

        private static void DrawPlanTable(IReadOnlyList<MovePlan> plans)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp;

            using var table = ImRaii.Table("###InventoryCleanerPreview", 4, flags, new Vector2(-1, 220));
            if (!table)
                return;

            ImGui.TableSetupColumn("Item");
            ImGui.TableSetupColumn("ilvl");
            ImGui.TableSetupColumn("From");
            ImGui.TableSetupColumn("Reason");
            ImGui.TableHeadersRow();

            foreach (var plan in plans)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(plan.Item.Name);
                if (plan.Item.IsHq)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted("\uE03c");
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(plan.Item.Ilvl.ToString());

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted($"{plan.Item.Container}:{plan.Item.SlotIndex}");

                ImGui.TableSetColumnIndex(3);
                var color = ReasonColor(plan.Reason);
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.TextUnformatted(plan.WhyText);
                ImGui.PopStyleColor();
            }
        }

        private static bool IsKept(DecisionReason r)
            => r == DecisionReason.Kept_Allowlist
               || r == DecisionReason.Kept_Equipped
               || r == DecisionReason.Kept_GearsetRef
               || r == DecisionReason.Kept_Upgrade;

        private static Vector4 ReasonColor(DecisionReason r) => r switch
        {
            DecisionReason.Kept_Allowlist => KeptAllowlistColor,
            DecisionReason.Kept_Equipped => KeptColor,
            DecisionReason.Kept_GearsetRef => KeptColor,
            DecisionReason.Kept_Upgrade => KeptDimColor,
            DecisionReason.Moved_NoGearsetRef => MovedNoRefColor,
            DecisionReason.Moved_LowerIlvl => MovedLowerIlvlColor,
            DecisionReason.Moved_Duplicate => MovedDuplicateColor,
            DecisionReason.Moved_ReverseGearsetRef => MovedNoRefColor,
            _ => new Vector4(1f, 1f, 1f, 1f),
        };

        private static void Icon(FontAwesomeIcon icon)
        {
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                ImGui.TextUnformatted(icon.ToIconString());
        }
    }
}