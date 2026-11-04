using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace CruciblePilot;

/// <summary>Everything the plugin needs: the degree it forces, the combat switches and the whistle plan.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    /// <summary>Crucible Mode to force on the pre-battle window (1 = First, 2 = Second, 3 = Third).</summary>
    public int Degree { get; set; } = 1;

    /// <summary>Keep forcing the degree on every stage window.</summary>
    public bool ForceDegree { get; set; } = true;

    /// <summary>Run the ACR resolvers directly through the bridge (their compiled logic, our tick and submission).</summary>
    public bool UseXszyysBridge { get; set; }

    /// <summary>Run the built-in Beastmaster Crucible rotation (turn RSR off for Beastmaster).</summary>
    public bool EnableCombat { get; set; } = true;

    /// <summary>Summon the battlehorns as part of the combat loop.</summary>
    public bool CombatHorns { get; set; } = true;

    /// <summary>Run the pre-pull countdown opener (third horn, borrow, first horn).</summary>
    public bool CombatCountdownOpener { get; set; } = true;

    /// <summary>Parting Blow while the familiar is finishing its own ability.</summary>
    public bool CombatPetFinisher { get; set; }

    /// <summary>Use the ranged fallback action when the target is out of melee reach.</summary>
    public bool CombatRangedFallback { get; set; } = true;

    /// <summary>Use the 250 TP burst actions when a burst status is up.</summary>
    public bool CombatBurst { get; set; } = true;

    /// <summary>Use Capture to mark targets (capture runs only).</summary>
    public bool CombatCapture { get; set; }

    /// <summary>Spend TP on Avalanche Axe when nothing is in range.</summary>
    public bool CombatDowntimeAxe { get; set; } = true;

    /// <summary>Drink crucible healing potions when low.</summary>
    public bool CombatPotions { get; set; } = true;

    /// <summary>Drink a crucible potion at or below this HP percentage.</summary>
    public int CombatPotionHpPercent { get; set; } = 70;

    /// <summary>Press Tempered Release when the target is below the active horn's finisher threshold.</summary>
    public bool WhistlePlan { get; set; } = true;

    /// <summary>Finisher HP% per horn slot: first horn, second horn, third horn.</summary>
    public List<int> WhistleThresholds { get; set; } = new() { 10, 40, 20 };

    /// <summary>Use Borrow instead of Tempered Release on the third horn.</summary>
    public bool WhistleAutoBorrow { get; set; }

    /// <summary>Window visibility (also toggled with /cp).</summary>
    public bool ShowWindow { get; set; } = true;

    public void Save() => Plugin.Instance.SaveConfig();

    public static string DegreeName(int degree) => degree switch
    {
        1 => "First",
        2 => "Second",
        3 => "Third",
        _ => "Standard",
    };
}
