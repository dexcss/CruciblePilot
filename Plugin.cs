using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CruciblePilot;

/// <summary>
/// CruciblePilot: AutoDuty drives the board, this plugin sets the degree and puts the beasts you want on each
/// battlehorn before every fight (AutoDuty's own FightPicks can't look at the encounter).
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public const string CommandName = "/cp";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly AutoDutyIpc autoDuty;
    private readonly AutoDutyTeam autoDutyTeam;
    private readonly EnemyIntel intel;
    private readonly FoodHelper food;
    private readonly HornAutopilot horns;
    private readonly Ui ui;
    private readonly CpLog cpLog;
    private bool pushedOverrides;
    private (uint Territory, Dictionary<string, string> Overrides, List<uint> Roster)? pendingStart;
    private int foodTries;
    private DateTime nextFoodTry;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IChatGui chat,
        IPluginLog log,
        IFramework framework,
        IGameGui gameGui,
        IClientState clientState,
        IObjectTable objects,
        IDataManager data)
    {
        Instance = this;
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.chat = chat;
        this.log = log;
        this.framework = framework;

        this.Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.cpLog = new CpLog(pluginInterface.GetPluginConfigDirectory());
        this.autoDuty = new AutoDutyIpc(pluginInterface, log);
        this.autoDutyTeam = new AutoDutyTeam(log);
        this.intel = new EnemyIntel(objects, data, log);
        this.food = new FoodHelper(this, objects, data);
        this.horns = new HornAutopilot(this, log, gameGui, clientState);
        this.ui = new Ui(this);

        this.commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "CruciblePilot window; subcommands: board, degree, trio, auto, start, stop, status, team, dump",
            ShowInHelp = true,
        });

        this.framework.Update += this.OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw += this.ui.Draw;
        this.log.Information("[CP] CruciblePilot loaded");
        this.cpLog.Info($"loaded v{typeof(Plugin).Assembly.GetName().Version} - log file {this.cpLog.CurrentFile}");
        this.cpLog.Info($"config: board={this.Config.Board} degree={Configuration.DegreeName(this.Config.Degree)} autopilot={this.Config.Autopilot} autopick={this.Config.AutoPick} defaults={this.Config.UseDefaults} roster=[" + string.Join(",", this.Config.RosterFor(this.Config.Board)) + "]");
    }

    public static Plugin Instance { get; private set; } = null!;

    public Configuration Config { get; }

    public EnemyIntel Intel => this.intel;

    public CpLog CpLog => this.cpLog;

    public string Name => "CruciblePilot";

    public void Chat(string message)
    {
        this.chat.Print(message);
        this.cpLog.Info("chat: " + message);
    }

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
        this.pluginInterface.UiBuilder.Draw -= this.ui.Draw;
        this.commands.RemoveHandler(CommandName);
        if (this.pushedOverrides && this.autoDuty.IsRunning() != true)
        {
            this.autoDuty.Pop();
        }

        this.cpLog.Info("unloaded");
        this.cpLog.Dispose();
        this.log.Information("[CP] CruciblePilot unloaded");
    }

    public void SaveConfig() => this.pluginInterface.SavePluginConfig(this.Config);

    public bool IsAutoDutyRunning() => this.autoDuty.IsRunning() == true;

    public string GetStatusLine()
    {
        uint territory = (uint)(1338 + this.Config.Board);
        bool? running = this.autoDuty.IsRunning();
        bool? hasPath = this.autoDuty.HasPath(territory);
        return $"AD: {running switch { true => "running", false => "idle", null => "no IPC" }} | path: {hasPath switch { true => "yes", false => "no", null => "?" }} | territory {territory} | degree {Configuration.DegreeName(this.Config.Degree)}";
    }

    public string GetHornLine() => "Horns: " + this.horns.Dump();

    public string GetEnemyLine()
    {
        var enemies = this.intel.CurrentEnemies();
        return enemies.Count == 0
            ? "Enemies: none in range"
            : "Enemies: " + string.Join(", ", enemies);
    }

    public string GetPickLine()
    {
        var last = this.horns.LastEnemies;
        string enemies = last.Count == 0 ? "(none yet)" : string.Join(", ", last);
        return $"Auto pick: {this.horns.LastPickReason} | seen: {enemies}";
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        this.horns.Tick();
        this.TickPendingStart();
    }

    private void OnCommand(string command, string arguments)
    {
        var parts = arguments.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "ui";
        this.cpLog.Info("/cp " + arguments);
        try
        {
            switch (verb)
            {
                case "ui":
                    this.Config.ShowWindow = !this.Config.ShowWindow;
                    this.SaveConfig();
                    break;
                case "board":
                    this.SetBoard(parts);
                    break;
                case "degree":
                    this.SetDegree(parts);
                    break;
                case "trio":
                    this.SetTrio(parts);
                    break;
                case "auto":
                    this.SetAuto(parts);
                    break;
                case "start":
                    this.StartRun();
                    break;
                case "stop":
                    this.StopRun();
                    break;
                case "status":
                    this.Chat("[CruciblePilot] " + this.GetStatusLine());
                    break;
                case "dump":
                    this.Chat("[CruciblePilot] " + this.GetHornLine());
                    break;
                case "log":
                    this.Chat("[CruciblePilot] log: " + this.cpLog.CurrentFile);
                    break;
                case "scan":
                    this.Chat("[CruciblePilot] " + this.GetEnemyLine());
                    var roster = this.intel.Profiles().Keys.ToList();
                    this.Chat("[CruciblePilot] profiles: " + this.intel.DescribeProfiles(roster.Take(8)));
                    var pick = this.intel.PickTrio(this.Config.Trio(this.Config.Board), out string reason);
                    this.Chat($"[CruciblePilot] pick for board {this.Config.Board}: {string.Join(",", pick)} ({reason})");
                    break;
                case "team":
                    this.ApplyTeam();
                    break;
                default:
                    this.Help();
                    break;
            }
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "[CP] command failed: {Args}", arguments);
            this.Chat("[CruciblePilot] error: " + ex.Message);
        }
    }

    public void SetBoard(string[] parts)
    {
        if (parts.Length < 2 || !int.TryParse(parts[1], out int board) || board < 1 || board > 5)
        {
            this.Chat("[CruciblePilot] usage: /cp board 1-5");
            return;
        }

        this.Config.Board = board;
        this.SaveConfig();
        this.Chat($"[CruciblePilot] board set to {board} (territory {1338 + board})");
    }

    public void SetDegree(string[] parts)
    {
        if (parts.Length < 2)
        {
            this.Chat($"[CruciblePilot] degree is {Configuration.DegreeName(this.Config.Degree)}");
            return;
        }

        int? degree = parts[1].ToLowerInvariant() switch
        {
            "standard" or "normal" or "0" => 0,
            "first" or "1" => 1,
            "second" or "2" => 2,
            "third" or "3" => 3,
            _ => null,
        };

        if (degree == null)
        {
            this.Chat("[CruciblePilot] usage: /cp degree standard|first|second|third");
            return;
        }

        this.Config.Degree = degree.Value;
        this.SaveConfig();
        this.Chat($"[CruciblePilot] degree set to {Configuration.DegreeName(this.Config.Degree)}");
    }

    public void SetTrio(string[] parts)
    {
        if (parts.Length == 1)
        {
            var current = this.Config.Trio(this.Config.Board);
            this.Chat($"[CruciblePilot] board {this.Config.Board} trio: {(current.Count == 0 ? "none" : string.Join(", ", current))}");
            return;
        }

        if (parts.Length != 4)
        {
            this.Chat("[CruciblePilot] usage: /cp trio <beast1> <beast2> <beast3>");
            return;
        }

        var trio = new List<uint>();
        foreach (var part in parts.Skip(1))
        {
            if (!uint.TryParse(part, out uint number) || number < 1 || number > 50)
            {
                this.Chat($"[CruciblePilot] not a familiar number 1-50: {part}");
                return;
            }

            trio.Add(number);
        }

        this.Config.Trios[this.Config.Board] = trio;
        this.SaveConfig();
        this.Chat($"[CruciblePilot] board {this.Config.Board} trio set to {string.Join(", ", trio)}");
    }

    public void SetAuto(string[] parts)
    {
        if (parts.Length < 2)
        {
            this.Chat($"[CruciblePilot] autopilot is {(this.Config.Autopilot ? "on" : "off")}");
            return;
        }

        this.Config.Autopilot = parts[1].ToLowerInvariant() is "on" or "true" or "1";
        this.SaveConfig();
        this.Chat($"[CruciblePilot] autopilot {(this.Config.Autopilot ? "on" : "off")}");
    }

    public void ApplyTeam()
    {
        var trio = this.Config.RosterFor(this.Config.Board);
        if (trio.Count == 0)
        {
            this.Chat("[CruciblePilot] pick a trio first");
            return;
        }

        this.Chat(this.autoDutyTeam.Set(trio)
            ? $"[CruciblePilot] AutoDuty custom team set to {string.Join(", ", trio)}"
            : "[CruciblePilot] could not set AutoDuty's team (build may differ) - set it in AutoDuty's Crucible tab");
    }

    public void StartRun()
    {
        var roster = this.Config.RosterFor(this.Config.Board).Distinct().Where(n => n is >= 1 and <= 50).ToList();
        if (roster.Count < 3)
        {
            this.Chat("[CruciblePilot] set a roster of at least three familiars (window: Roster field)");
            return;
        }

        if (!this.autoDuty.IsAvailable())
        {
            this.Chat("[CruciblePilot] AutoDuty is not answering - install/enable AutoDuty (erdelf build) first");
            return;
        }

        if (this.autoDuty.IsRunning() == true)
        {
            this.Chat("[CruciblePilot] AutoDuty is already running - stop it first");
            return;
        }

        uint territory = (uint)(1338 + this.Config.Board);
        if (this.autoDuty.HasPath(territory) == false)
        {
            this.Chat($"[CruciblePilot] this AutoDuty build has no path for territory {territory}");
            return;
        }

        this.cpLog.Info("run: board=" + this.Config.Board + " degree=" + Configuration.DegreeName(this.Config.Degree) + " roster=[" + string.Join(",", roster) + "]");
        if (this.Config.SetAutoDutyTeam)
        {
            this.autoDutyTeam.Set(roster);
        }

        var overrides = new Dictionary<string, string>
        {
            ["Meta.DutyModeEnum"] = "Crucible",
            ["Meta.Crucible.Degree"] = Configuration.DegreeName(this.Config.Degree),
            ["Meta.Crucible.TeamMode"] = "Custom",
            ["Meta.Crucible.FightPicks"] = this.Config.Autopilot ? "false" : "true",
            ["DutyConfig.AutoExitDuty"] = "true",
            ["DutyConfig.AutoManageBossModAISettings"] = "true",
            ["DutyConfig.AutoManageRotationPluginState"] = "true",
            ["DutyConfig.RotationPlugin"] = "RotationSolverReborn",
        };

        // Wait for the meal buff before entering: eating has to happen out of the duty and takes a moment to apply.
        this.cpLog.Info("food: Well Fed " + (this.food.HasFood() ? "active" : "missing"));

        if (this.Config.EatFood && !this.food.HasFood())
        {
            this.food.TryEat(this.Config);
            this.pendingStart = (territory, overrides, roster);
            this.foodTries = 1;
            this.nextFoodTry = DateTime.UtcNow.AddMilliseconds(700);
            this.Chat("[CruciblePilot] waiting for the food buff before starting the run");
            return;
        }

        this.StartRunNow(territory, overrides, roster);
    }

    private void StartRunNow(uint territory, Dictionary<string, string> overrides, List<uint> roster)
    {
        if (!this.autoDuty.Push(overrides))
        {
            this.cpLog.Error("AutoDuty rejected the run settings");
            this.Chat("[CruciblePilot] AutoDuty rejected the run settings");
            return;
        }

        this.pushedOverrides = true;
        this.cpLog.Info("pushed AutoDuty overrides: " + string.Join(", ", overrides.Select(kv => kv.Key + "=" + kv.Value)));

        try
        {
            this.autoDuty.Run(territory, 1, true);
            this.Chat($"[CruciblePilot] queued board {this.Config.Board} ({Configuration.DegreeName(this.Config.Degree)}) with roster {string.Join(", ", roster)}");
        }
        catch (Exception ex)
        {
            this.cpLog.Error(ex, "AutoDuty.Run failed");
            this.log.Error(ex, "[CP] AutoDuty.Run failed");
            this.autoDuty.Pop();
            this.pushedOverrides = false;
            this.Chat("[CruciblePilot] AutoDuty.Run failed: " + ex.Message);
        }
    }

    private void TickPendingStart()
    {
        if (this.pendingStart is not { } pending)
        {
            return;
        }

        if (this.food.HasFood())
        {
            this.pendingStart = null;
            this.Chat("[CruciblePilot] food buff is up - starting the run");
            this.StartRunNow(pending.Territory, pending.Overrides, pending.Roster);
            return;
        }

        if (DateTime.UtcNow < this.nextFoodTry)
        {
            return;
        }

        if (this.foodTries >= 8)
        {
            this.pendingStart = null;
            this.Chat("[CruciblePilot] food buff did not apply - starting anyway");
            this.cpLog.Warn("food: buff never appeared after 8 attempts; starting without it");
            this.StartRunNow(pending.Territory, pending.Overrides, pending.Roster);
            return;
        }

        this.foodTries++;
        this.food.TryEat(this.Config);
        this.nextFoodTry = DateTime.UtcNow.AddMilliseconds(700);
    }

    public void StopRun()
    {
        this.autoDuty.StopRun();
        this.Chat("[CruciblePilot] AutoDuty stop requested");
    }

    private void Help()
    {
        this.Chat("[CruciblePilot] /cp (window) | board 1-5 | degree standard..third | trio <a> <b> <c> | auto on|off");
        this.Chat("[CruciblePilot] start | stop | status | team | dump");
    }
}
