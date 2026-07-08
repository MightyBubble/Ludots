using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Components;
using Ludots.WebUI.DataPlane;

namespace LiveMapEditorMod.WebUi;

internal sealed class LiveMapEditorGenerationResolver : IWebUiEntityGenerationResolver
{
    private static readonly QueryDescription StableIdQuery = new QueryDescription().WithAll<PresentationStableId>();
    private readonly GameEngine _engine;

    public LiveMapEditorGenerationResolver(GameEngine engine)
    {
        _engine = engine;
    }

    public bool IsCurrent(WebUiEntityRef entityRef)
    {
        if (entityRef.StableId <= 0)
        {
            return false;
        }

        bool current = false;
        _engine.World.Query(in StableIdQuery, (Entity entity, ref PresentationStableId stableId) =>
        {
            if (stableId.Value == entityRef.StableId &&
                (entityRef.Generation == 0 || entity.Version == entityRef.Generation))
            {
                current = true;
            }
        });
        return current;
    }
}

internal sealed class LiveMapEditorPermissionValidator : IWebUiCommandPermissionValidator
{
    private readonly GameEngine _engine;

    public LiveMapEditorPermissionValidator(GameEngine engine)
    {
        _engine = engine;
    }

    public bool CanUse(WebUiCommandRequest request, out string error)
    {
        if (_engine.CurrentMapSession == null)
        {
            error = "Live map editor command requires a focused map session.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
