using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CruciblePilot;

/// <summary>
/// CruciblePilot: a small combat companion for the Beastmaster Crucible. It sets the Crucible Mode, runs the
/// Beastmaster rotation, summons the battlehorns, drinks crucible potions and presses the per-horn finisher.
/// Routing, shopping and presets are left to Pawprint.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/cp";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly DegreeHelper degree;
    private readonly WhistleHelper whistle;
    private readonly CrucibleItems crucibleItems;
    private readonly CrucibleCombat crucibleCombat;
    private readonly Ui ui;
    private readonly CpLog cpLog;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IPluginLog log,
        IFramework framework,
        IGameGui gameGui,
        IClientState clientState,
        IObjectTable objects,
        ICondition condition,
        ITargetManager targetManager,
        IGameInteropProvider interop)
    {
        Instance = this;
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.log = log;
        this.framework = framework;

        this.Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.cpLog = new CpLog(pluginInterface.GetPluginConfigDirectory());

        this.degree = new DegreeHelper(this, gameGui, clientState);
        this.whistle = new WhistleHelper(this, objects, targetManager, condition, clientState, gameGui);
        this.crucibleItems = new CrucibleItems(interop, gameGui, log);
        this.crucibleCombat = new CrucibleCombat(this, objects, targetManager, condition, clientState, gameGui, this.crucibleItems);
        this.ui = new Ui(this);

        this.commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Toggle the CruciblePilot window.",
            ShowInHelp = true,
        });

        this.pluginInterface.UiBuilder.Draw += this.ui.Draw;
        this.framework.Update += this.OnFrameworkUpdate;

        this.cpLog.Info($"loaded v{typeof(Plugin).Assembly.GetName().Version} - log file ");
        this.cpLog.Info($"config: degree={this.Config.Degree} forceDegree={this.Config.ForceDegree} combat={this.Config.EnableCombat} whistle={this.Config.WhistlePlan}");
        this.log.Information("[CP] CruciblePilot loaded");
    }

    public static Plugin Instance { get; private set; } = null!;

    public Configuration Config { get; }

    public CpLog CpLog => this.cpLog;

    public void SaveConfig() => this.pluginInterface.SavePluginConfig(this.Config);

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw -= this.ui.Draw;
        this.commands.RemoveHandler(CommandName);
        this.crucibleItems.Dispose();
        this.cpLog.Info("unloaded");
        this.cpLog.Dispose();
        this.log.Information("[CP] CruciblePilot unloaded");
    }

    private void OnCommand(string command, string args) => this.ui.Toggle();

    private void OnFrameworkUpdate(IFramework fw)
    {
        this.degree.Tick();
        this.whistle.Tick();
        this.crucibleCombat.Tick();
    }
}
