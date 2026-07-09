using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.WebUI.DataPlane;

namespace Ludots.WebUI.PanelKit;

/// <summary>
/// DataPlane topic producer for a resource attribute panel. Reads AttributeBuffer and/or
/// GraphOutputValueStore projections; never hand-sums entities and never invents ResourceStore.
/// </summary>
public sealed class WebUiResourceAttributeTopicProducer : IWebUiTopicProducer
{
	public const string JsonContentType = "application/json+ludots-resource-attribute";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly World _world;
	private readonly Entity _owner;
	private readonly WebUiResourceAttributeDescriptor _descriptor;
	private readonly GraphOutputValueStore? _graphOutputs;
	private readonly Func<string, int> _resolveAttributeId;
	private uint _revision;

	public WebUiResourceAttributeTopicProducer(
		string topic,
		World world,
		Entity owner,
		WebUiResourceAttributeDescriptor descriptor,
		GraphOutputValueStore? graphOutputs = null,
		Func<string, int>? resolveAttributeId = null)
	{
		Topic = string.IsNullOrWhiteSpace(topic)
			? throw new ArgumentException("Topic is required.", nameof(topic))
			: topic.Trim();
		_world = world ?? throw new ArgumentNullException(nameof(world));
		if (owner == Entity.Null)
		{
			throw new ArgumentException("Owner entity is required.", nameof(owner));
		}

		_owner = owner;
		_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		_graphOutputs = graphOutputs;
		_resolveAttributeId = resolveAttributeId ?? AttributeRegistry.GetId;

		EnsureDescriptorCanProduce();
	}

	public string Topic { get; }
	public string DescriptorId => _descriptor.DescriptorId;
	public Entity Owner => _owner;
	public uint Revision => _revision;

	public bool TryCreateSnapshot(in WebUiTopicContext context, out WebUiOutboundPacket packet)
	{
		WebUiResourceAttributeSnapshot snapshot = CreateSnapshot();
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

	public WebUiResourceAttributeSnapshot CreateSnapshot()
	{
		if (!_world.IsAlive(_owner))
		{
			throw new InvalidOperationException(
				$"Resource attribute topic '{Topic}' owner entity {_owner.Id} is not alive.");
		}

		var values = new WebUiResourceAttributeValue[ _descriptor.Fields.Count ];
		uint fieldRevision = 0;
		for (int i = 0; i < _descriptor.Fields.Count; i++)
		{
			WebUiResourceAttributeField field = _descriptor.Fields[i];
			values[i] = ResolveField(field, out uint contribution);
			fieldRevision ^= contribution + ((uint)(i + 1) * 397u);
		}

		_revision++;
		uint revision = _revision ^ fieldRevision;
		return new WebUiResourceAttributeSnapshot(
			Owner: new WebUiResourceAttributeOwnerRef(_owner.Id, _owner.WorldId, _owner.Version),
			Descriptor: _descriptor.DescriptorId,
			Revision: revision,
			Values: values);
	}

	private void EnsureDescriptorCanProduce()
	{
		foreach (WebUiResourceAttributeField field in _descriptor.Fields)
		{
			switch (field.SourceKind)
			{
				case WebUiResourceAttributeSourceKind.SingleAttribute:
				case WebUiResourceAttributeSourceKind.DerivedAttribute:
				{
					int attributeId = _resolveAttributeId(field.AttributeId!);
					if (attributeId == AttributeRegistry.InvalidId || attributeId < 0)
					{
						throw new InvalidOperationException(
							$"Resource attribute descriptor '{_descriptor.DescriptorId}' field '{field.FieldId}' references unknown attribute '{field.AttributeId}'.");
					}

					break;
				}
				case WebUiResourceAttributeSourceKind.AggregateProjection:
					if (_graphOutputs == null)
					{
						throw new InvalidOperationException(
							$"Resource attribute descriptor '{_descriptor.DescriptorId}' field '{field.FieldId}' requires GraphOutputValueStore for aggregateProjection key '{field.GraphOutputKey}'.");
					}

					break;
				default:
					throw new InvalidOperationException(
						$"Resource attribute descriptor '{_descriptor.DescriptorId}' field '{field.FieldId}' has unsupported sourceKind '{field.SourceKind}'.");
			}
		}
	}

	private WebUiResourceAttributeValue ResolveField(WebUiResourceAttributeField field, out uint contribution)
	{
		switch (field.SourceKind)
		{
			case WebUiResourceAttributeSourceKind.SingleAttribute:
			case WebUiResourceAttributeSourceKind.DerivedAttribute:
				return ResolveAttributeField(field, out contribution);
			case WebUiResourceAttributeSourceKind.AggregateProjection:
				return ResolveAggregateField(field, out contribution);
			default:
				throw new InvalidOperationException(
					$"Resource attribute field '{field.FieldId}' has unsupported sourceKind '{field.SourceKind}'.");
		}
	}

	private WebUiResourceAttributeValue ResolveAttributeField(WebUiResourceAttributeField field, out uint contribution)
	{
		int attributeId = _resolveAttributeId(field.AttributeId!);
		if (attributeId == AttributeRegistry.InvalidId || attributeId < 0)
		{
			throw new InvalidOperationException(
				$"Resource attribute field '{field.FieldId}' references unknown attribute '{field.AttributeId}'.");
		}

		if (!_world.TryGet(_owner, out AttributeBuffer buffer))
		{
			throw new InvalidOperationException(
				$"Resource attribute field '{field.FieldId}' requires AttributeBuffer on owner {_owner.Id} for attribute '{field.AttributeId}'.");
		}

		if (!buffer.HasAttribute(attributeId))
		{
			throw new InvalidOperationException(
				$"Resource attribute field '{field.FieldId}' attribute '{field.AttributeId}' is not defined on owner {_owner.Id}.");
		}

		float value = buffer.GetCurrent(attributeId);
		contribution = (uint)BitConverter.SingleToInt32Bits(value);
		return new WebUiResourceAttributeValue(
			field.FieldId,
			field.SourceKind.ToString(),
			field.AttributeId,
			null,
			value,
			field.DisplayTokenId,
			field.UnitTokenId,
			field.GroupId,
			field.SortOrder);
	}

	private WebUiResourceAttributeValue ResolveAggregateField(WebUiResourceAttributeField field, out uint contribution)
	{
		if (_graphOutputs == null)
		{
			throw new InvalidOperationException(
				$"Resource attribute field '{field.FieldId}' requires GraphOutputValueStore for graph output '{field.GraphOutputKey}'.");
		}

		string key = field.GraphOutputKey!;
		if (!_graphOutputs.TryGet(_owner, key, out GraphOutputValueHandle handle) ||
		    !_graphOutputs.TryGetView(handle, out GraphOutputValueView view))
		{
			throw new InvalidOperationException(
				$"Resource attribute field '{field.FieldId}' missing graph output '{key}' on owner {_owner.Id}.");
		}

		float value = view.Kind switch
		{
			GraphOutputValueKind.Float => view.FloatValue,
			GraphOutputValueKind.Int => view.IntValue,
			GraphOutputValueKind.Bool => view.BoolValue ? 1f : 0f,
			_ => throw new InvalidOperationException(
				$"Resource attribute field '{field.FieldId}' graph output '{key}' has unsupported kind '{view.Kind}'.")
		};

		contribution = view.Revision ^ (uint)BitConverter.SingleToInt32Bits(value);
		return new WebUiResourceAttributeValue(
			field.FieldId,
			field.SourceKind.ToString(),
			null,
			key,
			value,
			field.DisplayTokenId,
			field.UnitTokenId,
			field.GroupId,
			field.SortOrder);
	}
}

/// <summary>
/// DataPlane payload for a resource attribute panel snapshot.
/// </summary>
public sealed record WebUiResourceAttributeSnapshot(
	WebUiResourceAttributeOwnerRef Owner,
	string Descriptor,
	uint Revision,
	WebUiResourceAttributeValue[] Values);

public sealed record WebUiResourceAttributeOwnerRef(int EntityId, int WorldId, int Version);

public sealed record WebUiResourceAttributeValue(
	string FieldId,
	string SourceKind,
	string? AttributeId,
	string? GraphOutputKey,
	float Value,
	string DisplayTokenId,
	string UnitTokenId,
	string GroupId,
	int SortOrder);
