using System.Collections.Frozen;
using System.Globalization;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering;

public sealed class RaylibAnimationProfileBindings
{
    private readonly AnimationProfileRegistry _profiles;
    private readonly AnimationClipRegistry _clips;
    private readonly IRenderMeshAssets _meshes;
    private readonly IRenderAssetPathResolver _paths;
    private readonly int _profileRevision;
    private readonly int _clipRevision;
    private readonly Binding[] _bindings;

    public RaylibAnimationProfileBindings(
        AnimationProfileRegistry profiles,
        AnimationClipRegistry clips,
        IRenderMeshAssets meshes,
        IRenderAssetPathResolver paths)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _profileRevision = profiles.Revision;
        _clipRevision = clips.Revision;
        _bindings = new Binding[checked(profiles.Count + 1)];
        for (int id = 1; id < _bindings.Length; id++)
        {
            if (!profiles.TryGet(id, out AnimationProfileDefinition profile))
            {
                throw new InvalidOperationException($"Animation profile id={id} has no definition.");
            }

            _bindings[id] = Compile(profile, clips, paths);
        }
    }

    public IReadOnlyDictionary<int, int>? Resolve(int profileId, int logicalMeshAssetId)
    {
        if (profileId == 0) return null;
        if (_profiles.Revision != _profileRevision || _clips.Revision != _clipRevision)
        {
            throw new InvalidOperationException("Animation configuration changed after Raylib bindings were compiled.");
        }

        if ((uint)profileId >= (uint)_bindings.Length)
        {
            throw new InvalidOperationException($"Unknown animation profile id={profileId}.");
        }

        ref readonly Binding binding = ref _bindings[profileId];
        if (binding.Error != null)
        {
            throw new InvalidOperationException($"Raylib animation profile id={profileId}: {binding.Error}");
        }

        if (!_meshes.TryGetDescriptor(logicalMeshAssetId, out MeshAssetDescriptor descriptor) ||
            descriptor.Type != MeshAssetType.Model ||
            descriptor.SourceUris == null ||
            descriptor.SourceUris.Length == 0)
        {
            throw new InvalidOperationException(
                $"Raylib animation profile id={profileId} requires logical Model meshAssetId={logicalMeshAssetId} with host sourceUris.");
        }

        bool matchesCanonicalSource = false;
        for (int i = 0; i < descriptor.SourceUris.Length; i++)
        {
            if (_paths.TryResolveFullPath(descriptor.SourceUris[i], out string modelPath) &&
                PathsEqual(binding.CanonicalModelPath!, Path.GetFullPath(modelPath)))
            {
                matchesCanonicalSource = true;
                break;
            }
        }

        if (!matchesCanonicalSource)
        {
            throw new InvalidOperationException(
                $"Raylib animation profile id={profileId} canonical source '{binding.CanonicalModelPath}' does not belong to logical meshAssetId={logicalMeshAssetId}.");
        }

        return binding.StateClips;
    }

    private static Binding Compile(
        AnimationProfileDefinition profile,
        AnimationClipRegistry clips,
        IRenderAssetPathResolver paths)
    {
        if (profile.StateClips.Length == 0)
        {
            return new Binding(null, null, "No state clips are configured.");
        }

        var map = new Dictionary<int, int>(profile.StateClips.Length);
        string? canonicalModelPath = null;
        for (int i = 0; i < profile.StateClips.Length; i++)
        {
            AnimationStateClipBinding state = profile.StateClips[i];
            if (!clips.TryResolveLocator(state.ClipAssetId, "raylib", out AnimationClipLocatorDefinition locator))
            {
                return new Binding(null, null, $"Clip id={state.ClipAssetId} has no raylib locator.");
            }

            int marker = locator.AssetRef.LastIndexOf("#anim:", StringComparison.Ordinal);
            if (marker <= 0 ||
                !int.TryParse(locator.AssetRef.AsSpan(marker + 6), NumberStyles.None, CultureInfo.InvariantCulture, out int clipIndex) ||
                clipIndex < 0)
            {
                return new Binding(null, null, $"Locator '{locator.AssetRef}' requires a nonnegative #anim:index.");
            }

            string uri = locator.AssetRef[..marker];
            if (!paths.TryResolveFullPath(uri, out string resolvedPath))
            {
                return new Binding(null, null, $"Animation source '{uri}' could not be resolved.");
            }

            string fullPath = Path.GetFullPath(resolvedPath);
            if (canonicalModelPath != null && !PathsEqual(canonicalModelPath, fullPath))
            {
                return new Binding(null, null, "State clips must belong to the same logical model source.");
            }

            canonicalModelPath = fullPath;
            if (!map.TryAdd(state.PackedStateIndex, clipIndex))
            {
                return new Binding(null, null, $"State {state.PackedStateIndex} is bound more than once.");
            }
        }

        return new Binding(canonicalModelPath, map.ToFrozenDictionary(), null);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private readonly record struct Binding(
        string? CanonicalModelPath,
        IReadOnlyDictionary<int, int>? StateClips,
        string? Error);
}
