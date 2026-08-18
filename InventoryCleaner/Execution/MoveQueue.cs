using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using InventoryCleaner.Logic;
using InventoryCleaner.Models;
using ZhyraPluginKit.Inventory;
using KitMoveQueue = ZhyraPluginKit.Inventory.MoveQueue;

namespace InventoryCleaner.Execution;

/// <summary>
/// Adapts InventoryCleaner's <see cref="MovePlan"/>-based API onto the shared kit
/// <see cref="KitMoveQueue"/> (<see cref="MoveOp"/>-based). Maps each <see cref="MovePlan"/> to a
/// <see cref="MoveOp"/> at <see cref="Enqueue"/> time — <c>DstCandidates</c> is
/// <c>Inventory1..4</c> in clean mode, or the single armoury container matching
/// <see cref="MovePlan.Slot"/> in reverse mode — and maps the kit's <see cref="MoveOp"/>
/// <see cref="KitMoveQueue.Current"/> back to the originating <see cref="MovePlan"/> (by source
/// container+slot, which uniquely identifies each enqueued plan for the run) so callers still see a
/// <see cref="MovePlan"/>. Reproduces the original <c>Execution/MoveQueue.cs</c> behavior exactly;
/// only the queue mechanics moved to the kit.
/// </summary>
internal sealed class MoveQueue
{
    private static readonly InventoryType[] MainInventoryContainers =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    };

    private readonly KitMoveQueue inner = new();
    private Dictionary<(InventoryType, ushort), MovePlan> plansBySource = new();

    /// <summary>Plans not yet executed this run.</summary>
    internal int Pending => this.inner.Pending;

    /// <summary>Successfully moved this run.</summary>
    internal int Done => this.inner.Done;

    /// <summary>Failed moves this run.</summary>
    internal int Failed => this.inner.Failed;

    /// <summary>True while there are plans to execute or one is in flight.</summary>
    internal bool IsRunning => this.inner.IsRunning;

    /// <summary>The plan currently being processed (most recently dequeued), null when idle.</summary>
    internal MovePlan? Current
    {
        get
        {
            var op = this.inner.Current;
            if (op == null)
                return null;
            return this.plansBySource.TryGetValue((op.Value.SrcType, op.Value.SrcSlot), out var plan)
                ? plan
                : null;
        }
    }

    /// <summary>
    /// Resets all run state and loads <paramref name="plans"/> as the new work set. When
    /// <paramref name="reverse"/> is true, destinations are armoury containers matching each
    /// plan's <see cref="MovePlan.Slot"/> instead of the main inventory.
    /// </summary>
    internal void Enqueue(IEnumerable<MovePlan> plans, bool reverse = false)
    {
        var planList = plans as IReadOnlyList<MovePlan> ?? plans.ToList();

        var bySource = new Dictionary<(InventoryType, ushort), MovePlan>();
        foreach (var plan in planList)
            bySource[(plan.Item.Container, (ushort)plan.Item.SlotIndex)] = plan;
        this.plansBySource = bySource;

        var ops = planList.Select(p => ToMoveOp(p, reverse));
        if (reverse)
            this.inner.Enqueue(ops, reverse: true, destinationNoun: "Armoury container");
        else
            this.inner.Enqueue(ops);
    }

    /// <summary>
    /// Executes at most one move. Returns true if a move was attempted (or the queue still has
    /// pending work), false when the queue has drained and the run is over.
    /// </summary>
    internal bool Tick() => this.inner.Tick();

    /// <summary>Hard-stops the current run: clears the queue and resets <see cref="IsRunning"/>.</summary>
    internal void Abort() => this.inner.Abort();

    private static MoveOp ToMoveOp(MovePlan plan, bool reverse)
    {
        IReadOnlyList<InventoryType> dst = reverse
            ? new[] { EquipSlotExtensions.ToArmouryContainer(plan.Slot) }
            : MainInventoryContainers;
        return new MoveOp(plan.Item.Container, (ushort)plan.Item.SlotIndex, plan.Item.ItemId, dst);
    }
}
