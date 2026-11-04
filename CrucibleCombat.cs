using System;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CruciblePilot;

/// <summary>
/// A Crucible-focused Beastmaster combat loop. Replaces the generic RSR rotation inside the arena with the
/// action set the Crucible actually uses: battlehorns, the axe combo, TP spenders, familiar oGCDs, crucible
/// potions and the per-horn Tempered Release finisher.
/// </summary>
public sealed class CrucibleCombat
{
    private const uint SmashAxe = 44879;
    private const uint AxebladeBite = 44883;
    private const uint Shieldsplitter = 44885;
    private const uint AvalancheAxe = 44884;
    private const uint MistralAxe = 44887;
    private const uint SpinningAxe = 44888;
    private const uint GaleAxe = 44889;
    private const uint Trick = 47093;
    private const uint PartingBlow = 44891;
    private const uint BeastMode = 44886;
    private const uint RallyingCheer = 44904;
    private const uint Rally = 44905;
    private const uint ShieldCharge = 44893;
    private const uint TemperedRelease = 44890;
    private const uint Borrow = 44895;
    private static readonly uint[] Horns = { 44881, 44892, 44894 };
    private static readonly uint[] FightPotions = { 79, 78, 77, 76, 82, 81, 80 };

    private readonly Plugin plugin;
    private readonly IObjectTable objects;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;
    private readonly IClientState clientState;
    private readonly CrucibleItems items;
    private DateTime nextTick = DateTime.MinValue;
    private int comboStage;
    private uint comboAction;
    private DateTime comboAt = DateTime.MinValue;
    private int hornStage;
    private DateTime summonPendingUntil = DateTime.MinValue;
    private DateTime lastHornAt = DateTime.MinValue;

    public CrucibleCombat(Plugin plugin, IObjectTable objects, ITargetManager targetManager, ICondition condition, IClientState clientState, CrucibleItems items)
    {
        this.plugin = plugin;
        this.objects = objects;
        this.targetManager = targetManager;
        this.condition = condition;
        this.clientState = clientState;
        this.items = items;
    }

    public unsafe void Tick()
    {
        uint territory = this.clientState.TerritoryType;
        if (territory is < 1339 or > 1343)
        {
            this.comboStage = 0;
            this.hornStage = 0;
            this.lastHornAt = DateTime.MinValue;
            return;
        }

        if (!this.condition[ConditionFlag.InCombat])
        {
            // New fight: start the horn order over once we have been out of combat for a moment.
            if (this.hornStage > 0 && DateTime.UtcNow - this.lastHornAt > TimeSpan.FromSeconds(5))
            {
                this.hornStage = 0;
                this.comboStage = 0;
            }

            return;
        }

        if (!this.plugin.Config.EnableCombat)
        {
            return;
        }

        var player = this.objects.LocalPlayer;
        if (player == null || player.CurrentHp == 0)
        {
            return;
        }

        if (DateTime.UtcNow < this.nextTick)
        {
            return;
        }

        this.nextTick = DateTime.UtcNow.AddMilliseconds(100);

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        this.TickPotions(player);

        var target = this.EnsureTarget();
        if (target == null)
        {
            return;
        }

        // Combo resets after 30 seconds without the finisher.
        if (DateTime.UtcNow - this.comboAt > TimeSpan.FromSeconds(30))
        {
            this.comboStage = 0;
        }

        // Battlehorns: XSZYYS only summons when no familiar is out (plus a 3s pending window for the 1s cast
        // and latency), and cycles through the three horns.
        if (this.plugin.Config.CombatHorns)
        {
            bool familiarOut = this.HasFamiliar(player);
            if (!familiarOut && DateTime.UtcNow >= this.summonPendingUntil)
            {
                uint horn = Horns[this.hornStage % Horns.Length];
                if (actionManager->GetActionStatus(ActionType.Action, horn) == 0 &&
                    actionManager->UseAction(ActionType.Action, horn, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
                {
                    this.plugin.CpLog.Info($"combat: summon horn {(this.hornStage % Horns.Length) + 1}");
                    this.hornStage = (this.hornStage + 1) % Horns.Length;
                    this.summonPendingUntil = DateTime.UtcNow.AddSeconds(3);
                    return;
                }
            }
        }

        // oGCDs first so they weave before the GCD lock.
        // oGCD priority, mirroring the XSZYYS resolver order: shield charge, borrow/beast mode, rallying cheer,
        // rally, trick, parting blow. Action status gates cooldowns and unmet requirements.
        if (actionManager->GetActionStatus(ActionType.Action, ShieldCharge) == 0 &&
            actionManager->UseAction(ActionType.Action, ShieldCharge, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info("combat: ogcd shield charge");
            return;
        }

        if (this.HasFamiliar(player) &&
            actionManager->GetActionStatus(ActionType.Action, Borrow) == 0 &&
            actionManager->UseAction(ActionType.Action, Borrow, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info("combat: ogcd borrow");
            return;
        }

        if (actionManager->GetActionStatus(ActionType.Action, BeastMode) == 0 &&
            actionManager->UseAction(ActionType.Action, BeastMode, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info("combat: ogcd beast mode");
            return;
        }

        foreach (uint ogcd in new[] { RallyingCheer, Rally, Trick, PartingBlow })
        {
            if (actionManager->GetActionStatus(ActionType.Action, ogcd) == 0 &&
                actionManager->UseAction(ActionType.Action, ogcd, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
            {
                this.plugin.CpLog.Info($"combat: ogcd {ogcd}");
                return;
            }
        }

        // GCD: TP spender when available, otherwise the axe combo.
        uint gcd = this.PickGcd(actionManager);
        if (gcd != 0 &&
            actionManager->UseAction(ActionType.Action, gcd, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.comboAt = DateTime.UtcNow;
            this.comboAction = gcd;
            this.comboStage = gcd switch
            {
                SmashAxe => 1,
                AxebladeBite => 2,
                _ => 0,
            };
            return;
        }
    }

    private unsafe uint PickGcd(ActionManager* actionManager)
    {
        // Finish the combo chain first; it is the reliable damage.
        if (this.comboStage == 2 && actionManager->GetActionStatus(ActionType.Action, Shieldsplitter) == 0)
        {
            return Shieldsplitter;
        }

        if (this.comboStage == 1 && actionManager->GetActionStatus(ActionType.Action, AxebladeBite) == 0)
        {
            return AxebladeBite;
        }

        // TP spenders on cooldown (they only report ready with enough TP).
        foreach (uint spender in new[] { AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe })
        {
            if (actionManager->GetActionStatus(ActionType.Action, spender) == 0)
            {
                return spender;
            }
        }

        if (actionManager->GetActionStatus(ActionType.Action, SmashAxe) == 0)
        {
            return SmashAxe;
        }

        return 0;
    }

    private unsafe void TickPotions(IPlayerCharacter player)
    {
        if (!this.plugin.Config.CombatPotions || !this.items.Available)
        {
            return;
        }

        double hp = player.MaxHp == 0 ? 100 : (double)player.CurrentHp / player.MaxHp * 100;
        if (hp > this.plugin.Config.CombatPotionHpPercent)
        {
            return;
        }

        var entries = this.items.ReadItems();
        foreach (uint potion in FightPotions)
        {
            var entry = entries.FirstOrDefault(e => e.ItemId == potion && e.Available);
            if (entry.ItemId == potion && this.items.Use((uint)entry.Slot))
            {
                this.plugin.CpLog.Info($"combat: used potion #{potion} from slot {entry.Slot} at {hp:F0}% hp");
                return;
            }
        }
    }

    private bool HasFamiliar(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        foreach (var obj in this.objects)
        {
            if (obj is IBattleNpc npc && npc.OwnerId == player.GameObjectId && npc.CurrentHp > 0)
            {
                return true;
            }
        }

        return false;
    }

    private IBattleNpc? EnsureTarget()
    {
        var current = this.targetManager.Target as IBattleNpc;
        if (current != null && current.CurrentHp > 0 && current.IsTargetable)
        {
            return current;
        }

        var player = this.objects.LocalPlayer;
        if (player == null)
        {
            return null;
        }

        IBattleNpc? best = null;
        foreach (var obj in this.objects)
        {
            if (obj is not IBattleNpc npc || npc.CurrentHp == 0 || !npc.IsTargetable || npc.ObjectKind != ObjectKind.BattleNpc)
            {
                continue;
            }

            if (npc.MaxHp > (best?.MaxHp ?? 0))
            {
                best = npc;
            }
        }

        if (best != null)
        {
            this.targetManager.Target = best;
        }

        return best;
    }
}
