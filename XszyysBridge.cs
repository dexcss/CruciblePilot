using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace CruciblePilot;

/// <summary>
/// Loads the ACR's own compiled resolvers (PromeRotation engine + XSZYYS rotation) and runs them directly.
/// This is their logic, untouched: we only supply the 100ms tick, the action submission and the target.
/// </summary>
public sealed class XszyysBridge : IDisposable
{
    private static readonly (string Type, bool OffGcd)[] Resolvers =
    {
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTTimedFinisherOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTShieldChargeOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTCheerOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTDrumOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTBurstOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTFinisherOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTReleaseOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTComboOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTDowntimeAxeOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.OffGcd.BSTWhistleOffGcd", true),
        ("XSZYYS_PR.Jobs.BST.Action.Gcd.BSTCaptureGcd", false),
        ("XSZYYS_PR.Jobs.BST.Action.Gcd.BSTRangedFallbackGcd", false),
        ("XSZYYS_PR.Jobs.BST.Action.Gcd.BSTBaseComboGcd", false),
    };

    private readonly IPluginLog log;
    private readonly CpLog cpLog;
    private readonly ITargetManager targetManager;
    private readonly List<(object Instance, bool OffGcd, MethodInfo Check, MethodInfo GetAction)> loaded = new();
    private readonly List<(PropertyInfo Field, string Name)> checkReaders = new();
    private readonly List<(PropertyInfo Field, string Name)> actionReaders = new();
    private DateTime nextTick = DateTime.MinValue;
    private DateTime lastActionAt = DateTime.MinValue;
    private DateTime combatSince = DateTime.MinValue;
    private bool broken;
    private bool loadAttempted;

    public XszyysBridge(IPluginLog log, CpLog cpLog, ITargetManager targetManager)
    {
        this.log = log;
        this.cpLog = cpLog;
        this.targetManager = targetManager;
    }

    public bool Loaded => this.loaded.Count > 0;

    public bool Broken => this.broken;

    public void Dispose()
    {
        this.loaded.Clear();
    }

    /// <summary>Resolves Dalamud / FFXIVClientStructs / ECommons for the loaded ACR assemblies.</summary>
    private readonly HashSet<string> logged = new();

    private void LogOnce(string key, string message)
    {
        if (this.logged.Add(key))
        {
            this.cpLog.Info(message);
        }
    }

    private static string Describe(Exception ex)
    {
        var text = ex.GetType().Name + ": " + ex.Message;
        var inner = ex.InnerException;
        int depth = 0;
        while (inner != null && depth < 3)
        {
            text += " <- " + inner.GetType().Name + ": " + inner.Message;
            inner = inner.InnerException;
            depth++;
        }

        return text;
    }

    private static Assembly? ResolveFromKnownPlaces(object? sender, ResolveEventArgs args)
    {
        string simple = new AssemblyName(args.Name).Name ?? string.Empty;
        if (simple.Length == 0)
        {
            return null;
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, simple, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] roots =
        {
            Path.Combine(appData, "XIVLauncher", "addon", "Hooks"),
            Path.Combine(appData, "XIVLauncher", "installedPlugins", "PromeRotation"),
            Path.Combine(appData, "XIVLauncher", "pluginConfigs", "PromeRotation", "ACR", "XSZYYS"),
            Path.Combine(appData, "XIVLauncher", "installedPlugins", "RotationSolver"),
            Path.Combine(appData, "XIVLauncher", "installedPlugins", "BossModReborn"),
            Path.Combine(appData, "XIVLauncher", "installedPlugins"),
            Path.Combine(appData, "XIVLauncher", "devPlugins"),
        };

        foreach (string file in SafeFind(roots, simple + ".dll"))
        {
            try
            {
                return Assembly.LoadFrom(file);
            }
            catch
            {
                // try next candidate
            }
        }

        return null;
    }

    /// <summary>Depth-first search that ignores unreadable folders instead of aborting the whole walk.</summary>
    private static IEnumerable<string> SafeFind(IEnumerable<string> roots, string fileName)
    {
        var pending = new Stack<string>(roots.Where(Directory.Exists));
        while (pending.Count > 0)
        {
            string dir = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            string? match = null;
            try
            {
                match = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // enumeration failure, keep walking
            }

            if (match != null)
            {
                yield return match;
            }

            IEnumerable<string> subs;
            try
            {
                subs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (string sub in subs)
            {
                pending.Push(sub);
            }
        }
    }

    /// <summary>
    /// Loads a shared dependency into the default context before the ACR asks for it. Picks the newest copy on
    /// disk so the ACR's exact-version reference resolves to a real assembly.
    /// </summary>
    private static string PreloadDependency(string simpleName)
    {
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            {
                return "already loaded " + loaded.GetName().Version;
            }
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] roots =
        {
            Path.Combine(appData, "XIVLauncher", "addon", "Hooks"),
            Path.Combine(appData, "XIVLauncher", "installedPlugins"),
            Path.Combine(appData, "XIVLauncher", "devPlugins"),
        };

        string? best = null;
        Version bestVersion = new(0, 0, 0, 0);
        foreach (string file in SafeFind(roots, simpleName + ".dll"))
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(file);
                if (best == null || name.Version > bestVersion)
                {
                    best = file;
                    bestVersion = name.Version ?? bestVersion;
                }
            }
            catch
            {
                // unreadable candidate
            }
        }

        if (best == null)
        {
            return "not found";
        }

        try
        {
            var assembly = Assembly.LoadFrom(best);
            return $"loaded {assembly.GetName().Version} from {Path.GetDirectoryName(best)}";
        }
        catch (Exception ex)
        {
            return "failed " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static string? FindRotationSolverAssembly()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "installedPlugins", "PromeRotation");
        if (Directory.Exists(root))
        {
            var dll = Directory.GetFiles(root, "PromeRotation.dll", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (dll != null)
            {
                return dll;
            }
        }

        string dev = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "devPlugins", "PromeRotation");
        return Directory.Exists(dev) ? Directory.GetFiles(dev, "PromeRotation.dll", SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    private static string? FindAcrAssembly()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "PromeRotation", "ACR", "XSZYYS", "XSZYYS_PR.dll");
        return File.Exists(path) ? path : null;
    }

    public bool TryLoad()
    {
        if (this.loadAttempted)
        {
            return this.Loaded;
        }

        this.loadAttempted = true;
        try
        {
            string? prPath = FindRotationSolverAssembly();
            string? acrPath = FindAcrAssembly();
            if (prPath == null || acrPath == null)
            {
                this.cpLog.Info($"bridge: assemblies not found (pr={prPath ?? "none"}, acr={acrPath ?? "none"})");
                return false;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromKnownPlaces;

            // Preload the shared libraries the ACR binds against into the default context, so its type
            // initialisers find a real assembly instead of failing the binder.
            foreach (string dependency in new[] { "ECommons", "Dalamud", "FFXIVClientStructs", "InteropGenerator.Runtime", "CheapLoc" })
            {
                this.cpLog.Info($"bridge: preload {dependency} -> {PreloadDependency(dependency)}");
            }

            var prAssembly = Assembly.LoadFrom(prPath);
            this.cpLog.Info($"bridge: PromeRotation {prAssembly.GetName().Version} loaded");

            var acrAssembly = Assembly.LoadFrom(acrPath);
            this.cpLog.Info($"bridge: {acrAssembly.GetName().Name} loaded");

            foreach (var (typeName, offGcd) in Resolvers)
            {
                var type = acrAssembly.GetType(typeName);
                if (type == null)
                {
                    this.cpLog.Info($"bridge: type missing {typeName}");
                    continue;
                }

                object? instance = Activator.CreateInstance(type);
                var check = type.GetMethod("Check", Type.EmptyTypes);
                var getAction = type.GetMethod("GetAction", Type.EmptyTypes);
                if (instance == null || check == null || getAction == null)
                {
                    this.cpLog.Info($"bridge: unusable {type.Name}");
                    continue;
                }

                this.loaded.Add((instance, offGcd, check, getAction));
            }

            if (this.loaded.Count > 0)
            {
                var sampleCheck = this.loaded[0].Check.ReturnType;
                var sampleAction = this.loaded[0].GetAction.ReturnType;
                this.checkReaders.Clear();
                this.actionReaders.Clear();
                foreach (var p in sampleCheck.GetProperties())
                {
                    this.checkReaders.Add((p, p.Name));
                }

                foreach (var p in sampleAction.GetProperties())
                {
                    this.actionReaders.Add((p, p.Name));
                }

                this.cpLog.Info($"bridge: {this.loaded.Count} resolvers live; Check={string.Join("/", this.checkReaders.Select(r => r.Name))}; PAction={string.Join("/", this.actionReaders.Select(r => r.Name))}");
            }

            return this.Loaded;
        }
        catch (Exception ex)
        {
            this.cpLog.Info($"bridge: load failed {ex.GetType().Name}: {ex.Message}");
            this.log.Warning("[CP] XSZYYS bridge load failed: " + ex);
            return false;
        }
    }

    public unsafe void Tick(uint territory)
    {
        if (territory is < 1339 or > 1343 || !this.Loaded)
        {
            return;
        }

        if (DateTime.UtcNow < this.nextTick)
        {
            return;
        }

        this.nextTick = DateTime.UtcNow.AddMilliseconds(100);

        var actionManager = ActionManager.Instance();
        if (actionManager == null)
        {
            return;
        }

        if (this.combatSince == DateTime.MinValue)
        {
            this.combatSince = DateTime.UtcNow;
        }

        if (DateTime.UtcNow - this.combatSince > TimeSpan.FromSeconds(6) &&
            (this.lastActionAt == DateTime.MinValue || DateTime.UtcNow - this.lastActionAt > TimeSpan.FromSeconds(6)))
        {
            if (!this.broken)
            {
                this.broken = true;
                this.cpLog.Info("bridge: produced no actions for 6s, falling back to the built-in loop");
            }

            return;
        }

        foreach (var (instance, offGcd, check, getAction) in this.loaded)
        {
            object? result;
            try
            {
                result = check.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                this.LogOnce("check:" + instance.GetType().Name, $"bridge: {instance.GetType().Name}.Check threw {Describe(ex)}");
                continue;
            }

            if (result == null || !ReadBool(result, "Success", "Ok", "IsSuccess"))
            {
                continue;
            }

            object? action;
            try
            {
                action = getAction.Invoke(instance, null);
            }
            catch (Exception ex)
            {
                this.LogOnce("action:" + instance.GetType().Name, $"bridge: {instance.GetType().Name}.GetAction threw {Describe(ex)}");
                continue;
            }

            if (action == null)
            {
                continue;
            }

            uint actionId = ReadUInt(action, "ActionId", "Id");
            if (actionId == 0)
            {
                continue;
            }

            int targetType = ReadInt(action, "Target", "TargetType", "ActionTarget");
            ulong targetId = targetType == 1 ? (ulong)(this.targetManager.Target?.GameObjectId ?? 0xE0000000UL) : 0xE0000000UL;
            if (targetId == 0)
            {
                targetId = 0xE0000000UL;
            }

            uint adjusted = actionManager->GetAdjustedActionId(actionId);
            uint use = adjusted != 0 ? adjusted : actionId;
            if (actionManager->GetActionStatus(ActionType.Action, use) != 0)
            {
                continue;
            }

            if (actionManager->UseAction(ActionType.Action, use, targetId, 0xFFFF, ActionManager.UseActionMode.Queue, 0, null))
            {
                this.lastActionAt = DateTime.UtcNow;
                this.cpLog.Info($"bridge: {instance.GetType().Name} -> {use} ({(offGcd ? "oGCD" : "GCD")})");
                return;
            }
        }
    }

    private static bool ReadBool(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            var property = instance.GetType().GetProperty(name);
            if (property?.GetValue(instance) is bool value)
            {
                return value;
            }

            var field = instance.GetType().GetField(name);
            if (field?.GetValue(instance) is bool fieldValue)
            {
                return fieldValue;
            }
        }

        return false;
    }

    private static bool ReadBoolUnused(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            var property = instance.GetType().GetProperty(name);
            if (property?.GetValue(instance) is bool value)
            {
                return value;
            }
        }

        return false;
    }

    private static uint ReadUInt(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = instance.GetType().GetProperty(name)?.GetValue(instance) ?? instance.GetType().GetField(name)?.GetValue(instance);
            if (value is uint u)
            {
                return u;
            }

            if (value != null && uint.TryParse(value.ToString(), out uint parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static uint ReadUIntUnused(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            var property = instance.GetType().GetProperty(name);
            object? value = property?.GetValue(instance);
            if (value is uint u)
            {
                return u;
            }

            if (value != null && uint.TryParse(value.ToString(), out uint parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static int ReadInt(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = instance.GetType().GetProperty(name)?.GetValue(instance) ?? instance.GetType().GetField(name)?.GetValue(instance);
            if (value != null && int.TryParse(value.ToString(), out int parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static int ReadIntUnused(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            var property = instance.GetType().GetProperty(name);
            object? value = property?.GetValue(instance);
            if (value != null && int.TryParse(value.ToString(), out int parsed))
            {
                return parsed;
            }
        }

        return 0;
    }
}
