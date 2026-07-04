using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.EntityView;

public static class EntityViewRuntime
{
    public static bool TryResolveCurrentProfile(
        Dictionary<string, object> globals,
        EntityViewRuntimeConfig config,
        out EntityViewProfileEntry profile)
    {
        profile = null!;
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(config);

        if (!TryGetCurrentViewKey(globals, out string viewKey))
        {
            return false;
        }

        return config.TryGetProfile(viewKey, out profile);
    }

    public static bool TryGetCurrentViewKey(Dictionary<string, object> globals, out string viewKey)
    {
        viewKey = string.Empty;
        if (globals.TryGetValue(CoreServiceKeys.EntityViewKey.Name, out object? configured) &&
            configured is string configuredViewKey &&
            !string.IsNullOrWhiteSpace(configuredViewKey))
        {
            viewKey = configuredViewKey;
            return true;
        }

        return false;
    }

    public static bool TryGetCurrentViewer(World world, Dictionary<string, object> globals, out Entity viewer)
    {
        viewer = default;
        if (globals.TryGetValue(CoreServiceKeys.EntityViewViewerEntity.Name, out object? configured) &&
            configured is Entity configuredViewer &&
            world.IsAlive(configuredViewer))
        {
            viewer = configuredViewer;
            return true;
        }

        return false;
    }

    public static bool TrySetCurrentView(
        World world,
        Dictionary<string, object> globals,
        EntityViewRuntimeConfig config,
        Entity viewer,
        string viewKey)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(config);

        if (!world.IsAlive(viewer) || string.IsNullOrWhiteSpace(viewKey))
        {
            return false;
        }

        _ = config.RequireProfile(viewKey);
        globals[CoreServiceKeys.EntityViewViewerEntity.Name] = viewer;
        globals[CoreServiceKeys.EntityViewKey.Name] = viewKey;
        return true;
    }

    public static bool TryGetCommandSourceHandle(
        World world,
        Dictionary<string, object> globals,
        EntityViewRuntimeConfig config,
        out Entity owner,
        out EntityCollectionHandle handle)
    {
        owner = default;
        handle = EntityCollectionHandle.Invalid;
        if (!TryGetCurrentViewer(world, globals, out Entity viewer) ||
            !TryResolveCurrentProfile(globals, config, out EntityViewProfileEntry profile) ||
            !TryGetEntityCollectionStore(globals, out EntityCollectionStore collections))
        {
            return false;
        }

        owner = viewer;
        return collections.TryGet(viewer, profile.CommandSourceCollectionKey, out handle);
    }

    public static int GetCommandSourceCount(
        World world,
        Dictionary<string, object> globals,
        EntityViewRuntimeConfig config)
    {
        return TryGetCommandSourceHandle(world, globals, config, out _, out EntityCollectionHandle handle) &&
               TryGetEntityCollectionStore(globals, out EntityCollectionStore collections) &&
               collections.TryGetView(handle, out EntityCollectionView view)
            ? view.Count
            : 0;
    }

    public static int CopyCommandSourceEntities(
        World world,
        Dictionary<string, object> globals,
        EntityViewRuntimeConfig config,
        Span<Entity> destination)
    {
        if (!TryGetCommandSourceHandle(world, globals, config, out Entity owner, out EntityCollectionHandle handle) ||
            !TryGetEntityCollectionStore(globals, out EntityCollectionStore collections))
        {
            return 0;
        }

        return collections.CopyEntities(handle, 0, destination);
    }

    public static EntityCollectionHandle PromoteCommandSource(
        EntityCollectionStore collections,
        Entity owner,
        in EntityViewProfileEntry profile,
        ReadOnlySpan<Entity> entities,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(collections);
        if (owner == Entity.Null)
        {
            throw new ArgumentException("EntityView command source owner is required.", nameof(owner));
        }

        profile.Validate("EntityViewProfileEntry");
        var descriptor = EntityCollectionDescriptor.Create(
            profile.CommandSourceCollectionKey,
            EntityCollectionSourceKind.SelectionView,
            EntityCollectionRoleKind.CommandSource,
            owner,
            entities.Length > 0 ? entities[0] : Entity.Null,
            "Command source",
            summary);
        return collections.Replace(owner, descriptor, entities);
    }

    public static void ClearCurrentViewCollections(
        World world,
        Dictionary<string, object> globals,
        EntityViewRuntimeConfig config,
        EntityCollectionStore collections,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(collections);
        if (!TryGetCurrentViewer(world, globals, out Entity viewer) ||
            !TryResolveCurrentProfile(globals, config, out EntityViewProfileEntry profile))
        {
            return;
        }

        ReadOnlySpan<Entity> empty = ReadOnlySpan<Entity>.Empty;
        PromoteCommandSource(collections, viewer, in profile, empty, summary);
        PromoteDisplayCollection(collections, viewer, in profile, empty, summary);
    }

    public static EntityCollectionHandle PromoteDisplayCollection(
        EntityCollectionStore collections,
        Entity owner,
        in EntityViewProfileEntry profile,
        ReadOnlySpan<Entity> entities,
        string summary)
    {
        ArgumentNullException.ThrowIfNull(collections);
        if (owner == Entity.Null)
        {
            throw new ArgumentException("EntityView display collection owner is required.", nameof(owner));
        }

        profile.Validate("EntityViewProfileEntry");
        var descriptor = EntityCollectionDescriptor.Create(
            profile.DisplayCollectionKey,
            EntityCollectionSourceKind.SelectionView,
            EntityCollectionRoleKind.Display,
            owner,
            entities.Length > 0 ? entities[0] : Entity.Null,
            "Display selection",
            summary);
        return collections.Replace(owner, descriptor, entities);
    }

    public static bool TryGetEntityCollectionStore(Dictionary<string, object> globals, out EntityCollectionStore collections)
    {
        collections = default!;
        return globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? storeObj) &&
               storeObj is EntityCollectionStore store &&
               (collections = store) != null;
    }
}
