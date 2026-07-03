using System.Text.Json;
using Ludots.WebUI.DataPlane;

namespace BrowserReactFlowShowcaseMod;

internal sealed class BrowserReactFlowShowcaseWorldTopicProducer : IWebUiTopicProducer
{
	public const string TopicName = "ludots.showcase.browserReactFlow.world";
	public const int EntityCount = 50_000;

	private const int FirstStableId = 1001;
	private const int DeltaRowsPerFrame = 2048;
	private readonly object _sync = new();
	private readonly int[] _stableIds = new int[EntityCount];
	private readonly byte[] _team = new byte[EntityCount];
	private readonly int[] _generation = new int[EntityCount];
	private readonly float[] _x = new float[EntityCount];
	private readonly float[] _y = new float[EntityCount];
	private readonly ushort[] _hp = new ushort[EntityCount];
	private readonly byte[] _state = new byte[EntityCount];
	private readonly byte[] _fullDeltaPayload =
		new byte[WebUiEntityColumnarPacket.GetSoaFullDeltaByteCount(EntityCount)];
	private readonly int[] _changedIndices = new int[DeltaRowsPerFrame + 1];
	private readonly WebUiEntityColumnarRow[] _changedRows = new WebUiEntityColumnarRow[DeltaRowsPerFrame + 1];
	private readonly byte[] _deltaPayload =
		new byte[WebUiEntityColumnarPacket.GetIndexedDeltaByteCount(DeltaRowsPerFrame + 1)];
	private int _tick;
	private int _commandCount;
	private int _selectedStableId = FirstStableId;
	private int _moveTargetX = 256;
	private int _moveTargetY = 128;
	private int _cursor;

	public BrowserReactFlowShowcaseWorldTopicProducer()
	{
		InitializeWorld();
	}

	public string Topic => TopicName;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		lock (_sync)
		{
			byte[] payload = new byte[WebUiEntityColumnarPacket.GetSoaSnapshotByteCount(EntityCount)];
			if (!WebUiEntityColumnarPacket.TryEncodeSoaSnapshot(
				WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
				sequence: _tick,
				tick: _tick,
				_stableIds,
				_generation,
				_x,
				_y,
				_hp,
				_team,
				_state,
				payload,
				out int bytesWritten))
			{
				throw new InvalidOperationException("Snapshot payload buffer is too small.");
			}

			packet = CreatePacket(
				context.SessionId,
				WebUiPacketKind.Snapshot,
				payload.AsMemory(0, bytesWritten),
				context.RequestId,
				clientSeq: _tick);
			return true;
		}
	}

	public WebUiOutboundPacket CreateFullUpdatePacket(string sessionId)
	{
		lock (_sync)
		{
			_tick++;
			UpdateAllDynamicColumns();
			if (!WebUiEntityColumnarPacket.TryEncodeSoaFullDelta(
				WebUiColumnarPacketSchemaRegistry.EntityCollectionSchemaId,
				sequence: _tick,
				tick: _tick,
				_generation,
				_x,
				_y,
				_hp,
				_state,
				_fullDeltaPayload,
				out int bytesWritten))
			{
				throw new InvalidOperationException("Preallocated WebUI full-frame payload buffer is too small.");
			}

			return CreatePacket(
				sessionId,
				WebUiPacketKind.Delta,
				_fullDeltaPayload.AsMemory(0, bytesWritten),
				requestId: 0,
				clientSeq: _tick);
		}
	}

	public WebUiOutboundPacket CreateIndexedDeltaPacket(string sessionId)
	{
		lock (_sync)
		{
			_tick++;
			int changedCount = UpdateChangedWindow();
			if (!WebUiEntityColumnarPacket.TryEncodeIndexedDelta(
				WebUiEntityColumnarPacket.CurrentSchemaId,
				_changedIndices.AsSpan(0, changedCount),
				_changedRows.AsSpan(0, changedCount),
				_deltaPayload,
				out int bytesWritten))
			{
				throw new InvalidOperationException("Preallocated WebUI indexed delta payload buffer is too small.");
			}

			return CreatePacket(
				sessionId,
				WebUiPacketKind.Delta,
				_deltaPayload.AsMemory(0, bytesWritten),
				requestId: 0,
				clientSeq: _tick);
		}
	}

	public void ApplyCommand(WebUiCommandRequest request)
	{
		lock (_sync)
		{
			_commandCount++;
			if (request.Payload.TryGetProperty("stableId", out JsonElement stableIdElement) &&
				stableIdElement.TryGetInt32(out int stableId))
			{
				_selectedStableId = Math.Clamp(stableId, FirstStableId, FirstStableId + EntityCount - 1);
			}
			else if (request.Payload.TryGetProperty("nodeId", out JsonElement nodeIdElement) &&
				nodeIdElement.ValueKind == JsonValueKind.String &&
				TryParseStableId(nodeIdElement.GetString(), out stableId))
			{
				_selectedStableId = Math.Clamp(stableId, FirstStableId, FirstStableId + EntityCount - 1);
			}

			if (request.Name == "issueMoveOrder" &&
				request.Payload.TryGetProperty("target", out JsonElement target) &&
				target.TryGetProperty("x", out JsonElement x) &&
				target.TryGetProperty("y", out JsonElement y))
			{
				_moveTargetX = x.GetInt32();
				_moveTargetY = y.GetInt32();
			}
		}
	}

	private void InitializeWorld()
	{
		for (int index = 0; index < EntityCount; index++)
		{
			_stableIds[index] = FirstStableId + index;
			_team[index] = (byte)(index & 7);
			_generation[index] = 0;
			_x[index] = index & 511;
			_y[index] = index >> 9;
			_hp[index] = (ushort)(72 + (index % 23));
			_state[index] = (byte)(index % 5);
		}
	}

	private int UpdateChangedWindow()
	{
		int changedCount = 0;
		int start = _cursor;
		_cursor = (_cursor + DeltaRowsPerFrame) % EntityCount;
		float targetX = _moveTargetX * 0.0025f;
		float targetY = _moveTargetY * 0.0025f;
		for (int i = 0; i < DeltaRowsPerFrame; i++)
		{
			int index = (start + i) % EntityCount;
			UpdateDynamicColumns(index, targetX, targetY);
			_changedIndices[changedCount] = index;
			_changedRows[changedCount] = CreateRow(index);
			changedCount++;
		}

		int selectedIndex = _selectedStableId - FirstStableId;
		if ((uint)selectedIndex < EntityCount)
		{
			_generation[selectedIndex] = _tick;
			_hp[selectedIndex] = 100;
			_state[selectedIndex] = (byte)(9 + (_commandCount & 1));
			_changedIndices[changedCount] = selectedIndex;
			_changedRows[changedCount] = CreateRow(selectedIndex);
			changedCount++;
		}

		return changedCount;
	}

	private void UpdateAllDynamicColumns()
	{
		int tick = _tick;
		float targetX = _moveTargetX * 0.0025f;
		float targetY = _moveTargetY * 0.0025f;
		Span<int> generations = _generation;
		generations.Fill(tick);

		int commandOffset = _commandCount & 3;
		Span<float> x = _x;
		Span<float> y = _y;
		Span<ushort> hp = _hp;
		Span<byte> state = _state;
		for (int index = 0; index < EntityCount; index++)
		{
			int phase = (tick + index) & 255;
			float wave = (phase - 128) * (1f / 128f);
			x[index] = (index & 511) + wave + targetX;
			y[index] = (index >> 9) - wave + targetY;
			hp[index] = (ushort)Math.Clamp(74 + ((index + tick) % 27) - commandOffset, 1, 100);
			state[index] = (byte)((index + tick + _commandCount) & 7);
		}

		int selectedIndex = _selectedStableId - FirstStableId;
		if ((uint)selectedIndex < EntityCount)
		{
			hp[selectedIndex] = 100;
			state[selectedIndex] = (byte)(9 + (_commandCount & 1));
		}
	}

	private void UpdateDynamicColumns(int index, float targetX, float targetY)
	{
		_generation[index] = _tick;
		int phase = (_tick + index) & 255;
		float wave = (phase - 128) * (1f / 128f);
		_x[index] = (index & 511) + wave + targetX;
		_y[index] = (index >> 9) - wave + targetY;
		_hp[index] = (ushort)Math.Clamp(74 + ((index + _tick) % 27) - (_commandCount & 3), 1, 100);
		_state[index] = (byte)((index + _tick + _commandCount) & 7);
	}

	private WebUiEntityColumnarRow CreateRow(int index)
	{
		return new WebUiEntityColumnarRow(
			_stableIds[index],
			_generation[index],
			_x[index],
			_y[index],
			_hp[index],
			_team[index],
			_state[index]);
	}

	private static WebUiOutboundPacket CreatePacket(
		string sessionId,
		WebUiPacketKind kind,
		ReadOnlyMemory<byte> payload,
		long requestId,
		long clientSeq)
	{
		return new WebUiOutboundPacket(
			sessionId,
			TopicName,
			kind,
			WebUiDeliverySemantics.LatestWins,
			payload,
			WebUiDataPlaneProtocol.BinaryContentType,
			requestId,
			clientSeq);
	}

	private static bool TryParseStableId(string? value, out int stableId)
	{
		stableId = 0;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		ReadOnlySpan<char> span = value.AsSpan().Trim();
		int dotIndex = span.LastIndexOf('.');
		if (dotIndex >= 0 && dotIndex < span.Length - 1)
		{
			span = span[(dotIndex + 1)..];
		}

		return int.TryParse(span, out stableId);
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
