using System;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Presentation.Assets
{
    public readonly struct EmitterAssetDescriptor
    {
        public EmitterAssetDescriptor(
            int id,
            AssetKind assetKind,
            int runtimeFormatVersion,
            string sha256,
            string[] sourceUris)
        {
            if (runtimeFormatVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(runtimeFormatVersion),
                    runtimeFormatVersion,
                    "Emitter asset runtime format version must be positive. The presentation adapter owns compatibility validation.");
            }

            Id = id;
            AssetKind = assetKind;
            RuntimeFormatVersion = runtimeFormatVersion;
            Sha256 = NormalizeSha256(sha256);
            SourceUris = sourceUris ?? throw new ArgumentNullException(nameof(sourceUris));
        }

        public int Id { get; }

        public AssetKind AssetKind { get; }

        public int RuntimeFormatVersion { get; }

        /// <summary>
        /// Lowercase content hash. A presentation Host must compare this value with the
        /// resolved .efkefc bytes before passing the asset to its runtime implementation.
        /// </summary>
        public string Sha256 { get; }

        /// <summary>
        /// Host-specific asset locations bound by PresentationHostAssetConfigLoader.
        /// The semantic emitter catalog always registers this as empty.
        /// </summary>
        public string[] SourceUris { get; }

        public static string NormalizeSha256(string sha256)
        {
            if (sha256 == null)
            {
                throw new ArgumentNullException(nameof(sha256));
            }

            if (sha256.Length != 64)
            {
                throw new ArgumentException("Emitter asset SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sha256));
            }

            for (int i = 0; i < sha256.Length; i++)
            {
                char c = sha256[i];
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F')))
                {
                    throw new ArgumentException(
                        $"Emitter asset SHA-256 contains non-hexadecimal character '{c}' at index {i}.",
                        nameof(sha256));
                }
            }

            return sha256.ToLowerInvariant();
        }
    }
}
