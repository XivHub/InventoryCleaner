using System.Buffers.Binary;
using System.Collections.Generic;
using Dalamud.Utility;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using KitItemSheet = XivHubPluginKit.Inventory.ItemSheet;

namespace InventoryCleaner.Game;

/// <summary>
/// Thin cached wrappers over the Lumina <see cref="Item"/> sheet. The generic accessors
/// (<see cref="ById"/>/<see cref="Name"/>/<see cref="Ilvl"/>/<see cref="EquipSlotCategory"/>/
/// <see cref="Rarity"/>) forward to the shared <see cref="KitItemSheet"/>; <see cref="ItemSheetOrNull"/>
/// and the ClassJob/<see cref="CanJobEquip"/>/job accessors stay local (gearset-specific, not part of
/// the kit surface).
/// </summary>
internal static class Sheets
{
    private static readonly ExcelSheet<Item>? ItemSheet = Plugin.DataManager.GetExcelSheet<Item>();

    /// <summary>Cache of ClassJobCategory RowId → the set of ClassJob RowIds it permits.</summary>
    private static readonly Dictionary<uint, HashSet<uint>> JobsByCategory = new();

    /// <summary>The cached <see cref="Item"/> sheet, or null if unavailable (e.g. before login).</summary>
    internal static ExcelSheet<Item>? ItemSheetOrNull => ItemSheet;

    /// <summary>Fetches an <see cref="Item"/> row by id; null for id 0 or when not found / sheet missing.</summary>
    internal static Item? ById(uint itemId) => KitItemSheet.ById(itemId);

    /// <summary>Item name; empty string for id 0 / not found / sheet missing.</summary>
    internal static string Name(uint itemId) => KitItemSheet.Name(itemId);

    /// <summary>Item level (<see cref="Item.LevelItem"/> RowId); 0 if not found.</summary>
    internal static ushort Ilvl(uint itemId) => KitItemSheet.Ilvl(itemId);

    /// <summary>Equip slot category (<see cref="Item.EquipSlotCategory"/> RowId); 0 if not found.</summary>
    internal static byte EquipSlotCategory(uint itemId) => KitItemSheet.EquipSlotCategory(itemId);

    /// <summary>Rarity (<see cref="Item.Rarity"/>); 0 if not found.</summary>
    internal static byte Rarity(uint itemId) => KitItemSheet.Rarity(itemId);

    /// <summary>Game icon id (<see cref="Item.Icon"/>); 0 if not found.</summary>
    internal static ushort IconId(uint itemId)
    {
        var row = ById(itemId);
        return row.HasValue ? row.Value.Icon : (ushort)0;
    }

    /// <summary>
    /// The item-name rarity color (white / green / blue / purple / pink) as an ImGui-packed ABGR
    /// value, resolved the same way the game resolves it: <see cref="ItemUtil.GetItemRarityColorType"/>
    /// maps the item's rarity to a <see cref="UIColor"/> row, whose big-endian RGBA <c>Dark</c> value
    /// is byte-reversed into ImGui's ABGR order. Falls back to opaque white.
    /// </summary>
    internal static uint RarityColorU32(uint itemId, bool edge = false)
    {
        var row = UIColorSheet?.GetRowOrDefault(ItemUtil.GetItemRarityColorType(itemId, edge));
        return row.HasValue
            ? BinaryPrimitives.ReverseEndianness(row.Value.Dark) | 0xFF000000u
            : 0xFFFFFFFFu;
    }

    /// <summary>
    /// Whether <paramref name="classJobId"/> (a <c>ClassJob</c> RowId, as stored on a gearset) may
    /// equip the item, per the item's <see cref="Item.ClassJobCategory"/>. Items with no category
    /// (RowId 0) are treated as equippable by anyone. Used so an armoury piece is only ever counted
    /// as an "upgrade" for a gearset whose job can actually wear it.
    /// </summary>
    internal static bool CanJobEquip(uint itemId, uint classJobId)
    {
        var row = ById(itemId);
        if (!row.HasValue)
            return false;

        var catRef = row.Value.ClassJobCategory;
        if (catRef.RowId == 0)
            return true;

        if (!JobsByCategory.TryGetValue(catRef.RowId, out var jobs))
        {
            jobs = BuildJobSet(catRef.ValueNullable);
            JobsByCategory[catRef.RowId] = jobs;
        }

        return jobs.Contains(classJobId);
    }

    private static readonly ExcelSheet<UIColor>? UIColorSheet = Plugin.DataManager.GetExcelSheet<UIColor>();

    private static readonly ExcelSheet<ClassJob>? ClassJobSheet = Plugin.DataManager.GetExcelSheet<ClassJob>();

    /// <summary>Uppercase job abbreviation (e.g. "PLD") for a ClassJob RowId; empty if not found.</summary>
    internal static string JobAbbrev(uint classJobId)
    {
        var row = ClassJobSheet?.GetRowOrDefault(classJobId);
        return row.HasValue ? row.Value.Abbreviation.ToString() : string.Empty;
    }

    /// <summary>Full job name (e.g. "Paladin") for a ClassJob RowId; empty if not found.</summary>
    internal static string JobName(uint classJobId)
    {
        var row = ClassJobSheet?.GetRowOrDefault(classJobId);
        return row.HasValue ? row.Value.Name.ToString() : string.Empty;
    }

    /// <summary>
    /// Game icon id for the framed job icon (62100 + RowId). Needs in-game confirmation; callers
    /// should fall back to <see cref="JobAbbrev"/> text if the texture fails to load.
    /// </summary>
    internal static uint JobIconId(uint classJobId) => 62100 + classJobId;

    /// <summary>Collects the ClassJob RowIds permitted by a <see cref="ClassJobCategory"/> row.</summary>
    private static HashSet<uint> BuildJobSet(ClassJobCategory? category)
    {
        var set = new HashSet<uint>();
        if (category == null)
            return set;

        var c = category.Value;
        // ClassJob RowId → its boolean column on the category row (RowIds match the in-game
        // ClassJob sheet order: 1 GLA .. 42 PCT). Adventurer (0) is never a gearset job.
        void Add(uint jobId, bool allowed)
        {
            if (allowed)
                set.Add(jobId);
        }

        Add(1, c.GLA); Add(2, c.PGL); Add(3, c.MRD); Add(4, c.LNC); Add(5, c.ARC);
        Add(6, c.CNJ); Add(7, c.THM); Add(8, c.CRP); Add(9, c.BSM); Add(10, c.ARM);
        Add(11, c.GSM); Add(12, c.LTW); Add(13, c.WVR); Add(14, c.ALC); Add(15, c.CUL);
        Add(16, c.MIN); Add(17, c.BTN); Add(18, c.FSH); Add(19, c.PLD); Add(20, c.MNK);
        Add(21, c.WAR); Add(22, c.DRG); Add(23, c.BRD); Add(24, c.WHM); Add(25, c.BLM);
        Add(26, c.ACN); Add(27, c.SMN); Add(28, c.SCH); Add(29, c.ROG); Add(30, c.NIN);
        Add(31, c.MCH); Add(32, c.DRK); Add(33, c.AST); Add(34, c.SAM); Add(35, c.RDM);
        Add(36, c.BLU); Add(37, c.GNB); Add(38, c.DNC); Add(39, c.RPR); Add(40, c.SGE);
        Add(41, c.VPR); Add(42, c.PCT);
        return set;
    }
}