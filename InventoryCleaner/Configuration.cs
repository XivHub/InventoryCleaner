using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace InventoryCleaner
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;

        public bool KeepEquipped { get; set; } = true;

        public bool KeepGearsetRef { get; set; } = true;

        public bool KeepUpgrade { get; set; } = true;

        public int UpgradeMarginIlvl { get; set; } = 0;

        public bool MoveNoGearsetRef { get; set; } = true;

        public List<uint> AllowlistItemIds { get; set; } = new List<uint>();

        /// <summary>
        /// Discard-grid cell size, in text line heights. Clamped to 2-8 when drawn so the grid stays
        /// usable at any font size.
        /// </summary>
        public float DiscardCellScale { get; set; } = 3.5f;

        /// <summary>
        /// Pause after answering each native discard confirmation, in milliseconds, before the next
        /// item is issued. Raise it if confirmations start being missed.
        /// </summary>
        public int DiscardConfirmDelayMs { get; set; } = 500;

        /// <summary>
        /// ClassJob RowIds excluded from the keep-upgrades check. A gearset whose job is listed here
        /// no longer keeps higher-ilvl armoury pieces as upgrades (equipped items and exact gearset
        /// references are still kept). Empty = every gearset job keeps its upgrades (default).
        /// </summary>
        public List<uint> DisabledUpgradeJobs { get; set; } = new List<uint>();

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
            this.AllowlistItemIds ??= new List<uint>();
            this.DisabledUpgradeJobs ??= new List<uint>();
        }

        public void Save()
        {
            this.pluginInterface!.SavePluginConfig(this);
        }
    }
}