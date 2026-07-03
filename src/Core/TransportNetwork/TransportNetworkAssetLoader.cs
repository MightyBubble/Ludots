using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.TransportNetwork
{
    public sealed class TransportNetworkAssetLoader
    {
        public const string DefaultRelativePath = "TransportNetwork/transport_network.json";

        private readonly ConfigPipeline _pipeline;

        public TransportNetworkAssetLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public TransportNetworkAsset Load(
            ConfigCatalog catalog,
            ConfigConflictReport? report = null,
            string relativePath = DefaultRelativePath)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                relativePath,
                ConfigMergePolicy.Replace);
            var merged = report != null
                ? _pipeline.MergeFromCatalog(in entry, report)
                : _pipeline.MergeFromCatalog(in entry);

            if (merged == null)
            {
                throw new InvalidOperationException($"TransportNetworkAsset '{relativePath}' not found in any source.");
            }

            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            TransportNetworkAsset? asset = merged.Deserialize<TransportNetworkAsset>(options);
            if (asset == null)
            {
                throw new InvalidOperationException($"TransportNetworkAsset '{relativePath}' deserialized to null.");
            }

            asset.Validate();
            return asset;
        }
    }
}
