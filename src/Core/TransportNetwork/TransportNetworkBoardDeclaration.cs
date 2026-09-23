using System;
using System.IO;

namespace Ludots.Core.TransportNetwork
{
    /// <summary>
    /// Board-level declaration that a board hosts an asset-authored transport network.
    /// Presence on a NodeGraph board makes the engine install the baked network into the
    /// board's chunk graph and the surface payload registry on map load — mods carry data only.
    /// </summary>
    public sealed class TransportNetworkBoardDeclaration
    {
        /// <summary>Config-catalog-relative asset path override. Empty = the standard
        /// catalog entry (TransportNetworkAssetLoader.DefaultRelativePath).</summary>
        public string AssetPath { get; set; } = string.Empty;

        public void Validate(string mapId, string boardName)
        {
            if (AssetPath is null || !string.Equals(AssetPath.Trim(), AssetPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{boardName}' TransportNetwork.assetPath must be canonical (no leading/trailing whitespace).");
            }

            if (AssetPath.Length > 0 && (AssetPath.StartsWith('/') || AssetPath.StartsWith('\\') || Path.IsPathRooted(AssetPath)))
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' board '{boardName}' TransportNetwork.assetPath must be config-catalog relative, not rooted: '{AssetPath}'.");
            }
        }
    }
}
