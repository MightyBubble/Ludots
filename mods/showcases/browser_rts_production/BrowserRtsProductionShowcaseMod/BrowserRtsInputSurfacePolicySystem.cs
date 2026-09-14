using System.Collections.Generic;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;

namespace BrowserRtsProductionShowcaseMod;

/// <summary>
/// Browser RTS input-surface policy (migration slice 2): the local order mapping installs
/// through the CoreInputMod auto assembly; this policy keeps the browser surface's skill-bar
/// state pinned (the view-mode switcher rewrites it on every mode change) and republishes the
/// active mapping global for the browser data plane.
/// </summary>
public sealed class BrowserRtsInputSurfacePolicySystem : ISystem<float>
{
    private readonly Dictionary<string, object> _globals;

    public BrowserRtsInputSurfacePolicySystem(Dictionary<string, object> globals)
    {
        _globals = globals;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }

    public void Update(in float dt)
    {
        _globals[SkillBarOverlaySystem.SkillBarEnabledKey] = false;
        _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
        if (_globals.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) &&
            mappingObj is InputOrderMappingSystem)
        {
            // already published by the auto install; kept as an explicit assertion of the
            // browser surface contract
            _globals[CoreServiceKeys.ActiveInputOrderMapping.Name] = mappingObj;
        }
    }

    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
