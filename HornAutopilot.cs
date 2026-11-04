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
    private string lastWarn = string.Empty;
    private DateTime lastWarnAt = DateTime.MinValue;

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
        if (party == null || !party->IsReady || !party->IsVisible)
        {
            return;
        }

        var stage = this.gameGui.GetAddonByName<AtkUnitBase>("XBMStageDetailList");
        if (stage == null || !stage->IsReady || !stage->IsVisible)
        {
            return;
        }

        // The game asks "start with unfilled horns?" right after we press start; say yes.
        var yesno = this.gameGui.GetAddonByName<AtkUnitBase>("SelectYesno");
        if (yesno != null && yesno->IsReady && yesno->IsVisible &&
            DateTime.UtcNow < this.lastConfirm.AddSeconds(8) && DateTime.UtcNow > this.nextYes)
        {
            this.Fire(yesno, 0);
            this.nextYes = DateTime.UtcNow.AddSeconds(1);
            this.log.Information("[CP] Answered the underfilled-horns prompt");
            return;
        }

        var rows = ReadRows(party);
        if (rows.Count == 0)
        {
            return;
        }

        var roster = rows.Select(r => r.Number).ToHashSet();
        var target = this.ResolveTrio(board, roster);
        if (target.Count == 0)
        {
            this.Warn($"no trio for board {board} - configure one in the window or check the roster");
            return;
        }

        var missing = target.Where(t => !roster.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            this.Warn($"not in the AutoDuty roster: {string.Join(", ", missing)} (roster: {string.Join(",", rows.Select(r => r.Number))})");
            return;
        }

        var selected = rows.Where(r => r.Selection < 3).ToList();
        bool done = selected.Count == 3 &&
                    selected.Select(r => r.Number).OrderBy(x => x).SequenceEqual(target.OrderBy(x => x));

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
            }

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
        this.log.Debug($"[CP] Toggle familiar #{row.Number} (row {row.Index}, selection {row.Selection})");
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

    private List<uint> ResolveTrio(int board, HashSet<uint> roster)
    {
        var fixedTrio = this.plugin.Config.Trio(board);
        if (!this.plugin.Config.AutoPick)
        {
            return fixedTrio;
        }

        var enemies = this.plugin.Intel.CurrentEnemies();
        if (enemies.Count > 0)
        {
            this.LastEnemies = enemies;
        }

        if (enemies.Count == 0)
        {
            return fixedTrio;
        }

        var picks = this.plugin.Intel.PickTrio(roster.ToList(), out string reason);
        this.LastPickReason = reason;
        if (picks.Count != 3 || picks.Any(p => !roster.Contains(p)))
        {
            this.log.Debug($"[CP] encounter picker fell back to the configured trio ({reason})");
            return fixedTrio;
        }

        return picks;
    }

    public string Dump()
    {
        var party = this.gameGui.GetAddonByName<AtkUnitBase>("XBMPetParty");
        if (party == null || !party->IsReady)
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
