using System;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PresentationImageLocatorDefinition
    {
        public readonly string BackendId;
        public readonly string AssetRef;

        public PresentationImageLocatorDefinition(string backendId, string assetRef)
        {
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Backend id must not be empty.", nameof(backendId));
            }

            if (string.IsNullOrWhiteSpace(assetRef))
            {
                throw new ArgumentException("Asset reference must not be empty.", nameof(assetRef));
            }

            BackendId = backendId;
            AssetRef = assetRef;
        }
    }
}
