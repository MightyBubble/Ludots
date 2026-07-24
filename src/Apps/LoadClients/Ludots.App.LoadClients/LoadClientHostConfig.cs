using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Session;
using Ludots.Core.Physics3DNet.Bridge;
using Ludots.Core.Physics3DNet.Input;

namespace Ludots.App.LoadClients;

/// <summary>
/// Strict data-driven contract for the headless replicated-client load host.
/// Malformed or missing required values fail explicitly; no silent substitution.
/// </summary>
public sealed class LoadClientHostConfig
{
    public const int DefaultClientCount = 149;
    public const int RequiredSimulationTickRateHz = 30;

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string ConnectionKey { get; init; } = string.Empty;
    public string PlanFingerprintHex { get; init; } = string.Empty;
    public int ClientCount { get; init; } = DefaultClientCount;
    public double DurationSeconds { get; init; }
    public double WarmUpSeconds { get; init; }
    public string CredentialDirectory { get; init; } = string.Empty;
    public int MaxStepsPerAdvance { get; init; }
    public int MaxAccumulatedSteps { get; init; }
    public double ConnectTimeoutSeconds { get; init; }
    public double ReadyTimeoutSeconds { get; init; }
    public NetworkRuntimeConfig Networking { get; init; } = null!;
    public Physics3DReplicationSchemaConfig Physics3DReplication { get; init; } = null!;
    public Physics3DLoadClientMovementConfig MovementInput { get; init; } = null!;

    public ContentFingerprint PlanFingerprint { get; private set; }

    public static LoadClientHostConfig ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Load-client config JSON is required and must be non-empty.");
        }

        LoadClientHostConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<LoadClientHostConfigDocument>(json, CreateJsonOptions());
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Load-client config JSON is malformed: {exception.Message}",
                exception);
        }

        if (document == null)
        {
            throw new InvalidOperationException("Load-client config JSON deserialized to null.");
        }

        return FromDocument(document);
    }

    public static LoadClientHostConfig LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Load-client config path is required.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Load-client config file was not found: '{fullPath}'.");
        }

        string json = File.ReadAllText(fullPath);
        return ParseJson(json);
    }

    public static LoadClientHostConfig ParseCommandLine(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            throw new InvalidOperationException(
                "Load-client host requires '--config <path>' with a validated data-driven configuration file.");
        }

        string? configPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--config", StringComparison.Ordinal))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    throw new InvalidOperationException("--config requires a non-empty file path.");
                }

                if (configPath != null)
                {
                    throw new InvalidOperationException("--config was specified more than once.");
                }

                configPath = args[++i];
                continue;
            }

            throw new InvalidOperationException(
                $"Unknown load-client argument '{arg}'. Only '--config <path>' is accepted.");
        }

        if (configPath == null)
        {
            throw new InvalidOperationException(
                "Load-client host requires '--config <path>' with a validated data-driven configuration file.");
        }

        return LoadFromFile(configPath);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            throw new InvalidOperationException("Load-client host is required.");
        }

        if ((uint)(Port - 1) >= ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Load-client port must be between 1 and {ushort.MaxValue}; got {Port}.");
        }

        if (string.IsNullOrWhiteSpace(ConnectionKey))
        {
            throw new InvalidOperationException("Load-client connectionKey is required.");
        }

        if (!ContentFingerprint.TryParseHex(PlanFingerprintHex, out ContentFingerprint fingerprint) ||
            fingerprint.IsEmpty)
        {
            throw new InvalidOperationException(
                "Load-client planFingerprint must be a non-empty 64-character lowercase or uppercase hex digest.");
        }

        PlanFingerprint = fingerprint;

        if (ClientCount <= 0)
        {
            throw new InvalidOperationException(
                $"Load-client clientCount must be positive; got {ClientCount}.");
        }

        if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0d)
        {
            throw new InvalidOperationException(
                $"Load-client durationSeconds must be a finite value > 0; got {DurationSeconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (!double.IsFinite(WarmUpSeconds) || WarmUpSeconds < 0d)
        {
            throw new InvalidOperationException(
                $"Load-client warmUpSeconds must be a finite value >= 0; got {WarmUpSeconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (WarmUpSeconds >= DurationSeconds)
        {
            throw new InvalidOperationException(
                "Load-client warmUpSeconds must be strictly less than durationSeconds.");
        }

        if (string.IsNullOrWhiteSpace(CredentialDirectory))
        {
            throw new InvalidOperationException("Load-client credentialDirectory is required.");
        }

        if (MaxStepsPerAdvance <= 0)
        {
            throw new InvalidOperationException(
                $"Load-client maxStepsPerAdvance must be positive; got {MaxStepsPerAdvance}.");
        }

        if (MaxAccumulatedSteps < MaxStepsPerAdvance)
        {
            throw new InvalidOperationException(
                $"Load-client maxAccumulatedSteps ({MaxAccumulatedSteps}) must be >= maxStepsPerAdvance ({MaxStepsPerAdvance}).");
        }

        if (!double.IsFinite(ConnectTimeoutSeconds) || ConnectTimeoutSeconds <= 0d)
        {
            throw new InvalidOperationException(
                $"Load-client connectTimeoutSeconds must be a finite value > 0; got {ConnectTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (!double.IsFinite(ReadyTimeoutSeconds) || ReadyTimeoutSeconds <= 0d)
        {
            throw new InvalidOperationException(
                $"Load-client readyTimeoutSeconds must be a finite value > 0; got {ReadyTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (Networking == null)
        {
            throw new InvalidOperationException("Load-client networking configuration is required.");
        }

        Networking.Validate();

        if (Networking.SimulationTickRateHz != RequiredSimulationTickRateHz)
        {
            throw new InvalidOperationException(
                $"Load-client networking.simulationTickRateHz must be exactly {RequiredSimulationTickRateHz}; got {Networking.SimulationTickRateHz}.");
        }

        if (Networking.FixedInputFramePayloadBytes != Physics3DFixedInputFrameCodec.PayloadBytes)
        {
            throw new InvalidOperationException(
                $"Load-client Physics3D input requires exactly {Physics3DFixedInputFrameCodec.PayloadBytes} payload bytes; got {Networking.FixedInputFramePayloadBytes}.");
        }

        if (!string.Equals(Networking.ReferenceTransport, "LiteNetLib/2.1.4", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Load-client production host requires ReferenceTransport 'LiteNetLib/2.1.4'; got '{Networking.ReferenceTransport}'.");
        }

        if (Networking.PlayerCapacity < ClientCount)
        {
            throw new InvalidOperationException(
                $"Networking playerCapacity {Networking.PlayerCapacity} is below configured clientCount {ClientCount}.");
        }

        ArgumentNullException.ThrowIfNull(Physics3DReplication);
        Physics3DReplication.Validate(Networking);
        ArgumentNullException.ThrowIfNull(MovementInput);
        MovementInput.Validate();
    }

    private static LoadClientHostConfig FromDocument(LoadClientHostConfigDocument document)
    {
        if (document.Networking == null)
        {
            throw new InvalidOperationException("Load-client networking configuration is required.");
        }

        if (document.ClientCount is int explicitCount and <= 0)
        {
            throw new InvalidOperationException(
                $"Load-client clientCount must be positive when provided; got {explicitCount}.");
        }

        var config = new LoadClientHostConfig
        {
            Host = document.Host ?? string.Empty,
            Port = document.Port ?? 0,
            ConnectionKey = document.ConnectionKey ?? string.Empty,
            PlanFingerprintHex = document.PlanFingerprint ?? string.Empty,
            ClientCount = document.ClientCount ?? DefaultClientCount,
            DurationSeconds = document.DurationSeconds ?? double.NaN,
            WarmUpSeconds = document.WarmUpSeconds ?? double.NaN,
            CredentialDirectory = document.CredentialDirectory ?? string.Empty,
            MaxStepsPerAdvance = document.MaxStepsPerAdvance ?? 0,
            MaxAccumulatedSteps = document.MaxAccumulatedSteps ?? 0,
            ConnectTimeoutSeconds = document.ConnectTimeoutSeconds ?? double.NaN,
            ReadyTimeoutSeconds = document.ReadyTimeoutSeconds ?? double.NaN,
            Networking = document.Networking,
            Physics3DReplication = document.Physics3DReplication!,
            MovementInput = document.MovementInput!,
        };

        RequirePresent(document.Host, "host");
        RequirePresent(document.Port, "port");
        RequirePresent(document.ConnectionKey, "connectionKey");
        RequirePresent(document.PlanFingerprint, "planFingerprint");
        RequirePresent(document.DurationSeconds, "durationSeconds");
        RequirePresent(document.WarmUpSeconds, "warmUpSeconds");
        RequirePresent(document.CredentialDirectory, "credentialDirectory");
        RequirePresent(document.MaxStepsPerAdvance, "maxStepsPerAdvance");
        RequirePresent(document.MaxAccumulatedSteps, "maxAccumulatedSteps");
        RequirePresent(document.ConnectTimeoutSeconds, "connectTimeoutSeconds");
        RequirePresent(document.ReadyTimeoutSeconds, "readyTimeoutSeconds");
        if (document.Physics3DReplication == null)
        {
            throw new InvalidOperationException(
                "Load-client physics3DReplication is required.");
        }

        if (document.MovementInput == null)
        {
            throw new InvalidOperationException("Load-client movementInput is required.");
        }

        config.Validate();
        return config;
    }

    private static void RequirePresent<T>(T? value, string jsonName)
        where T : struct
    {
        if (!value.HasValue)
        {
            throw new InvalidOperationException($"Load-client config is missing required field '{jsonName}'.");
        }
    }

    private static void RequirePresent(string? value, string jsonName)
    {
        if (value == null)
        {
            throw new InvalidOperationException($"Load-client config is missing required field '{jsonName}'.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}

internal sealed class LoadClientHostConfigDocument
{
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("connectionKey")]
    public string? ConnectionKey { get; set; }

    [JsonPropertyName("planFingerprint")]
    public string? PlanFingerprint { get; set; }

    [JsonPropertyName("clientCount")]
    public int? ClientCount { get; set; }

    [JsonPropertyName("durationSeconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("warmUpSeconds")]
    public double? WarmUpSeconds { get; set; }

    [JsonPropertyName("credentialDirectory")]
    public string? CredentialDirectory { get; set; }

    [JsonPropertyName("maxStepsPerAdvance")]
    public int? MaxStepsPerAdvance { get; set; }

    [JsonPropertyName("maxAccumulatedSteps")]
    public int? MaxAccumulatedSteps { get; set; }

    [JsonPropertyName("connectTimeoutSeconds")]
    public double? ConnectTimeoutSeconds { get; set; }

    [JsonPropertyName("readyTimeoutSeconds")]
    public double? ReadyTimeoutSeconds { get; set; }

    [JsonPropertyName("networking")]
    public NetworkRuntimeConfig? Networking { get; set; }

    [JsonPropertyName("physics3DReplication")]
    public Physics3DReplicationSchemaConfig? Physics3DReplication { get; set; }

    [JsonPropertyName("movementInput")]
    public Physics3DLoadClientMovementConfig? MovementInput { get; set; }
}

public sealed class Physics3DLoadClientMovementConfig
{
    [JsonRequired]
    public float X { get; init; }
    [JsonRequired]
    public float Y { get; init; }

    public Vector2 Value => new(X, Y);

    public void Validate()
    {
        Span<byte> payload = stackalloc byte[Physics3DFixedInputFrameCodec.PayloadBytes];
        if (!Physics3DFixedInputFrameCodec.TryEncode(Value, payload))
        {
            throw new InvalidOperationException(
                $"Load-client movementInput ({X.ToString(CultureInfo.InvariantCulture)}, {Y.ToString(CultureInfo.InvariantCulture)}) is not a finite normalized Physics3D movement vector.");
        }
    }
}
