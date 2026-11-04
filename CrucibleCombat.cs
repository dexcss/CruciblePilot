using System;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CruciblePilot;

/// <summary>
/// Crucible Beastmaster combat, ported from the observed behaviour of the XSZYYS ACR: countdown opener,
/// familiar-gated battlehorns, axe combo, TP spenders, the full familiar oGCD chain, gauge-driven instinct
/// spenders, crucible potions and the per-horn Tempered Release finisher.
/// </summary>
public sealed class CrucibleCombat
{
    private const uint SmashAxe = 44879;
    private const uint Capture = 44880;
    private const uint FirstHorn = 44881;
    private const uint AxebladeBite = 44883;
    private const uint AvalancheAxe = 44884;
    private const uint Shieldsplitter = 44885;
    private const uint BeastMode = 44886;
    private const uint MistralAxe = 44887;
    private const uint SpinningAxe = 44888;
    private const uint GaleAxe = 44889;
    private const uint TemperedRelease = 44890;
    private const uint PartingBlow = 44891;
    private const uint SecondHorn = 44892;
    private const uint ShieldCharge = 44893;
    private const uint ThirdHorn = 44894;
    private const uint Borrow = 44895;
    private const uint RangedFallback = 44900;
    private const uint RallyingCheer = 44904;
    private const uint Rally = 44905;
    private const uint Burst = 44930;
    private const uint Trick = 47093;

    private static readonly uint[] Horns = { FirstHorn, SecondHorn, ThirdHorn };
    private static readonly uint[] FightPotions = { 79, 78, 77, 76, 82, 81, 80 };

    private readonly Plugin plugin;
    private readonly IObjectTable objects;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;
    private readonly IClientState clientState;
    private readonly IGameGui gameGui;
    private readonly CrucibleItems items;
    private DateTime nextTick = DateTime.MinValue;
    private int comboStage;
    private DateTime comboAt = DateTime.MinValue;
    private int hornStage;
    private DateTime summonPendingUntil = DateTime.MinValue;
    private DateTime lastSummonAt = DateTime.MinValue;
    private int pendingHornSlot = -1;
    private System.Numerics.Vector3 lastPosition;
    private DateTime lastMovedAt = DateTime.MinValue;
    private DateTime nextDiagnostic = DateTime.MinValue;
    private DateTime lastPotionAt = DateTime.MinValue;
    private DateTime countdownStartedAt = DateTime.MinValue;
    private bool countdownHorn3;
    private bool countdownBorrow;
    private bool countdownHorn1;

    public CrucibleCombat(Plugin plugin, IObjectTable objects, ITargetManager targetManager, ICondition condition, IClientState clientState, IGameGui gameGui, CrucibleItems items)
    {
        this.plugin = plugin;
        this.objects = objects;
        this.targetManager = targetManager;
        this.condition = condition;
        this.clientState = clientState;
        this.gameGui = gameGui;
        this.items = items;
    }

    public unsafe void Tick()
    {
        uint territory = this.clientState.TerritoryType;
        if (territory is < 1339 or > 1343)
        {
            this.ResetFightState();
            return;
        }

        if (!this.plugin.Config.EnableCombat)
        {
            return;
        }

        if (!this.condition[ConditionFlag.InCombat])
        {
            // Between fights: run the pre-pull countdown sequence and reset once out for a moment.
            if (this.plugin.Config.CombatCountdownOpener)
            {
                this.TickCountdownOpener();
            }

            if (DateTime.UtcNow - this.summonPendingUntil > TimeSpan.FromSeconds(8))
            {
                this.comboStage = 0;
            }

            return;
        }

        this.countdownStartedAt = DateTime.MinValue;
        this.countdownHorn3 = this.countdownBorrow = this.countdownHorn1 = false;

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

        if (DateTime.UtcNow >= this.nextDiagnostic)
        {
            this.nextDiagnostic = DateTime.UtcNow.AddSeconds(5);
            this.LogDiagnostics(player);
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        // Combo resets after 30 seconds.
        if (DateTime.UtcNow - this.comboAt > TimeSpan.FromSeconds(30))
        {
            this.comboStage = 0;
        }

        this.TickPotions(player);

        var target = this.EnsureTarget();

        // Downtime axe: no enemy to hit, so spend the TP on Avalanche Axe when it is ready.
        if (target == null)
        {
            if (this.plugin.Config.CombatDowntimeAxe &&
                actionManager->GetActionStatus(ActionType.Action, AvalancheAxe) == 0 &&
                actionManager->UseAction(ActionType.Action, AvalancheAxe, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
            {
                this.plugin.CpLog.Info("combat: downtime avalanche axe");
            }

            return;
        }

        // Pet-skill finisher: Parting Blow while the familiar is finishing its own ability.
        if (this.plugin.Config.CombatPetFinisher && this.TryPetFinisher(actionManager, player))
        {
            return;
        }

        // Battlehorns. The summon is a 1s cast, so: only start it while standing still, do NOT advance the horn
        // slot when the cast starts (a cancelled cast would otherwise consume the horn), advance only when the
        // familiar actually appears, and retry the same horn if the cast was lost.
        if (this.plugin.Config.CombatHorns)
        {
            if (this.pendingHornSlot >= 0)
            {
                if (this.HasFamiliar(player))
                {
                    this.plugin.CpLog.Info($"combat: horn {this.pendingHornSlot + 1} summoned");
                    this.hornStage = (this.pendingHornSlot + 1) % Horns.Length;
                    this.pendingHornSlot = -1;
                    this.lastSummonAt = DateTime.UtcNow;
                    return;
                }

                if (DateTime.UtcNow >= this.summonPendingUntil)
                {
                    this.plugin.CpLog.Info($"combat: horn {this.pendingHornSlot + 1} cast lost (moving/interrupted), retrying same horn");
                    this.pendingHornSlot = -1;
                }

                return;
            }

            if (!this.HasFamiliar(player) && this.IsStandingStill(player) && DateTime.UtcNow - this.lastSummonAt > TimeSpan.FromSeconds(2))
            {
                uint horn = Horns[this.hornStage % Horns.Length];
                if (actionManager->GetActionStatus(ActionType.Action, horn) == 0 &&
                    actionManager->UseAction(ActionType.Action, horn, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
                {
                    this.plugin.CpLog.Info($"combat: casting horn {(this.hornStage % Horns.Length) + 1}");
                    this.pendingHornSlot = this.hornStage % Horns.Length;
                    this.summonPendingUntil = DateTime.UtcNow.AddSeconds(3);
                    return;
                }
            }
        }

        // Ranged fallback when the target is out of melee reach.
        float distance = Distance(player.Position, target.Position);
        if (this.plugin.Config.CombatRangedFallback && distance > 3.5f &&
            actionManager->GetActionStatus(ActionType.Action, RangedFallback) == 0 &&
            actionManager->UseAction(ActionType.Action, RangedFallback, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info($"combat: ranged fallback at {distance:F1}m");
            return;
        }

        // Capture marks the target for the capture mechanic (off by default).
        if (this.plugin.Config.CombatCapture &&
            actionManager->GetActionStatus(ActionType.Action, Capture) == 0 &&
            actionManager->UseAction(ActionType.Action, Capture, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info("combat: capture");
            return;
        }

        // oGCD priority: shield charge, borrow, beast mode, gauge-gated cheer/rally, burst, trick, parting blow.
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

        if (BeastGauge.TryRead(out var gauge))
        {
            if (gauge.SoulStacks > 0 &&
                actionManager->GetActionStatus(ActionType.Action, RallyingCheer) == 0 &&
                actionManager->UseAction(ActionType.Action, RallyingCheer, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
            {
                this.plugin.CpLog.Info($"combat: ogcd cheer (natural {gauge.SoulStacks})");
                return;
            }

            if (gauge.HeartStacks > 0 &&
                actionManager->GetActionStatus(ActionType.Action, Rally) == 0 &&
                actionManager->UseAction(ActionType.Action, Rally, 0xE0000000, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
            {
                this.plugin.CpLog.Info($"combat: ogcd rally (mastered {gauge.HeartStacks})");
                return;
            }
        }

        foreach (uint ogcd in new[] { Burst, Trick, PartingBlow })
        {
            if (actionManager->GetActionStatus(ActionType.Action, ogcd) == 0 &&
                actionManager->UseAction(ActionType.Action, ogcd, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
            {
                this.plugin.CpLog.Info($"combat: ogcd {ogcd}");
                return;
            }
        }

        // GCD: combo chain first, TP spenders when they report ready.
        uint gcd = this.PickGcd(actionManager);
        if (gcd != 0 &&
            actionManager->UseAction(ActionType.Action, gcd, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.comboAt = DateTime.UtcNow;
            this.plugin.CpLog.Info($"combat: gcd {gcd}");
            this.comboStage = gcd switch
            {
                SmashAxe => 1,
                AxebladeBite => 2,
                Shieldsplitter => 0,
                _ => 0,
            };
        }
    }

    private unsafe void TickCountdownOpener()
    {
        var addon = this.gameGui.GetAddonByName<AtkUnitBase>("Countdown");
        if (addon == null || !addon->IsReady || !addon->IsVisible)
        {
            this.countdownStartedAt = DateTime.MinValue;
            this.countdownHorn3 = this.countdownBorrow = this.countdownHorn1 = false;
            return;
        }

        if (this.countdownStartedAt == DateTime.MinValue)
        {
            this.countdownStartedAt = DateTime.UtcNow;
            return;
        }

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        double elapsed = (DateTime.UtcNow - this.countdownStartedAt).TotalSeconds;
        var countdownPlayer = this.objects.LocalPlayer;
        if (countdownPlayer == null || !this.IsStandingStill(countdownPlayer))
        {
            return;
        }

        if (!this.countdownHorn3 && elapsed >= 1.0 &&
            this.Use(actionManager, ThirdHorn, 0xE0000000))
        {
            this.countdownHorn3 = true;
            this.plugin.CpLog.Info("opener: third horn during countdown");
            return;
        }

        if (this.countdownHorn3 && !this.countdownBorrow && elapsed >= 2.5 &&
            this.Use(actionManager, Borrow, 0xE0000000))
        {
            this.countdownBorrow = true;
            this.plugin.CpLog.Info("opener: borrow during countdown");
            return;
        }

        if (this.countdownBorrow && !this.countdownHorn1 && elapsed >= 3.0 &&
            this.Use(actionManager, FirstHorn, 0xE0000000))
        {
            this.countdownHorn1 = true;
            this.plugin.CpLog.Info("opener: first horn during countdown");
        }
    }

    private unsafe bool TryPetFinisher(ActionManager* actionManager, IPlayerCharacter player)
    {
        var pet = this.FindFamiliar(player);
        if (pet == null || pet.CastActionId is not (49428u or 49429u))
        {
            return false;
        }

        double remaining = pet.TotalCastTime - pet.CurrentCastTime;
        if (remaining <= 0 || remaining > 2.0 || pet.Level % 5 != 0)
        {
            return false;
        }

        if (actionManager->GetActionStatus(ActionType.Action, PartingBlow) != 0)
        {
            return false;
        }

        var target = this.targetManager.Target;
        if (target == null)
        {
            return false;
        }

        if (actionManager->UseAction(ActionType.Action, PartingBlow, target.GameObjectId, 0xFFFF, ActionManager.UseActionMode.None, 0, null))
        {
            this.plugin.CpLog.Info($"combat: pet finisher parting blow (pet cast {pet.CastActionId}, {remaining:F1}s left, lv{pet.Level})");
            return true;
        }

        return false;
    }

    private unsafe bool Use(ActionManager* actionManager, uint action, ulong targetId)
        => actionManager->GetActionStatus(ActionType.Action, action) == 0 &&
           actionManager->UseAction(ActionType.Action, action, targetId, 0xFFFF, ActionManager.UseActionMode.None, 0, null);

    private unsafe uint PickGcd(ActionManager* actionManager)
    {
        if (this.comboStage == 2 && actionManager->GetActionStatus(ActionType.Action, Shieldsplitter) == 0)
        {
            return Shieldsplitter;
        }

        if (this.comboStage == 1 && actionManager->GetActionStatus(ActionType.Action, AxebladeBite) == 0)
        {
            return AxebladeBite;
        }

        foreach (uint spender in new[] { AvalancheAxe, MistralAxe, SpinningAxe, GaleAxe })
        {
            if (actionManager->GetActionStatus(ActionType.Action, spender) == 0)
            {
                return spender;
            }
        }

        return actionManager->GetActionStatus(ActionType.Action, SmashAxe) == 0 ? SmashAxe : 0;
    }

    private unsafe void TickPotions(IPlayerCharacter player)
    {
        if (!this.plugin.Config.CombatPotions || !this.items.Available)
        {
            return;
        }

        double hp = player.MaxHp == 0 ? 100 : (double)player.CurrentHp / player.MaxHp * 100;
        if (hp > this.plugin.Config.CombatPotionHpPercent || DateTime.UtcNow - this.lastPotionAt < TimeSpan.FromSeconds(3))
        {
            return;
        }

        var entries = this.items.ReadItems();
        foreach (uint potion in FightPotions)
        {
            var entry = entries.FirstOrDefault(e => e.ItemId == potion && e.Available);
            if (entry.ItemId == potion && this.items.Use(entry.InventorySlot))
            {
                this.lastPotionAt = DateTime.UtcNow;
                this.plugin.CpLog.Info($"combat: used potion #{potion} from slot {entry.Slot} at {hp:F0}% hp");
                return;
            }
        }
    }

    private void LogDiagnostics(IPlayerCharacter player)
    {
        try
        {
            var pets = this.objects.OfType<IBattleNpc>().Where(n => n.OwnerId != 0).Select(n => $"{n.Name}#{n.BaseId}/owner={n.OwnerId:X}/hp={n.CurrentHp}").Take(6);
            string gaugeText = BeastGauge.TryRead(out var g)
                ? $"tp={g.TpGauge} petTp={g.PetTpGauge} soul={g.SoulStacks} power={g.BeastPower} whistle={g.WhistleIndex} heart={g.HeartStacks} horn={g.ActiveBattlehorn}"
                : "gauge?";
            this.plugin.CpLog.Info("combat diag: player={player.GameObjectId:X} pets=[" + string.Join(", ", pets) + "] " + gaugeText);
        }
        catch (Exception)
        {
        }
    }

    private bool IsStandingStill(IPlayerCharacter player)
    {
        var position = player.Position;
        if (System.Numerics.Vector3.Distance(position, this.lastPosition) > 0.05f)
        {
            this.lastPosition = position;
            this.lastMovedAt = DateTime.UtcNow;
        }

        return DateTime.UtcNow - this.lastMovedAt > TimeSpan.FromSeconds(0.4);
    }

    private bool HasFamiliar(IPlayerCharacter player) => this.FindFamiliar(player) != null;

    private IBattleNpc? FindFamiliar(IPlayerCharacter player)
    {
        foreach (var obj in this.objects)
        {
            if (obj is IBattleNpc npc && npc.OwnerId == player.EntityId && npc.EntityId != player.EntityId && npc.ObjectKind == ObjectKind.BattleNpc && npc.CurrentHp > 0)
            {
                return npc;
            }
        }

        return null;
    }

    private IBattleNpc? EnsureTarget()
    {
        var current = this.targetManager.Target as IBattleNpc;
        if (current != null && current.CurrentHp > 0 && current.IsTargetable)
        {
            return current;
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

    private static float Distance(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
        => (a - b).Length();

    private void ResetFightState()
    {
        this.comboStage = 0;
        this.hornStage = 0;
        this.summonPendingUntil = DateTime.MinValue;
        this.countdownStartedAt = DateTime.MinValue;
        this.countdownHorn3 = this.countdownBorrow = this.countdownHorn1 = false;
    }
}
