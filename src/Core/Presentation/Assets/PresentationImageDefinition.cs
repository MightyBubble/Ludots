using System;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationImageDefinition
    {
        public int ImageAssetId;
        public PresentationImageAssetKind AssetKind;
        public PresentationImageLocatorDefinition[] Locators = Array.Empty<PresentationImageLocatorDefinition>();

        public bool TryResolveLocator(string backendId, out PresentationImageLocatorDefinition locator)
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
