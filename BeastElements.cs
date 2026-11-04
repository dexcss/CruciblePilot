using System;
using System.Collections.Generic;
using System.Linq;

namespace CruciblePilot;

/// <summary>
/// Beast-to-element/physical-type coverage, taken from the community-verified per-board tables (Eorzean Tavern):
/// which beasts deal which of the nine damage types the Crucible tags its enemies with (auto-attack, Trick or
/// Tempered Release). Used to answer the stage window's weakness lines directly.
/// </summary>
public static class BeastElements
{
    public static readonly Dictionary<string, uint[]> ByTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fire"] = new uint[] { 2, 14, 29, 34, 35, 38, 50 },
        ["ice"] = new uint[] { 20, 21, 27, 31, 34, 38, 47 },
        ["wind"] = new uint[] { 11, 20, 36, 40, 44, 46, 49 },
        ["earth"] = new uint[] { 6, 23, 28, 36, 45 },
        ["lightning"] = new uint[] { 7, 13, 25, 33, 50 },
        ["water"] = new uint[] { 4, 9, 15, 24, 41, 48 },
        ["blunt"] = new uint[] { 3, 5, 12, 26, 29, 30, 31, 46 },
        ["pierce"] = new uint[] { 8, 10, 22, 32, 37, 39, 42, 43, 48 },
        ["slash"] = new uint[] { 1, 14, 16, 18 },
    };

    /// <summary>Tags a single familiar deals.</summary>
    public static IEnumerable<string> TagsOf(uint beast)
        => ByTag.Where(kv => kv.Value.Contains(beast)).Select(kv => kv.Key);

    /// <summary>
    /// Pick three beasts from the roster: those covering the most of the encounter's weaknesses first, then the
    /// roster order (which the per-board lists put the most useful beasts in first).
    /// </summary>
    public static List<uint> Pick(IReadOnlyList<uint> roster, IReadOnlyCollection<string> weakTags, out string reason)
    {
        reason = string.Empty;
        var tags = weakTags.Where(t => ByTag.ContainsKey(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tags.Count == 0)
        {
            return new List<uint>();
        }

        var scored = roster.Distinct().Select((number, index) => new
        {
            Number = number,
            Index = index,
            Hits = tags.Count(t => ByTag[t].Contains(number)),
        }).ToList();

        var picks = scored.OrderByDescending(s => s.Hits).ThenBy(s => s.Index).Take(3).Select(s => s.Number).ToList();
        if (picks.Count < 3)
        {
            return new List<uint>();
        }

        reason = string.Join(", ", picks.Select(n => $"#{n} {string.Join("+", TagsOf(n).Where(t => tags.Contains(t)))} ({scored.First(s => s.Number == n).Hits})"));
        return picks;
    }
}
