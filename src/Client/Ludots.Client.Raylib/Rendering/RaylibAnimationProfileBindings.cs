using System.Collections.Frozen;
using System.Globalization;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Raylib.Rendering;

public sealed class RaylibAnimationProfileBindings
{
    private readonly AnimationProfileRegistry _profiles;
    private readonly AnimationClipRegistry _clips;
    private readonly int _profileRevision;
    private readonly int _clipRevision;
    private readonly Binding[] _bindings;

    public RaylibAnimationProfileBindings(
        AnimationProfileRegistry profiles,
        AnimationClipRegistry clips,
        IRenderAssetPathResolver paths)
    {
        _profiles = profiles;
        _clips = clips;
        _profileRevision = profiles.Revision;
        _clipRevision = clips.Revision;
        _bindings = new Binding[checked(profiles.Count + 1)];
        for (int id = 1; id < _bindings.Length; id++)
        {
            if (!profiles.TryGet(id, out AnimationProfileDefinition profile))
                throw new InvalidOperationException($"Animation profile id={id} has no definition.");
            _bindings[id] = Compile(profile, clips, paths);
        }
    }

    public IReadOnlyDictionary<int, int>? Resolve(int profileId, string modelPath)
    {
        if (profileId == 0) return null;
        if (_profiles.Revision != _profileRevision || _clips.Revision != _clipRevision)
            throw new InvalidOperationException("Animation configuration changed after Raylib bindings were compiled.");
        if ((uint)profileId >= (uint)_bindings.Length)
            throw new InvalidOperationException($"Unknown animation profile id={profileId}.");
        ref readonly Binding binding = ref _bindings[profileId];
        if (binding.Error != null)
            throw new InvalidOperationException($"Raylib animation profile id={profileId}: {binding.Error}");
        if (!string.Equals(binding.ModelPath, modelPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException($"Raylib animation profile id={profileId} references '{binding.ModelPath}', but the loaded mesh uses '{modelPath}'.");
        return binding.StateClips;
    }

    private static Binding Compile(AnimationProfileDefinition profile, AnimationClipRegistry clips, IRenderAssetPathResolver paths)
    {
        if (profile.StateClips.Length == 0) return new(null, null, "No state clips are configured.");
        var map = new Dictionary<int, int>(profile.StateClips.Length);
        string? modelPath = null;
        foreach (AnimationStateClipBinding state in profile.StateClips)
        {
            if (!clips.TryResolveLocator(state.ClipAssetId, "raylib", out AnimationClipLocatorDefinition locator))
                return new(null, null, $"Clip id={state.ClipAssetId} has no raylib locator.");
            int marker = locator.AssetRef.LastIndexOf("#anim:", StringComparison.Ordinal);
            if (marker <= 0 || !int.TryParse(locator.AssetRef.AsSpan(marker + 6), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                return new(null, null, $"Locator '{locator.AssetRef}' requires a nonnegative #anim:index.");
            string uri = locator.AssetRef[..marker];
            if (!paths.TryResolveFullPath(uri, out string path))
                return new(null, null, $"Animation source '{uri}' could not be resolved.");
            path = Path.GetFullPath(path);
            if (modelPath != null && !string.Equals(modelPath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return new(null, null, "State clips must belong to the same loaded model.");
            modelPath = path;
            if (!map.TryAdd(state.PackedStateIndex, index))
                return new(null, null, $"State {state.PackedStateIndex} is bound more than once.");
        }
        return new(modelPath, map.ToFrozenDictionary(), null);
    }

    private readonly record struct Binding(string? ModelPath, IReadOnlyDictionary<int, int>? StateClips, string? Error);
}
