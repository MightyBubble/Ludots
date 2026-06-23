using System.Text.Json;
using Ludots.WebUI.DataPlane;

namespace BrowserReactFlowShowcaseMod;

internal sealed class BrowserReactFlowShowcaseWorldTopicProducer : IWebUiTopicProducer
{
	public const string TopicName = "ludots.showcase.browserReactFlow.world";
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly BrowserReactFlowShowcaseEntity[] _entities;
	private int _tick;
	private int _commandCount;
	private int _coalesced;
	private string _selectedEntityId = "unit.scout";
	private string _lastReason = "snapshot";

	public BrowserReactFlowShowcaseWorldTopicProducer()
	{
		_entities =
		[
			new("unit.scout", "Scout", 94, 4, 2, "Hold", 0.84f),
			new("unit.guard", "Guard", 88, 7, 6, "Patrol", 0.72f),
			new("unit.engineer", "Engineer", 76, 2, 8, "Survey", 0.63f),
			new("unit.siege", "Siege", 67, 11, 3, "Deploy", 0.91f),
		];
	}

	public string Topic => TopicName;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		packet = CreateJsonPacket(context.SessionId, WebUiPacketKind.Snapshot, context.RequestId, "snapshot");
		return true;
	}

	public WebUiOutboundPacket CreateDeltaPacket(string sessionId)
	{
		_tick++;
		_coalesced += 3;
		for (int i = 0; i < _entities.Length; i++)
		{
			BrowserReactFlowShowcaseEntity entity = _entities[i];
			float pulse = MathF.Sin((_tick + i) * 0.31f) * 0.02f;
			_entities[i] = entity with
			{
				Signal = Math.Clamp(entity.Signal + pulse, 0.4f, 0.98f),
				Position = new BrowserReactFlowShowcasePoint(
					entity.Position.X + MathF.Sin((_tick + i) * 0.07f) * 0.03f,
					entity.Position.Y + MathF.Cos((_tick + i) * 0.06f) * 0.03f)
			};
		}

		return CreateJsonPacket(sessionId, WebUiPacketKind.Delta, 0, _lastReason);
	}

	public WebUiOutboundPacket CreateBinarySnapshotPacket(string sessionId)
	{
		var rows = new WebUiEntityColumnarRow[4096];
		for (int i = 0; i < rows.Length; i++)
		{
			BrowserReactFlowShowcaseEntity source = _entities[i % _entities.Length];
			rows[i] = new WebUiEntityColumnarRow(
				StableId: 100000 + i,
				Generation: _tick + 1,
				X: source.Position.X + (i % 256),
				Y: source.Position.Y + (i / 256),
				Hp: (ushort)Math.Clamp(source.Hp - (i % 17), 1, 100),
				Team: (byte)(i % 4),
				State: (byte)(i % 8));
		}

		byte[] payload = WebUiEntityColumnarPacket.EncodeSnapshot(WebUiEntityColumnarPacket.CurrentSchemaId, rows);
		return new WebUiOutboundPacket(
			sessionId,
			TopicName,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			WebUiDataPlaneProtocol.BinaryContentType);
	}

	public void ApplyCommand(WebUiCommandRequest request)
	{
		_commandCount++;
		_lastReason = request.Name;
		if (request.Payload.TryGetProperty("nodeId", out JsonElement nodeIdElement) &&
			nodeIdElement.ValueKind == JsonValueKind.String)
		{
			_selectedEntityId = nodeIdElement.GetString() ?? _selectedEntityId;
		}

		if (request.Name == "issueMoveOrder" &&
			request.Payload.TryGetProperty("target", out JsonElement target) &&
			target.TryGetProperty("x", out JsonElement x) &&
			target.TryGetProperty("y", out JsonElement y))
		{
			_entities[0] = _entities[0] with
			{
				Order = $"Move {x.GetInt32()},{y.GetInt32()}",
				Destination = new BrowserReactFlowShowcasePoint(x.GetInt32(), y.GetInt32())
			};
		}
	}

	private WebUiOutboundPacket CreateJsonPacket(string sessionId, WebUiPacketKind kind, long requestId, string reason)
	{
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
		{
			tick = _tick,
			selectedEntityId = _selectedEntityId,
			entityCount = 50000,
			entities = _entities,
			entityPatches = _entities,
			diagnostics = new
			{
				reason,
				hostFps = 60,
				frameTimeMs = 16.6,
				commandCount = _commandCount,
				coalescedPackets = _coalesced,
				droppedPackets = 0,
				binarySchema = WebUiEntityColumnarPacket.CurrentSchemaId
			}
		}, JsonOptions);
		return new WebUiOutboundPacket(
			sessionId,
			TopicName,
			kind,
			WebUiDeliverySemantics.LatestWins,
			payload,
			"application/json",
			requestId);
	}
}

internal sealed class BrowserReactFlowShowcaseCommandHandler : IWebUiCommandHandler
{
	private readonly BrowserReactFlowShowcaseWorldTopicProducer _producer;

	public BrowserReactFlowShowcaseCommandHandler(BrowserReactFlowShowcaseWorldTopicProducer producer)
	{
		_producer = producer;
	}

	public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
	{
		_producer.ApplyCommand(request);
		return ValueTask.FromResult(WebUiCommandResult.Ok());
	}
}

internal sealed class BrowserReactFlowShowcaseGenerationResolver : IWebUiEntityGenerationResolver
{
	public bool IsCurrent(WebUiEntityRef entityRef) => entityRef.Generation >= 0;
}

internal sealed class BrowserReactFlowShowcasePermissionValidator : IWebUiCommandPermissionValidator
{
	public bool CanUse(WebUiCommandRequest request, out string error)
	{
		error = string.Empty;
		return request.Name is "inspectEntity" or "issueMoveOrder";
	}
}

internal readonly record struct BrowserReactFlowShowcasePoint(float X, float Y);

internal sealed record BrowserReactFlowShowcaseEntity(
	string Id,
	string Label,
	int Hp,
	float X,
	float Y,
	string Order,
	float Signal)
{
	public BrowserReactFlowShowcasePoint Position { get; init; } = new(X, Y);
	public BrowserReactFlowShowcasePoint Destination { get; init; } = new(X, Y);
}
