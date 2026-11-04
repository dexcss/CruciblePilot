using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CruciblePilot;

/// <summary>
/// Emulates the XSZYYS ACR's whistle plan for the parts RSR cannot do: the Tempered Release finisher at the
/// per-horn HP thresholds from the shared whistle presets (horn 1 ~10%, horn 2 ~40%, horn 3 ~20%).
/// Horn slots have only a 2 second recast, so the active slot is latched when a horn is cast and kept until the
/// next horn, instead of being re-detected every tick.
/// </summary>
public sealed class WhistleHelper
{
    private const uint TemperedRelease = 44890;
    private const uint Borrow = 44895;
    private static readonly uint[] Horns = { 44881, 44892, 44894 };

    private readonly Plugin plugin;
    private readonly IObjectTable objects;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;
    private readonly IClientState clientState;
    private readonly IGameGui gameGui;
    private DateTime nextTry = DateTime.MinValue;
    private int activeHorn = -1;
    private bool hudDumped;

    public WhistleHelper(Plugin plugin, IObjectTable objects, ITargetManager targetManager, ICondition condition, IClientState clientState, IGameGui gameGui)
    {
        this.plugin = plugin;
        this.objects = objects;
        this.targetManager = targetManager;
        this.condition = condition;
        this.clientState = clientState;
        this.gameGui = gameGui;
    }

    public unsafe void Tick()
    {
        uint territory = this.clientState.TerritoryType;
        if (territory is < 1339 or > 1343)
        {
            this.activeHorn = -1;
            this.hudDumped = false;
            return;
        }

        if (!this.condition[ConditionFlag.InCombat])
        {
            this.activeHorn = -1;
            return;
        }

        if (!this.plugin.Config.WhistlePlan)
        {
            return;
        }

        // Prefer the job gauge (ActiveBattlehorn) and fall back to latching the horn recast window.
        if (BeastGauge.TryRead(out var gauge))
        {
            int gaugeSlot = gauge.WhistleIndex - 1;
            if (gaugeSlot >= 0 && gaugeSlot <= 2 && gaugeSlot != this.activeHorn)
            {
                this.activeHorn = gaugeSlot;
                this.plugin.CpLog.Info($"whistle: horn slot {gaugeSlot + 1} active (gauge) tp={gauge.TpGauge} petTp={gauge.PetTpGauge} soul={gauge.SoulStacks} power={gauge.BeastPower} whistle={gauge.WhistleIndex} heart={gauge.HeartStacks}");
            }
        }

        this.DetectHorn();

        if (!this.hudDumped)
        {
            this.hudDumped = this.DumpHud();
        }

        if (DateTime.UtcNow < this.nextTry)
        {
            return;
        }

        var player = this.objects.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
        {
            return;
        }

        if (this.targetManager.Target is not IBattleNpc target || target.MaxHp == 0 || target.CurrentHp == 0 || !target.IsTargetable)
        {
            return;
        }

        this.nextTry = DateTime.UtcNow.AddMilliseconds(150);

        double hpPercent = (double)target.CurrentHp / target.MaxHp * 100.0;
        int slot = this.activeHorn;
        int threshold = slot >= 0 && this.plugin.Config.WhistleThresholds.Count > slot
            ? this.plugin.Config.WhistleThresholds[slot]
            : 20;

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        if (!HasOneWithNature(player))
        {
            return;
        }

        bool wantBorrow = slot == 2 && this.plugin.Config.WhistleAutoBorrow;
        if (wantBorrow)
        {
            if (hpPercent > threshold)
            {
                return;
            }

            if (actionManager->GetActionStatus(ActionType.Action, Borrow) == 0 &&
                actionManager->UseAction(ActionType.Action, Borrow, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.Queue, 0, null))
            {
                this.plugin.CpLog.Info($"whistle: horn 3 borrow at target {hpPercent:F0}%");
            }

            return;
        }

        if (hpPercent > threshold)
        {
            return;
        }

        if (actionManager->GetActionStatus(ActionType.Action, TemperedRelease) == 0 &&
            actionManager->UseAction(ActionType.Action, TemperedRelease, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.Queue, 0, null))
        {
            this.plugin.CpLog.Info($"whistle: horn {(slot < 0 ? 0 : slot + 1)} tempered release at target {hpPercent:F0}% (threshold {threshold}%)");
        }
    }

    private static bool HasOneWithNature(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        const uint OneWithNature = 4601;
        foreach (var status in player.StatusList)
        {
            if (status.StatusId == OneWithNature)
            {
                return true;
            }
        }

        return false;
    }

    private unsafe void DetectHorn()
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        for (int i = 0; i < Horns.Length; i++)
        {
            float total = actionManager->GetRecastTime(ActionType.Action, Horns[i]);
            if (total <= 0.1f)
            {
                continue;
            }

            float elapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, Horns[i]);
            if (elapsed < total - 0.05f && elapsed < 2.5f)
            {
                if (this.activeHorn != i)
                {
                    this.activeHorn = i;
                    this.plugin.CpLog.Info($"whistle: horn slot {i + 1} active");
                }

                return;
            }
        }
    }

    /// <summary>Dump the crucible HUD values once per board so item slots can be mapped in the next iteration.</summary>
    private unsafe bool DumpHud()
    {
        var hud = this.gameGui.GetAddonByName<AtkUnitBase>("XBMContentsMainHUD");
        if (hud == null || !hud->IsReady)
        {
            return false;
        }

        try
        {
            var parts = new System.Collections.Generic.List<string>();
            int count = hud->AtkValuesCount;
            for (int i = 0; i < count && i < 200; i++)
            {
                var value = hud->AtkValues[i];
                parts.Add($"{i}:{value.Type}={value.Int}");
            }

            this.plugin.CpLog.Info("hud values(" + hud->AtkValuesCount + "): " + string.Join(" ", parts));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
