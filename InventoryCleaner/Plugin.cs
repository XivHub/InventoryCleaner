using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using InventoryCleaner.Windows;

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

        public Configuration Configuration { get; init; }
        public static Configuration C { get; private set; } = null!;
        public WindowSystem WindowSystem = new("InventoryCleaner");
        private readonly MainWindow mainWindow;
        private readonly ConfigWindow configWindow;

        public Plugin()
        {
            ECommonsMain.Init(PluginInterface, this, Module.DalamudReflector);

            this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(PluginInterface);
            C = this.Configuration;

            mainWindow = new MainWindow(this.Configuration);
            configWindow = new ConfigWindow(this.Configuration);
            WindowSystem.AddWindow(mainWindow);
            WindowSystem.AddWindow(configWindow);

            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the InventoryCleaner window."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

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
                case "start":
                    ChatGui.Print("[InventoryCleaner] start");
                    break;
                case "stop":
                    ChatGui.Print("[InventoryCleaner] stop");
                    break;
                default:
                    mainWindow.IsOpen = true;
                    break;
            }
        }

        private void ToggleMainUi() => mainWindow.Toggle();
        private void ToggleConfigUi() => configWindow.Toggle();
        private void DrawUI() => WindowSystem.Draw();
    }
}