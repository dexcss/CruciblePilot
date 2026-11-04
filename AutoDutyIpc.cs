using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace CruciblePilot;

/// <summary>Thin wrapper over AutoDuty's IPC (as used by Crucible SUX).</summary>
public sealed class AutoDutyIpc
{
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<bool> isStopped;
    private readonly ICallGateSubscriber<uint, bool> contentHasPath;
    private readonly ICallGateSubscriber<object, bool> pushOverrides;
    private readonly ICallGateSubscriber<bool> popOverrides;
    private readonly ICallGateSubscriber<uint, int, bool, object> run;
    private readonly ICallGateSubscriber<object> stop;

    public AutoDutyIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        this.isStopped = pluginInterface.GetIpcSubscriber<bool>("AutoDuty.IsStopped");
        this.contentHasPath = pluginInterface.GetIpcSubscriber<uint, bool>("AutoDuty.ContentHasPath");
        this.pushOverrides = pluginInterface.GetIpcSubscriber<object, bool>("AutoDuty.PushConfigOverrides");
        this.popOverrides = pluginInterface.GetIpcSubscriber<bool>("AutoDuty.PopConfigOverrides");
        this.run = pluginInterface.GetIpcSubscriber<uint, int, bool, object>("AutoDuty.Run");
        this.stop = pluginInterface.GetIpcSubscriber<object>("AutoDuty.Stop");
    }

    public bool IsAvailable()
    {
        try
        {
            _ = this.isStopped.InvokeFunc();
            return true;
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, "[CP] AutoDuty IPC unavailable");
            return false;
        }
    }

    public bool? IsRunning()
    {
        try
        {
            return !this.isStopped.InvokeFunc();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool? HasPath(uint territory)
    {
        try
        {
            return this.contentHasPath.InvokeFunc(territory);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool Push(Dictionary<string, string> overrides)
    {
        try
        {
            return this.pushOverrides.InvokeFunc(overrides);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "[CP] AutoDuty.PushConfigOverrides failed");
            return false;
        }
    }

    public void Pop()
    {
        try
        {
            this.popOverrides.InvokeFunc();
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, "[CP] AutoDuty.PopConfigOverrides failed");
        }
    }

    public void Run(uint territory, int loops, bool bare)
    {
        this.run.InvokeAction(territory, loops, bare);
    }

    public void StopRun()
    {
        try
        {
            this.stop.InvokeAction();
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "[CP] AutoDuty.Stop failed");
        }
    }
}
