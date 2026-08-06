using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Movement;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CapabilityStandardCrowdPhysicsArenaMod.Systems;

/// <summary>
/// Arena HUD (issue #734): three live numbers on the screen overlay —
/// selected unit count (command source view), units currently displaced/recovering
/// (pose authority windows), and total agent plate steps (ContactBegin count).
/// Reuses the core screen overlay text pipeline.
/// </summary>
internal sealed class CrowdPhysicsArenaHudSystem : ISystem<float>
{
    private const int HudStableIdBase = 73400;

    private readonly GameEngine _engine;
    private readonly CrowdPhysicsArenaPressurePlateDoorSystem _plateSystem;

    public CrowdPhysicsArenaHudSystem(GameEngine engine, CrowdPhysicsArenaPressurePlateDoorSystem plateSystem)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _plateSystem = plateSystem ?? throw new ArgumentNullException(nameof(plateSystem));
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
        if (!CapabilityStandardCrowdPhysicsArenaMapFocus.IsStartupMapFocused(_engine) ||
            _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
        {
            return;
        }

        int selectedCount = ResolveSelectedCount();
        int recoveringCount = _engine.GetService(CoreServiceKeys.PoseAuthorityArbiter) is PoseAuthorityArbiter arbiter
            ? arbiter.ActiveWindowCount
            : 0;
        long plateSteps = _plateSystem.AgentContactBeginCount;
        long plateActive = _plateSystem.ActiveAgentContacts;

        string selectedLine = $"Selected units: {selectedCount}";
        string recoveringLine = $"Displaced recovering: {recoveringCount}";
        string plateLine = $"Plate steps: {plateSteps} (on plate: {plateActive}, doors opened: {_plateSystem.OpenedDoorCount})";

        overlay.AddRect(12, 12, 480, 108, new Vector4(0.04f, 0.07f, 0.10f, 0.78f), new Vector4(0.35f, 0.51f, 0.60f, 0.92f), stableId: HudStableIdBase, dirtySerial: 1);
        overlay.AddText(24, 22, "Crowd Physics Arena", 18, new Vector4(0.94f, 0.96f, 0.98f, 1f), stableId: HudStableIdBase + 1, dirtySerial: 1);
        overlay.AddText(24, 48, selectedLine, 14, new Vector4(0.66f, 0.83f, 0.96f, 1f), stableId: HudStableIdBase + 2, dirtySerial: StringHash(selectedLine));
        overlay.AddText(24, 70, recoveringLine, 14, new Vector4(0.94f, 0.83f, 0.57f, 1f), stableId: HudStableIdBase + 3, dirtySerial: StringHash(recoveringLine));
        overlay.AddText(24, 92, plateLine, 14, new Vector4(0.72f, 0.94f, 0.70f, 1f), stableId: HudStableIdBase + 4, dirtySerial: StringHash(plateLine));
    }

    private int ResolveSelectedCount()
    {
        Entity owner = _engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (owner == Entity.Null ||
            !_engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore collections))
        {
            return 0;
        }

        return EntityCollectionContextRuntime.TryDescribeView(collections, owner, EntityCollectionKeys.CommandSource, out EntityCollectionView view)
            ? view.Count
            : 0;
    }

    private static int StringHash(string value)
    {
        return StringComparer.Ordinal.GetHashCode(value);
    }
}
