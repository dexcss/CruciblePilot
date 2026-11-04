using System;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace CruciblePilot;

/// <summary>
/// Food check before a run: the Crucible disables potions, so a meal is the only buff you can carry in.
/// Reads the Well Fed status (48) and else uses the item through the game's ActionManager (the same way
/// Artisan eats crafting food: UseAction(ActionType.Item, itemId, 0xE0000000, 0xFFFF)).
/// </summary>
public sealed class FoodHelper
{
    private const uint WellFedStatus = 48;
    private const ulong ItemUseParam = 0xE0000000;

    private readonly Plugin plugin;
    private readonly IObjectTable objects;
    private readonly IDataManager data;
    private uint? cachedFoodId;

    public FoodHelper(Plugin plugin, IObjectTable objects, IDataManager data)
    {
        this.plugin = plugin;
        this.objects = objects;
        this.data = data;
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
            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
            return character != null && character->HasStatus(WellFedStatus);
        }
    }

    /// <summary>Eats the configured food when the buff is missing and food eating is enabled.</summary>
    public void TryEat(Configuration config)
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

        uint itemId = this.ResolveFoodId(config.FoodItemName);
        if (itemId == 0)
        {
            this.plugin.CpLog.Warn("food: item not found: " + config.FoodItemName);
            this.plugin.Chat("[CruciblePilot] food item not found: " + config.FoodItemName);
            return;
        }

        unsafe
        {
            var inventory = InventoryManager.Instance();
            int normal = inventory == null ? 0 : inventory->GetInventoryItemCount(itemId, false);
            int hq = inventory == null ? 0 : inventory->GetInventoryItemCount(itemId, true);
            if (normal <= 0 && hq <= 0)
            {
                this.plugin.CpLog.Warn($"food: no {config.FoodItemName} in inventory ({itemId}, nq {normal}, hq {hq})");
                this.plugin.Chat($"[CruciblePilot] no {config.FoodItemName} in your bags");
                return;
            }

            // HQ food gives the better buff; the game wants the HQ item id (base + 1,000,000) for it.
            uint useId = hq > 0 ? itemId + 1_000_000 : itemId;
            bool used = ActionManager.Instance()->UseAction(
                ActionType.Item,
                useId,
                ItemUseParam,
                0xFFFF,
                ActionManager.UseActionMode.None,
                0,
                null);
            this.plugin.CpLog.Info($"food: UseAction(Item, {useId} \"{config.FoodItemName}\", nq {normal} hq {hq}) = {used}");
            if (!used)
            {
                this.plugin.CpLog.Warn("food: the game refused the item action (already Well Fed, in combat, or a menu is open)");
            }
        }
    }

    private uint ResolveFoodId(string itemName)
    {
        if (this.cachedFoodId is { } cached)
        {
            return cached;
        }

        try
        {
            var sheet = this.data.GetExcelSheet<Item>();
            foreach (var row in sheet)
            {
                string name = row.Name.ExtractText();
                if (string.Equals(name.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    this.cachedFoodId = row.RowId;
                    return row.RowId;
                }
            }
        }
        catch (Exception ex)
        {
            this.plugin.CpLog.Error(ex, "food: item lookup failed");
        }

        return 0;
    }
}
