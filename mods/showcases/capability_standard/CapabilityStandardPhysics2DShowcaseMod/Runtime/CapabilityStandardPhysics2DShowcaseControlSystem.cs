using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace CapabilityStandardPhysics2DShowcaseMod.Runtime;

internal sealed class CapabilityStandardPhysics2DShowcaseControlSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CapabilityStandardPhysics2DShowcaseRuntime _runtime;

    public CapabilityStandardPhysics2DShowcaseControlSystem(
        GameEngine engine,
        CapabilityStandardPhysics2DShowcaseRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (!_runtime.IsActive)
        {
            return;
        }

        _runtime.BindStaticObstacleSpawnReceipts();
        ApplyPolygonDrawingInput();
    }

    private void ApplyPolygonDrawingInput()
    {
        if (_engine.GetService(CoreServiceKeys.UiCaptured))
        {
            return;
        }

        if (!PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer))
        {
            return;
        }

        if (pointer.Cancel.PressedThisFrame)
        {
            _runtime.ClearPolygonDraft();
            return;
        }

        if (pointer.Command.PressedThisFrame && pointer.HasGroundPoint)
        {
            _runtime.TryAddPolygonVertexFromPointer(pointer.GroundWorldCm);
        }
    }
}
