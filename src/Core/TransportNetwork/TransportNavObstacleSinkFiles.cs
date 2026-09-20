using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.TransportNetwork
{
    public static class TransportNavObstacleSinkFiles
    {
        public const string SinkConfigRelativePath = "Navigation/transport_nav_obstacle_sink.json";
        public const string TransportAssetRelativePath = TransportNetworkAssetLoader.DefaultRelativePath;

        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public static TransportNavObstacleSinkConfig LoadSinkConfig(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                throw new InvalidOperationException("Transport nav obstacle sink config path is required.");
            }

            TransportNavObstacleSinkConfig? config =
                JsonSerializer.Deserialize<TransportNavObstacleSinkConfig>(File.ReadAllText(absolutePath), JsonOptions);
            if (config == null)
            {
                throw new InvalidOperationException($"Transport nav obstacle sink config '{absolutePath}' deserialized to null.");
            }

            config.Validate();
            return config;
        }

        public static TransportNetworkAsset LoadTransportAsset(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                throw new InvalidOperationException("TransportNetwork asset path is required.");
            }

            TransportNetworkAsset? asset =
                JsonSerializer.Deserialize<TransportNetworkAsset>(File.ReadAllText(absolutePath), JsonOptions);
            if (asset == null)
            {
                throw new InvalidOperationException($"TransportNetwork asset '{absolutePath}' deserialized to null.");
            }

            asset.Validate();
            return asset;
        }

        public static void MergeFromExplicitFiles(
            NavObstacleSet target,
            string? sinkConfigPath,
            string? transportAssetPath,
            string mapId)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            // Opt-in: only the sink config arms transport→nav carve. A bare transport asset stays graph/ribbon only.
            if (sinkConfigPath == null)
            {
                return;
            }

            if (transportAssetPath == null)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' has '{SinkConfigRelativePath}' but no '{TransportAssetRelativePath}'.");
            }

            TransportNavObstacleSinkConfig config = LoadSinkConfig(sinkConfigPath);
            TransportNetworkAsset asset = LoadTransportAsset(transportAssetPath);
            TransportNavObstacleSink.AppendTo(target, asset, config);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            };
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            return options;
        }
    }
}
