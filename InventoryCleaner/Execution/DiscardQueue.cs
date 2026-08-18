using System;
using System.Collections.Generic;
using ECommons;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InventoryItem = InventoryCleaner.Models.InventoryItem;

namespace InventoryCleaner.Execution;

/// <summary>
/// Frame-driven executor for item destruction. Each item goes through the game's own discard flow:
/// <see cref="AgentInventoryContext.DiscardItem"/> raises the native "Discard?" confirmation, the
/// queue waits for that specific dialog, clicks Yes, then verifies the slot actually emptied before
/// moving on. Nothing is destroyed without the game's own dialog having been raised and answered.
///
/// The source slot is re-read live immediately before every discard and must still hold the item id
/// the plan was built from; anything else is skipped rather than discarded, so a stale grid can
/// never destroy whatever moved into that slot in the meantime.
/// </summary>
internal sealed unsafe class DiscardQueue
{
    /// <summary>
    /// Substrings of the native discard confirmation across the client languages, matched against the
    /// dialog's prompt so an unrelated Yes/No (a trade, a quest) is never answered by this queue.
    /// </summary>
    private static readonly string[] DiscardPrompts =
    {
        "Discard",
        "を捨てます。",
        "wegwerfen",
        "Jeter",
        "确定要舍弃",
        "確定要捨棄",
    };

    private const int DialogTimeoutMs = 5000;
    private const int MinVerifyDelayMs = 400;
    private const int MaxConfirmRetries = 3;

    private enum Step
    {
        Idle,
        WaitDialog,
        Cooldown,
    }

    private readonly Queue<InventoryItem> queue = new();
    private Step step = Step.Idle;
    private long stepStartedMs;
    private int confirmRetries;

    /// <summary>Items not yet processed this run.</summary>
    internal int Pending => this.queue.Count;

    /// <summary>Items confirmed destroyed this run.</summary>
    internal int Done { get; private set; }

    /// <summary>Items whose discard was issued but never confirmed gone.</summary>
    internal int Failed { get; private set; }

    /// <summary>Items whose source slot no longer held them when their turn came.</summary>
    internal int Skipped { get; private set; }

    /// <summary>The item currently being processed, null when idle.</summary>
    internal InventoryItem? Current { get; private set; }

    /// <summary>True while there are items to process or one is in flight.</summary>
    internal bool IsRunning { get; private set; }

    /// <summary>Resets all run state and loads <paramref name="items"/> as the new work set.</summary>
    internal void Enqueue(IEnumerable<InventoryItem> items)
    {
        this.queue.Clear();
        foreach (var item in items)
            this.queue.Enqueue(item);
        this.Done = 0;
        this.Failed = 0;
        this.Skipped = 0;
        this.Current = null;
        this.step = Step.Idle;
        this.confirmRetries = 0;
        this.IsRunning = true;
    }

    /// <summary>
    /// Advances the state machine by one frame. Returns false once the queue has drained and the run
    /// is over, true while work remains (including while merely waiting on a dialog).
    /// </summary>
    internal bool Tick()
    {
        if (!this.IsRunning)
            return false;

        if (!GenericHelpers.IsScreenReady())
            return true;

        return this.step switch
        {
            Step.WaitDialog => this.TickWaitDialog(),
            Step.Cooldown => this.TickCooldown(),
            _ => this.TickIdle(),
        };
    }

    /// <summary>Hard-stops the run: clears the queue and leaves any open dialog for the player.</summary>
    internal void Abort()
    {
        this.queue.Clear();
        this.IsRunning = false;
        this.Current = null;
        this.step = Step.Idle;
        this.confirmRetries = 0;
    }

    private bool TickIdle()
    {
        if (this.queue.Count == 0)
        {
            this.IsRunning = false;
            this.Current = null;
            return false;
        }

        // A dialog standing open before we issue anything is someone else's; answering it would be
        // answering a prompt we did not raise.
        if (FindDiscardDialog() != null)
        {
            Plugin.ChatGui.Print("[InventoryCleaner] A discard confirmation is already open; aborting.");
            this.Abort();
            return false;
        }

        var item = this.queue.Dequeue();
        this.Current = item;

        // Re-checked here and not only at plan time: nothing reaches the game's discard call without
        // passing the protection rules on the same frame it is issued.
        if (Logic.ItemProtection.IsProtected(item, out var reason))
        {
            Plugin.Logger.Warning($"Refusing to discard {item.Name}: {reason}.");
            this.Skipped++;
            return true;
        }

        var slot = LiveSlot(item);
        if (slot == null)
        {
            this.Skipped++;
            return true;
        }

        AgentInventoryContext.Instance()->DiscardItem(slot, item.Container, item.SlotIndex, 0);
        this.step = Step.WaitDialog;
        this.stepStartedMs = Environment.TickCount64;
        this.confirmRetries = 0;
        return true;
    }

    private bool TickWaitDialog()
    {
        var dialog = FindDiscardDialog();
        if (dialog != null)
        {
            dialog.Yes();
            this.step = Step.Cooldown;
            this.stepStartedMs = Environment.TickCount64;
            return true;
        }

        if (Environment.TickCount64 - this.stepStartedMs > DialogTimeoutMs)
        {
            Plugin.Logger.Warning($"Discard confirmation never appeared for {this.Current?.Name}; skipping.");
            this.Failed++;
            this.step = Step.Idle;
        }

        return true;
    }

    private bool TickCooldown()
    {
        var delay = Math.Max(MinVerifyDelayMs, Plugin.C.DiscardConfirmDelayMs);
        if (Environment.TickCount64 - this.stepStartedMs < delay)
            return true;

        // The click can land while the dialog is still animating in, in which case it is ignored and
        // the dialog is still standing; re-click rather than counting the item as failed.
        var dialog = FindDiscardDialog();
        if (dialog != null)
        {
            if (++this.confirmRetries > MaxConfirmRetries)
            {
                Plugin.Logger.Warning($"Discard confirmation for {this.Current?.Name} would not close; skipping.");
                this.Failed++;
                this.step = Step.Idle;
                return true;
            }

            dialog.Yes();
            this.stepStartedMs = Environment.TickCount64;
            return true;
        }

        if (this.Current != null && LiveSlot(this.Current) == null)
            this.Done++;
        else
            this.Failed++;

        this.step = Step.Idle;
        this.confirmRetries = 0;
        return true;
    }

    /// <summary>
    /// The live game slot backing <paramref name="item"/>, or null if that slot no longer holds the
    /// same item id (moved, already destroyed, or replaced).
    /// </summary>
    private static FFXIVClientStructs.FFXIV.Client.Game.InventoryItem* LiveSlot(InventoryItem item)
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null)
            return null;

        var slot = mgr->GetInventorySlot(item.Container, item.SlotIndex);
        return slot != null && slot->ItemId == item.ItemId ? slot : null;
    }

    /// <summary>
    /// The first ready <c>SelectYesno</c> whose prompt reads as a discard confirmation, or null when
    /// no such dialog is open. Indices are 1-based and contiguous, so the first missing one ends the
    /// scan.
    /// </summary>
    private static AddonMaster.SelectYesno? FindDiscardDialog()
    {
        for (var i = 1; i < 8; i++)
        {
            var addon = Plugin.GameGui.GetAddonByName<AtkUnitBase>("SelectYesno", i);
            if (addon == null)
                return null;
            if (!GenericHelpers.IsAddonReady(addon))
                continue;

            var master = new AddonMaster.SelectYesno(addon);
            string prompt;
            try
            {
                prompt = master.Text;
            }
            catch (Exception ex)
            {
                Plugin.Logger.Debug(ex, "Could not read SelectYesno prompt");
                continue;
            }

            foreach (var needle in DiscardPrompts)
            {
                if (prompt.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return master;
            }
        }

        return null;
    }
}
