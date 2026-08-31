using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Raylib_cs;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// Client-side clip playback surface for GpuSkinnedInstance.
    /// Maps AnimatorPackedState primary state + normalized time to clip/frame (default: stateIndex == clipIndex).
    /// </summary>
    public sealed unsafe class RaylibSkinnedPlayback
    {
        private ModelAnimation* _animations;
        private int _animCount;
        private Dictionary<string, int>? _nameToClipIndex;
        private IReadOnlyDictionary<int, int>? _stateToClipMap;
        private int _clipIndex;
        private float _normalizedTime01;
        private bool _playing;

        public int ClipIndex => _clipIndex;
        public float NormalizedTime01 => _normalizedTime01;
        public bool IsPlaying => _playing;
        public int AnimCount => _animCount;

        public void BindAnimations(ModelAnimation* animations, int animCount)
        {
            if (animations == null || animCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} requires at least one ModelAnimation for GpuSkinnedInstance.");
            }

            _animations = animations;
            _animCount = animCount;
            _nameToClipIndex = null;
            _clipIndex = 0;
            _normalizedTime01 = 0f;
            _playing = false;
        }

        public void SetStateToClipMap(IReadOnlyDictionary<int, int>? stateToClipMap)
        {
            _stateToClipMap = stateToClipMap;
        }

        public void Play(int clipIndex)
        {
            EnsureBound();
            ValidateClipIndex(clipIndex, _animCount);
            _clipIndex = clipIndex;
            _normalizedTime01 = 0f;
            _playing = true;
        }

        public void Play(string clipName)
        {
            EnsureBound();
            if (string.IsNullOrWhiteSpace(clipName))
            {
                throw new ArgumentException("Clip name is required.", nameof(clipName));
            }

            EnsureNameIndex();
            if (_nameToClipIndex == null || !_nameToClipIndex.TryGetValue(clipName, out int clipIndex))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} cannot Play unknown clip '{clipName}' (animCount={_animCount}).");
            }

            Play(clipIndex);
        }

        public void SeekNormalized(float normalizedTime01)
        {
            EnsureBound();
            _normalizedTime01 = Math.Clamp(normalizedTime01, 0f, 1f);
        }

        public void Stop()
        {
            _playing = false;
            _normalizedTime01 = 0f;
        }

        public void ApplyAnimatorPackedState(in AnimatorPackedState packed)
        {
            EnsureBound();
            ResolveFromAnimator(
                in packed,
                _animations,
                _animCount,
                _stateToClipMap,
                out int clipIndex,
                out _,
                out float normalizedTime01);

            _clipIndex = clipIndex;
            _normalizedTime01 = normalizedTime01;
            _playing = (packed.GetFlags() & AnimatorPackedStateFlags.Active) != 0;
        }

        public int ResolveFrameIndex()
        {
            EnsureBound();
            bool looping = true;
            return ResolveFrameIndex(_animations[_clipIndex].frameCount, _normalizedTime01, looping);
        }

        public static void ResolveFromAnimator(
            in AnimatorPackedState packed,
            ModelAnimation* animations,
            int animCount,
            IReadOnlyDictionary<int, int>? stateToClipMap,
            out int clipIndex,
            out int frameIndex)
        {
            ResolveFromAnimator(
                in packed,
                animations,
                animCount,
                stateToClipMap,
                out clipIndex,
                out frameIndex,
                out _);
        }

        public static void ResolveFromAnimator(
            in AnimatorPackedState packed,
            ModelAnimation* animations,
            int animCount,
            int animationProfileId,
            IRenderAnimationClipResolver? animationResolver,
            string[]? animationNames,
            out int clipIndex,
            out int frameIndex,
            out float normalizedTime01)
        {
            ResolveFromAnimator(
                in packed,
                animations,
                animCount,
                animationProfileId,
                animationResolver,
                animationNames,
                out _,
                out clipIndex,
                out frameIndex,
                out normalizedTime01);
        }

        public static void ResolveFromAnimator(
            in AnimatorPackedState packed,
            ModelAnimation* animations,
            int animCount,
            int animationProfileId,
            IRenderAnimationClipResolver? animationResolver,
            string[]? animationNames,
            out ClipAssetLocatorSelector selector,
            out int clipIndex,
            out int frameIndex,
            out float normalizedTime01)
        {
            selector = default;
            if (animationProfileId > 0)
            {
                if (animationResolver == null)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibSkinnedPlayback)} requires an animation resolver for profileId={animationProfileId}.");
                }

                if (!animationResolver.TryResolve(animationProfileId, packed.GetPrimaryStateIndex(), out selector))
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibSkinnedPlayback)} has no clip locator for profileId={animationProfileId}, stateIndex={packed.GetPrimaryStateIndex()}.");
                }

                clipIndex = ResolveSelectorIndex(selector, animations, animCount, animationNames);
                ValidateClipIndex(clipIndex, animCount);
                normalizedTime01 = packed.GetNormalizedTime01();
                bool looping = (packed.GetFlags() & AnimatorPackedStateFlags.Looping) != 0;
                frameIndex = ResolveFrameIndex(animations[clipIndex].frameCount, normalizedTime01, looping);
                return;
            }

            ResolveFromAnimator(
                in packed,
                animations,
                animCount,
                stateToClipMap: null,
                out clipIndex,
                out frameIndex,
                out normalizedTime01);
        }

        public static void ValidateSelectorSource(
            in ClipAssetLocatorSelector selector,
            string loadedSourcePath,
            IRenderAssetPathResolver assetPaths)
        {
            if (string.IsNullOrWhiteSpace(loadedSourcePath))
            {
                throw new ArgumentException("Loaded source path is required.", nameof(loadedSourcePath));
            }

            if (assetPaths == null)
            {
                throw new ArgumentNullException(nameof(assetPaths));
            }

            if (!assetPaths.TryResolveFullPath(selector.AssetPath, out string expectedSourcePath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} could not resolve animation locator source '{selector.AssetPath}'.");
            }

            string expected = Path.GetFullPath(expectedSourcePath);
            string actual = Path.GetFullPath(loadedSourcePath);
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(expected, actual, comparison))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} animation locator source '{expected}' does not match loaded model '{actual}'.");
            }
        }

        public static void ResolveFromAnimator(
            in AnimatorPackedState packed,
            ModelAnimation* animations,
            int animCount,
            IReadOnlyDictionary<int, int>? stateToClipMap,
            out int clipIndex,
            out int frameIndex,
            out float normalizedTime01)
        {
            if (animations == null || animCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} ResolveFromAnimator requires loaded animations (GpuSkinnedInstance).");
            }

            int stateIndex = packed.GetPrimaryStateIndex();
            clipIndex = MapStateToClipIndex(stateIndex, stateToClipMap);
            ValidateClipIndex(clipIndex, animCount);

            normalizedTime01 = packed.GetNormalizedTime01();
            bool looping = (packed.GetFlags() & AnimatorPackedStateFlags.Looping) != 0;
            frameIndex = ResolveFrameIndex(animations[clipIndex].frameCount, normalizedTime01, looping);
        }

        public static int MapStateToClipIndex(int stateIndex, IReadOnlyDictionary<int, int>? stateToClipMap)
        {
            if (stateToClipMap == null)
            {
                return stateIndex;
            }

            if (!stateToClipMap.TryGetValue(stateIndex, out int clipIndex))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} stateToClipMap has no entry for stateIndex={stateIndex}.");
            }

            return clipIndex;
        }

        public static void ResolveFromAnimator(
            in AnimatorPackedState packed,
            ModelAnimation* animations,
            int animCount,
            int animationProfileId,
            IRenderAnimationClipResolver? animationResolver,
            out int clipIndex,
            out int frameIndex,
            out float normalizedTime01)
        {
            ResolveFromAnimator(
                in packed,
                animations,
                animCount,
                animationProfileId,
                animationResolver,
                animationNames: null,
                out clipIndex,
                out frameIndex,
                out normalizedTime01);
        }

        private static int ResolveSelectorIndex(
            ClipAssetLocatorSelector selector,
            ModelAnimation* animations,
            int animCount,
            string[]? animationNames)
        {
            if (!string.Equals(selector.Kind, ClipAssetLocatorSelector.AnimKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} Raylib supports only '#anim:' clip selectors, got '#{selector.Kind}:{selector.Selector}'.");
            }

            if (selector.IsIndex)
            {
                return selector.Index;
            }

            for (int i = 0; i < animCount; i++)
            {
                string animationName = animationNames != null && i < animationNames.Length
                    ? animationNames[i]
                    : ReadAnimationName(animations[i]);
                if (string.Equals(animationName, selector.Selector, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            throw new InvalidOperationException(
                $"{nameof(RaylibSkinnedPlayback)} could not find animation '{selector.Selector}' in the loaded model (animCount={animCount}).");
        }

        public static int ResolveFrameIndex(int frameCount, float normalizedTime01, bool looping)
        {
            if (frameCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} clip has frameCount={frameCount}; GpuSkinnedInstance requires a usable clip.");
            }

            float t = Math.Clamp(normalizedTime01, 0f, 1f);
            if (frameCount == 1)
            {
                return 0;
            }

            if (looping)
            {
                int frame = (int)(t * frameCount);
                return frame >= frameCount ? 0 : frame;
            }

            return (int)(t * (frameCount - 1));
        }

        private void EnsureBound()
        {
            if (_animations == null || _animCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} has no bound animations; call {nameof(BindAnimations)} first.");
            }
        }

        private void EnsureNameIndex()
        {
            if (_nameToClipIndex != null)
            {
                return;
            }

            _nameToClipIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _animCount; i++)
            {
                string name = ReadAnimationName(_animations[i]);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                _nameToClipIndex[name] = i;
            }
        }

        private static void ValidateClipIndex(int clipIndex, int animCount)
        {
            if ((uint)clipIndex >= (uint)animCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibSkinnedPlayback)} clipIndex={clipIndex} is outside animCount={animCount} (GpuSkinnedInstance).");
            }
        }

        internal static string ReadAnimationName(in ModelAnimation animation)
        {
            fixed (byte* name = animation.name)
            {
                int len = 0;
                while (len < 32 && name[len] != 0)
                {
                    len++;
                }

                return len == 0 ? string.Empty : Encoding.UTF8.GetString(name, len);
            }
        }
    }
}
