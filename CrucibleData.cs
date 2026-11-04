using System;
using System.Collections.Generic;
using System.Linq;

namespace CruciblePilot;

/// <summary>
/// Curated per-encounter trios and per-board rosters, built from the game's own familiar data (element and
/// physical attack type read from each beast's Trick description) plus the community encounter weakness lists.
/// </summary>
public static class CrucibleData
{
    public static readonly (string Label, string[] Patterns, uint[] Trio)[] Board1 =
    {
        ("Bone Knight/Bishop - blunt", new[] { "bone knight", "bone bishop" }, new uint[] { 5, 31, 25 }),
        ("Arch Demon/Piscodemon - lightning/slash", new[] { "arch demon", "piscodemon" }, new uint[] { 13, 25, 1 }),
        ("Banemite/Mitelings - ice", new[] { "banemite", "mitelings" }, new uint[] { 21, 5, 25 }),
        ("Ogre/Wisps - wind/water", new[] { "ogre", "wisp" }, new uint[] { 11, 40, 4 }),
        ("Pas de Seul - pierce, fire adds", new[] { "pas de seul", "succubus" }, new uint[] { 1, 29, 31 }),
    };

    public static readonly (string Label, string[] Patterns, uint[] Trio)[] Board2 =
    {
        ("Manticore - wind", new[] { "manticore" }, new uint[] { 11, 40, 1 }),
        ("Taurus/Ahriman/Wyvern - pierce/ice/lightning", new[] { "taurus", "ahriman", "wyvern" }, new uint[] { 1, 20, 13 }),
        ("Voidmancer/Zombies - earth/ice", new[] { "voidmancer", "zombie" }, new uint[] { 23, 20, 26 }),
        ("Tablitaurs - fire", new[] { "tablitaur" }, new uint[] { 2, 29, 26 }),
        ("Demon pack - slash/ice/earth", new[] { "demon", "evil weapon", "devilet" }, new uint[] { 1, 20, 23 }),
        ("Loosefrox/Chewchum - blunt/water", new[] { "loosefrox", "chewchum" }, new uint[] { 5, 26, 41 }),
    };

    public static readonly (string Label, string[] Patterns, uint[] Trio)[] Board3 =
    {
        ("Cavalier/Bishops - fire/blunt", new[] { "cavalier", "bone bishop" }, new uint[] { 29, 2, 26 }),
        ("Ymir/Sahagin - ice/blunt", new[] { "ymir", "sahagin" }, new uint[] { 21, 34, 26 }),
        ("Zu/Catoblepas - earth/wind", new[] { "zu", "catoblepas" }, new uint[] { 23, 11, 40 }),
        ("Lakhamu/Golems - water", new[] { "lakhamu", "golem" }, new uint[] { 15, 41, 1 }),
        ("Siren/undead - pierce", new[] { "siren", "crawler" }, new uint[] { 1, 10, 15 }),
        ("Campeador/Guardia - lightning/ice", new[] { "campeador", "guardia" }, new uint[] { 13, 21, 50 }),
        ("Guttler - lightning adds", new[] { "guttler", "thanatos" }, new uint[] { 13, 50, 26 }),
    };

    public static readonly (string Label, string[] Patterns, uint[] Trio)[] Board4 =
    {
        ("Strix - pierce", new[] { "strix" }, new uint[] { 10, 1, 16 }),
        ("Morbol route - slash/ice/wind/blunt", new[] { "morbol", "corpse flower", "ochu", "seedling", "carrion broth", "queen hawk" }, new uint[] { 16, 15, 26 }),
        ("Ice Dragon/sprites - fire", new[] { "ice dragon", "ice sprite" }, new uint[] { 29, 35, 16 }),
        ("Gargoyle - no weakness", new[] { "gargoyle" }, new uint[] { 16, 1, 10 }),
        ("Administrator - lightning, Cobra piercing", new[] { "administrator", "cobra", "dreadnaught" }, new uint[] { 13, 50, 10 }),
        ("Golem/Treant - water/blunt/ice", new[] { "golem", "treant", "slug", "sapling", "diremite", "biloko" }, new uint[] { 15, 26, 21 }),
        ("Progenitrix - wind/fire/water", new[] { "progenitrix", "grenade", "bomb", "snoll", "pyrobolus" }, new uint[] { 40, 29, 15 }),
        ("Borgny/Toxic Mass - earth/water", new[] { "borgny", "toxic mass" }, new uint[] { 23, 15, 26 }),
    };

    public static readonly (string Label, string[] Patterns, uint[] Trio)[] Board5 =
    {
        ("Flauros/sprites - earth", new[] { "flauros", "lightning sprite" }, new uint[] { 23, 28, 40 }),
        ("Mindflayer/Myconids - slash/water", new[] { "mindflayer", "myconid" }, new uint[] { 16, 15, 1 }),
        ("Boogyman/Atomos/Drake - ice/blunt/wind", new[] { "boogyman", "atomos", "drake", "barbmole" }, new uint[] { 21, 26, 40 }),
        ("Durga/Spinner-rooks - fire/blunt", new[] { "durga", "spinner-rook" }, new uint[] { 29, 26, 13 }),
        ("Medusa/Lamia - pierce", new[] { "medusa", "lamia" }, new uint[] { 1, 10, 15 }),
        ("Chimera/Sphinx - water/slash", new[] { "chimera", "sphinx" }, new uint[] { 15, 16, 1 }),
        ("Gigantis - earth/wind", new[] { "gigantis", "congealed" }, new uint[] { 23, 40, 11 }),
        ("Moogles - lightning/blunt/water", new[] { "moogle", "kupo" }, new uint[] { 13, 26, 15 }),
        ("Behemoth/King Ahriman - earth/lightning/ice", new[] { "behemoth", "king ahriman", "hapalit" }, new uint[] { 23, 15, 40 }),
        ("Lauda/Thanatos - lightning adds", new[] { "lauda", "thanatos" }, new uint[] { 1, 16, 13 }),
    };

    public static readonly Dictionary<int, (string Label, string[] Patterns, uint[] Trio)[]> ByBoard = new()
    {
        [1] = Board1,
        [2] = Board2,
        [3] = Board3,
        [4] = Board4,
        [5] = Board5,
    };

    /// <summary>Rosters sized to each board's cap, covering every trio above.</summary>
    public static Dictionary<int, List<uint>> DefaultRosters() => new()
    {
        [1] = new() { 5, 26, 20, 21, 48, 4, 11, 44, 7, 13 },
        [2] = new() { 29, 20, 48, 1, 7, 21, 4, 11, 13, 5, 26, 36 },
        [3] = new() { 50, 31, 48, 36, 21, 4, 11, 23, 13, 5, 34, 29, 10, 26 },
        [4] = new() { 50, 31, 48, 36, 21, 4, 11, 23, 13, 5, 34, 16 },
        [5] = new() { 50, 31, 48, 36, 21, 4, 11, 23, 13, 5, 34, 16, 29, 26, 10 },
    };

    public static (string Label, string[] Patterns, uint[] Trio)? Match(int board, IEnumerable<string> enemies)
    {
        if (!ByBoard.TryGetValue(board, out var entries))
        {
            return null;
        }

        string joined = string.Join(" ", enemies).ToLowerInvariant();
        (string Label, string[] Patterns, uint[] Trio)? best = null;
        int bestHits = 0;
        foreach (var entry in entries)
        {
            int hits = entry.Patterns.Count(p => joined.Contains(p, StringComparison.Ordinal));
            if (hits > bestHits)
            {
                bestHits = hits;
                best = entry;
            }
        }

        return best;
    }
}
