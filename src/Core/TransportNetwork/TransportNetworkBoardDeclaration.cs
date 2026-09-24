using System;
using System.IO;

namespace Ludots.Core.TransportNetwork
{
    /// <summary>
    /// A NodeGraph board's binding to one catalog transport asset.
    /// The asset does not name a map; this declaration is the binding.
    /// </summary>
    public sealed class TransportNetworkBoardDeclaration
    {
        public string AssetPath { get; set; } = string.Empty;

        public void Validate(string mapId, string boardName)
        {
            string where = $"Map '{mapId}' board '{boardName}' TransportNetwork.AssetPath";
            if (AssetPath is null || !string.Equals(AssetPath.Trim(), AssetPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{where} must be a canonical catalog-relative path.");
            }

            if (AssetPath.Length == 0)
            {
                throw new InvalidOperationException($"{where} is required. Name the catalog path of the transport asset.");
            }

            if (AssetPath.StartsWith('/') || AssetPath.StartsWith('\\') || Path.IsPathRooted(AssetPath))
            {
                throw new InvalidOperationException($"{where} must be catalog-relative, not rooted: '{AssetPath}'.");
            }
        }

        public TransportNetworkBoardDeclaration Clone()
        {
            return new TransportNetworkBoardDeclaration { AssetPath = AssetPath };
        }
    }
}
