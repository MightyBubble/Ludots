using System;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct AnimationClipLocatorDefinition
    {
        public readonly string BackendId;
        public readonly string AssetRef;
        public readonly string Variant;

        public AnimationClipLocatorDefinition(string backendId, string assetRef, string variant = "")
        {
            if (string.IsNullOrWhiteSpace(backendId))
                throw new ArgumentException("Backend id must not be empty.", nameof(backendId));

            if (string.IsNullOrWhiteSpace(assetRef))
                throw new ArgumentException("Asset reference must not be empty.", nameof(assetRef));

            BackendId = backendId;
            AssetRef = assetRef;
            Variant = variant ?? string.Empty;
        }
    }
}
