using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace CruciblePilot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    /// <summary>Master switch for the battlehorn autopilot.</summary>
    public bool Autopilot { get; set; } = true;

    /// <summary>Press "start battle" once the desired trio is on the horns.</summary>
    public bool AutoStartBattle { get; set; } = true;

    /// <summary>Which board the /cp start command queues, 1-5 (Novice I .. Advanced II).</summary>
    public int Board { get; set; } = 1;

    /// <summary>0 = Standard, 1 = First, 2 = Second, 3 = Third.</summary>
    public int Degree { get; set; } = 0;

    /// <summary>Desired battlehorn trio per board (familiar numbers); used when AutoPick is off or no enemies are read.</summary>
    public Dictionary<int, List<uint>> Trios { get; set; } = new();

    /// <summary>Familiar numbers AutoDuty registers as the team; the per-fight picker chooses three of these.</summary>
    public List<uint> Roster { get; set; } = new() { 1, 2, 10, 13, 15, 16, 23, 25, 26, 28, 29, 31, 34, 40, 47 };

    /// <summary>Roster per board, sized to that board's cap and covering every curated trio for it.</summary>
    public Dictionary<int, List<uint>> BoardRosters { get; set; } = DefaultRosters();

    public static Dictionary<int, List<uint>> DefaultRosters() => CrucibleData.DefaultRosters();

    public List<uint> RosterFor(int board)
        => this.BoardRosters.TryGetValue(board, out var list) && list.Count > 0 ? list : this.Roster;

    /// <summary>Try to rewrite AutoDuty's CustomTeam via reflection before starting a run.</summary>
    public bool SetAutoDutyTeam { get; set; } = true;

    /// <summary>Pick the trio from the enemies currently in the arena instead of the fixed list.</summary>
    public bool AutoPick { get; set; } = true;

    /// <summary>Assign the trio to the battlehorns in list order so One With Nature (Tempered/Borrow) lines up.</summary>
    public bool EnforceHornOrder { get; set; } = true;

    /// <summary>Eat the configured food before a run when the Well Fed buff is missing.</summary>
    public bool EatFood { get; set; } = true;

    /// <summary>Food item the plugin asks the game to use.</summary>
    public string FoodItemName { get; set; } = "Rock-fisted Popoto Stew";

    /// <summary>Prefer the curated per-encounter trio table over the weakness scoring.</summary>
    public bool UseDefaults { get; set; } = true;

    /// <summary>Whether the settings window is shown.</summary>
    public bool ShowWindow { get; set; } = true;

    public List<uint> Trio(int board)
        => this.Trios.TryGetValue(board, out var trio) ? trio : new List<uint>();

    public static string FormatRoster(IEnumerable<uint> roster) => string.Join(", ", roster);

    public static List<uint> ParseRoster(string text)
    {
        var result = new List<uint>();
        foreach (var part in text.Split(new[] { ',', ' ', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(part, out uint number) && number >= 1 && number <= 50 && !result.Contains(number))
            {
                result.Add(number);
            }
        }

        return result;
    }

    public static string DegreeName(int degree) => degree switch
    {
        0 => "Standard",
        1 => "First",
        2 => "Second",
        _ => "Third",
    };
}
