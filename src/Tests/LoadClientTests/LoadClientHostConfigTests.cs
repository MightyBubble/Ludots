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
        Assert.That(config.SimulationTickRateHz, Is.EqualTo(30));
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
        string json = CreateValidJson()
            .Replace("\"simulationTickRateHz\": 30", "\"simulationTickRateHz\": 60", StringComparison.Ordinal)
            .Replace("\"simulationTickRateHz\": 30", "\"simulationTickRateHz\": 60", StringComparison.Ordinal);
        // Top-level and networking both need to stay consistent; force top-level 60 with networking 30.
        json = CreateValidJson(simulationTickRateHz: 60, networkingTickRateHz: 30);
        Assert.That(
            () => LoadClientHostConfig.ParseJson(json),
            Throws.InvalidOperationException.With.Message.Contains("30"));
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
        int simulationTickRateHz = 30,
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
          "simulationTickRateHz": {{simulationTickRateHz}},
          "fixedInputLeadTicks": 2,
          "durationSeconds": 2.0,
          "warmUpSeconds": 0.5,
          "credentialDirectory": "credentials",
          "clientReconnectRetryMilliseconds": 250,
          "maxStepsPerAdvance": 4,
          "maxAccumulatedSteps": 8,
          "connectTimeoutSeconds": 2.0,
          "readyTimeoutSeconds": 2.0,
          "replicationSchemaIds": [1],
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
            "fixedInputFramePayloadBytes": 12,
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
