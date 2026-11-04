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
        "1 - First Board",
        "2 - Second Board",
        "3 - Third Board",
        "4 - First Master's Board",
        "5 - Second Master's Board",
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
    private string? rosterText;
    private string? foodText;
    private int lastBoard = -1;

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
        ImGui.TextUnformatted("Roster AutoDuty registers (numbers, comma separated; up to the board's cap)");
        if (this.lastBoard != config.Board) { this.lastBoard = config.Board; this.rosterText = Configuration.FormatRoster(config.RosterFor(config.Board)); }
        ImGui.SetNextItemWidth(390);
        if (ImGui.InputText("Roster", ref this.rosterText, 512))
        {
            var parsed = Configuration.ParseRoster(this.rosterText);
            if (parsed.Count > 0)
            {
                config.BoardRosters[config.Board] = parsed;
                this.plugin.SaveConfig();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Food (the Crucible disables potions; the meal buff is all you can bring in)");

        bool eatFood = config.EatFood;
        if (ImGui.Checkbox("Eat food before the run", ref eatFood))
        {
            config.EatFood = eatFood;
            this.plugin.SaveConfig();
        }

        this.foodText ??= config.FoodItemName;
        ImGui.SetNextItemWidth(390);
        if (ImGui.InputText("Food item", ref this.foodText, 128))
        {
            if (!string.IsNullOrWhiteSpace(this.foodText))
            {
                config.FoodItemName = this.foodText.Trim();
                this.plugin.SaveConfig();
            }
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

        bool enforceOrder = config.EnforceHornOrder;
        if (ImGui.Checkbox("Assign horns in trio order (for One With Nature)", ref enforceOrder))
        {
            config.EnforceHornOrder = enforceOrder;
            this.plugin.SaveConfig();
        }

        bool setTeam = config.SetAutoDutyTeam;
        if (ImGui.Checkbox("Push roster into AutoDuty's custom team", ref setTeam))
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

        bool useDefaults = config.UseDefaults;
        if (ImGui.Checkbox("Prefer the curated per-fight trios", ref useDefaults))
        {
            config.UseDefaults = useDefaults;
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
