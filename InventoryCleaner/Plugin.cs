using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using InventoryCleaner.Execution;
using InventoryCleaner.Game;
using InventoryCleaner.Logic;
using InventoryCleaner.Windows;
using ZhyraPluginKit;

namespace InventoryCleaner
{
    public sealed class Plugin : IDalamudPlugin
    {
        public static string Name => "InventoryCleaner";

        private const string commandName = "/inventorycleaner";

        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IDataManager DataManager { get; private set; } = null!;
        [PluginService] public static IPluginLog Logger { get; private set; } = null!;
        [PluginService] public static IClientState ClientState { get; private set; } = null!;
        [PluginService] public static ICondition Condition { get; private set; } = null!;
        [PluginService] public static IFramework Framework { get; private set; } = null!;
        [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] public static ITextureProvider Texture { get; private set; } = null!;
        [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
        [PluginService] public static IGameGui GameGui { get; private set; } = null!;

        public Configuration Configuration { get; init; }
        public static Configuration C { get; private set; } = null!;
        public WindowSystem WindowSystem = new("InventoryCleaner");
        private readonly MainWindow mainWindow;
        private readonly ConfigWindow configWindow;
        private readonly AllowlistContextMenu allowlistContextMenu;

        internal MoveQueue MoveQueue { get; }
        internal DiscardQueue DiscardQueue { get; }
        internal List<MovePlan>? PendingPreview { get; set; }
        internal bool pendingReverse;

        public Plugin()
        {
            KitServices.Init(DataManager, Logger, ChatGui, "[InventoryCleaner]");

            ECommonsMain.Init(PluginInterface, this, Module.DalamudReflector);

            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(PluginInterface);
            C = this.Configuration;

            mainWindow = new MainWindow(this, this.Configuration);
            configWindow = new ConfigWindow(this.Configuration);
            WindowSystem.AddWindow(mainWindow);
            WindowSystem.AddWindow(configWindow);

            this.MoveQueue = new MoveQueue();
            this.DiscardQueue = new DiscardQueue();
            this.allowlistContextMenu = new AllowlistContextMenu(this.Configuration);

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the InventoryCleaner window. Subcommands: clean, reverse, discard, confirm, cancel, stop."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
            Framework.Update += OnFrameworkUpdate;
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
            Framework.Update -= OnFrameworkUpdate;

            allowlistContextMenu.Dispose();

            WindowSystem.RemoveAllWindows();
            mainWindow.Dispose();
            configWindow.Dispose();

            CommandManager.RemoveHandler(commandName);

            ECommonsMain.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            var arg = args.Trim().ToLowerInvariant();
            switch (arg)
            {
                case "clean":
                case "start":
                    this.PendingPreview = CleanPlanner.Plan();
                    this.pendingReverse = false;
                    ChatGui.Print($"[InventoryCleaner] Preview built: {this.PendingPreview.Count} plans. Use '/inventorycleaner confirm' to run, '/inventorycleaner cancel' to discard.");
                    break;
                case "reverse":
                    this.PendingPreview = ReversePlanner.Plan(C);
                    this.pendingReverse = true;
                    ChatGui.Print($"[InventoryCleaner] Reverse preview built: {this.PendingPreview.Count} plans. Use '/inventorycleaner confirm' to run, '/inventorycleaner cancel' to discard.");
                    break;
                case "confirm":
                    if (this.PendingPreview == null)
                    {
                        ChatGui.Print("[InventoryCleaner] No preview to confirm. Run '/inventorycleaner clean' or '/inventorycleaner reverse' first.");
                        break;
                    }
                    if (this.MoveQueue.IsRunning)
                    {
                        ChatGui.Print("[InventoryCleaner] A run is already in progress; use '/inventorycleaner stop' first.");
                        break;
                    }
                    var toMove = this.PendingPreview.Where(p => !IsKept(p.Reason)).ToList();
                    this.MoveQueue.Enqueue(toMove, this.pendingReverse);
                    ChatGui.Print($"[InventoryCleaner] Enqueued {toMove.Count} moves; running.");
                    this.PendingPreview = null;
                    this.pendingReverse = false;
                    break;
                case "cancel":
                    this.PendingPreview = null;
                    this.pendingReverse = false;
                    ChatGui.Print("[InventoryCleaner] Preview discarded.");
                    break;
                case "discard":
                    mainWindow.OpenDiscard();
                    break;
                case "stop":
                    this.MoveQueue.Abort();
                    this.DiscardQueue.Abort();
                    ChatGui.Print("[InventoryCleaner] Stopped.");
                    break;
                default:
                    mainWindow.IsOpen = true;
                    break;
            }
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (this.MoveQueue.IsRunning)
                this.MoveQueue.Tick();
            else if (this.DiscardQueue.IsRunning)
                this.DiscardQueue.Tick();
        }

        internal static bool IsKept(DecisionReason reason)
            => reason == DecisionReason.Kept_Equipped
               || reason == DecisionReason.Kept_GearsetRef
               || reason == DecisionReason.Kept_Upgrade
               || reason == DecisionReason.Kept_Allowlist;

        private void ToggleMainUi() => mainWindow.Toggle();
        private void ToggleConfigUi() => configWindow.Toggle();
        private void DrawUI() => WindowSystem.Draw();
    }
}