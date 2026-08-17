using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class AnimationClipDefinition
    {
        public int ClipAssetId;
        public AnimationClipAssetKind AssetKind;
        public AnimationBlendInputSource BlendInputX = AnimationBlendInputSource.Scalar0;
        public AnimationBlendInputSource BlendInputY = AnimationBlendInputSource.Scalar1;
        public AnimationClipLocatorDefinition[] Locators = Array.Empty<AnimationClipLocatorDefinition>();

        public bool TryResolveLocator(string backendId, out AnimationClipLocatorDefinition locator)
        {
            if (!string.IsNullOrWhiteSpace(backendId))
            {
                for (int i = 0; i < Locators.Length; i++)
                {
                    if (string.Equals(Locators[i].BackendId, backendId, StringComparison.Ordinal))
                    {
                        locator = Locators[i];
                        return true;
                    }
                }
            }

            locator = default;
            return false;
        }
    }
}
