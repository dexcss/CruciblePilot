using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CruciblePilot;

/// <summary>ImGui settings/status window.</summary>
public sealed class Ui
{
    private static readonly string[] BoardNames =
    {
        "1 - Novice I (First Board)",
        "2 - Novice II (Second Board)",
        "3 - Novice III (Third Board)",
        "4 - Advanced I (First Master's)",
        "5 - Advanced II (Second Master's)",
    };

    private static readonly string[] DegreeNames = { "Standard", "First", "Second", "Third" };

    private static readonly string[] BeastNames =
    {
        "Cu Sith", "Squirrel", "Lamb", "Pugil", "Opo-opo", "Dodo", "Coblyn", "Diremite", "Megalocrab", "Wespe",
        "Vulture", "Mandragora", "Geshunpest", "Puk", "Crab", "Mantis", "Slime", "Dullahan", "Bat", "Flying Trap",
        "Ziz", "Sabotender", "Golem", "Apkallu", "Adamantoise", "Buffalo", "Uragnite", "Worm", "Spriggan", "Goobbue",
        "Gigantoad", "Colibri", "Coeurl", "Raptor", "Drake", "Treant", "Antling", "Chimera", "Morbol", "Ghost",
        "Salamander", "Cobra", "Hydra", "Damselfly", "Rotting Goobbue", "Zu", "Ice Golem", "Karlabos", "Rafflesia", "Behemoth",
    };

    private static readonly string[] BeastItems = BeastNames
        .Select((name, index) => $"{index + 1:00} {name}")
        .ToArray();

    private readonly Plugin plugin;

    public Ui(Plugin plugin) => this.plugin = plugin;

    public void Draw()
    {
        var config = this.plugin.Config;
        if (!config.ShowWindow)
        {
            return;
        }

        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(430, 430), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("CruciblePilot", ref open, ImGuiWindowFlags.None))
        {
            ImGui.End();
            config.ShowWindow = open;
            return;
        }

        try
        {
            this.DrawBody(config);
        }
        finally
        {
            ImGui.End();
        }

        config.ShowWindow = open;
    }

    private void DrawBody(Configuration config)
    {
        ImGui.TextUnformatted("Crucible of the Unbroken pilot - AutoDuty runs the board, this picks the horns.");
        ImGui.Separator();

        int board = config.Board - 1;
        ImGui.SetNextItemWidth(300);
        if (ImGui.Combo("Board", ref board, BoardNames))
        {
            config.Board = board + 1;
            this.plugin.SaveConfig();
        }

        int degree = config.Degree;
        ImGui.SetNextItemWidth(300);
        if (ImGui.Combo("Degree", ref degree, DegreeNames))
        {
            config.Degree = degree;
            this.plugin.SaveConfig();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Battlehorn trio for this board");

        var trio = config.Trio(config.Board);
        var selected = new uint[3];
        for (int i = 0; i < 3; i++)
        {
            selected[i] = i < trio.Count ? trio[i] : 0;
        }

        bool trioChanged = false;
        for (int i = 0; i < 3; i++)
        {
            int slot = selected[i] == 0 ? 0 : (int)selected[i] - 1;
            ImGui.SetNextItemWidth(300);
            if (ImGui.Combo($"Horn {i + 1}", ref slot, BeastItems))
            {
                selected[i] = (uint)(slot + 1);
                trioChanged = true;
            }
        }

        if (trioChanged)
        {
            config.Trios[config.Board] = selected.Where(x => x > 0).Distinct().ToList();
            this.plugin.SaveConfig();
        }

        ImGui.Separator();

        bool autopilot = config.Autopilot;
        if (ImGui.Checkbox("Autopilot (assign the trio every fight)", ref autopilot))
        {
            config.Autopilot = autopilot;
            this.plugin.SaveConfig();
        }

        bool autoStart = config.AutoStartBattle;
        if (ImGui.Checkbox("Press start battle once the trio is set", ref autoStart))
        {
            config.AutoStartBattle = autoStart;
            this.plugin.SaveConfig();
        }

        bool setTeam = config.SetAutoDutyTeam;
        if (ImGui.Checkbox("Push trio into AutoDuty's custom team", ref setTeam))
        {
            config.SetAutoDutyTeam = setTeam;
            this.plugin.SaveConfig();
        }

        bool autoPick = config.AutoPick;
        if (ImGui.Checkbox("Pick the trio from the enemies in the arena", ref autoPick))
        {
            config.AutoPick = autoPick;
            this.plugin.SaveConfig();
        }

        ImGui.Separator();

        ImGui.BeginDisabled(this.plugin.IsAutoDutyRunning());
        if (ImGui.Button("Start run", new Vector2(120, 0)))
        {
            this.plugin.StartRun();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Stop", new Vector2(80, 0)))
        {
            this.plugin.StopRun();
        }

        ImGui.SameLine();
        if (ImGui.Button("Set AD team", new Vector2(120, 0)))
        {
            this.plugin.ApplyTeam();
        }

        ImGui.Separator();
        ImGui.TextUnformatted(this.plugin.GetStatusLine());
        ImGui.TextUnformatted(this.plugin.GetHornLine());
        ImGui.TextUnformatted(this.plugin.GetEnemyLine());
        ImGui.TextUnformatted(this.plugin.GetPickLine());
    }
}
