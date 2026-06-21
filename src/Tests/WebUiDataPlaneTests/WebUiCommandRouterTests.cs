using System.Text.Json;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class WebUiCommandRouterTests
{
	[Test]
	public async Task ValidIssueMoveOrder_CallsOrderSink_AndReturnsAckWithClientSeq()
	{
		var handler = new RecordingCommandHandler();
		WebUiCommandRouter router = CreateRouter(generations: new Dictionary<int, int> { [10] = 2 });
		router.Register("issueMoveOrder", handler);
		WebUiInboundPacket packet = CommandPacket(new WebUiCommandRequest(
			"issueMoveOrder",
			77,
			new[] { new WebUiEntityRef(10, 2) },
			JsonSerializer.SerializeToElement(new { target = new { x = 12, y = 24 } })));

		WebUiOutboundPacket response = await router.HandleAsync(packet, TestContext.CurrentContext.CancellationToken);

		Assert.That(handler.Handled, Has.Count.EqualTo(1));
		Assert.That(handler.Handled[0].Payload.GetProperty("target").GetProperty("x").GetInt32(), Is.EqualTo(12));
		Assert.That(response.Kind, Is.EqualTo(WebUiPacketKind.CommandAck));
		Assert.That(response.Delivery, Is.EqualTo(WebUiDeliverySemantics.ReliableOrdered));
		Assert.That(response.ClientSeq, Is.EqualTo(77));
	}

	[Test]
	public async Task StaleEntityRef_IsRejectedBeforeCommandSink()
	{
		var handler = new RecordingCommandHandler();
		WebUiCommandRouter router = CreateRouter(generations: new Dictionary<int, int> { [10] = 3 });
		router.Register("issueMoveOrder", handler);

		WebUiOutboundPacket response = await router.HandleAsync(CommandPacket(new WebUiCommandRequest(
			"issueMoveOrder",
			11,
			new[] { new WebUiEntityRef(10, 2) },
			JsonSerializer.SerializeToElement(new { }))), TestContext.CurrentContext.CancellationToken);

		Assert.That(handler.Handled, Is.Empty);
		AssertError(response, "stale_entity_ref");
	}

	[Test]
	public async Task PermissionFailure_IsRejectedBeforeCommandSink()
	{
		var handler = new RecordingCommandHandler();
		var validator = new FakePermissionValidator(canUse: false, error: "fog or ownership denied");
		var router = new WebUiCommandRouter(new DictionaryGenerationResolver(new Dictionary<int, int> { [10] = 2 }), validator);
		router.Register("issueMoveOrder", handler);

		WebUiOutboundPacket response = await router.HandleAsync(CommandPacket(new WebUiCommandRequest(
			"issueMoveOrder",
			11,
			new[] { new WebUiEntityRef(10, 2) },
			JsonSerializer.SerializeToElement(new { }))), TestContext.CurrentContext.CancellationToken);

		Assert.That(handler.Handled, Is.Empty);
		AssertError(response, "permission_denied");
	}

	[Test]
	public async Task UnknownCommand_ReturnsTypedError_WithoutThrowing()
	{
		WebUiCommandRouter router = CreateRouter(generations: new Dictionary<int, int>());

		WebUiOutboundPacket response = await router.HandleAsync(CommandPacket(new WebUiCommandRequest(
			"missing",
			7,
			Array.Empty<WebUiEntityRef>(),
			JsonSerializer.SerializeToElement(new { }))), TestContext.CurrentContext.CancellationToken);

		AssertError(response, "unknown_command");
	}

	private static WebUiCommandRouter CreateRouter(Dictionary<int, int> generations)
	{
		return new WebUiCommandRouter(
			new DictionaryGenerationResolver(generations),
			new FakePermissionValidator(canUse: true, string.Empty));
	}

	private static WebUiInboundPacket CommandPacket(WebUiCommandRequest request)
	{
		return new WebUiInboundPacket(
			"session-a",
			"orders",
			WebUiPacketKind.Command,
			WebUiDeliverySemantics.ReliableOrdered,
			JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
			"application/json",
			RequestId: 12,
			ClientSeq: request.ClientSeq);
	}

	private static void AssertError(WebUiOutboundPacket response, string code)
	{
		Assert.That(response.Kind, Is.EqualTo(WebUiPacketKind.CommandError));
		Assert.That(response.Delivery, Is.EqualTo(WebUiDeliverySemantics.ReliableOrdered));
		Assert.That(WebUiDataPlaneProtocol.TryParseControlEnvelope(response.Payload.Span, out WebUiControlEnvelope envelope, out _), Is.True);
		Assert.That(envelope.Payload.GetProperty("code").GetString(), Is.EqualTo(code));
	}

	private sealed class DictionaryGenerationResolver : IWebUiEntityGenerationResolver
	{
		private readonly IReadOnlyDictionary<int, int> _generations;

		public DictionaryGenerationResolver(IReadOnlyDictionary<int, int> generations)
		{
			_generations = generations;
		}

		public bool IsCurrent(WebUiEntityRef entityRef)
		{
			return WebUiEntityColumnarPacket.IsCurrentGeneration(entityRef, _generations);
		}
	}

	private sealed class FakePermissionValidator : IWebUiCommandPermissionValidator
	{
		private readonly bool _canUse;
		private readonly string _error;

		public FakePermissionValidator(bool canUse, string error)
		{
			_canUse = canUse;
			_error = error;
		}

		public bool CanUse(WebUiCommandRequest request, out string error)
		{
			error = _error;
			return _canUse;
		}
	}

	private sealed class RecordingCommandHandler : IWebUiCommandHandler
	{
		public List<WebUiCommandRequest> Handled { get; } = new();

		public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
		{
			Handled.Add(request);
			return ValueTask.FromResult(WebUiCommandResult.Ok());
		}
	}
}
