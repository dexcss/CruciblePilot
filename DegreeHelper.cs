using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CruciblePilot;

/// <summary>
/// Sets the Crucible Mode (degree) on the board's pre-battle window. Pawprint does not touch the degree, so
/// without this every Pawprint run inherits whatever mode the game remembered (usually Standard).
/// </summary>
public sealed class DegreeHelper
{
    private readonly Plugin plugin;
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private bool fired;

    public DegreeHelper(Plugin plugin, IGameGui gameGui, IClientState clientState)
    {
        this.plugin = plugin;
        this.gameGui = gameGui;
        this.clientState = clientState;
    }

    public unsafe void Tick()
    {
        if (!this.plugin.Config.ForceDegree)
        {
            return;
        }

        uint territory = this.clientState.TerritoryType;
        if (territory is < 1339 or > 1343)
        {
            this.fired = false;
            return;
        }

        var stage = this.gameGui.GetAddonByName<AtkUnitBase>("XBMStageDetailList");
        if (stage == null || !stage->IsReady || !stage->IsVisible)
        {
            this.fired = false;
            return;
        }

        if (this.fired)
        {
            return;
        }

        this.fired = true;
        var values = stackalloc AtkValue[2];
        values[0].Type = AtkValueType.Int;
        values[0].Int = 2;
        values[1].Type = AtkValueType.Int;
        values[1].Int = this.plugin.Config.Degree;
        stage->FireCallback(2, values, true);
        this.plugin.CpLog.Info("degree: set Crucible Mode to " + Configuration.DegreeName(this.plugin.Config.Degree) + " on the stage window");
    }
}
