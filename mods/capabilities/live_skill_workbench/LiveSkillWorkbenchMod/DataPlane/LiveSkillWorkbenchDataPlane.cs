using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiveSkillWorkbenchMod.Contracts;
using LiveSkillWorkbenchMod.Runtime;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.WebUI.DataPlane;

namespace LiveSkillWorkbenchMod.DataPlane;

public sealed class LiveSkillWorkbenchTopicProducer : IWebUiTopicProducer
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly LiveSkillWorkbenchRuntime _runtime;
	private readonly object _publishSync = new();
	private ulong? _lastPublishedStateVersion;

	public LiveSkillWorkbenchTopicProducer(LiveSkillWorkbenchRuntime runtime)
	{
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	public string Topic => LiveSkillWorkbenchIds.Topic;

	/// <summary>
	/// True when workbench state version advanced since the last published/subscribed snapshot.
	/// Presentation pump uses this to skip idle-frame serialization.
	/// </summary>
	public bool HasUnpublishedStateChange
	{
		get
		{
			ulong version = _runtime.StateVersion;
			lock (_publishSync)
			{
				return !_lastPublishedStateVersion.HasValue || _lastPublishedStateVersion.Value != version;
			}
		}
	}

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		// Always emit when called: subscribe needs the initial snapshot; the pump gates idle frames
		// via HasUnpublishedStateChange before invoking PublishTopicsAsync.
		LiveSkillWorkbenchSessionSnapshotDto snapshot = _runtime.BuildSnapshot("connected");
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			"application/json",
			context.RequestId);

		lock (_publishSync)
		{
			_lastPublishedStateVersion = snapshot.StateVersion;
		}

		return true;
	}
}

public sealed class LiveSkillWorkbenchCommandHandler : IWebUiCommandHandler
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private readonly LiveSkillWorkbenchRuntime _runtime;

	public LiveSkillWorkbenchCommandHandler(LiveSkillWorkbenchRuntime runtime)
	{
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
	}

	public ValueTask<WebUiCommandResult> HandleAsync(WebUiCommandRequest request, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(Handle(request));
	}

	public WebUiCommandResult Handle(WebUiCommandRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		return request.Name switch
		{
			LiveSkillWorkbenchIds.StageEditCommand => HandleStageEdit(request.Payload),
			LiveSkillWorkbenchIds.DiscardEditsCommand => HandleDiscard(),
			LiveSkillWorkbenchIds.SelectCatalogItemCommand => HandleSelect(request.Payload),
			LiveSkillWorkbenchIds.PrecheckCommand => HandlePrecheck(),
			LiveSkillWorkbenchIds.ApplyNextCastCommand => HandleApply(),
			LiveSkillWorkbenchIds.ApplyImmediateAttributeCommand => HandleImmediate(request.Payload),
			LiveSkillWorkbenchIds.GenerateAiDraftCommand => HandleGenerateAi(request.Payload),
			LiveSkillWorkbenchIds.BindAiDraftCommand => HandleBindAi(request.Payload),
			LiveSkillWorkbenchIds.PreviewSaveCommand => HandlePreviewSave(),
			LiveSkillWorkbenchIds.SaveToModCommand => HandleSave(),
			LiveSkillWorkbenchIds.RefreshEffectChainCommand => HandleRefreshChain(),
			_ => WebUiCommandResult.Fail("unknown_command", $"Unknown Live Skill Workbench command '{request.Name}'.")
		};
	}

	private WebUiCommandResult HandleStageEdit(JsonElement payload)
	{
		LiveSkillWorkbenchStageEditRequestDto? edit;
		try
		{
			edit = JsonSerializer.Deserialize<LiveSkillWorkbenchStageEditRequestDto>(payload.GetRawText(), JsonOptions);
		}
		catch (JsonException ex)
		{
			return WebUiCommandResult.Fail("invalid_payload", ex.Message);
		}

		if (edit == null)
		{
			return WebUiCommandResult.Fail("invalid_payload", "stageEdit payload is required.");
		}

		LiveEditStageResult result = _runtime.StageEdit(edit);
		if (!result.Succeeded)
		{
			string message = result.Diagnostics.Count > 0
				? result.Diagnostics[0].Message
				: "Stage edit failed.";
			string code = result.Diagnostics.Count > 0 ? result.Diagnostics[0].Code : "stage_failed";
			return WebUiCommandResult.Fail(code, message);
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleDiscard()
	{
		_runtime.DiscardEdits();
		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleSelect(JsonElement payload)
	{
		string? catalogId = null;
		if (payload.ValueKind == JsonValueKind.Object &&
			payload.TryGetProperty("catalogId", out JsonElement catalogIdElement) &&
			catalogIdElement.ValueKind == JsonValueKind.String)
		{
			catalogId = catalogIdElement.GetString();
		}

		if (string.IsNullOrWhiteSpace(catalogId))
		{
			return WebUiCommandResult.Fail("invalid_payload", "selectCatalogItem requires payload.catalogId.");
		}

		if (!_runtime.SelectCatalogItem(catalogId))
		{
			return WebUiCommandResult.Fail("catalog_not_found", $"Catalog item '{catalogId}' was not found.");
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandlePrecheck()
	{
		if (!_runtime.TryPrecheck(out LiveApplyClassificationReport? report, out LiveSkillWorkbenchDiagnosticDto? error))
		{
			LiveSkillWorkbenchDiagnosticDto diagnostic = error ?? _runtime.CreatePrecheckNotSupportedDiagnostic();
			return WebUiCommandResult.Fail(diagnostic.Code, diagnostic.Message);
		}

		_ = report;
		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleApply()
	{
		if (!_runtime.TryApplyNextCast(out LiveApplyCommitResult commit, out LiveSkillWorkbenchDiagnosticDto? error))
		{
			LiveSkillWorkbenchDiagnosticDto diagnostic = error ?? _runtime.CreateApplyNotSupportedDiagnostic();
			return WebUiCommandResult.Fail(diagnostic.Code, diagnostic.Message);
		}

		_ = commit;
		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleImmediate(JsonElement payload)
	{
		string attributeName = payload.TryGetProperty("attributeName", out JsonElement a) ? a.GetString() ?? "" : "";
		string mutationText = payload.TryGetProperty("mutation", out JsonElement m) ? m.GetString() ?? "Set" : "Set";
		double value = payload.TryGetProperty("numericValue", out JsonElement v) && v.TryGetDouble(out double d) ? d : double.NaN;
		if (!Enum.TryParse(mutationText, ignoreCase: true, out ActorAttributeMutationKind mutation))
		{
			mutation = ActorAttributeMutationKind.Set;
		}

		if (!_runtime.TryApplyImmediateAttribute(attributeName, mutation, value, out LiveSkillWorkbenchDiagnosticDto? error))
		{
			return WebUiCommandResult.Fail(error?.Code ?? "immediate_failed", error?.Message ?? "Immediate attribute failed.");
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleGenerateAi(JsonElement payload)
	{
		string prompt = payload.TryGetProperty("prompt", out JsonElement p) ? p.GetString() ?? "" : "";
		if (!_runtime.TryGenerateAiDraft(prompt, out _, out LiveSkillWorkbenchDiagnosticDto? error))
		{
			return WebUiCommandResult.Fail(error?.Code ?? "ai_failed", error?.Message ?? "AI draft failed.");
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleBindAi(JsonElement payload)
	{
		int actorId = payload.TryGetProperty("actorEntityId", out JsonElement a) && a.TryGetInt32(out int id) ? id : 0;
		if (!_runtime.TryBindLastAiDraft(actorId, out LiveSkillWorkbenchDiagnosticDto? error))
		{
			return WebUiCommandResult.Fail(error?.Code ?? "bind_failed", error?.Message ?? "Bind failed.");
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandlePreviewSave()
	{
		if (!_runtime.TryPreviewSave(out _, out LiveSkillWorkbenchDiagnosticDto? error))
		{
			return WebUiCommandResult.Fail(error?.Code ?? "preview_failed", error?.Message ?? "Preview failed.");
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleSave()
	{
		if (!_runtime.TrySaveToMod(out LiveSkillWorkbenchDiagnosticDto? error))
		{
			return WebUiCommandResult.Fail(error?.Code ?? "save_failed", error?.Message ?? "Save failed.");
		}

		return WebUiCommandResult.Ok();
	}

	private WebUiCommandResult HandleRefreshChain()
	{
		_runtime.PublishTracerToEffectChain();
		return WebUiCommandResult.Ok();
	}
}

internal sealed class LiveSkillWorkbenchGenerationResolver : IWebUiEntityGenerationResolver
{
	public bool IsCurrent(WebUiEntityRef entityRef) => entityRef.Generation >= 0;
}

internal sealed class LiveSkillWorkbenchPermissionValidator : IWebUiCommandPermissionValidator
{
	public bool CanUse(WebUiCommandRequest request, out string error)
	{
		error = string.Empty;
		return request.Name is
			LiveSkillWorkbenchIds.StageEditCommand or
			LiveSkillWorkbenchIds.DiscardEditsCommand or
			LiveSkillWorkbenchIds.SelectCatalogItemCommand or
			LiveSkillWorkbenchIds.PrecheckCommand or
			LiveSkillWorkbenchIds.ApplyNextCastCommand or
			LiveSkillWorkbenchIds.ApplyImmediateAttributeCommand or
			LiveSkillWorkbenchIds.GenerateAiDraftCommand or
			LiveSkillWorkbenchIds.BindAiDraftCommand or
			LiveSkillWorkbenchIds.PreviewSaveCommand or
			LiveSkillWorkbenchIds.SaveToModCommand or
			LiveSkillWorkbenchIds.RefreshEffectChainCommand;
	}
}
