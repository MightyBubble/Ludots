using System;
using Arch.System;
using DynamicNavBakeShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace DynamicNavBakeShowcaseMod.Systems;

/// <summary>
/// Construction-mode pointer handling on the formal interaction snapshot:
/// Confirm places a building; Cancel / Command exits construction.
/// Does not read Raylib input directly.
/// </summary>
internal sealed class DynamicNavBakeShowcasePlayerControlSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly DynamicNavBakeShowcaseRuntime _runtime;

    public DynamicNavBakeShowcasePlayerControlSystem(
        GameEngine engine,
        DynamicNavBakeShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    public void Update(in float dt)
    {
        _ = dt;
        if (!_runtime.IsActive || !_runtime.ConstructionMode)
        {
            return;
        }

        if (_engine.GetService(CoreServiceKeys.UiCaptured))
        {
            return;
        }

        if (!PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer))
        {
            return;
        }

        if (pointer.Cancel.PressedThisFrame || pointer.Command.PressedThisFrame)
        {
            if (!_runtime.TryExitConstructionMode(_engine, out string exitError) &&
                string.IsNullOrEmpty(exitError))
            {
                throw new InvalidOperationException(
                    "Dynamic NavBake construction cancel returned false without an error.");
            }

            return;
        }

        if (!pointer.Confirm.PressedThisFrame)
        {
            return;
        }

        if (!_runtime.TryPlaceBuildingAtPreview(_engine, out string placeError) &&
            string.IsNullOrEmpty(placeError))
        {
            throw new InvalidOperationException(
                "Dynamic NavBake construction place returned false without an error.");
        }
    }
}
