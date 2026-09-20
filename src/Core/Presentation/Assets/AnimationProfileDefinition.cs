using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimationProfileDefinition
    {
        public int ProfileId;
        public int AnimatorControllerId;
        public AnimationStateClipBinding[] StateClips = Array.Empty<AnimationStateClipBinding>();

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
    }
}
