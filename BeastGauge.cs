using System;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CruciblePilot;

/// <summary>
/// Reads the Beastmaster job gauge exactly the way the ACR does: the raw bytes that start at the JobGaugeManager
/// CurrentGauge field address (not the pointer target), with TP at +16, beast power at +17, whistle index at +19
/// and beast heart at +24.
/// </summary>
public static unsafe class BeastGauge
{
    public readonly record struct Snapshot(byte Tp, byte BeastPower, byte WhistleIndex, byte BeastHeart);

    public static bool TryRead(out Snapshot snapshot)
    {
        snapshot = default;
        var manager = JobGaugeManager.Instance();
        if (manager == null)
        {
            return false;
        }

        byte* raw = (byte*)&manager->CurrentGauge;
        snapshot = new Snapshot(raw[16], raw[17], raw[19], raw[24]);
        return true;
    }

    /// <summary>Raw hex of the first 48 gauge bytes, for diagnostics.</summary>
    public static string RawHex()
    {
        var manager = JobGaugeManager.Instance();
        if (manager == null)
        {
            return string.Empty;
        }

        byte* raw = (byte*)&manager->CurrentGauge;
        var copy = new byte[48];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = raw[i];
        }

        return BitConverter.ToString(copy).Replace('-', ' ');
    }
}
