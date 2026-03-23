using System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimationProfileDefinition
    {
        public int ProfileId;
        public int AnimatorControllerId;
        public AnimationStateClipBinding[] StateClips = Array.Empty<AnimationStateClipBinding>();
        public AnimationBuiltinClipBinding[] BuiltinClips = Array.Empty<AnimationBuiltinClipBinding>();

        public bool TryResolveStateClipId(int packedStateIndex, out int clipAssetId)
        {
            for (int i = 0; i < StateClips.Length; i++)
            {
                if (StateClips[i].PackedStateIndex == packedStateIndex)
                {
                    clipAssetId = StateClips[i].ClipAssetId;
                    return clipAssetId > 0;
                }
            }

            clipAssetId = 0;
            return false;
        }

        public bool TryResolveBuiltinClipId(AnimatorBuiltinClipId builtinClipId, out int clipAssetId)
        {
            for (int i = 0; i < BuiltinClips.Length; i++)
            {
                if (BuiltinClips[i].BuiltinClipId == builtinClipId)
                {
                    clipAssetId = BuiltinClips[i].ClipAssetId;
                    return clipAssetId > 0;
                }
            }

            clipAssetId = 0;
            return false;
        }
    }
}
