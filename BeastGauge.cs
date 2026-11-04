using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Gauge;

namespace CruciblePilot;

/// <summary>
/// Reads the Beastmaster job gauge. The struct fields are used for TP, but the whistle/instinct values are read
/// from the raw gauge bytes because that is how the ACR reads them (offsets 16, 17, 19 and 24).
/// </summary>
public static unsafe class BeastGauge
{
    public readonly record struct Snapshot(
        byte TpGauge,
        byte PetTpGauge,
        byte SoulStacks,
        byte BeastPower,
        byte WhistleIndex,
        byte HeartStacks,
        byte ActiveBattlehorn);

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

        byte* raw = (byte*)gauge;
        snapshot = new Snapshot(
            gauge->TPGauge,
            gauge->PetTPGauge,
            raw[16],
            raw[17],
            raw[19],
            raw[24],
            gauge->ActiveBattlehorn);
        return true;
    }
}
