using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// DataPlane topic producer for tooltip panels. Projects EntityInsight / ability presentation
/// tokens into structured rich-text blocks — never HTML, never Unknown/Ability#N fallbacks.
/// </summary>
public sealed class WebUiTooltipTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-tooltip";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	private readonly WebUiTooltipDescriptor _descriptor;
	private readonly Func<string, bool> _isTokenRegistered;
	private readonly Func<string, string, bool> _hasLocaleTemplate;
	private readonly WebUiTooltipEntityInsightProjection? _entityProjection;
	private readonly WebUiTooltipAbilityProjection? _abilityProjection;
	private readonly HashSet<string> _stateFlags;
	private uint _revision;

	public WebUiTooltipTopicProducer(
		string topic,
		WebUiTooltipDescriptor descriptor,
		Func<string, bool> isTokenRegistered,
		Func<string, string, bool> hasLocaleTemplate,
		WebUiTooltipEntityInsightProjection? entityProjection = null,
		WebUiTooltipAbilityProjection? abilityProjection = null,
		IEnumerable<string>? stateFlags = null)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		_isTokenRegistered = isTokenRegistered ?? throw new ArgumentNullException(nameof(isTokenRegistered));
		_hasLocaleTemplate = hasLocaleTemplate ?? throw new ArgumentNullException(nameof(hasLocaleTemplate));

		switch (_descriptor.TargetKind)
		{
			case WebUiTooltipTargetKind.EntityInsight:
				_entityProjection = entityProjection
					?? throw new ArgumentNullException(
						nameof(entityProjection),
						$"Tooltip topic '{Topic}' targetKind EntityInsight requires an EntityInsight projection.");
				if (!string.Equals(_entityProjection.InsightProfileId, _descriptor.ProfileId, StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						$"Tooltip topic '{Topic}' profileId '{_descriptor.ProfileId}' does not match EntityInsightProfile '{_entityProjection.InsightProfileId}'. Tooltip must reuse EntityInsightProfile ids.");
				}

				if (abilityProjection != null)
				{
					throw new ArgumentException(
						$"Tooltip topic '{Topic}' EntityInsight target must not carry an ability projection.",
						nameof(abilityProjection));
				}

				break;

			case WebUiTooltipTargetKind.Ability:
				_abilityProjection = abilityProjection
					?? throw new ArgumentNullException(
						nameof(abilityProjection),
						$"Tooltip topic '{Topic}' targetKind Ability requires an ability projection.");
				if (entityProjection != null)
				{
					throw new ArgumentException(
						$"Tooltip topic '{Topic}' Ability target must not carry an EntityInsight projection.",
						nameof(entityProjection));
				}

				break;

			default:
				throw new InvalidOperationException(
					$"Tooltip topic '{Topic}' has unsupported targetKind '{_descriptor.TargetKind}'.");
		}

		_stateFlags = new HashSet<string>(StringComparer.Ordinal);
		if (stateFlags != null)
		{
			foreach (string flag in stateFlags)
			{
				if (string.IsNullOrWhiteSpace(flag))
				{
					throw new ArgumentException("Tooltip state flags must be non-empty ids.", nameof(stateFlags));
				}

				string trimmed = flag.Trim();
				if (!string.Equals(flag, trimmed, StringComparison.Ordinal))
				{
					throw new ArgumentException(
						$"Tooltip state flag '{flag}' must not contain leading or trailing whitespace.",
						nameof(stateFlags));
				}

				if (!_stateFlags.Add(trimmed))
				{
					throw new ArgumentException($"Duplicate tooltip state flag '{trimmed}'.", nameof(stateFlags));
				}
			}
		}

		EnsureDescriptorTokensExist();
	}

	public string Topic { get; }
	public string DescriptorId => _descriptor.DescriptorId;
	public uint Revision => _revision;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		WebUiTooltipSnapshot snapshot = CreateSnapshot();
		byte[] payload = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
		packet = new WebUiOutboundPacket(
			context.SessionId,
			Topic,
			WebUiPacketKind.Snapshot,
			WebUiDeliverySemantics.LatestWins,
			payload,
			JsonContentType,
			context.RequestId);
		return true;
	}

	public WebUiTooltipSnapshot CreateSnapshot()
	{
		EnsureDescriptorTokensExist();
		_revision++;

		var sections = new WebUiTooltipSectionPayload[_descriptor.Sections.Count];
		for (int i = 0; i < _descriptor.Sections.Count; i++)
		{
			WebUiTooltipSection section = _descriptor.Sections[i];
			var blocks = new WebUiRichTextBlockPayload[section.Blocks.Count];
			for (int b = 0; b < section.Blocks.Count; b++)
			{
				WebUiRichTextBlock block = section.Blocks[b];
				var runs = new WebUiRichTextRunPayload[block.Runs.Count];
				for (int r = 0; r < block.Runs.Count; r++)
				{
					runs[r] = ProjectRun(block.Runs[r], $"{section.SectionId}/{block.BlockId}/run[{r}]");
				}

				blocks[b] = new WebUiRichTextBlockPayload(block.BlockId, runs);
			}

			sections[i] = new WebUiTooltipSectionPayload(section.SectionId, section.TemplateId, blocks);
		}

		string? targetId = _descriptor.TargetKind switch
		{
			WebUiTooltipTargetKind.EntityInsight => _entityProjection!.InsightProfileId,
			WebUiTooltipTargetKind.Ability => _abilityProjection!.AbilityId,
			_ => throw new InvalidOperationException(
				$"Tooltip topic '{Topic}' has unsupported targetKind '{_descriptor.TargetKind}'.")
		};

		RejectFallbackStrings(targetId, sections);

		return new WebUiTooltipSnapshot(
			Target: new WebUiTooltipTargetRef(_descriptor.TargetKind.ToString(), targetId),
			ProfileId: _descriptor.ProfileId,
			TemplateId: _descriptor.TemplateId,
			LocaleId: _descriptor.LocaleId,
			Revision: _revision,
			Anchor: _descriptor.Anchor,
			StateFlags: _stateFlags.OrderBy(static f => f, StringComparer.Ordinal).ToArray(),
			Sections: sections);
	}

	private void EnsureDescriptorTokensExist()
	{
		foreach (WebUiTooltipSection section in _descriptor.Sections)
		{
			foreach (WebUiRichTextBlock block in section.Blocks)
			{
				foreach (WebUiRichTextRun run in block.Runs)
				{
					if (run.Role != WebUiRichTextRunRole.Token)
					{
						continue;
					}

					string tokenId = run.TokenId!;
					if (!_isTokenRegistered(tokenId))
					{
						throw new InvalidOperationException(
							$"Tooltip descriptor '{_descriptor.DescriptorId}' references unknown text token '{tokenId}'.");
					}

					if (!_hasLocaleTemplate(tokenId, _descriptor.LocaleId))
					{
						throw new InvalidOperationException(
							$"Tooltip descriptor '{_descriptor.DescriptorId}' token '{tokenId}' has no template for locale '{_descriptor.LocaleId}'.");
					}
				}
			}
		}

		if (_descriptor.TargetKind == WebUiTooltipTargetKind.EntityInsight)
		{
			RequireProjectionToken(_entityProjection!.TitleTokenId, "title");
			RequireProjectionToken(_entityProjection.BodyTokenId, "body");
			foreach (string token in _entityProjection.BadgeTokenIds)
			{
				RequireProjectionToken(token, "badge");
			}

			foreach (string token in _entityProjection.StatLabelTokenIds)
			{
				RequireProjectionToken(token, "stat");
			}

			foreach (string token in _entityProjection.TipTokenIds)
			{
				RequireProjectionToken(token, "tip");
			}

			foreach ((string title, string body) in _entityProjection.ActionTokenPairs)
			{
				RequireProjectionToken(title, "action.title");
				RequireProjectionToken(body, "action.body");
			}
		}
		else if (_descriptor.TargetKind == WebUiTooltipTargetKind.Ability)
		{
			RequireProjectionToken(_abilityProjection!.DisplayNameTokenId, "displayName");
			RequireProjectionToken(_abilityProjection.HintTextTokenId, "hintText");
			foreach (KeyValuePair<string, string> pair in _abilityProjection.ModeHintTokenIds)
			{
				RequireProjectionToken(pair.Value, $"modeHints.{pair.Key}");
			}
		}
	}

	private void RequireProjectionToken(string tokenId, string field)
	{
		if (!_isTokenRegistered(tokenId))
		{
			throw new InvalidOperationException(
				$"Tooltip topic '{Topic}' {field} references unknown text token '{tokenId}'.");
		}

		if (!_hasLocaleTemplate(tokenId, _descriptor.LocaleId))
		{
			throw new InvalidOperationException(
				$"Tooltip topic '{Topic}' {field} token '{tokenId}' has no template for locale '{_descriptor.LocaleId}'.");
		}
	}

	private WebUiRichTextRunPayload ProjectRun(WebUiRichTextRun run, string path)
	{
		switch (run.Role)
		{
			case WebUiRichTextRunRole.Text:
			case WebUiRichTextRunRole.Emphasis:
				WebUiRichTextGuard.RejectHtml(run.Text!, path);
				RejectForbiddenFallback(run.Text!, path);
				return new WebUiRichTextRunPayload(run.Role, Text: run.Text);

			case WebUiRichTextRunRole.Token:
				return new WebUiRichTextRunPayload(run.Role, TokenId: run.TokenId);

			case WebUiRichTextRunRole.Icon:
				return new WebUiRichTextRunPayload(run.Role, IconId: run.IconId);

			case WebUiRichTextRunRole.Value:
				return new WebUiRichTextRunPayload(run.Role, ValueId: run.ValueId);

			case WebUiRichTextRunRole.State:
				return new WebUiRichTextRunPayload(run.Role, StateId: run.StateId);

			default:
				throw new InvalidOperationException($"{path} has unknown rich-text run role '{run.Role}'.");
		}
	}

	private static void RejectFallbackStrings(string targetId, WebUiTooltipSectionPayload[] sections)
	{
		RejectForbiddenFallback(targetId, "target.id");
		foreach (WebUiTooltipSectionPayload section in sections)
		{
			foreach (WebUiRichTextBlockPayload block in section.Blocks)
			{
				foreach (WebUiRichTextRunPayload run in block.Runs)
				{
					if (!string.IsNullOrEmpty(run.Text))
					{
						RejectForbiddenFallback(run.Text, $"section '{section.SectionId}' text run");
					}
				}
			}
		}
	}

	private static void RejectForbiddenFallback(string value, string path)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidOperationException($"{path} must not be empty; empty tooltip text is forbidden.");
		}

		if (string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"{path} must not use 'Unknown' fallback text.");
		}

		if (value.StartsWith("Ability#", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException($"{path} must not use 'Ability#N' fallback text ('{value}').");
		}
	}
}

/// <summary>
/// DataPlane payload for a tooltip snapshot. Structured rich text only — no HTML strings.
/// </summary>
public sealed record WebUiTooltipSnapshot(
	WebUiTooltipTargetRef Target,
	string ProfileId,
	string TemplateId,
	string LocaleId,
	uint Revision,
	string Anchor,
	string[] StateFlags,
	WebUiTooltipSectionPayload[] Sections);

public sealed record WebUiTooltipTargetRef(string Kind, string Id);

public sealed record WebUiTooltipSectionPayload(
	string SectionId,
	string TemplateId,
	WebUiRichTextBlockPayload[] Blocks);

public sealed record WebUiRichTextBlockPayload(
	string BlockId,
	WebUiRichTextRunPayload[] Runs);

public sealed record WebUiRichTextRunPayload(
	WebUiRichTextRunRole Role,
	string? Text = null,
	string? TokenId = null,
	string? IconId = null,
	string? ValueId = null,
	string? StateId = null);
