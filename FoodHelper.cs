using System;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace CruciblePilot;

/// <summary>
/// Food check before a run: the Crucible disables potions, so a meal is the only buff you can carry in.
/// Reads the Well Fed status (48) and, when it is missing, asks the game to use the configured food item.
/// </summary>
public sealed class FoodHelper
{
    private const uint WellFedStatus = 48;

    private readonly Plugin plugin;
    private readonly IObjectTable objects;

    public FoodHelper(Plugin plugin, IObjectTable objects)
    {
        this.plugin = plugin;
        this.objects = objects;
    }

    public bool HasFood()
    {
        var player = this.objects.LocalPlayer;
        if (player == null)
        {
            return false;
        }

        unsafe
        {
            var character = (Character*)player.Address;
            return character != null && character->HasStatus(WellFedStatus);
        }
    }

    /// <summary>Eats the configured food when the buff is missing and food eating is enabled.</summary>
    public void EnsureFood(Configuration config)
    {
        if (!config.EatFood)
        {
            return;
        }

        if (this.HasFood())
        {
            this.plugin.CpLog.Info("food: Well Fed already active");
            return;
        }

        Chat.ExecuteCommand($"/item {config.FoodItemName}");
        this.plugin.CpLog.Info("food: requested /item " + config.FoodItemName);
        this.plugin.Chat("[CruciblePilot] No food buff - eating " + config.FoodItemName);
    }
}
