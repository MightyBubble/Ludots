using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Client;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace RtsRedAlertLikeShowcaseMod.Systems;

internal sealed class RtsRedAlertSelectionFeedbackPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly GroundOverlayBuffer _overlays;
    private float _elapsedSeconds;

    public RtsRedAlertSelectionFeedbackPresentationSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
            ?? throw new InvalidOperationException("GroundOverlayBuffer service is missing.");
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        _elapsedSeconds += MathF.Max(0f, dt);
        if (!IsRedAlertMapActive() ||
            !TryResolveCommandSourcePrimary(out Entity selected) ||
            !_engine.World.IsAlive(selected) ||
            !TryResolveWorldPosition(selected, out Vector3 center))
        {
            return;
        }

        Vector4 accent = TeamColorResolver.Resolve(_engine.World, selected);
        float radius = ResolveSelectionRadius(selected);
        float pulse = 1f + (0.05f * MathF.Sin(_elapsedSeconds * 5.2f));
        _overlays.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = center + new Vector3(0f, 0.035f, 0f),
            Radius = radius * pulse,
            InnerRadius = MathF.Max(0.05f, radius - 0.18f),
            FillColor = new Vector4(accent.X, accent.Y, accent.Z, 0.09f),
            BorderColor = new Vector4(accent.X, accent.Y, accent.Z, 0.78f),
            BorderWidth = 0.055f
        });
    }

    private bool TryResolveCommandSourcePrimary(out Entity entity)
    {
        entity = Entity.Null;
        return TryResolveLocalCommandSourceOwner(out Entity owner) &&
               EntityCollectionContextRuntime.TryGetPrimary(
                   _engine.World,
                   _engine.GlobalContext,
                   owner,
                   EntityCollectionKeys.CommandSource,
                   out entity);
    }

    private bool TryResolveLocalCommandSourceOwner(out Entity owner)
    {
        owner = Entity.Null;
        Entity local = ClientLocalSeatAccess.RequireSolePossessedRep(_engine);
        if (local == Entity.Null || !_engine.World.IsAlive(local))
        {
            return false;
        }

        owner = local;
        return true;
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private bool TryResolveWorldPosition(Entity selected, out Vector3 center)
    {
        if (_engine.World.TryGet(selected, out VisualTransform visual))
        {
            center = visual.Position;
            return true;
        }

        if (_engine.World.TryGet(selected, out WorldPositionCm worldPosition))
        {
            center = new Vector3(
                worldPosition.Value.X.ToFloat() * 0.01f,
                0f,
                worldPosition.Value.Y.ToFloat() * 0.01f);
            return true;
        }

        center = default;
        return false;
    }

    private float ResolveSelectionRadius(Entity selected)
    {
        if (_engine.World.TryGet(selected, out SpatialBox3D box))
        {
            return MathF.Max(0.75f, (MathF.Max(box.HalfSizeXCm, box.HalfSizeZCm) * 0.01f) + 0.35f);
        }

        return 1.15f;
    }

    private bool IsRedAlertMapActive()
    {
        var tags = _engine.CurrentMapSession?.MapConfig?.Tags;
        if (tags == null)
        {
            return false;
        }

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], "red_alert_like", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tags[i], "cnc", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
