using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CruciblePilot;

/// <summary>
/// Emulates the XSZYYS ACR's whistle plan for the parts RSR cannot do: the Tempered Release finisher at the
/// per-horn HP thresholds taken from the shared whistle presets (horn 1 ~10%, horn 2 ~40%, horn 3 ~20%).
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
    private DateTime nextTry = DateTime.MinValue;

    public WhistleHelper(Plugin plugin, IObjectTable objects, ITargetManager targetManager, ICondition condition, IClientState clientState)
    {
        this.plugin = plugin;
        this.objects = objects;
        this.targetManager = targetManager;
        this.condition = condition;
        this.clientState = clientState;
    }

    public unsafe void Tick()
    {
        if (!this.plugin.Config.WhistlePlan)
        {
            return;
        }

        uint territory = this.clientState.TerritoryType;
        if (territory is < 1339 or > 1343)
        {
            return;
        }

        if (!this.condition[ConditionFlag.InCombat] || DateTime.UtcNow < this.nextTry)
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

        double hp = (double)target.CurrentHp / target.MaxHp;

        int slot = this.ActiveHornSlot();
        if (slot < 0)
        {
            return;
        }

        this.nextTry = DateTime.UtcNow.AddMilliseconds(250);

        int threshold = this.plugin.Config.WhistleThresholds.Count > slot
            ? this.plugin.Config.WhistleThresholds[slot]
            : 20;

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        bool wantBorrow = slot == 2 && this.plugin.Config.WhistleAutoBorrow;
        if (wantBorrow)
        {
            if (hp * 100 > threshold)
            {
                return;
            }

            if (actionManager->GetActionStatus(ActionType.Action, Borrow) == 0 &&
                actionManager->UseAction(ActionType.Action, Borrow, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
            {
                this.plugin.CpLog.Info($"whistle: horn 3 borrow at target {hp:P0}");
            }

            return;
        }

        if (hp * 100 > threshold)
        {
            return;
        }

        if (actionManager->GetActionStatus(ActionType.Action, TemperedRelease) == 0 &&
            actionManager->UseAction(ActionType.Action, TemperedRelease, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info($"whistle: horn {slot + 1} tempered release at target {hp:P0} (threshold {threshold}%)");
        }
    }

    /// <summary>The active horn slot (0-2), or -1 when no horn is running.</summary>
    private unsafe int ActiveHornSlot()
    {
        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return -1;
        }

        int best = -1;
        float bestElapsed = float.MaxValue;
        for (int i = 0; i < Horns.Length; i++)
        {
            float total = actionManager->GetRecastTime(ActionType.Action, Horns[i]);
            if (total <= 0.5f)
            {
                continue;
            }

            float elapsed = actionManager->GetRecastTimeElapsed(ActionType.Action, Horns[i]);
            if (elapsed < total && elapsed < bestElapsed)
            {
                bestElapsed = elapsed;
                best = i;
            }
        }

        return best;
    }
}
