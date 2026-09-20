using System;
using System.Collections.Generic;
using System.Text.Json;
using LiveSkillWorkbenchMod;
using LiveSkillWorkbenchMod.Contracts;
using LiveSkillWorkbenchMod.DataPlane;
using LiveSkillWorkbenchMod.Runtime;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using NUnit.Framework;

namespace Ludots.Tests.WebUiDataPlane;

[TestFixture]
public sealed class LiveSkillWorkbenchDataPlaneTests
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	[Test]
	public void Topic_And_Command_Names_Are_Stable()
	{
		Assert.That(LiveSkillWorkbenchIds.Topic, Is.EqualTo("ludots.capability.liveSkillWorkbench.session"));
		Assert.That(LiveSkillWorkbenchIds.StageEditCommand, Is.EqualTo("lsw.stageEdit"));
		Assert.That(LiveSkillWorkbenchIds.ApplyNextCastCommand, Is.EqualTo("lsw.applyNextCast"));
		Assert.That(LiveSkillWorkbenchIds.PrecheckCommand, Is.EqualTo("lsw.precheck"));
	}

	[Test]
	public void ServiceKeys_Are_Stable_Capability_Local_Names()
	{
		Assert.That(LiveSkillWorkbenchServiceKeys.Runtime.Name, Is.EqualTo("LiveSkillWorkbenchMod.Runtime"));
		Assert.That(LiveSkillWorkbenchServiceKeys.DocumentSource.Name, Is.EqualTo("LiveSkillWorkbenchMod.DocumentSource"));
		Assert.That(typeof(ServiceKey<LiveSkillWorkbenchRuntime>), Is.EqualTo(LiveSkillWorkbenchServiceKeys.Runtime.GetType()));
		Assert.That(
			typeof(ServiceKey<ILiveSkillWorkbenchDocumentSource>),
			Is.EqualTo(LiveSkillWorkbenchServiceKeys.DocumentSource.GetType()));
	}

	[Test]
	public void Runtime_Starts_Without_Authored_Document()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");

		Assert.That(runtime.HasDocument, Is.False);
		Assert.That(snapshot.HasDocument, Is.False);
		Assert.That(snapshot.Catalog, Is.Empty);
		Assert.That(snapshot.Fields, Is.Empty);
		Assert.That(snapshot.Graph, Is.Null);
		Assert.That(snapshot.ApplyMode, Is.EqualTo(LiveSkillWorkbenchIds.ApplyModeNotClassified));
		Assert.That(snapshot.ApplyStatusLabel, Is.EqualTo(LiveSkillWorkbenchIds.ApplyStatusNotPrechecked));
		Assert.That(snapshot.ApplySupported, Is.False);
	}

	[Test]
	public void StageEdit_Uses_LiveEditSession_And_Advances_Revision()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateFireballTestDocument());
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto(
				"ability.Fireball",
				"damage",
				80d,
				"workbench://ability.Fireball/damage")));

		Assert.That(result.Success, Is.True);
		Assert.That(runtime.Revision, Is.EqualTo(1u));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.IsDirty, Is.True);
		Assert.That(snapshot.Revision, Is.EqualTo(1u));
		Assert.That(snapshot.Changes, Has.Count.EqualTo(1));
		Assert.That(snapshot.Changes[0].FieldPath, Is.EqualTo("damage"));
		Assert.That(snapshot.Changes[0].BeforeValue, Is.EqualTo(50d));
		Assert.That(snapshot.Changes[0].AfterValue, Is.EqualTo(80d));
		Assert.That(snapshot.Changes[0].ApplyMode, Is.EqualTo(LiveSkillWorkbenchIds.ApplyModeNotClassified));
		Assert.That(snapshot.ApplySupported, Is.False);
		Assert.That(snapshot.ApplyMode, Is.EqualTo(LiveSkillWorkbenchIds.ApplyModeNotClassified));
		Assert.That(snapshot.ApplyStatusLabel, Does.Contain("尚未预检"));
	}

	[Test]
	public void StageEdit_Failure_Returns_Readable_Diagnostic()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateFireballTestDocument());
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto(
				"ability.Missing",
				"damage",
				80d)));

		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorCode, Is.EqualTo(LiveEditDiagnosticCodes.MissingDefinitionId));
		Assert.That(result.Message, Does.Contain("Unknown definition"));
		Assert.That(runtime.Revision, Is.EqualTo(0u));
	}

	[Test]
	public void ApplyNextCast_WithoutPipeline_Returns_Explicit_Not_Supported_Diagnostic()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateFireballTestDocument());
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(LiveSkillWorkbenchIds.ApplyNextCastCommand, new { }));

		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorCode, Is.EqualTo(LiveSkillWorkbenchIds.DiagnosticApplyNotSupported));
		Assert.That(result.Message, Does.Contain("LiveGasEditPipeline"));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.Diagnostics, Has.Some.Matches<LiveSkillWorkbenchDiagnosticDto>(
			d => d.Code == LiveSkillWorkbenchIds.DiagnosticApplyNotSupported));
	}

	[Test]
	public void PrecheckAndApply_WithPipeline_CommitsHotDurationTicks()
	{
		EffectTemplateIdRegistry.Clear();
		string effectName = "effect.FireballHotDuration";
		int templateId = EffectTemplateIdRegistry.Register(effectName);
		var effects = new EffectTemplateRegistry();
		effects.Register(templateId, new EffectTemplateData { DurationTicks = 10, PeriodTicks = 0 });

		var pipeline = new LiveGasEditPipeline(new GraphProgramRegistry(), new GraphFunctionCatalog(), effects);
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.BindPipeline(pipeline);
		runtime.ReplaceDocument(new LiveSkillWorkbenchDocumentDto(
			Catalog: new[]
			{
				new LiveSkillWorkbenchCatalogItemDto(effectName, "effect", "火球持续", null, new[] { "效果" }),
			},
			FieldBindings: new[]
			{
				new LiveSkillWorkbenchFieldBindingDto(
					effectName,
					new LiveSkillWorkbenchFieldDescriptorDto(
						"duration.durationTicks",
						"持续 Tick",
						"number",
						10d,
						10d,
						"tick",
						"时间",
						ReadOnly: false,
						Min: 0d,
						Max: 600d,
						Step: 1d,
						Description: "hot duration",
						SourceUri: "test://effect/duration")),
			},
			Graphs: Array.Empty<LiveSkillWorkbenchGraphDto>(),
			EffectChain: Array.Empty<LiveSkillWorkbenchEffectChainEventDto>(),
			SelectedCatalogId: effectName,
			SourceUri: "test://effect"));

		var handler = new LiveSkillWorkbenchCommandHandler(runtime);
		Assert.That(handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto(effectName, "duration.durationTicks", 33d))).Success, Is.True);

		Assert.That(handler.Handle(CreateCommand(LiveSkillWorkbenchIds.PrecheckCommand, new { })).Success, Is.True);
		LiveSkillWorkbenchSessionSnapshotDto afterPrecheck = runtime.BuildSnapshot("connected");
		Assert.That(afterPrecheck.ApplyMode, Is.EqualTo(LiveSkillWorkbenchIds.ApplyModeNextCast));
		Assert.That(afterPrecheck.ApplySupported, Is.True);

		Assert.That(handler.Handle(CreateCommand(LiveSkillWorkbenchIds.ApplyNextCastCommand, new { })).Success, Is.True);
		Assert.That(effects.TryGet(templateId, out EffectTemplateData data), Is.True);
		Assert.That(data.DurationTicks, Is.EqualTo(33));

		LiveSkillWorkbenchSessionSnapshotDto afterApply = runtime.BuildSnapshot("connected");
		Assert.That(afterApply.ApplyStatusLabel, Is.EqualTo(LiveSkillWorkbenchIds.ApplyStatusApplied));
	}

	[Test]
	public void TopicProducer_Emits_Json_Session_Snapshot_From_Injected_Document()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateFireballTestDocument());
		var producer = new LiveSkillWorkbenchTopicProducer(runtime);
		var context = new WebUiTopicContext(
			"session-a",
			LiveSkillWorkbenchIds.Topic,
			7,
			JsonSerializer.SerializeToElement(new { }));

		Assert.That(producer.TryCreateSnapshot(in context, out WebUiOutboundPacket packet), Is.True);
		Assert.That(packet.Topic, Is.EqualTo(LiveSkillWorkbenchIds.Topic));
		Assert.That(packet.ContentType, Is.EqualTo("application/json"));

		LiveSkillWorkbenchSessionSnapshotDto? snapshot =
			JsonSerializer.Deserialize<LiveSkillWorkbenchSessionSnapshotDto>(packet.Payload.Span, JsonOptions);
		Assert.That(snapshot, Is.Not.Null);
		Assert.That(snapshot!.Ready, Is.True);
		Assert.That(snapshot.HasDocument, Is.True);
		Assert.That(snapshot.SelectedCatalogId, Is.EqualTo("ability.Fireball"));
		Assert.That(snapshot.Fields, Has.Count.EqualTo(4));
		Assert.That(snapshot.Fields[0].Min, Is.Not.Null);
		Assert.That(snapshot.Graph, Is.Not.Null);
		Assert.That(snapshot.EffectChain, Has.Count.EqualTo(4));
		Assert.That(snapshot.ApplyMode, Is.EqualTo(LiveSkillWorkbenchIds.ApplyModeNotClassified));
	}

	[Test]
	public void StateVersion_Advances_On_Mutations_And_Idle_Producer_Skips_Republish()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		Assert.That(runtime.StateVersion, Is.EqualTo(0u));

		runtime.ReplaceDocument(CreateFireballTestDocument());
		ulong afterDocument = runtime.StateVersion;
		Assert.That(afterDocument, Is.GreaterThan(0u));

		var producer = new LiveSkillWorkbenchTopicProducer(runtime);
		Assert.That(producer.HasUnpublishedStateChange, Is.True);

		var context = new WebUiTopicContext(
			"session-a",
			LiveSkillWorkbenchIds.Topic,
			1,
			JsonSerializer.SerializeToElement(new { }));
		Assert.That(producer.TryCreateSnapshot(in context, out _), Is.True);
		Assert.That(producer.HasUnpublishedStateChange, Is.False);

		Assert.That(producer.TryCreateSnapshot(in context, out _), Is.True);
		Assert.That(producer.HasUnpublishedStateChange, Is.False);

		runtime.SelectCatalogItem("graph.FireballCast");
		Assert.That(runtime.StateVersion, Is.GreaterThan(afterDocument));
		Assert.That(producer.HasUnpublishedStateChange, Is.True);

		Assert.That(producer.TryCreateSnapshot(in context, out _), Is.True);
		Assert.That(producer.HasUnpublishedStateChange, Is.False);

		var handler = new LiveSkillWorkbenchCommandHandler(runtime);
		Assert.That(handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto("ability.Fireball", "damage", 90d))).Success, Is.True);
		Assert.That(producer.HasUnpublishedStateChange, Is.True);

		handler.Handle(CreateCommand(LiveSkillWorkbenchIds.DiscardEditsCommand, new { }));
		Assert.That(producer.HasUnpublishedStateChange, Is.True);

		handler.Handle(CreateCommand(LiveSkillWorkbenchIds.PrecheckCommand, new { }));
		Assert.That(producer.HasUnpublishedStateChange, Is.True);
	}

	[Test]
	public void Injected_Custom_Document_Is_Descriptor_Driven()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(new LiveSkillWorkbenchDocumentDto(
			Catalog: new[]
			{
				new LiveSkillWorkbenchCatalogItemDto("ability.CustomBolt", "ability", "自定义弹", null, new[] { "技能" })
			},
			FieldBindings: new[]
			{
				new LiveSkillWorkbenchFieldBindingDto(
					"ability.CustomBolt",
					new LiveSkillWorkbenchFieldDescriptorDto(
						"arcaneChargeDensity",
						"奥术充能密度",
						"number",
						1.5d,
						1.5d,
						"单位",
						"数值",
						ReadOnly: false,
						Min: 0.5d,
						Max: 4d,
						Step: 0.25d,
						Description: "任意自定义字段",
						SourceUri: "test://custom/arcaneChargeDensity"))
			},
			Graphs: Array.Empty<LiveSkillWorkbenchGraphDto>(),
			EffectChain: Array.Empty<LiveSkillWorkbenchEffectChainEventDto>(),
			SelectedCatalogId: "ability.CustomBolt",
			SourceUri: "test://custom"));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.HasDocument, Is.True);
		Assert.That(snapshot.DocumentSourceUri, Is.EqualTo("test://custom"));
		Assert.That(snapshot.Fields, Has.Count.EqualTo(1));
		Assert.That(snapshot.Fields[0].FieldPath, Is.EqualTo("arcaneChargeDensity"));
		Assert.That(snapshot.Fields[0].Min, Is.EqualTo(0.5d));
		Assert.That(snapshot.Fields[0].Max, Is.EqualTo(4d));
		Assert.That(snapshot.Fields[0].Step, Is.EqualTo(0.25d));
		Assert.That(snapshot.Fields[0].SourceUri, Is.EqualTo("test://custom/arcaneChargeDensity"));
	}

	[Test]
	public void ReplaceDocument_Snapshots_Caller_Owned_Collections()
	{
		var tags = new[] { "技能" };
		var nodes = new List<LiveSkillWorkbenchGraphNodeDto>
		{
			new("cast", "Cast", "cast", 40, 80)
		};
		var edges = new List<LiveSkillWorkbenchGraphEdgeDto>
		{
			new("e1", "cast", "query", "commit")
		};
		var effectChain = new List<LiveSkillWorkbenchEffectChainEventDto>
		{
			new("evt.1", "cast", "Cast started", "ability.Custom", "detail", 1)
		};

		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(new LiveSkillWorkbenchDocumentDto(
			Catalog: new[]
			{
				new LiveSkillWorkbenchCatalogItemDto("ability.Custom", "ability", "自定义", null, tags),
				new LiveSkillWorkbenchCatalogItemDto("graph.Custom", "graph", "图", "ability.Custom", new[] { "Graph" }),
			},
			FieldBindings: new[]
			{
				Bind("ability.Custom", "damage", "伤害", 10d, "点", "数值", 0d, 100d, 1d)
			},
			Graphs: new[]
			{
				new LiveSkillWorkbenchGraphDto("graph.Custom", nodes, edges)
			},
			EffectChain: effectChain,
			SelectedCatalogId: "ability.Custom",
			SourceUri: "test://mutable-input"));

		tags[0] = "mutated-tag";
		nodes.Add(new LiveSkillWorkbenchGraphNodeDto("extra", "Extra", "effect", 100, 100));
		edges.Clear();
		effectChain[0] = new LiveSkillWorkbenchEffectChainEventDto(
			"evt.mutated", "effect", "Mutated", "ability.Custom", "mutated", 99);
		effectChain.Add(new LiveSkillWorkbenchEffectChainEventDto(
			"evt.2", "effect", "Extra", "ability.Custom", "extra", 2));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.Catalog[0].Tags![0], Is.EqualTo("技能"));
		Assert.That(snapshot.Graph, Is.Not.Null);
		Assert.That(snapshot.Graph!.Nodes, Has.Count.EqualTo(1));
		Assert.That(snapshot.Graph.Nodes[0].Id, Is.EqualTo("cast"));
		Assert.That(snapshot.Graph.Edges, Has.Count.EqualTo(1));
		Assert.That(snapshot.EffectChain, Has.Count.EqualTo(1));
		Assert.That(snapshot.EffectChain[0].Id, Is.EqualTo("evt.1"));
	}

	[Test]
	public void BuildSnapshot_Exposes_Immutable_Nested_Collections()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateFireballTestDocument());
		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");

		Assert.That(snapshot.Catalog as List<LiveSkillWorkbenchCatalogItemDto>, Is.Null);
		Assert.That(snapshot.Fields as List<LiveSkillWorkbenchFieldDescriptorDto>, Is.Null);
		Assert.That(snapshot.EffectChain as List<LiveSkillWorkbenchEffectChainEventDto>, Is.Null);
		Assert.That(snapshot.Diagnostics as List<LiveSkillWorkbenchDiagnosticDto>, Is.Null);
		Assert.That(snapshot.UnavailableActions as List<LiveSkillWorkbenchUnavailableActionDto>, Is.Null);

		IList<LiveSkillWorkbenchEffectChainEventDto>? effectChainList =
			snapshot.EffectChain as IList<LiveSkillWorkbenchEffectChainEventDto>;
		Assert.That(effectChainList, Is.Not.Null);
		Assert.Throws<NotSupportedException>(() => effectChainList!.Clear());
		Assert.Throws<NotSupportedException>(() => effectChainList!.Add(
			new LiveSkillWorkbenchEffectChainEventDto("x", "cast", "x", null, null, 1)));

		Assert.That(snapshot.Graph, Is.Not.Null);
		IList<LiveSkillWorkbenchGraphNodeDto>? nodes = snapshot.Graph!.Nodes as IList<LiveSkillWorkbenchGraphNodeDto>;
		IList<LiveSkillWorkbenchGraphEdgeDto>? edges = snapshot.Graph.Edges as IList<LiveSkillWorkbenchGraphEdgeDto>;
		Assert.That(nodes, Is.Not.Null);
		Assert.That(edges, Is.Not.Null);
		Assert.Throws<NotSupportedException>(() => nodes!.Clear());
		Assert.Throws<NotSupportedException>(() => edges!.Clear());

		string[] tags = snapshot.Catalog[1].Tags!;
		tags[0] = "mutated-after-snapshot";
		LiveSkillWorkbenchSessionSnapshotDto again = runtime.BuildSnapshot("connected");
		Assert.That(again.Catalog[1].Tags![0], Is.EqualTo("技能"));
		Assert.That(again.EffectChain, Has.Count.EqualTo(4));
		Assert.That(again.Graph!.Nodes, Has.Count.EqualTo(4));
		Assert.That(again.Graph.Edges, Has.Count.EqualTo(3));
	}

	[Test]
	public void StageEdit_Rejects_ReadOnly_Field_Without_Mutating_Runtime()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateDocumentWithConstrainedField(
			readOnly: true,
			min: 0d,
			max: 100d,
			baseline: 40d));
		ulong stateBefore = runtime.StateVersion;
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto("ability.Constrained", "lockedStat", 55d)));

		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorCode, Is.EqualTo(LiveSkillWorkbenchIds.DiagnosticFieldReadOnly));
		Assert.That(runtime.Revision, Is.EqualTo(0u));
		Assert.That(runtime.StateVersion, Is.GreaterThan(stateBefore));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.Fields[0].ReadOnly, Is.True);
		Assert.That(snapshot.Fields[0].NumericValue, Is.EqualTo(40d));
		Assert.That(snapshot.IsDirty, Is.False);
		Assert.That(snapshot.Changes, Is.Empty);
		Assert.That(snapshot.Diagnostics, Has.Some.Matches<LiveSkillWorkbenchDiagnosticDto>(
			d => d.Code == LiveSkillWorkbenchIds.DiagnosticFieldReadOnly));
	}

	[Test]
	public void StageEdit_Rejects_Below_Min_Without_Mutating_Runtime()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateDocumentWithConstrainedField(
			readOnly: false,
			min: 10d,
			max: 100d,
			baseline: 40d));
		ulong stateBefore = runtime.StateVersion;
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto("ability.Constrained", "lockedStat", 5d)));

		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorCode, Is.EqualTo(LiveSkillWorkbenchIds.DiagnosticValueBelowMin));
		Assert.That(runtime.Revision, Is.EqualTo(0u));
		Assert.That(runtime.StateVersion, Is.GreaterThan(stateBefore));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.Fields[0].NumericValue, Is.EqualTo(40d));
		Assert.That(snapshot.IsDirty, Is.False);
		Assert.That(snapshot.Changes, Is.Empty);
		Assert.That(snapshot.Diagnostics, Has.Some.Matches<LiveSkillWorkbenchDiagnosticDto>(
			d => d.Code == LiveSkillWorkbenchIds.DiagnosticValueBelowMin));
	}

	[Test]
	public void StageEdit_Rejects_Above_Max_Without_Mutating_Runtime()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateDocumentWithConstrainedField(
			readOnly: false,
			min: 10d,
			max: 100d,
			baseline: 40d));
		ulong stateBefore = runtime.StateVersion;
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto("ability.Constrained", "lockedStat", 150d)));

		Assert.That(result.Success, Is.False);
		Assert.That(result.ErrorCode, Is.EqualTo(LiveSkillWorkbenchIds.DiagnosticValueAboveMax));
		Assert.That(runtime.Revision, Is.EqualTo(0u));
		Assert.That(runtime.StateVersion, Is.GreaterThan(stateBefore));

		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.Fields[0].NumericValue, Is.EqualTo(40d));
		Assert.That(snapshot.IsDirty, Is.False);
		Assert.That(snapshot.Changes, Is.Empty);
		Assert.That(snapshot.Diagnostics, Has.Some.Matches<LiveSkillWorkbenchDiagnosticDto>(
			d => d.Code == LiveSkillWorkbenchIds.DiagnosticValueAboveMax));
	}

	[Test]
	public void StageEdit_Allows_Value_Not_Aligned_To_Step()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.ReplaceDocument(CreateDocumentWithConstrainedField(
			readOnly: false,
			min: 0d,
			max: 100d,
			baseline: 40d,
			step: 5d));
		var handler = new LiveSkillWorkbenchCommandHandler(runtime);

		WebUiCommandResult result = handler.Handle(CreateCommand(
			LiveSkillWorkbenchIds.StageEditCommand,
			new LiveSkillWorkbenchStageEditRequestDto("ability.Constrained", "lockedStat", 42d)));

		Assert.That(result.Success, Is.True);
		Assert.That(runtime.Revision, Is.EqualTo(1u));
		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.Fields[0].NumericValue, Is.EqualTo(42d));
	}

	[Test]
	public void LoadFromSource_Uses_Injected_Document_Source()
	{
		var runtime = new LiveSkillWorkbenchRuntime();
		runtime.LoadFromSource(new FixedDocumentSource(CreateFireballTestDocument()));

		Assert.That(runtime.HasDocument, Is.True);
		LiveSkillWorkbenchSessionSnapshotDto snapshot = runtime.BuildSnapshot("connected");
		Assert.That(snapshot.SelectedCatalogId, Is.EqualTo("ability.Fireball"));
		Assert.That(snapshot.DocumentSourceUri, Is.EqualTo("test://fireball"));
	}

	/// <summary>
	/// Test-only Fireball document. Production Mod must not seed this.
	/// </summary>
	internal static LiveSkillWorkbenchDocumentDto CreateFireballTestDocument()
	{
		return new LiveSkillWorkbenchDocumentDto(
			Catalog: new[]
			{
				new LiveSkillWorkbenchCatalogItemDto("actor.mage", "actor", "法师", null, new[] { "角色" }),
				new LiveSkillWorkbenchCatalogItemDto("ability.Fireball", "ability", "火球术", "actor.mage", new[] { "技能" }),
				new LiveSkillWorkbenchCatalogItemDto("effect.FireballDamage", "effect", "火球伤害", "ability.Fireball", new[] { "效果" }),
				new LiveSkillWorkbenchCatalogItemDto("graph.FireballCast", "graph", "火球施放图", "ability.Fireball", new[] { "Graph" }),
				new LiveSkillWorkbenchCatalogItemDto("tag.State.Burning", "tag", "State.Burning", null, new[] { "标签" }),
				new LiveSkillWorkbenchCatalogItemDto("attr.Health", "attribute", "Health", null, new[] { "属性" }),
			},
			FieldBindings: new[]
			{
				Bind("ability.Fireball", "damage", "伤害", 50d, "点", "数值", 0d, 9999d, 1d),
				Bind("ability.Fireball", "manaCost", "蓝耗", 25d, "点", "数值", 0d, 999d, 1d),
				Bind("ability.Fireball", "cooldown", "冷却", 3d, "秒", "时间", 0d, 120d, 0.1d),
				Bind("ability.Fireball", "radius", "范围", 2.5d, "米", "空间", 0.1d, 50d, 0.1d),
			},
			Graphs: new[]
			{
				new LiveSkillWorkbenchGraphDto(
					"graph.FireballCast",
					new[]
					{
						new LiveSkillWorkbenchGraphNodeDto("cast", "Cast", "cast", 40, 80),
						new LiveSkillWorkbenchGraphNodeDto("query", "Target Query", "query", 220, 80),
						new LiveSkillWorkbenchGraphNodeDto("damage", "Damage Effect", "effect", 420, 80),
						new LiveSkillWorkbenchGraphNodeDto("delta", "Attribute Delta", "attribute", 620, 80),
					},
					new[]
					{
						new LiveSkillWorkbenchGraphEdgeDto("e1", "cast", "query", "commit"),
						new LiveSkillWorkbenchGraphEdgeDto("e2", "query", "damage", "targets"),
						new LiveSkillWorkbenchGraphEdgeDto("e3", "damage", "delta", "Health"),
					})
			},
			EffectChain: new[]
			{
				new LiveSkillWorkbenchEffectChainEventDto("evt.1", "cast", "Cast started", "ability.Fireball", "Fireball", 1),
				new LiveSkillWorkbenchEffectChainEventDto("evt.2", "query", "Target query resolved", "graph.FireballCast", "1 target", 2),
				new LiveSkillWorkbenchEffectChainEventDto("evt.3", "effect", "Damage effect requested", "effect.FireballDamage", "pending apply", 3),
				new LiveSkillWorkbenchEffectChainEventDto("evt.4", "attribute", "Attribute delta", "attr.Health", "-50 (baseline)", 4),
			},
			SelectedCatalogId: "ability.Fireball",
			SourceUri: "test://fireball");
	}

	private static LiveSkillWorkbenchDocumentDto CreateDocumentWithConstrainedField(
		bool readOnly,
		double min,
		double max,
		double baseline,
		double? step = 1d)
	{
		return new LiveSkillWorkbenchDocumentDto(
			Catalog: new[]
			{
				new LiveSkillWorkbenchCatalogItemDto("ability.Constrained", "ability", "约束技能", null, new[] { "技能" })
			},
			FieldBindings: new[]
			{
				new LiveSkillWorkbenchFieldBindingDto(
					"ability.Constrained",
					new LiveSkillWorkbenchFieldDescriptorDto(
						"lockedStat",
						"锁定数值",
						"number",
						baseline,
						baseline,
						"点",
						"数值",
						ReadOnly: readOnly,
						Min: min,
						Max: max,
						Step: step,
						Description: "约束字段测试",
						SourceUri: "test://constrained/lockedStat"))
			},
			Graphs: Array.Empty<LiveSkillWorkbenchGraphDto>(),
			EffectChain: Array.Empty<LiveSkillWorkbenchEffectChainEventDto>(),
			SelectedCatalogId: "ability.Constrained",
			SourceUri: "test://constrained");
	}

	private static LiveSkillWorkbenchFieldBindingDto Bind(
		string definitionId,
		string fieldPath,
		string label,
		double value,
		string unit,
		string group,
		double min,
		double max,
		double step)
	{
		return new LiveSkillWorkbenchFieldBindingDto(
			definitionId,
			new LiveSkillWorkbenchFieldDescriptorDto(
				fieldPath,
				label,
				"number",
				value,
				value,
				unit,
				group,
				ReadOnly: false,
				Min: min,
				Max: max,
				Step: step,
				Description: $"{label}（测试夹具）",
				SourceUri: $"test://fireball/{fieldPath}"));
	}

	private static WebUiCommandRequest CreateCommand(string name, object payload)
	{
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
		using var document = JsonDocument.Parse(bytes);
		return new WebUiCommandRequest(name, 1, Array.Empty<WebUiEntityRef>(), document.RootElement.Clone());
	}

	private sealed class FixedDocumentSource : ILiveSkillWorkbenchDocumentSource
	{
		private readonly LiveSkillWorkbenchDocumentDto _document;

		public FixedDocumentSource(LiveSkillWorkbenchDocumentDto document)
		{
			_document = document;
		}

		public LiveSkillWorkbenchDocumentDto? GetDocument() => _document;
	}
}
