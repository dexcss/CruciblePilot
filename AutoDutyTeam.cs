using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Services;

namespace CruciblePilot;

/// <summary>
/// Rewrites AutoDuty's Crucible custom team in memory. AutoDuty's config-override IPC refuses list values,
/// so the roster can only be set by reaching into the loaded assembly (same process, so reflection works).
/// Everything is best effort and version-fragile: a failure only means the user must set the team in AutoDuty's UI.
/// </summary>
public sealed class AutoDutyTeam
{
    private readonly IPluginLog log;

    public AutoDutyTeam(IPluginLog log) => this.log = log;

    public bool Set(IReadOnlyList<uint> beasts)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "AutoDuty", StringComparison.OrdinalIgnoreCase));
            if (asm == null)
            {
                this.log.Warning("[CP] AutoDuty assembly not found");
                return false;
            }

            var mainType = asm.GetType("AutoDuty.Configurations.ConfigurationMain", throwOnError: false);
            var instance = Get(mainType, null, "Instance");
            if (instance == null)
            {
                this.log.Warning("[CP] AutoDuty ConfigurationMain.Instance not found");
                return false;
            }

            var config = Get(instance.GetType(), instance, "GetCurrentConfig");
            if (config == null)
            {
                this.log.Warning("[CP] AutoDuty current config not found");
                return false;
            }

            var meta = Get(config.GetType(), config, "Meta");
            var crucible = meta == null ? null : Get(meta.GetType(), meta, "Crucible");
            if (crucible == null)
            {
                this.log.Warning("[CP] AutoDuty Crucible config not found");
                return false;
            }

            var teamModeProp = SetMember(crucible.GetType(), crucible, "TeamMode", "Custom");
            var customTeam = SetMember(crucible.GetType(), crucible, "CustomTeam", beasts.ToList());
            this.log.Information($"[CP] AutoDuty team: TeamMode={teamModeProp}, CustomTeam={customTeam} ({string.Join(",", beasts)})");
            return teamModeProp && customTeam;
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "[CP] Could not set the AutoDuty team");
            return false;
        }
    }

    private static object? Get(Type? type, object? instance, string name)
    {
        if (type == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var prop = type.GetProperty(name, flags);
        if (prop != null)
        {
            return prop.GetValue(prop.GetMethod!.IsStatic ? null : instance);
        }

        var field = type.GetField(name, flags);
        return field?.GetValue(field.IsStatic ? null : instance);
    }

    private static bool SetMember(Type type, object instance, string name, object value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
            {
                var target = value;
                if (prop.PropertyType.IsEnum && value is string s)
                {
                    target = Enum.Parse(prop.PropertyType, s, ignoreCase: true);
                }
                else if (prop.PropertyType != value.GetType())
                {
                    target = Convert.ChangeType(value, prop.PropertyType);
                }

                prop.SetValue(instance, target);
                return true;
            }

            var field = type.GetField(name, flags);
            if (field != null && value.GetType().IsAssignableTo(field.FieldType))
            {
                field.SetValue(instance, value);
                return true;
            }
        }
        catch
        {
            // best effort
        }

        return false;
    }
}
