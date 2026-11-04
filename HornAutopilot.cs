using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CruciblePilot;

/// <summary>
/// Watches the battlehorn window and puts the configured trio on the horns before the fight starts,
/// then presses start (and answers the underfilled-horns prompt).
/// </summary>
public sealed unsafe class HornAutopilot
{
    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;

    private DateTime nextAction = DateTime.MinValue;
    private DateTime lastConfirm = DateTime.MinValue;
    private DateTime nextYes = DateTime.MinValue;
    private DateTime nextHeartbeat = DateTime.MinValue;
    private string lastWarn = string.Empty;
    private DateTime lastWarnAt = DateTime.MinValue;
    private string lastRosterSignature = string.Empty;
    private string lastStageDumpSignature = string.Empty;
    private string lastEncounterSignature = string.Empty;
    private DateTime nextEncounterLog = DateTime.MinValue;
    private int toggleCount;
    private string toggleEncounter = string.Empty;

    public HornAutopilot(Plugin plugin, IPluginLog log, IGameGui gameGui, IClientState clientState)
    {
        this.plugin = plugin;
        this.log = log;
        this.gameGui = gameGui;
        this.clientState = clientState;
    }

    private sealed record Row(int Index, uint Number, uint Hp, uint MaxHp, uint Selection);

    public void Tick()
    {
        if (!this.plugin.Config.Autopilot)
        {
            return;
        }

        if (DateTime.UtcNow < this.nextAction)
        {
            return;
        }

        int board = (int)this.clientState.TerritoryType - 1338;
        if (board < 1 || board > 5)
        {
            return;
        }

        var party = this.gameGui.GetAddonByName<AtkUnitBase>("XBMPetParty");
        var stage = this.gameGui.GetAddonByName<AtkUnitBase>("XBMStageDetailList");
        bool partyOpen = party != null && party->IsReady && party->IsVisible;
        bool stageOpen = stage != null && stage->IsReady && stage->IsVisible;
        if (!partyOpen || !stageOpen)
        {
            if (DateTime.UtcNow > this.nextHeartbeat)
            {
                this.nextHeartbeat = DateTime.UtcNow.AddSeconds(10);
                var idleEnemies = this.plugin.Intel.CurrentEnemies();
                var player = this.plugin.Objects.LocalPlayer;
                string hp = player == null ? "?" : $"{player.CurrentHp}/{player.MaxHp}";
                this.plugin.CpLog.Info($"heartbeat board {board}: party={partyOpen} stage={stageOpen} enemies={idleEnemies.Count} [{string.Join(", ", idleEnemies)}] playerHp={hp}");
            }

            return;
        }

        // The game asks "start with unfilled horns?" right after we press start; say yes.
        var yesno = this.gameGui.GetAddonByName<AtkUnitBase>("SelectYesno");
        if (yesno != null && yesno->IsReady && yesno->IsVisible &&
            DateTime.UtcNow < this.lastConfirm.AddSeconds(8) && DateTime.UtcNow > this.nextYes)
        {
            this.Fire(yesno, 0);
            this.nextYes = DateTime.UtcNow.AddSeconds(1);
            this.plugin.CpLog.Info("answered the unfilled-horns prompt with Yes");
            this.log.Information("[CP] Answered the underfilled-horns prompt");
            return;
        }

        var rows = ReadRows(party);
        if (rows.Count == 0)
        {
            return;
        }

        string rosterSignature = string.Join(",", rows.Select(r => r.Number));
        if (rosterSignature != this.lastRosterSignature)
        {
            this.lastRosterSignature = rosterSignature;
            this.plugin.CpLog.Info("roster " + string.Join(" ", rows.Select(r => $"#{r.Number}(hp {r.Hp}/{r.MaxHp} sel {r.Selection})")));
        }

        var roster = rows.Select(r => r.Number).ToHashSet();
        var enemies = this.plugin.Intel.CurrentEnemies();
        var stageText = ReadStageStrings(stage);
        if (enemies.Count == 0 && stageText.Count > 0)
        {
            // The arena's actors only spawn after the battle starts; the stage window already lists them.
            enemies = stageText;
        }

        var weaknesses = ExtractWeaknesses(stageText);

        string encounterSignature = string.Join("|", enemies) + "#" + rosterSignature;
        if (encounterSignature != this.lastEncounterSignature && DateTime.UtcNow > this.nextEncounterLog)
        {
            this.lastEncounterSignature = encounterSignature;
            this.nextEncounterLog = DateTime.UtcNow.AddSeconds(3);
            this.plugin.CpLog.Info("horn setup: enemies " + (enemies.Count == 0 ? "(none)" : "[" + string.Join(", ", enemies) + "]") +
                                   " weaknesses [" + string.Join(",", weaknesses) + "] roster [" + string.Join(", ", rows.Select(r => r.Number)) + "]");
        }

        this.DumpStage(stage, enemies);

        if (encounterSignature != this.toggleEncounter)
        {
            this.toggleEncounter = encounterSignature;
            this.toggleCount = 0;
        }

        var target = this.ResolveTrio(board, roster, enemies, weaknesses);
        if (target.Count != 3)
        {
            this.Warn("no trio could be resolved for this encounter - waiting (no fallback trio)");
            return;
        }

        // A KO'd familiar cannot be summoned again for the rest of the board; substitute instead of looping.
        var alive = rows.Where(r => r.MaxHp == 0 || r.Hp != 0).ToList();
        var aliveNumbers = alive.Select(r => r.Number).ToHashSet();
        if (target.Any(t => !aliveNumbers.Contains(t)))
        {
            var substituted = target.Where(aliveNumbers.Contains).ToList();

            // Prefer replacements that still match the encounter's weaknesses before falling back to raw HP.
            if (weaknesses.Count > 0 && substituted.Count < 3)
            {
                var candidates = aliveNumbers.Where(n => !substituted.Contains(n)).ToList();
                foreach (uint pick in this.plugin.Intel.PickFromTags(candidates, weaknesses, out _))
                {
                    if (substituted.Count >= 3)
                    {
                        break;
                    }

                    if (!substituted.Contains(pick))
                    {
                        substituted.Add(pick);
                    }
                }
            }

            foreach (var row in alive.OrderByDescending(r => r.Hp).ThenBy(r => r.Number))
            {
                if (substituted.Count >= 3)
                {
                    break;
                }

                if (!substituted.Contains(row.Number))
                {
                    substituted.Add(row.Number);
                }
            }

            this.plugin.CpLog.Info("KO substitution: [" + string.Join(",", target) + "] -> [" + string.Join(",", substituted) +
                                   "] (dead: " + string.Join(",", target.Where(t => !aliveNumbers.Contains(t))) + ")");
            target = substituted;
            if (target.Count != 3)
            {
                this.Warn("not enough living familiars left for three horns");
                return;
            }
        }

        var missing = target.Where(t => !roster.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            this.Warn($"not in the AutoDuty roster: {string.Join(", ", missing)} (roster: {string.Join(",", rows.Select(r => r.Number))})");
            return;
        }

        var selected = rows.Where(r => r.Selection < 3).OrderBy(r => r.Selection).ToList();
        bool enforceOrder = this.plugin.Config.EnforceHornOrder;
        bool done = selected.Count == 3 &&
                    (enforceOrder
                        ? selected.Select(r => r.Number).SequenceEqual(target)
                        : selected.Select(r => r.Number).OrderBy(x => x).SequenceEqual(target.OrderBy(x => x)));

        if (done)
        {
            if (!this.plugin.Config.AutoStartBattle)
            {
                return;
            }

            if (DateTime.UtcNow >= this.lastConfirm.AddSeconds(2))
            {
                this.Fire(stage, 8);
                this.lastConfirm = DateTime.UtcNow;
                this.log.Information($"[CP] Trio ({string.Join(",", target)}) on the horns - starting the fight");
                this.plugin.CpLog.Info($"starting fight with {string.Join(",", target)}" +
                                       (enforceOrder ? $" (horn order {string.Join(" | ", target)})" : string.Empty));
            }

            return;
        }

        if (this.toggleCount > 40)
        {
            if (selected.Count == 3)
            {
                this.Warn("horn order could not settle - starting with the three selected beasts");
                this.Fire(stage, 8);
                this.lastConfirm = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow > this.nextEncounterLog)
            {
                this.Warn("horn assignment is not settling - waiting");
                this.nextEncounterLog = DateTime.UtcNow.AddSeconds(5);
            }

            return;
        }

        if (enforceOrder)
        {
            // Clear anything that is not in the wanted slot, one toggle per tick; picks then land in list order.
            var wrongSlot = selected.FirstOrDefault(r => r.Selection >= target.Count || target[(int)r.Selection] != r.Number);
            if (wrongSlot != null)
            {
                this.Toggle(party, wrongSlot);
                return;
            }

            foreach (uint number in target)
            {
                var row = rows.FirstOrDefault(r => r.Number == number && r.Selection >= 3 && (r.MaxHp == 0 || r.Hp != 0));
                if (row != null)
                {
                    this.Toggle(party, row);
                    return;
                }
            }

            this.Warn("no selectable familiar for the configured trio (all knocked out?)");
            return;
        }

        // Swap one row per tick: drop a wrong selection first, then add a wanted one.
        var toUnpick = selected.FirstOrDefault(r => !target.Contains(r.Number));
        if (toUnpick != null)
        {
            this.Toggle(party, toUnpick);
            return;
        }

        var toPick = rows.FirstOrDefault(r => target.Contains(r.Number) && r.Selection >= 3 && (r.MaxHp == 0 || r.Hp != 0));
        if (toPick != null)
        {
            this.Toggle(party, toPick);
            return;
        }

        this.Warn("no selectable familiar for the configured trio (all knocked out?)");
    }

    private void Toggle(AtkUnitBase* party, Row row)
    {
        this.Fire(party, 1, row.Index);
        this.nextAction = DateTime.UtcNow.AddMilliseconds(350);
        this.toggleCount++;
        this.log.Debug($"[CP] Toggle familiar #{row.Number} (row {row.Index}, selection {row.Selection})");
        this.plugin.CpLog.Info($"toggle #{row.Number} row {row.Index} (was sel {row.Selection})");
    }

    private void Fire(AtkUnitBase* addon, params int[] values)
    {
        var atkValues = stackalloc AtkValue[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            atkValues[i].Type = AtkValueType.Int;
            atkValues[i].Int = values[i];
        }

        addon->FireCallback((uint)values.Length, atkValues, true);
    }

    private void Warn(string message)
    {
        if (message == this.lastWarn && DateTime.UtcNow < this.lastWarnAt.AddSeconds(15))
        {
            return;
        }

        this.lastWarn = message;
        this.lastWarnAt = DateTime.UtcNow;
        this.log.Warning($"[CP] {message}");
        this.plugin.Chat($"[CruciblePilot] {message}");
    }

    public IReadOnlyList<string> LastEnemies { get; private set; } = Array.Empty<string>();

    public string LastPickReason { get; private set; } = string.Empty;

    private string lastEnemySignature = string.Empty;
    private List<uint> cachedPick = new();

    /// <summary>Resolve the trio for the encounter currently being set up (recomputed only when the encounter changes).</summary>
    private List<uint> ResolveTrio(int board, HashSet<uint> roster, IReadOnlyList<string> enemies, IReadOnlyCollection<string> weaknesses)
    {
        // Score runs bring exactly three beasts and keep them for every fight (Beast Squad III).
        if (this.plugin.Config.ScoreMode)
        {
            var scoreTrio = this.plugin.Config.ScoreTrio(board).Where(roster.Contains).ToList();
            return scoreTrio.Count == 3 ? scoreTrio : new List<uint>();
        }

        if (!this.plugin.Config.AutoPick)
        {
            var manual = this.plugin.Config.Trio(board);
            return manual.Count == 3 ? manual : new List<uint>();
        }

        string signature = string.Join("|", enemies) + "##" + string.Join(",", weaknesses);
        if (signature == this.lastEnemySignature && this.cachedPick.Count == 3 && this.cachedPick.All(roster.Contains))
        {
            return this.cachedPick;
        }

        this.lastEnemySignature = signature;
        if (enemies.Count > 0)
        {
            this.LastEnemies = enemies;
        }

        // The stage window lists the encounter's weaknesses; answer those directly (authoritative beast-element table).
        if (weaknesses.Count > 0)
        {
            var picks = this.plugin.Intel.PickFromTags(roster.ToList(), weaknesses, out string reason);
            if (picks.Count == 3)
            {
                this.LastPickReason = "weaknesses [" + string.Join(",", weaknesses) + "]: " + reason;
                this.plugin.CpLog.Info("encounter [" + string.Join(", ", enemies) + "] -> weakness pick " + string.Join(",", picks) + " (" + reason + ")");
                this.cachedPick = picks;
                return picks;
            }
        }

        // Fall back to the curated per-encounter list when the stage window shows no usable weakness.
        if (this.plugin.Config.UseDefaults)
        {
            var curated = this.plugin.Intel.DefaultTrio(board, enemies, out string label);
            if (curated.Count == 3 && curated.All(roster.Contains))
            {
                this.LastPickReason = "default: " + label;
                this.plugin.CpLog.Info("encounter [" + string.Join(", ", enemies) + "] -> default " + label + " " + string.Join(",", curated));
                this.cachedPick = curated;
                return curated;
            }
        }

        this.LastPickReason = "unresolved";
        return new List<uint>();
    }

    private static readonly HashSet<string> WeaknessWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "fire", "ice", "wind", "earth", "lightning", "water", "holy", "dark", "blunt", "piercing", "slashing",
    };

    /// <summary>
    /// Weakness tags shown in the stage window. Every weakness value is a standalone string carrying the element
    /// icon (so " Wind", " Water"); labels like "Strength" or "Phys. Resistance" are not in the word set, so scanning
    /// all strings catches every enemy's weakness instead of only the first one.
    /// </summary>
    private static List<string> ExtractWeaknesses(IReadOnlyList<string> stageText)
    {
        var tags = new List<string>();
        foreach (string raw in stageText)
        {
            string letters = new string(raw.Where(char.IsLetter).ToArray());
            if (letters.Length == 0 || letters.Length > 12)
            {
                continue;
            }

            string word = letters.ToLowerInvariant();
            if (!WeaknessWords.Contains(word))
            {
                continue;
            }

            string tag = word switch
            {
                "slashing" => "slash",
                "piercing" => "pierce",
                _ => word,
            };

            if (!tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    /// <summary>Texts shown in the stage window (it lists the encounter's enemies before the battle starts).</summary>
    private static List<string> ReadStageStrings(AtkUnitBase* stage)
    {
        var list = new List<string>();
        try
        {
            int count = Math.Min((int)stage->AtkValuesCount, 200);
            for (int i = 0; i < count; i++)
            {
                var value = stage->AtkValues[i];
                if (value.Type is not (AtkValueType.String or AtkValueType.ConstString) || !value.String.HasValue)
                {
                    continue;
                }

                string text = (string)value.String;
                if (string.IsNullOrWhiteSpace(text) || text.Length > 80 || list.Contains(text))
                {
                    continue;
                }

                list.Add(text.Trim());
            }
        }
        catch
        {
            // stage values change while it populates; ignore a partial read
        }

        return list;
    }

    /// <summary>Log the stage-detail addon's values once per enemy set (helps map the encounter data).</summary>
    private void DumpStage(AtkUnitBase* stage, IReadOnlyList<string> enemies)
    {
        string signature = string.Join("|", enemies);
        if (signature.Length > 0 && signature == this.lastStageDumpSignature)
        {
            return;
        }

        this.lastStageDumpSignature = signature;
        try
        {
            int count = Math.Min((int)stage->AtkValuesCount, 160);
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                var value = stage->AtkValues[i];
                builder.Append(i).Append(':').Append(value.Type);
                switch (value.Type)
                {
                    case AtkValueType.Int:
                        builder.Append('=').Append(value.Int);
                        break;
                    case AtkValueType.UInt:
                        builder.Append('=').Append(value.UInt);
                        break;
                    case AtkValueType.Bool:
                        builder.Append('=').Append(value.Bool);
                        break;
                    case AtkValueType.Float:
                        builder.Append('=').Append(value.Float.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                        break;
                }

                builder.Append(' ');
            }

            this.plugin.CpLog.Info($"stage values({count}): {builder}");
        }
        catch (Exception ex)
        {
            this.plugin.CpLog.Error(ex, "stage dump failed");
        }
    }

    public string Dump()
    {
        var party = this.gameGui.GetAddonByName<AtkUnitBase>("XBMPetParty");        if (party == null || !party->IsReady)
        {
            return "XBMPetParty is not open";
        }

        var rows = ReadRows(party);
        return rows.Count == 0
            ? "XBMPetParty has no rows"
            : string.Join(" | ", rows.Select(r => $"row{r.Index}: #{r.Number} hp {r.Hp}/{r.MaxHp} sel {r.Selection}"));
    }

    private static List<Row> ReadRows(AtkUnitBase* party)
    {
        var rows = new List<Row>();
        uint count = ReadUInt(party, 5) ?? 0;
        if (count == 0 || count > 20)
        {
            return rows;
        }

        for (int i = 0; i < count; i++)
        {
            int offset = 6 + i * 77;
            uint? number = ReadUInt(party, offset + 76);
            if (number == null)
            {
                continue;
            }

            rows.Add(new Row(
                i,
                number.Value,
                ReadUInt(party, offset + 5) ?? 0,
                ReadUInt(party, offset + 6) ?? 0,
                ReadUInt(party, offset + 74) ?? 3));
        }

        return rows;
    }

    private static uint? ReadUInt(AtkUnitBase* addon, int index)
    {
        if (addon == null || index < 0 || index >= addon->AtkValuesCount || addon->AtkValues == null)
        {
            return null;
        }

        var value = addon->AtkValues[index];
        return value.Type switch
        {
            AtkValueType.UInt => value.UInt,
            AtkValueType.Int => (uint)value.Int,
            _ => null,
        };
    }
}
