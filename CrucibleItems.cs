using System;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CruciblePilot;

/// <summary>
/// Uses crucible consumables (potions/heals) through the game's own item-use call, and reads the crucible HUD to
/// know which items sit in which slot (the HUD has ten five-value item groups starting at index 11).
/// </summary>
public sealed unsafe class CrucibleItems : IDisposable
{
    private const string UseItemSignature = "E8 ?? ?? ?? ?? 83 7F 44 00 48 8D 57 44";
    private const int FirstItemValue = 11;
    private const int ItemGroupStride = 5;

    private delegate void UseItemDelegate(InstanceContentDirector* director, uint slot, int beastId);

    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly Hook<UseItemDelegate>? hook;

    public CrucibleItems(IGameInteropProvider interop, IGameGui gameGui, IPluginLog log)
    {
        this.gameGui = gameGui;
        this.log = log;
        try
        {
            this.hook = interop.HookFromSignature<UseItemDelegate>(UseItemSignature, this.UseItemDetour);
            this.hook.Enable();
        }
        catch (Exception ex)
        {
            this.log.Warning("[CP] crucible item hook failed: " + ex.Message);
            this.hook = null;
        }
    }

    public bool Available => this.hook != null;

    public void Dispose() => this.hook?.Dispose();

    public bool Use(uint slot, int beastId = 0)
    {
        if (this.hook == null)
        {
            return false;
        }

        var framework = EventFramework.Instance();
        var director = framework == null ? null : framework->GetInstanceContentDirector();
        if (director == null)
        {
            return false;
        }

        this.hook.Original(director, slot, beastId);
        return true;
    }

    private void UseItemDetour(InstanceContentDirector* director, uint slot, int beastId)
        => this.hook!.Original(director, slot, beastId);

    /// <summary>Returns (slot, itemId, available) for every item shown in the crucible HUD.</summary>
    public System.Collections.Generic.List<(int Slot, uint ItemId, bool Available)> ReadItems()
    {
        var result = new System.Collections.Generic.List<(int, uint, bool)>();
        var hud = this.gameGui.GetAddonByName<AtkUnitBase>("XBMContentsMainHUD");
        if (hud == null || !hud->IsReady)
        {
            return result;
        }

        int count = hud->AtkValuesCount;
        for (int slot = 0; slot < 10; slot++)
        {
            int baseIndex = FirstItemValue + slot * ItemGroupStride;
            int idIndex = baseIndex + 1;
            int availableIndex = baseIndex + 3;
            if (availableIndex >= count)
            {
                break;
            }

            var idValue = hud->AtkValues[idIndex];
            var availableValue = hud->AtkValues[availableIndex];
            uint itemId = (uint)idValue.Int;
            bool available = availableValue.Byte != 0;
            result.Add((slot, itemId, available));
        }

        return result;
    }
}
