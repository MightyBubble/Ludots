using System.Text.Json;
using Ludots.App.LoadClients;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Session;
using NUnit.Framework;

namespace Ludots.Tests.LoadClients;

[TestFixture]
public sealed class LoadClientHostConfigTests
{
    [Test]
    public void ParseJson_ValidConfig_DefaultsClientCountTo149WhenOmitted()
    {
        string json = CreateValidJson(omitClientCount: true);
        LoadClientHostConfig config = LoadClientHostConfig.ParseJson(json);
        Assert.That(config.ClientCount, Is.EqualTo(149));
        Assert.That(config.Networking.SimulationTickRateHz, Is.EqualTo(30));
        Assert.That(config.Physics3DReplication.SchemaId, Is.EqualTo(1));
        Assert.That(config.PlanFingerprint.IsEmpty, Is.False);
    }

    [Test]
    public void ParseJson_MissingRequiredField_FailsExplicitly()
    {
        string json = CreateValidJson().Replace("\"host\": \"127.0.0.1\",", string.Empty, StringComparison.Ordinal);
        Assert.That(
            () => LoadClientHostConfig.ParseJson(json),
            Throws.InvalidOperationException.With.Message.Contains("host"));
    }

    [Test]
    public void ParseJson_MalformedPort_FailsWithoutSubstitution()
    {
        string json = CreateValidJson().Replace("\"port\": 9050", "\"port\": 0", StringComparison.Ordinal);
        Assert.That(
            () => LoadClientHostConfig.ParseJson(json),
            Throws.InvalidOperationException.With.Message.Contains("port"));
    }

    [Test]
    public void ParseJson_NonThirtyHz_FailsExplicitly()
    {
        string json = CreateValidJson(networkingTickRateHz: 60);
        Assert.That(
            () => LoadClientHostConfig.ParseJson(json),
            Throws.InvalidOperationException.With.Message.Contains("30"));
    }

    [Test]
    public void ParseJson_LegacySchemaArrayAndDuplicateTickTruth_AreRejected()
    {
        string legacySchema = CreateValidJson().Replace(
            "\"physics3DReplication\": {",
            "\"replicationSchemaIds\": [1],\n  \"physics3DReplication\": {",
            StringComparison.Ordinal);
        string duplicateTick = CreateValidJson().Replace(
            "\"durationSeconds\": 2.0,",
            "\"simulationTickRateHz\": 30,\n  \"durationSeconds\": 2.0,",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => LoadClientHostConfig.ParseJson(legacySchema),
                Throws.InvalidOperationException.With.Message.Contains("replicationSchemaIds"));
            Assert.That(
                () => LoadClientHostConfig.ParseJson(duplicateTick),
                Throws.InvalidOperationException.With.Message.Contains("simulationTickRateHz"));
        });
    }

    [Test]
    public void ParseJson_NonPhysics3DPayloadSizeAndInvalidMovement_AreRejected()
    {
        string wrongPayload = CreateValidJson().Replace(
            "\"fixedInputFramePayloadBytes\": 8",
            "\"fixedInputFramePayloadBytes\": 12",
            StringComparison.Ordinal);
        string invalidMovement = CreateValidJson().Replace(
            "\"movementInput\": { \"x\": 1.0, \"y\": 0.0 }",
            "\"movementInput\": { \"x\": 1.0, \"y\": 1.0 }",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => LoadClientHostConfig.ParseJson(wrongPayload),
                Throws.InvalidOperationException.With.Message.Contains("8"));
            Assert.That(
                () => LoadClientHostConfig.ParseJson(invalidMovement),
                Throws.InvalidOperationException.With.Message.Contains("movementInput"));
        });
    }

    [Test]
    public void ParseJson_MissingPhysics3DQuantizationOrMovementAxis_FailsWithoutDefaults()
    {
        string missingQuantization = CreateValidJson().Replace(
            "\"positionResolutionCm\": 0.5,",
            string.Empty,
            StringComparison.Ordinal);
        string missingMovementAxis = CreateValidJson().Replace(
            "\"movementInput\": { \"x\": 1.0, \"y\": 0.0 }",
            "\"movementInput\": { \"x\": 1.0 }",
            StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => LoadClientHostConfig.ParseJson(missingQuantization),
                Throws.InvalidOperationException.With.Message.Contains("positionResolutionCm"));
            Assert.That(
                () => LoadClientHostConfig.ParseJson(missingMovementAxis),
                Throws.InvalidOperationException.With.Message.Contains("y"));
        });
    }

    [Test]
    public void ParseJson_MalformedFingerprint_FailsExplicitly()
    {
        string json = CreateValidJson(planFingerprint: "abcd");
        Assert.That(
            () => LoadClientHostConfig.ParseJson(json),
            Throws.InvalidOperationException.With.Message.Contains("planFingerprint"));
    }

    [Test]
    public void ParseJson_ZeroClientCount_FailsExplicitly()
    {
        string json = CreateValidJson(clientCount: 0);
        Assert.That(
            () => LoadClientHostConfig.ParseJson(json),
            Throws.InvalidOperationException.With.Message.Contains("clientCount"));
    }

    [Test]
    public void ParseCommandLine_RequiresConfigPath()
    {
        Assert.That(
            () => LoadClientHostConfig.ParseCommandLine(Array.Empty<string>()),
            Throws.InvalidOperationException.With.Message.Contains("--config"));
    }

    [Test]
    public void ParseCommandLine_UnknownArgument_Fails()
    {
        Assert.That(
            () => LoadClientHostConfig.ParseCommandLine(new[] { "--host", "127.0.0.1" }),
            Throws.InvalidOperationException.With.Message.Contains("Unknown"));
    }

    [Test]
    public void ParseCommandLine_LoadsFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"load-client-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, CreateValidJson(clientCount: 3));
        try
        {
            LoadClientHostConfig config = LoadClientHostConfig.ParseCommandLine(new[] { "--config", path });
            Assert.That(config.ClientCount, Is.EqualTo(3));
            Assert.That(config.Host, Is.EqualTo("127.0.0.1"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static string CreateValidJson(
        int? clientCount = 2,
        bool omitClientCount = false,
        string planFingerprint = "",
        int networkingTickRateHz = 30)
    {
        if (string.IsNullOrEmpty(planFingerprint))
        {
            planFingerprint = ContentFingerprintBuilder.FromCanonicalBytes("load-client-test-plan"u8).ToHexString();
        }

        string clientCountLine = omitClientCount
            ? string.Empty
            : $"\"clientCount\": {clientCount},";

        return $$"""
        {
          "host": "127.0.0.1",
          "port": 9050,
          "connectionKey": "load-client-test",
          "planFingerprint": "{{planFingerprint}}",
          {{clientCountLine}}
          "durationSeconds": 2.0,
          "warmUpSeconds": 0.5,
          "credentialDirectory": "credentials",
          "maxStepsPerAdvance": 4,
          "maxAccumulatedSteps": 8,
          "connectTimeoutSeconds": 2.0,
          "readyTimeoutSeconds": 2.0,
          "physics3DReplication": {
            "schemaId": 1,
            "quantization": {
              "positionResolutionCm": 0.5,
              "quaternionResolution": 0.00003051851,
              "linearVelocityResolutionCmPerSecond": 0.5,
              "angularVelocityResolutionRadiansPerSecond": 0.001
            }
          },
          "movementInput": { "x": 1.0, "y": 0.0 },
          "networking": {
            "profileId": "load_client_test",
            "referenceTransport": "LiteNetLib/2.1.4",
            "protocolMajor": 1,
            "protocolMinor": 0,
            "playerCapacity": 150,
            "simulationTickRateHz": {{networkingTickRateHz}},
            "statePublishRateHz": 10,
            "globalNetworkEntityCapacity": 1024,
            "replicationEntityCapacityPerSeat": 64,
            "orderQueueCapacity": 128,
            "maxCommandBatchesPerSecondPerPlayer": 16,
            "commandBurstBatchCapacity": 8,
            "maxActorsPerCommandBatch": 16,
            "commandSequenceHistoryCapacity": 1200,
            "maxPastTargetTicks": 3,
            "maxFutureTargetTicks": 6,
            "networkAdmissionResultCapacity": 1200,
            "entityAdmissionResultCapacity": 128,
            "reconnectWindowSeconds": 30,
            "clientReconnectRetryMilliseconds": 250,
            "replicationSchemaCapacity": 1,
            "baselineCapacity": 16,
            "disclosureChangeLogCapacity": 256,
            "datagramQueueCapacity": 128,
            "connectionEventCapacity": 16,
            "maxDatagramPayloadBytes": 1200,
            "transportMaxConnectAttempts": 3,
            "transportDisconnectTimeoutMilliseconds": 5000,
            "reliableDisconnectFlushTimeoutMilliseconds": 4000,
            "transportChannelCount": 4,
            "controlChannelId": 0,
            "commandChannelId": 1,
            "stateChannelId": 2,
            "inputChannelId": 3,
            "fixedInputHistoryTicksPerSeat": 8,
            "fixedInputSchemaId": 1,
            "fixedInputFramePayloadBytes": 8,
            "fixedInputMaxFutureTicks": 4,
            "fixedInputLeadTicks": 2,
            "fixedInputMaxFramesPerBatch": 4,
            "fixedInputPendingFrameCapacity": 8,
            "snapshotChunkCapacity": 32,
            "maxServerOutboundBytesPerSecondPerClient": 262144,
            "tickP95BudgetMicroseconds": 26700,
            "tickP99BudgetMicroseconds": 31000,
            "commandSchemas": [
              {
                "orderTypeKey": "moveTo",
                "targetKind": "WorldPositionCm",
                "submitMode": "Queued"
              }
            ],
            "normalConnection": {
              "roundTripLatencyMs": 0,
              "jitterMs": 0,
              "packetLossPermille": 0,
              "reorderPermille": 0
            },
            "unstableConnection": {
              "roundTripLatencyMs": 0,
              "jitterMs": 0,
              "packetLossPermille": 0,
              "reorderPermille": 0
            }
          }
        }
        """;
    }
}
