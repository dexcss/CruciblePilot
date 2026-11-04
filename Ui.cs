using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace CruciblePilot;

/// <summary>Small settings window: degree, combat switches and the whistle thresholds.</summary>
public sealed class Ui
{
    private static readonly string[] DegreeNames = { "Standard", "First", "Second", "Third" };

    private readonly Plugin plugin;

    public Ui(Plugin plugin) => this.plugin = plugin;

    public void Toggle()
    {
        this.plugin.Config.ShowWindow = !this.plugin.Config.ShowWindow;
        this.plugin.SaveConfig();
    }

    public void Draw()
    {
        var config = this.plugin.Config;
        if (!config.ShowWindow)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(430, 320), ImGuiCond.FirstUseEver);
        bool windowOpen = config.ShowWindow;
        if (!ImGui.Begin("CruciblePilot###cp", ref windowOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (!windowOpen)
            {
                config.ShowWindow = false;
                this.plugin.SaveConfig();
            }

            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("Pawprint handles routing, shop and presets. This plugin handles Crucible Mode, combat, horns and potions.");

        bool forceDegree = config.ForceDegree;
        if (ImGui.Checkbox("Force Crucible Mode", ref forceDegree))
        {
            config.ForceDegree = forceDegree;
            this.plugin.SaveConfig();
        }

        ImGui.SetNextItemWidth(140);
        int degree = config.Degree;
        if (ImGui.Combo("Mode", ref degree, DegreeNames, DegreeNames.Length))
        {
            config.Degree = degree;
            this.plugin.SaveConfig();
        }

        ImGui.Separator();

        bool combat = config.EnableCombat;
        if (ImGui.Checkbox("Own Beastmaster combat loop (turn RSR off)", ref combat))
        {
            config.EnableCombat = combat;
            this.plugin.SaveConfig();
        }

        if (config.EnableCombat)
        {
            bool horns = config.CombatHorns;
            if (ImGui.Checkbox("  battlehorns", ref horns))
            {
                config.CombatHorns = horns;
                this.plugin.SaveConfig();
            }

            bool opener = config.CombatCountdownOpener;
            if (ImGui.Checkbox("  countdown opener (3rd horn, borrow, 1st horn)", ref opener))
            {
                config.CombatCountdownOpener = opener;
                this.plugin.SaveConfig();
            }

            bool petFinisher = config.CombatPetFinisher;
            if (ImGui.Checkbox("  pet-skill Parting Blow", ref petFinisher))
            {
                config.CombatPetFinisher = petFinisher;
                this.plugin.SaveConfig();
            }

            bool ranged = config.CombatRangedFallback;
            if (ImGui.Checkbox("  ranged fallback out of reach", ref ranged))
            {
                config.CombatRangedFallback = ranged;
                this.plugin.SaveConfig();
            }

            bool downtime = config.CombatDowntimeAxe;
            if (ImGui.Checkbox("  downtime Avalanche Axe", ref downtime))
            {
                config.CombatDowntimeAxe = downtime;
                this.plugin.SaveConfig();
            }

            bool potions = config.CombatPotions;
            if (ImGui.Checkbox("  crucible potions", ref potions))
            {
                config.CombatPotions = potions;
                this.plugin.SaveConfig();
            }

            ImGui.SetNextItemWidth(140);
            int potionHp = config.CombatPotionHpPercent;
            if (ImGui.SliderInt("  potion HP %", ref potionHp, 20, 90))
            {
                config.CombatPotionHpPercent = potionHp;
                this.plugin.SaveConfig();
            }
        }

        ImGui.Separator();

        bool whistle = config.WhistlePlan;
        if (ImGui.Checkbox("Whistle finisher (Tempered Release)", ref whistle))
        {
            config.WhistlePlan = whistle;
            this.plugin.SaveConfig();
        }

        if (config.WhistlePlan)
        {
            ImGui.SetNextItemWidth(140);
            int horn1 = config.WhistleThresholds[0];
            if (ImGui.SliderInt("horn 1 %", ref horn1, 1, 50))
            {
                config.WhistleThresholds[0] = horn1;
                this.plugin.SaveConfig();
            }

            ImGui.SetNextItemWidth(140);
            int horn2 = config.WhistleThresholds[1];
            if (ImGui.SliderInt("horn 2 %", ref horn2, 1, 60))
            {
                config.WhistleThresholds[1] = horn2;
                this.plugin.SaveConfig();
            }

            ImGui.SetNextItemWidth(140);
            int horn3 = config.WhistleThresholds[2];
            if (ImGui.SliderInt("horn 3 %", ref horn3, 1, 50))
            {
                config.WhistleThresholds[2] = horn3;
                this.plugin.SaveConfig();
            }

            bool borrow = config.WhistleAutoBorrow;
            if (ImGui.Checkbox("  Borrow instead of Release on horn 3", ref borrow))
            {
                config.WhistleAutoBorrow = borrow;
                this.plugin.SaveConfig();
            }
        }

        ImGui.End();
    }
}
