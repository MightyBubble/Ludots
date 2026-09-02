using System.Collections.Concurrent;
using System.Text.Json;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;
using SangoWebUiSpikeMod;

namespace Ludots.Tests.SangoWebUi;

[TestFixture]
public sealed class SangoMapStatsDataPlaneTests
{
    private const string SessionId = "sango-spike-test";

    [Test]
    public void TryCreateSnapshot_SubscriptionRequest_PublishesMapStatsWithEightSampleCities()
    {
        var producer = new SangoMapStatsTopicProducer(new RecordingModContext());
        var context = new WebUiTopicContext(SessionId, SangoMapStatsTopicProducer.TopicName, RequestId: 7, Parameters: default);

        bool created = producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet);

        Assert.That(created, Is.True, "The Sango map stats topic must always produce a snapshot.");
        Assert.That(packet.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
        Assert.That(packet.Topic, Is.EqualTo(SangoMapStatsTopicProducer.TopicName));
        using JsonDocument document = ParsePacket(packet);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("mapName").GetString(), Is.EqualTo("群雄割据·中原"));
        Assert.That(root.GetProperty("width").GetInt32(), Is.EqualTo(120));
        Assert.That(root.GetProperty("height").GetInt32(), Is.EqualTo(90));
        JsonElement histogram = root.GetProperty("terrainHistogram");
        Assert.That(histogram.GetProperty("平原").GetInt32(), Is.GreaterThan(0));
        Assert.That(histogram.EnumerateObject().Count(), Is.GreaterThanOrEqualTo(5), "The sample terrain histogram must cover several terrain kinds.");
        JsonElement cities = root.GetProperty("cities");
        Assert.That(cities.GetArrayLength(), Is.EqualTo(8), "The spike snapshot must carry exactly eight hardcoded Sango cities.");
        Assert.That(cities[0].GetProperty("id").GetString(), Is.EqualTo("city-luoyang"));
        Assert.That(cities[0].GetProperty("name").GetString(), Is.EqualTo("洛阳"));
    }

    [Test]
    public async Task RuntimeSubscribe_SendsTopicSnapshotThroughTransport()
    {
        var producer = new SangoMapStatsTopicProducer(new RecordingModContext());
        await using var runtime = new WebUiDataPlaneRuntime();
        var transport = new FakeWebUiDataTransport();
        runtime.RegisterTopic(producer);
        runtime.AttachSession(SessionId, transport);

        transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, 7, "subscribe", SangoMapStatsTopicProducer.TopicName, new { }));

        WebUiOutboundPacket sent = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(sent.Kind, Is.EqualTo(WebUiPacketKind.Snapshot));
        Assert.That(sent.SessionId, Is.EqualTo(SessionId));
        Assert.That(sent.Topic, Is.EqualTo(SangoMapStatsTopicProducer.TopicName));
        Assert.That(sent.RequestId, Is.EqualTo(7));
        using JsonDocument document = ParsePacket(sent);
        Assert.That(document.RootElement.GetProperty("cities").GetArrayLength(), Is.EqualTo(8));
    }

    [Test]
    public async Task SelectCityCommand_SpikeWiringRoundTrip_AcksAndPublishesSelectedCity()
    {
        var modContext = new RecordingModContext();
        var producer = new SangoMapStatsTopicProducer(modContext);
        var router = new WebUiCommandRouter(
            new SangoWebUiSpikeGenerationResolver(),
            new SangoWebUiSpikePermissionValidator());
        router.Register("sango.selectCity", new SangoWebUiSpikeCommandHandler(producer));
        await using var dispatcher = new WebUiQueuedCommandDispatcher(router);
        await using var runtime = new WebUiDataPlaneRuntime(dispatcher);
        var transport = new FakeWebUiDataTransport();
        runtime.RegisterTopic(producer);
        runtime.AttachSession(SessionId, transport);
        var pump = new WebUiDataPlaneTickPump(runtime, dispatcher);
        pump.TrackTopic(SangoMapStatsTopicProducer.TopicName);

        transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, 1, "subscribe", SangoMapStatsTopicProducer.TopicName, new { }));
        _ = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);

        transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, 2, "command", "orders", new
            {
                name = "sango.selectCity",
                clientSeq = 5,
                entityRefs = Array.Empty<object>(),
                payload = new { cityId = "city-luoyang" }
            }));
        await pump.FlushCommandsAsync(TestContext.CurrentContext.CancellationToken);

        WebUiOutboundPacket ack = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(ack.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));
        Assert.That(ack.ClientSeq, Is.EqualTo(5));

        await pump.PublishTopicsAsync(TestContext.CurrentContext.CancellationToken);
        WebUiOutboundPacket delta = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(delta.Kind, Is.EqualTo(WebUiPacketKind.Delta));
        Assert.That(delta.Topic, Is.EqualTo(SangoMapStatsTopicProducer.TopicName));
        using JsonDocument document = ParsePacket(delta);
        JsonElement luoyang = document.RootElement
            .GetProperty("cities")
            .EnumerateArray()
            .Single(city => city.GetProperty("id").GetString() == "city-luoyang");
        Assert.That(luoyang.GetProperty("selected").GetBoolean(), Is.True, "The next topic publish must carry the selected city.");
        Assert.That(document.RootElement.GetProperty("diagnostics").GetProperty("selectedCityId").GetString(), Is.EqualTo("city-luoyang"));

        Assert.That(
            modContext.Messages.Any(message => message.Contains("city-luoyang", StringComparison.Ordinal)),
            Is.True,
            "The selectCity handler must log the received city id through the ModContext.");
    }

    [Test]
    public async Task SelectCityCommand_UnknownCity_ReturnsTypedCommandError()
    {
        var producer = new SangoMapStatsTopicProducer(new RecordingModContext());
        var router = new WebUiCommandRouter(
            new SangoWebUiSpikeGenerationResolver(),
            new SangoWebUiSpikePermissionValidator());
        router.Register("sango.selectCity", new SangoWebUiSpikeCommandHandler(producer));
        await using var dispatcher = new WebUiQueuedCommandDispatcher(router);
        await using var runtime = new WebUiDataPlaneRuntime(dispatcher);
        var transport = new FakeWebUiDataTransport();
        runtime.AttachSession(SessionId, transport);

        transport.Receive(WebUiDataPlaneProtocol.CreateControlPacket(
            SessionId, 3, "command", "orders", new
            {
                name = "sango.selectCity",
                clientSeq = 9,
                entityRefs = Array.Empty<object>(),
                payload = new { cityId = "city-nowhere" }
            }));
        await dispatcher.FlushAsync(TestContext.CurrentContext.CancellationToken);

        WebUiOutboundPacket error = await transport.WaitForSentAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(error.Kind, Is.EqualTo(WebUiPacketKind.CommandError));
        Assert.That(
            WebUiDataPlaneProtocol.TryParseControlEnvelope(error.Payload.Span, out WebUiControlEnvelope envelope, out _),
            Is.True);
        Assert.That(envelope.Payload.GetProperty("code").GetString(), Is.EqualTo("city_not_found"));
    }

    private static JsonDocument ParsePacket(WebUiOutboundPacket packet)
    {
        return JsonDocument.Parse(packet.Payload);
    }

    private sealed class RecordingModContext : IModContext
    {
        public List<string> Messages { get; } = [];

        public string ModId => "SangoWebUiSpikeMod.Tests";
        public IVirtualFileSystem VFS => null!;
        public FunctionRegistry FunctionRegistry => null!;
        public SystemFactoryRegistry SystemFactoryRegistry => null!;
        public TriggerDecoratorRegistry TriggerDecorators => null!;
        public IModExtensionRegistration Extensions => null!;
        public LogChannel LogChannel => default;

        public void OnEvent(EventKey eventKey, Func<ScriptContext, Task> handler)
        {
        }

        public void Log(string message)
        {
            Messages.Add(message);
        }

        public void Log(LogLevel level, string message)
        {
            Messages.Add(message);
        }

        public Stream GetResource(string uri)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeWebUiDataTransport : IWebUiDataTransport
    {
        private readonly ConcurrentQueue<WebUiOutboundPacket> _sent = new();
        private readonly SemaphoreSlim _sentSignal = new(0);

        public WebUiTransportCapabilities Capabilities { get; } = WebUiTransportCapabilities.StringBridge();

        public event EventHandler<WebUiInboundPacket>? PacketReceived;

        public IReadOnlyCollection<WebUiOutboundPacket> Sent => _sent.ToArray();

        public ValueTask SendAsync(WebUiOutboundPacket packet, CancellationToken cancellationToken = default)
        {
            _sent.Enqueue(packet);
            _sentSignal.Release();
            return ValueTask.CompletedTask;
        }

        public void Receive(WebUiInboundPacket packet)
        {
            PacketReceived?.Invoke(this, packet);
        }

        public async Task<WebUiOutboundPacket> WaitForSentAsync(CancellationToken cancellationToken = default)
        {
            await _sentSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (_sent.TryDequeue(out WebUiOutboundPacket? packet))
            {
                return packet;
            }

            throw new InvalidOperationException("Send signal was raised without a packet.");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
