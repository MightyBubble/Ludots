using System;
using System.IO;
using Ludots.Core.Modding;

namespace Ludots.Core.Presentation.Assets
{
    public sealed class PresentationImageSourceResolver
    {
        private readonly PresentationImageRegistry _registry;
        private readonly IVirtualFileSystem _vfs;
        private readonly string _backendId;

        public PresentationImageSourceResolver(
            PresentationImageRegistry registry,
            IVirtualFileSystem vfs,
            string backendId)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            if (string.IsNullOrWhiteSpace(backendId))
            {
                throw new ArgumentException("Presentation backend id must not be empty.", nameof(backendId));
            }

            _backendId = backendId;
        }

        public string ResolveRequiredSource(int imageAssetId)
        {
            if (!_registry.TryResolveLocator(imageAssetId, _backendId, out PresentationImageLocatorDefinition locator))
            {
                throw new InvalidOperationException(
                    $"Presentation image asset '{_registry.GetName(imageAssetId)}' does not define a locator for backend '{_backendId}'.");
            }

            return ResolveAssetRef(locator.AssetRef);
        }

        public bool TryResolveSource(int imageAssetId, out string source)
        {
            source = string.Empty;
            if (!_registry.TryResolveLocator(imageAssetId, _backendId, out PresentationImageLocatorDefinition locator))
            {
                return false;
            }

            source = ResolveAssetRef(locator.AssetRef);
            return true;
        }

        private string ResolveAssetRef(string assetRef)
        {
            if (string.IsNullOrWhiteSpace(assetRef))
            {
                throw new InvalidOperationException("Presentation image asset reference must not be empty.");
            }

            string trimmed = assetRef.Trim();
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (Path.IsPathRooted(trimmed))
            {
                return trimmed;
            }

            if (!_vfs.TryResolveFullPath(trimmed, out string fullPath))
            {
                throw new InvalidOperationException(
                    $"Presentation image asset reference '{trimmed}' must be an absolute path, data URI, URL, or VFS URI.");
            }

            return fullPath;
        }
    }
}
