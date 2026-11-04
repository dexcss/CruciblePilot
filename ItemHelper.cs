using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CruciblePilot;

/// <summary>
/// Uses Crucible consumables (Beastmaster Reraisser, Temporal Sand, Beast Potion/Remedy Kit) so a death
/// does not end the run. AutoDuty buys items but does not use these, and they have to be held in hand.
/// </summary>
public sealed class ItemHelper
{
    private const ulong ItemUseParam = 0xE0000000;

    private readonly Plugin plugin;
    private readonly IObjectTable objects;

    public ItemHelper(Plugin plugin, IObjectTable objects)
    {
        this.plugin = plugin;
        this.objects = objects;
    }

    public int Count(uint itemId)
    {
        unsafe
        {
            var inventory = InventoryManager.Instance();
            if (inventory == null)
            {
                return 0;
            }

            int normal = inventory->GetInventoryItemCount(itemId, false);
            int hq = inventory->GetInventoryItemCount(itemId, true);
            return normal + hq;
        }
    }

    /// <summary>Returns true when the game accepted the item action.</summary>
    public bool UseItem(uint itemId)
    {
        unsafe
        {
            return ActionManager.Instance()->UseAction(
                ActionType.Item,
                itemId,
                ItemUseParam,
                0xFFFF,
                ActionManager.UseActionMode.None,
                0,
                null);
        }
    }
}
