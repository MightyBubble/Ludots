using System.Text.Json;

namespace Ludots.WebUI.DataPlane;

public readonly record struct WebUiEntityRef(int StableId, int Generation);

public sealed record WebUiCommandRequest(
	string Name,
	long ClientSeq,
	WebUiEntityRef[] EntityRefs,
	JsonElement Payload);

public sealed record WebUiCommandResult(bool Success, string ErrorCode, string Message)
{
	public static WebUiCommandResult Ok() => new(true, string.Empty, string.Empty);

	public static WebUiCommandResult Fail(string code, string message) => new(false, code, message);
}

public interface IWebUiEntityGenerationResolver
{
	bool IsCurrent(WebUiEntityRef entityRef);
}

public interface IWebUiCommandPermissionValidator
{
	bool CanUse(WebUiCommandRequest request, out string error);
}

public interface IWebUiCommandHandler
{
	ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default);
}

public interface IWebUiCommandDispatcher
{
	ValueTask<WebUiOutboundPacket> HandleAsync(
		WebUiInboundPacket packet,
		CancellationToken cancellationToken = default);
}

public sealed class WebUiCommandRouter : IWebUiCommandDispatcher
{
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
	private readonly Dictionary<string, IWebUiCommandHandler> _handlers = new(StringComparer.Ordinal);
	private readonly IWebUiEntityGenerationResolver _generationResolver;
	private readonly IWebUiCommandPermissionValidator _permissionValidator;

	public WebUiCommandRouter(
		IWebUiEntityGenerationResolver generationResolver,
		IWebUiCommandPermissionValidator permissionValidator)
	{
		_generationResolver = generationResolver ?? throw new ArgumentNullException(nameof(generationResolver));
		_permissionValidator = permissionValidator ?? throw new ArgumentNullException(nameof(permissionValidator));
	}

	public void Register(string commandName, IWebUiCommandHandler handler)
	{
		if (string.IsNullOrWhiteSpace(commandName))
		{
			throw new ArgumentException("Command name is required.", nameof(commandName));
		}

		_handlers[commandName.Trim()] = handler ?? throw new ArgumentNullException(nameof(handler));
	}

	public bool IsRegistered(string commandName)
	{
		return !string.IsNullOrWhiteSpace(commandName) && _handlers.ContainsKey(commandName.Trim());
	}

	public async ValueTask<WebUiOutboundPacket> HandleAsync(
		WebUiInboundPacket packet,
		CancellationToken cancellationToken = default)
	{
		WebUiCommandRequest? request;
		try
		{
			request = JsonSerializer.Deserialize<WebUiCommandRequest>(packet.Payload.Span, Options);
		}
		catch (JsonException ex)
		{
			return CreateError(packet, 0, "invalid_json", ex.Message);
		}

		if (request == null || string.IsNullOrWhiteSpace(request.Name))
		{
			return CreateError(packet, request?.ClientSeq ?? packet.ClientSeq, "invalid_command", "Command name is required.");
		}

		if (!_handlers.TryGetValue(request.Name, out IWebUiCommandHandler? handler))
		{
			return CreateError(packet, request.ClientSeq, "unknown_command", $"Unknown WebUI command '{request.Name}'.");
		}

		for (int i = 0; i < request.EntityRefs.Length; i++)
		{
			if (!_generationResolver.IsCurrent(request.EntityRefs[i]))
			{
				return CreateError(packet, request.ClientSeq, "stale_entity_ref", $"Entity ref {request.EntityRefs[i].StableId} is stale.");
			}
		}

		if (!_permissionValidator.CanUse(request, out string permissionError))
		{
			return CreateError(packet, request.ClientSeq, "permission_denied", permissionError);
		}

		WebUiCommandResult result = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
		return result.Success
			? CreateAck(packet, request.ClientSeq)
			: CreateError(packet, request.ClientSeq, result.ErrorCode, result.Message);
	}

	private static WebUiOutboundPacket CreateAck(WebUiInboundPacket packet, long clientSeq)
	{
		return WebUiDataPlaneProtocol.CreateControlResponse(
			packet.SessionId,
			packet.RequestId,
			"commandAck",
			packet.Topic,
			new { clientSeq },
			WebUiPacketKind.CommandAck) with
		{
			ClientSeq = clientSeq,
			Delivery = WebUiDeliverySemantics.ReliableOrdered
		};
	}

	private static WebUiOutboundPacket CreateError(WebUiInboundPacket packet, long clientSeq, string code, string message)
	{
		return WebUiDataPlaneProtocol.CreateControlResponse(
			packet.SessionId,
			packet.RequestId,
			"commandError",
			packet.Topic,
			new { clientSeq, code, message },
			WebUiPacketKind.CommandError) with
		{
			ClientSeq = clientSeq,
			Delivery = WebUiDeliverySemantics.ReliableOrdered
		};
	}
}
