using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets.Experimental;
using Lumina.Text.ReadOnly;

namespace CruciblePilot;

/// <summary>
/// Enemy weaknesses for the Crucible boards (community lists: Off Duty / Icy Veins / Eorzean Tavern) and the
/// familiar damage profile read from the game sheets, plus the picker that scores a roster against the enemies
/// currently standing in the arena.
/// </summary>
public sealed class EnemyIntel
{
    public sealed record BeastProfile(uint Number, string Name, string DamageType, string Element);

    /// <summary>Longest patterns are matched first so "Arch Demon" wins over "Demon".</summary>
    private static readonly (string Pattern, string[] Tags)[] WeaknessTable =
    {
        // First Board (Novice I)
        ("bone knight", new[] { "blunt" }),
        ("bone bishop", new[] { "blunt" }),
        ("arch demon", new[] { "lightning" }),
        ("piscodemon", new[] { "slash" }),
        ("banemite", new[] { "ice" }),
        ("mitelings", new[] { "ice" }),
        ("ogre", new[] { "wind" }),
        ("wisp", new[] { "water" }),
        ("pas de seul", new[] { "pierce" }),
        ("succubus", new[] { "fire" }),

        // Second Board (Novice II)
        ("manticore", new[] { "wind" }),
        ("taurus", new[] { "pierce" }),
        ("ahriman", new[] { "ice" }),
        ("wyvern", new[] { "lightning" }),
        ("voidmancer", new[] { "earth" }),
        ("zombie", new[] { "ice" }),
        ("tablitaur", new[] { "fire" }),
        ("evil weapon", new[] { "ice" }),
        ("devilet", new[] { "earth" }),
        ("demon", new[] { "slash" }),
        ("loosefrox", new[] { "blunt" }),
        ("chewchum", new[] { "water" }),

        // Third Board (Novice III)
        ("cavalier", new[] { "fire" }),
        ("ymir", new[] { "ice" }),
        ("sahagin", new[] { "blunt" }),
        ("zu", new[] { "earth" }),
        ("catoblepas", new[] { "wind" }),
        ("lakhamu", new[] { "water" }),
        ("golem", new[] { "water" }),
        ("siren", new[] { "pierce" }),
        ("crawler", new[] { "fire" }),
        ("campeador", new[] { "lightning" }),
        ("guardia", new[] { "ice" }),
        ("thanatos", new[] { "lightning" }),
        ("guttler", Array.Empty<string>()),

        // First Master's Board (Advanced I)
        ("strix", new[] { "pierce" }),
        ("morbol", new[] { "slash" }),
        ("seedling", new[] { "slash" }),
        ("ochu", new[] { "slash" }),
        ("carrion broth", new[] { "ice" }),
        ("corpse flower", new[] { "wind" }),
        ("queen hawk", new[] { "blunt" }),
        ("ice dragon", new[] { "fire" }),
        ("ice sprite", new[] { "fire" }),
        ("administrator", new[] { "lightning" }),
        ("cobra", new[] { "pierce" }),
        ("treant", new[] { "blunt" }),
        ("slug", new[] { "blunt" }),
        ("sapling", new[] { "blunt" }),
        ("diremite", new[] { "ice" }),
        ("biloko", new[] { "ice" }),
        ("progenitrix", new[] { "wind" }),
        ("grenade", new[] { "wind" }),
        ("bomb", new[] { "wind" }),
        ("snoll", new[] { "fire" }),
        ("pyrobolus", new[] { "water" }),
        ("borgny", new[] { "earth" }),
        ("toxic mass", new[] { "water" }),

        // Second Master's Board (Advanced II)
        ("flauros", new[] { "earth" }),
        ("lightning sprite", new[] { "earth" }),
        ("mindflayer", new[] { "slash" }),
        ("myconid", new[] { "water" }),
        ("boogyman", new[] { "ice" }),
        ("atomos", new[] { "blunt" }),
        ("drake", new[] { "wind" }),
        ("barbmole", new[] { "wind" }),
        ("durga", new[] { "fire" }),
        ("spinner-rook", new[] { "blunt" }),
        ("medusa", new[] { "pierce" }),
        ("lamia", new[] { "pierce" }),
        ("chimera", new[] { "water" }),
        ("sphinx", new[] { "slash" }),
        ("congealed lightning", new[] { "earth" }),
        ("congealed kindling", new[] { "wind" }),
        ("gigantis", new[] { "earth", "wind" }),
        ("moogle", new[] { "lightning", "blunt", "water" }),
        ("king ahriman", new[] { "ice" }),
        ("hapalit", new[] { "wind" }),
        ("behemoth", new[] { "earth" }),
        ("giant", new[] { "lightning" }),
        ("lauda", Array.Empty<string>()),
    };

    private readonly IObjectTable objects;
    private readonly IDataManager data;
    private readonly IPluginLog log;
    private Dictionary<uint, BeastProfile>? profiles;

    public EnemyIntel(IObjectTable objects, IDataManager data, IPluginLog log)
    {
        this.objects = objects;
        this.data = data;
        this.log = log;
    }

    /// <summary>Hostile-looking battle NPCs within range of the player (the arena's current encounter).</summary>
    public List<string> CurrentEnemies(float maxDistance = 60f)
    {
        var result = new List<string>();
        Vector3 center = this.objects.LocalPlayer?.Position ?? Vector3.Zero;
        foreach (var obj in this.objects)
        {
            if (obj is not IBattleNpc npc)
            {
                continue;
            }

            string name = npc.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (npc.MaxHp > 0 && npc.CurrentHp == 0)
            {
                continue;
            }

            if (Vector3.Distance(npc.Position, center) > maxDistance)
            {
                continue;
            }

            result.Add(name.Trim());
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string[] TagsFor(string enemyName)
    {
        string name = enemyName.ToLowerInvariant();
        foreach (var (pattern, tags) in WeaknessTable)
        {
            if (name.Contains(pattern, StringComparison.Ordinal))
            {
                return tags;
            }
        }

        return Array.Empty<string>();
    }

    public IReadOnlyDictionary<uint, BeastProfile> Profiles()
    {
        if (this.profiles != null)
        {
            return this.profiles;
        }

        this.profiles = new Dictionary<uint, BeastProfile>();
        try
        {
            var sheet = this.data.GetExcelSheet<XBMPet>();
            foreach (var row in sheet)
            {
                uint number = row.RowId;
                string name = row.Pet.Value.Name.ExtractText();
                string element = ElementName(row.Action.Value.Aspect);
                string damageType = DamageTypeName(row.Pet.Value.AutoAction.Value);
                this.profiles[number] = new BeastProfile(number, name, damageType, element);
            }
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "[CP] Could not read the XBMPet sheet; encounter-aware picking disabled");
        }

        return this.profiles;
    }

    public List<uint> PickTrio(IReadOnlyList<uint> roster, out string reason)
    {
        var enemies = this.CurrentEnemies().Where(e => TagsFor(e).Length > 0).ToList();
        if (enemies.Count == 0)
        {
            reason = "no recognised enemies nearby";
            return new List<uint>();
        }

        var profiles = this.Profiles();
        var scored = new List<(uint Number, int Score, string Why)>();
        foreach (uint number in roster)
        {
            if (!profiles.TryGetValue(number, out var profile))
            {
                continue;
            }

            int score = 0;
            var why = new List<string>();
            foreach (string enemy in enemies)
            {
                string[] tags = TagsFor(enemy);
                if (profile.Element.Length > 0 && tags.Contains(profile.Element))
                {
                    score += 4;
                    why.Add($"{profile.Element} vs {enemy}");
                }

                if (profile.DamageType.Length > 0 && tags.Contains(profile.DamageType))
                {
                    score += 4;
                    why.Add($"{profile.DamageType} vs {enemy}");
                }
            }

            scored.Add((number, score, string.Join(", ", why)));
        }

        if (scored.Count == 0)
        {
            reason = "no familiar profiles available";
            return new List<uint>();
        }

        var picks = scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => profiles[s.Number].Element.Length)
            .ThenBy(s => s.Number)
            .Take(3)
            .ToList();

        reason = string.Join(" | ", picks.Select(p => $"#{p.Number} {profiles[p.Number].Name} {p.Score} ({p.Why})"));
        return picks.Select(p => p.Number).ToList();
    }

    public string DescribeProfiles(IEnumerable<uint> numbers)
    {
        var profiles = this.Profiles();
        return string.Join(" | ", numbers.Where(profiles.ContainsKey)
            .Select(n => $"#{n} {profiles[n].Name}: dmg={profiles[n].DamageType}, el={profiles[n].Element}"));
    }

    private static string ElementName(byte aspect) => aspect switch
    {
        1 => "fire",
        2 => "ice",
        3 => "wind",
        4 => "earth",
        5 => "lightning",
        6 => "water",
        7 => "holy",
        8 => "dark",
        _ => string.Empty,
    };

    private static string DamageTypeName(Lumina.Excel.Sheets.Experimental.Action action)
    {
        try
        {
            string name = action.AttackType.Value.Name.ExtractText().Trim();
            return name.ToLowerInvariant() switch
            {
                "slashing" => "slash",
                "piercing" => "pierce",
                "blunt" => "blunt",
                "magic" => "magic",
                var other => other,
            };
        }
        catch
        {
            return string.Empty;
        }
    }
}
