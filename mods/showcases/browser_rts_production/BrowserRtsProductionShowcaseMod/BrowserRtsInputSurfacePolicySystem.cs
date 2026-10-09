using System.Collections.Generic;
using Arch.System;
using CoreInputMod.Systems;

namespace BrowserRtsProductionShowcaseMod;

/// <summary>
/// Keeps the browser surface's skill-bar state pinned: the view-mode switcher rewrites it on
/// every mode change.
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
    }

    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}
