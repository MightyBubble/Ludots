using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Raylib
{
    internal sealed class CoreRenderAnimationClipResolver : IRenderAnimationClipResolver
    {
        private const string BackendId = "raylib";
        private readonly AnimationProfileRegistry _profiles;
        private readonly AnimationClipRegistry _clips;
        private readonly Dictionary<(int ProfileId, int StateIndex), ClipAssetLocatorSelector> _selectors = new();

        public CoreRenderAnimationClipResolver(AnimationProfileRegistry profiles, AnimationClipRegistry clips)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        }

        public bool TryResolve(int animationProfileId, int packedStateIndex, out ClipAssetLocatorSelector selector)
        {
            selector = default;
            if (animationProfileId <= 0)
            {
                return false;
            }

            var key = (animationProfileId, packedStateIndex);
            if (_selectors.TryGetValue(key, out selector))
            {
                return true;
            }

            if (!_profiles.TryResolveStateClipId(animationProfileId, packedStateIndex, out int clipAssetId))
            {
                throw new InvalidOperationException(
                    $"Raylib animation profile {animationProfileId} has no clip binding for packed state {packedStateIndex}.");
            }

            if (!_clips.TryResolveLocator(clipAssetId, BackendId, out AnimationClipLocatorDefinition locator))
            {
                throw new InvalidOperationException(
                    $"Raylib animation clip {clipAssetId} has no '{BackendId}' locator.");
            }

            selector = ClipAssetLocatorSelector.Parse(locator.AssetRef);
            _selectors.Add(key, selector);
            return true;
        }
    }
}
