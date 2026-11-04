using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace CruciblePilot;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    /// <summary>Master switch for the battlehorn autopilot.</summary>
    public bool Autopilot { get; set; } = true;

    /// <summary>Press "start battle" once the desired trio is on the horns.</summary>
    public bool AutoStartBattle { get; set; } = true;

    /// <summary>Which board the /cp start command queues, 1-5 (Novice I .. Advanced II).</summary>
    public int Board { get; set; } = 1;

    /// <summary>0 = Standard, 1 = First, 2 = Second, 3 = Third.</summary>
    public int Degree { get; set; } = 0;

    /// <summary>
    /// Community meta: keep exactly three beasts for the whole board, as the shared Pawprint "perfect run" presets do
    /// (Mantis 16 + Wespe 10 + Cu Sith 1). No per-fight swapping.
    /// </summary>
    public bool MetaTrioMode { get; set; } = true;

    /// <summary>Force the Crucible Mode (degree) on the pre-battle window, for runs driven by another plugin (Pawprint).</summary>
    public bool ForceDegree { get; set; } = true;

    /// <summary>
    /// XSZYYS-style whistle plan: press Tempered Release when the current target is below the active horn's
    /// finisher threshold (the values the shared whistle presets use).
    /// </summary>
    public bool WhistlePlan { get; set; } = true;

    /// <summary>Finisher HP% per horn slot: first horn, second horn, third horn.</summary>
    public List<int> WhistleThresholds { get; set; } = new() { 10, 40, 20 };

    /// <summary>Use Borrow instead of Tempered Release for the third horn (the "AutoBorrow" preset flag).</summary>
    public bool WhistleAutoBorrow { get; set; }

    /// <summary>Per-board meta trios taken from those presets.</summary>
    public Dictionary<int, List<uint>> MetaTrios { get; set; } = new()
    {
        [1] = new() { 16, 10, 1 },
        [2] = new() { 16, 10, 1 },
        [3] = new() { 16, 10, 1 },
        [4] = new() { 16, 10, 32 },
        [5] = new() { 16, 1, 10 },
    };

    public List<uint> MetaTrio(int board)
        => this.MetaTrios.TryGetValue(board, out var list) ? list : new List<uint>();

    /// <summary>Clear mode brings a full roster and swaps per fight; score mode brings exactly three beasts for Beast Squad III and the no-shop/no-rest bonuses.</summary>
    /// <summary>Drink defensive Crucible consumables (reraisser/sand/kits) between fights when held.</summary>
    public bool UseCrucibleBuffs { get; set; } = true;

    /// <summary>XBM item rows to use, in order: G2 Beastmaster Reraisser, Beast Potion Kit, Beast Remedy Kit, Temporal Sand.</summary>
    public List<uint> BuffItemIds { get; set; } = new() { 99, 140, 141, 138 };

    public bool ScoreMode { get; set; }

    /// <summary>The three beasts used in score mode, per board (they must clear the whole board alone).</summary>
    public Dictionary<int, List<uint>> ScoreTrios { get; set; } = new()
    {
        [1] = new() { 50, 26, 1 },
        [2] = new() { 50, 26, 11 },
        [3] = new() { 50, 26, 21 },
        [4] = new() { 50, 26, 16 },
        [5] = new() { 50, 26, 16 },
    };

    public List<uint> ScoreTrio(int board)
        => this.ScoreTrios.TryGetValue(board, out var list) ? list : new List<uint>();

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

    /// <summary>Push Rotation Solver targeting overrides so the bot attacks the strongest enemy (the boss), not dying adds.</summary>
    public bool FocusStrongestEnemy { get; set; } = true;

    /// <summary>Food item the plugin asks the game to use.</summary>
    public string FoodItemName { get; set; } = "Rock-fisted Popoto Stew";

    /// <summary>Re-eat when the Well Fed buff has less than this many minutes left.</summary>
    public int FoodRefreshMinutes { get; set; } = 20;

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
