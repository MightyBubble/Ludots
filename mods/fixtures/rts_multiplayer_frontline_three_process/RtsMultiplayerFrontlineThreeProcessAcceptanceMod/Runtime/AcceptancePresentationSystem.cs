using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace RtsMultiplayerFrontlineThreeProcessAcceptanceMod.Runtime;

internal sealed class AcceptancePresentationSystem : ISystem<float>
{
    private static readonly Vector4 Fill = new(0.04f, 0.06f, 0.07f, 0.92f);
    private static readonly Vector4 Border = new(0.91f, 0.72f, 0.25f, 0.96f);
    private static readonly Vector4 TitleColor = new(0.96f, 0.97f, 0.94f, 1f);
    private static readonly Vector4 TextColor = new(0.78f, 0.88f, 0.84f, 1f);

    private readonly AcceptancePlan.AcceptancePresentationCopy _copy;
    private readonly AcceptanceProgress _progress;
    private readonly ScreenOverlayBuffer _overlay;

    public AcceptancePresentationSystem(GameEngine engine, AcceptancePlan plan, AcceptanceProgress progress)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _copy = plan?.Presentation ?? throw new ArgumentNullException(nameof(plan));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("Acceptance presentation requires the screen overlay buffer.");
    }

    public void Initialize()
    {
    }

    public void Update(in float dt)
    {
        string status = _progress.Stage switch
        {
            AcceptanceProgressStage.Connecting => _copy.Connecting,
            AcceptanceProgressStage.Ready => _copy.Ready,
            AcceptanceProgressStage.Gathering => _copy.Gathering,
            AcceptanceProgressStage.Training => _copy.Training,
            AcceptanceProgressStage.Advancing => _copy.Advancing,
            AcceptanceProgressStage.Engaging => _copy.Engaging,
            AcceptanceProgressStage.Completed => _copy.Completed,
            AcceptanceProgressStage.Failed => _copy.Failed,
            _ => throw new InvalidOperationException($"Unknown acceptance progress stage {_progress.Stage}."),
        };
        Vector4 border = _progress.Stage == AcceptanceProgressStage.Failed
            ? new Vector4(0.92f, 0.27f, 0.24f, 1f)
            : Border;
        _overlay.AddRect(14, 392, 760, 74, Fill, border, stableId: 71900, dirtySerial: (int)_progress.Stage + 1);
        _overlay.AddText(28, 404, _copy.Title, 18, TitleColor, stableId: 71901, dirtySerial: 1);
        _overlay.AddText(28, 432, status, 15, TextColor, stableId: 71902, dirtySerial: (int)_progress.Stage + 1);
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
}
