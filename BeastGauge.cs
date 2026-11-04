using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace CruciblePilot;

/// <summary>
/// Reads the Beastmaster job gauge the same way the ACR does: TP gauge, active battlehorn, instinct stacks and
/// the per-horn beast values, straight from the game's job gauge manager.
/// </summary>
public static unsafe class BeastGauge
{
    public readonly record struct Snapshot(
        byte TpGauge,
        byte PetTpGauge,
        byte ActiveBattlehorn,
        byte InstinctualComboState,
        byte NaturalInstinct,
        byte MasteredInstinct);

    public static bool TryRead(out Snapshot snapshot)
    {
        snapshot = default;
        var manager = JobGaugeManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var gauge = (BeastmasterGauge*)manager->CurrentGauge;
        if (gauge == null)
        {
            return false;
        }

        snapshot = new Snapshot(
            gauge->TPGauge,
            gauge->PetTPGauge,
            gauge->ActiveBattlehorn,
            gauge->InstinctualComboState,
            (byte)(gauge->InstinctState & 0x3),
            (byte)((gauge->InstinctState >> 2) & 0x3));
        return true;
    }
}
