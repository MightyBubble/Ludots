using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Physics3D;
using Ludots.Core.Physics3DNet.Input;

namespace Ludots.Core.Physics3DNet.Bridge;

public sealed class Physics3DNetworkRuntimeConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }
    public int ReplicationSchemaId { get; init; }
    public int KnowledgeRecordCapacity { get; init; }
    public Physics3DReplicationQuantizationConfig Quantization { get; init; } = null!;
    public Physics3DNetworkPlayerBodyConfig PlayerBody { get; init; } = null!;
    public Physics3DNetworkPlayerSpawnConfig PlayerSpawn { get; init; } = null!;
    public Physics3DNetworkAoiConfig Aoi { get; init; } = null!;
    public Physics3DNetworkMovementConfig Movement { get; init; } = null!;

    public void Validate(NetworkRuntimeConfig network)
    {
        ArgumentNullException.ThrowIfNull(network);
        network.Validate();
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Physics3D network schema version {SchemaVersion} is unsupported; expected {CurrentSchemaVersion}.");
        }

        if (ReplicationSchemaId <= 0 || ReplicationSchemaId > network.ReplicationSchemaCapacity)
        {
            throw new InvalidOperationException(
                $"Physics3D replication schema {ReplicationSchemaId} is outside configured registry capacity {network.ReplicationSchemaCapacity}.");
        }

        int minimumKnowledgeRecords = checked(network.PlayerCapacity * network.PlayerCapacity);
        if (KnowledgeRecordCapacity < minimumKnowledgeRecords)
        {
            throw new InvalidOperationException(
                $"Physics3D knowledge capacity {KnowledgeRecordCapacity} is below the {minimumKnowledgeRecords} records required for {network.PlayerCapacity} mutually visible players.");
        }

        ArgumentNullException.ThrowIfNull(Quantization);
        ArgumentNullException.ThrowIfNull(PlayerBody);
        ArgumentNullException.ThrowIfNull(PlayerSpawn);
        ArgumentNullException.ThrowIfNull(Aoi);
        ArgumentNullException.ThrowIfNull(Movement);
        Quantization.Validate();
        PlayerBody.Validate();
        PlayerSpawn.Validate();
        Aoi.Validate();
        Movement.Validate();
        if (Aoi.GlobalEntityCapacity != network.GlobalNetworkEntityCapacity)
        {
            throw new InvalidOperationException(
                $"Physics3D AOI global capacity {Aoi.GlobalEntityCapacity} differs from networking capacity {network.GlobalNetworkEntityCapacity}.");
        }

        if (Movement.SchemaId != network.FixedInputSchemaId)
        {
            throw new InvalidOperationException(
                $"Physics3D movement input schema {Movement.SchemaId} differs from networking schema {network.FixedInputSchemaId}.");
        }

        if (network.FixedInputFramePayloadBytes != Physics3DFixedInputFrameCodec.PayloadBytes)
        {
            throw new InvalidOperationException(
                $"Physics3D fixed input requires {Physics3DFixedInputFrameCodec.PayloadBytes} payload bytes, got {network.FixedInputFramePayloadBytes}.");
        }
    }
}

public sealed class Physics3DNetworkRuntimeConfigLoader
{
    public const string DefaultRelativePath = "Physics3D/network.v1.json";

    private readonly ConfigPipeline _pipeline;

    public Physics3DNetworkRuntimeConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public Physics3DNetworkRuntimeConfig Load(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        NetworkRuntimeConfig network,
        string relativePath = DefaultRelativePath)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(network);
        ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
        JsonObject merged = _pipeline.MergeDeepObjectFromCatalog(in entry, report) ??
            throw new InvalidOperationException($"Required Physics3D network config '{relativePath}' has no fragments.");
        RequireProperties(
            merged,
            relativePath,
            nameof(Physics3DNetworkRuntimeConfig.SchemaVersion),
            nameof(Physics3DNetworkRuntimeConfig.ReplicationSchemaId),
            nameof(Physics3DNetworkRuntimeConfig.KnowledgeRecordCapacity),
            nameof(Physics3DNetworkRuntimeConfig.Quantization),
            nameof(Physics3DNetworkRuntimeConfig.PlayerBody),
            nameof(Physics3DNetworkRuntimeConfig.PlayerSpawn),
            nameof(Physics3DNetworkRuntimeConfig.Aoi),
            nameof(Physics3DNetworkRuntimeConfig.Movement));
        RequireProperties(
            RequireObject(merged, nameof(Physics3DNetworkRuntimeConfig.Quantization), relativePath),
            relativePath,
            nameof(Physics3DReplicationQuantizationConfig.PositionResolutionCm),
            nameof(Physics3DReplicationQuantizationConfig.QuaternionResolution),
            nameof(Physics3DReplicationQuantizationConfig.LinearVelocityResolutionCmPerSecond),
            nameof(Physics3DReplicationQuantizationConfig.AngularVelocityResolutionRadiansPerSecond));
        JsonObject playerBody = RequireObject(merged, nameof(Physics3DNetworkRuntimeConfig.PlayerBody), relativePath);
        RequireProperties(
            playerBody,
            relativePath,
            nameof(Physics3DNetworkPlayerBodyConfig.RadiusCm),
            nameof(Physics3DNetworkPlayerBodyConfig.CylinderLengthCm),
            nameof(Physics3DNetworkPlayerBodyConfig.Mass),
            nameof(Physics3DNetworkPlayerBodyConfig.CollisionLayer),
            nameof(Physics3DNetworkPlayerBodyConfig.Material),
            nameof(Physics3DNetworkPlayerBodyConfig.ContinuousDetection));
        RequireProperties(
            RequireObject(playerBody, nameof(Physics3DNetworkPlayerBodyConfig.CollisionLayer), relativePath),
            relativePath,
            "Category",
            "Mask");
        RequireProperties(
            RequireObject(playerBody, nameof(Physics3DNetworkPlayerBodyConfig.Material), relativePath),
            relativePath,
            "FrictionCoefficient",
            "MaximumRecoveryVelocityCmPerSecond",
            "SpringAngularFrequency",
            "SpringTwiceDampingRatio");
        JsonObject playerSpawn = RequireObject(merged, nameof(Physics3DNetworkRuntimeConfig.PlayerSpawn), relativePath);
        RequireProperties(
            playerSpawn,
            relativePath,
            nameof(Physics3DNetworkPlayerSpawnConfig.OriginCm),
            nameof(Physics3DNetworkPlayerSpawnConfig.ColumnSpacingCm),
            nameof(Physics3DNetworkPlayerSpawnConfig.RowSpacingCm),
            nameof(Physics3DNetworkPlayerSpawnConfig.Columns));
        RequireProperties(
            RequireObject(playerSpawn, nameof(Physics3DNetworkPlayerSpawnConfig.OriginCm), relativePath),
            relativePath,
            "X",
            "Y",
            "Z");
        RequireProperties(
            RequireObject(merged, nameof(Physics3DNetworkRuntimeConfig.Aoi), relativePath),
            relativePath,
            nameof(Physics3DNetworkAoiConfig.GlobalEntityCapacity),
            nameof(Physics3DNetworkAoiConfig.RadiusCm));
        RequireProperties(
            RequireObject(merged, nameof(Physics3DNetworkRuntimeConfig.Movement), relativePath),
            relativePath,
            nameof(Physics3DNetworkMovementConfig.SchemaId),
            nameof(Physics3DNetworkMovementConfig.MaximumSpeedCmPerSecond),
            nameof(Physics3DNetworkMovementConfig.MaximumAccelerationCmPerSecondSquared),
            nameof(Physics3DNetworkMovementConfig.VelocityResponsePerSecond));

        JsonSerializerOptions options = StrictJsonOptions.CreateExact(includeFields: true);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.Converters.Add(new Physics3DMaterialJsonConverter());
        Physics3DNetworkRuntimeConfig config = merged.Deserialize<Physics3DNetworkRuntimeConfig>(options) ??
            throw new InvalidOperationException($"Physics3D network config '{relativePath}' deserialized to null.");
        config.Validate(network);
        return config;
    }

    private static JsonObject RequireObject(JsonObject owner, string propertyName, string relativePath)
    {
        return owner[propertyName] as JsonObject ??
            throw new InvalidOperationException(
                $"Physics3D network config '{relativePath}' property '{propertyName}' must be an object.");
    }

    private static void RequireProperties(JsonObject owner, string relativePath, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            string propertyName = propertyNames[i];
            if (!owner.ContainsKey(propertyName))
            {
                throw new InvalidOperationException(
                    $"Physics3D network config '{relativePath}' must explicitly define '{propertyName}'.");
            }
        }
    }

    private sealed class Physics3DMaterialJsonConverter : JsonConverter<Physics3DMaterial>
    {
        public override Physics3DMaterial Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Physics3D material must be an object.");
            }

            int count = 0;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                count++;
                if (property.Name is not ("FrictionCoefficient"
                    or "MaximumRecoveryVelocityCmPerSecond"
                    or "SpringAngularFrequency"
                    or "SpringTwiceDampingRatio"))
                {
                    throw new JsonException($"Unknown Physics3D material property '{property.Name}'.");
                }
            }

            if (count != 4)
            {
                throw new JsonException("Physics3D material must define exactly four properties.");
            }

            return new Physics3DMaterial(
                root.GetProperty("FrictionCoefficient").GetSingle(),
                root.GetProperty("MaximumRecoveryVelocityCmPerSecond").GetSingle(),
                root.GetProperty("SpringAngularFrequency").GetSingle(),
                root.GetProperty("SpringTwiceDampingRatio").GetSingle());
        }

        public override void Write(
            Utf8JsonWriter writer,
            Physics3DMaterial value,
            JsonSerializerOptions options) =>
            throw new NotSupportedException("Physics3D network configuration is read-only at runtime.");
    }
}
