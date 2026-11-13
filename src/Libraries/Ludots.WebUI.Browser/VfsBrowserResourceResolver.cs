using System.IO;
using Ludots.Core.Modding;
using Ludots.UI.Browser;

namespace Ludots.WebUI.Browser;

public sealed class VfsBrowserResourceResolver : IBrowserResourceResolver
{
    private readonly IVirtualFileSystem _virtualFileSystem;

    public VfsBrowserResourceResolver(IVirtualFileSystem virtualFileSystem)
    {
        _virtualFileSystem = virtualFileSystem ?? throw new ArgumentNullException(nameof(virtualFileSystem));
    }

    public async ValueTask<BrowserResource?> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BrowserVfsUri.TryParse(uri, out string modId, out string relativePath))
        {
            return null;
        }

        string mountedPath = BrowserVfsUri.ToMountedPath(modId, relativePath);
        if (!_virtualFileSystem.TryResolveFullPath(mountedPath, out string fullPath) ||
            string.IsNullOrWhiteSpace(fullPath) ||
            !IsRequestedAssetFile(fullPath, relativePath))
        {
            return null;
        }

        byte[] content = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new BrowserResource(BrowserContentTypes.ForFile(fullPath), content);
    }

    private static bool IsRequestedAssetFile(string fullPath, string relativePath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        string normalized = Path.GetFullPath(fullPath);
        string suffix = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Length <= suffix.Length ||
            !normalized.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        return normalized[normalized.Length - suffix.Length - 1] == Path.DirectorySeparatorChar;
    }
}
