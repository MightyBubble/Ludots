using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    /// <summary>
    /// Formal Live GAS edit pipeline: Stage (candidate compile) → Classify → Commit.
    /// Never Clear+Register-all live registries. Not a ReloadConfigs branch.
    /// </summary>
    public sealed class LiveGasEditPipeline
    {
        private readonly GraphProgramRegistry _graphs;
        private readonly EffectTemplateRegistry? _effects;
        private readonly TagOps? _tagOps;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly List<StagedGraphCandidate> _stagedGraphs = new(4);
        private readonly List<StagedEffectNumericCandidate> _stagedEffects = new(4);
        private readonly List<StagedTagRuleCandidate> _stagedTagRules = new(4);
        private readonly List<StagedAttrConstraintCandidate> _stagedAttrConstraints = new(4);
        private readonly List<StagedEffectRefCandidate> _stagedEffectRefs = new(4);
        private readonly List<StagedEffectGrantedTagCandidate> _stagedGrantedTags = new(4);
        private readonly List<LiveDebugPatchOperation> _stagedImmediate = new(4);
        private bool _safeFrameOpen;

        public LiveGasEditPipeline(
            GraphProgramRegistry graphs,
            EffectTemplateRegistry? effects = null,
            TagOps? tagOps = null,
            JsonSerializerOptions? jsonOptions = null)
        {
            _graphs = graphs ?? throw new ArgumentNullException(nameof(graphs));
            _effects = effects;
            _tagOps = tagOps;
            _jsonOptions = jsonOptions ?? StrictJsonOptions.CreateCamelCase();
        }

        /// <summary>
        /// Marks that the host is in a safe frame where NextCastLiveApply may commit.
        /// </summary>
        public void BeginSafeFrame() => _safeFrameOpen = true;

        public void EndSafeFrame() => _safeFrameOpen = false;

        /// <summary>
        /// Classifies staged session ops into apply modes. Compiles Graph candidates without touching live registries.
        /// </summary>
        public LiveApplyClassificationReport Classify(LiveEditSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            _stagedGraphs.Clear();
            _stagedEffects.Clear();
            _stagedTagRules.Clear();
            _stagedAttrConstraints.Clear();
            _stagedEffectRefs.Clear();
            _stagedGrantedTags.Clear();
            _stagedImmediate.Clear();

            var items = new List<LiveApplyClassificationItem>(session.Patch.Count);
            bool canImmediate = false;
            bool canNextCast = false;
            bool mapReload = false;
            bool engineRestart = false;

            for (int i = 0; i < session.Patch.Count; i++)
            {
                LiveDebugPatchOperation op = session.Patch.Operations[i];
                switch (op.Kind)
                {
                    case LiveDebugPatchOperationKind.SelectedActorAttribute:
                    {
                        _stagedImmediate.Add(op);
                        canImmediate = true;
                        items.Add(new LiveApplyClassificationItem(
                            op.Kind,
                            op.AttributeName ?? string.Empty,
                            LiveApplyMode.ImmediateCommand,
                            "Attribute set/add is an ImmediateCommand applied through AttributeMutationOps.",
                            Array.Empty<LiveEditDiagnostic>()));
                        break;
                    }
                    case LiveDebugPatchOperationKind.SkillEffectNumeric:
                    {
                        ClassifyEffectNumeric(in op, items, ref canNextCast, ref mapReload, ref engineRestart);
                        break;
                    }
                    case LiveDebugPatchOperationKind.GraphBodyReplace:
                    {
                        ClassifyGraphBody(in op, items, ref canNextCast, ref mapReload, ref engineRestart);
                        break;
                    }
                    case LiveDebugPatchOperationKind.TagRuleBodyReplace:
                    {
                        ClassifyTagRuleBody(in op, items, ref canNextCast, ref mapReload, ref engineRestart);
                        break;
                    }
                    case LiveDebugPatchOperationKind.AttrConstraintNumeric:
                    {
                        ClassifyAttrConstraint(in op, items, ref canNextCast, ref mapReload, ref engineRestart);
                        break;
                    }
                    case LiveDebugPatchOperationKind.EffectTemplateRef:
                    {
                        ClassifyEffectRef(in op, items, ref canNextCast, ref mapReload, ref engineRestart);
                        break;
                    }
                    case LiveDebugPatchOperationKind.EffectGrantedTag:
                    {
                        ClassifyGrantedTag(in op, items, ref canNextCast, ref mapReload, ref engineRestart);
                        break;
                    }
                    default:
                    {
                        engineRestart = true;
                        items.Add(new LiveApplyClassificationItem(
                            op.Kind,
                            op.DefinitionId ?? string.Empty,
                            LiveApplyMode.EngineRestartRequired,
                            $"Operation kind '{op.Kind}' has no hot-apply path.",
                            new[]
                            {
                                new LiveEditDiagnostic(
                                    LiveEditDiagnosticSeverity.Error,
                                    LiveEditDiagnosticCodes.UnsupportedOperationKind,
                                    $"Unsupported operation kind '{op.Kind}'.",
                                    op.DefinitionId)
                            }));
                        break;
                    }
                }
            }

            return new LiveApplyClassificationReport(
                session.SessionId,
                session.Revision,
                items,
                canImmediate,
                canNextCast,
                mapReload,
                engineRestart);
        }

        public LiveApplyCommitResult CommitImmediate(ILiveAttributeCommandSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (_stagedImmediate.Count == 0)
            {
                return new LiveApplyCommitResult(true, 0, Array.Empty<LiveEditDiagnostic>());
            }

            int applied = 0;
            for (int i = 0; i < _stagedImmediate.Count; i++)
            {
                sink.Apply(_stagedImmediate[i]);
                applied++;
            }

            _stagedImmediate.Clear();
            return new LiveApplyCommitResult(true, applied, Array.Empty<LiveEditDiagnostic>());
        }

        /// <summary>
        /// Commits NextCast candidates into live registries. Requires an open safe frame.
        /// Does not Clear registries.
        /// </summary>
        public LiveApplyCommitResult CommitNextCastSafeFrame()
        {
            if (!_safeFrameOpen)
            {
                return new LiveApplyCommitResult(
                    false,
                    0,
                    new[]
                    {
                        new LiveEditDiagnostic(
                            LiveEditDiagnosticSeverity.Error,
                            LiveEditDiagnosticCodes.SafeFrameRequired,
                            "NextCastLiveApply requires an open safe frame (BeginSafeFrame).")
                    });
            }

            var diagnostics = new List<LiveEditDiagnostic>(2);
            int applied = 0;

            for (int i = 0; i < _stagedGraphs.Count; i++)
            {
                StagedGraphCandidate c = _stagedGraphs[i];
                try
                {
                    _graphs.ReplaceProgram(c.GraphId, c.Program, c.Kind, c.SourceMap);
                    applied++;
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.GraphCompileFailed,
                        ex.Message,
                        c.GraphKey));
                }
            }

            for (int i = 0; i < _stagedEffects.Count; i++)
            {
                StagedEffectNumericCandidate c = _stagedEffects[i];
                if (_effects == null)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.EffectTemplateMissing,
                        "EffectTemplateRegistry was not provided to LiveGasEditPipeline.",
                        c.DefinitionId));
                    continue;
                }

                if (!_effects.TryReplaceHotNumericField(c.TemplateId, c.FieldPath, c.NumericValue, out string? reason))
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.EffectFieldNotHotEditable,
                        reason ?? "Effect numeric replace failed.",
                        c.DefinitionId));
                    continue;
                }

                applied++;
            }

            for (int i = 0; i < _stagedTagRules.Count; i++)
            {
                StagedTagRuleCandidate c = _stagedTagRules[i];
                if (_tagOps == null)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.TagRuleMissing,
                        "TagOps was not provided to LiveGasEditPipeline.",
                        c.TagKey));
                    continue;
                }

                try
                {
                    _tagOps.ReplaceTagRuleSet(c.TagId, c.RuleSet);
                    applied++;
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.TagRuleCompileFailed,
                        ex.Message,
                        c.TagKey));
                }
            }

            for (int i = 0; i < _stagedAttrConstraints.Count; i++)
            {
                StagedAttrConstraintCandidate c = _stagedAttrConstraints[i];
                try
                {
                    AttributeRegistry.ReplaceConstraints(c.AttributeId, c.Constraints);
                    applied++;
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.AttrConstraintMissing,
                        ex.Message,
                        c.AttributeName));
                }
            }

            for (int i = 0; i < _stagedEffectRefs.Count; i++)
            {
                StagedEffectRefCandidate c = _stagedEffectRefs[i];
                if (_effects == null)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.EffectTemplateMissing,
                        "EffectTemplateRegistry was not provided.",
                        c.DefinitionId));
                    continue;
                }

                if (!_effects.TryReplaceHotProjectileEffectRef(
                        c.TemplateId, c.FieldPath, c.TargetEffectTemplateId, out string? reason))
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.EffectFieldNotHotEditable,
                        reason ?? "Effect ref replace failed.",
                        c.DefinitionId));
                    continue;
                }

                applied++;
            }

            for (int i = 0; i < _stagedGrantedTags.Count; i++)
            {
                StagedEffectGrantedTagCandidate c = _stagedGrantedTags[i];
                if (_effects == null)
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.EffectTemplateMissing,
                        "EffectTemplateRegistry was not provided.",
                        c.DefinitionId));
                    continue;
                }

                if (!_effects.TryReplaceHotGrantedTagFixed(c.TemplateId, c.TagId, c.Amount, out string? reason))
                {
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.EffectFieldNotHotEditable,
                        reason ?? "Granted tag replace failed.",
                        c.DefinitionId));
                    continue;
                }

                applied++;
            }

            _stagedGraphs.Clear();
            _stagedEffects.Clear();
            _stagedTagRules.Clear();
            _stagedAttrConstraints.Clear();
            _stagedEffectRefs.Clear();
            _stagedGrantedTags.Clear();

            if (diagnostics.Count > 0)
            {
                return new LiveApplyCommitResult(false, applied, diagnostics);
            }

            return new LiveApplyCommitResult(true, applied, Array.Empty<LiveEditDiagnostic>());
        }

        private void ClassifyEffectNumeric(
            in LiveDebugPatchOperation op,
            List<LiveApplyClassificationItem> items,
            ref bool canNextCast,
            ref bool mapReload,
            ref bool engineRestart)
        {
            string definitionId = op.DefinitionId ?? string.Empty;
            string fieldPath = op.FieldPath ?? string.Empty;

            if (_effects == null)
            {
                mapReload = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    definitionId,
                    LiveApplyMode.MapReloadRequired,
                    "EffectTemplateRegistry is unavailable in this host; map reload is required.",
                    new[]
                    {
                        new LiveEditDiagnostic(
                            LiveEditDiagnosticSeverity.Error,
                            LiveEditDiagnosticCodes.EffectTemplateMissing,
                            "EffectTemplateRegistry is null.",
                            definitionId)
                    }));
                return;
            }

            int templateId = EffectTemplateIdRegistry.GetId(definitionId);
            if (templateId == EffectTemplateIdRegistry.InvalidId)
            {
                mapReload = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    definitionId,
                    LiveApplyMode.MapReloadRequired,
                    $"Effect template '{definitionId}' is not registered in the current map.",
                    new[]
                    {
                        new LiveEditDiagnostic(
                            LiveEditDiagnosticSeverity.Error,
                            LiveEditDiagnosticCodes.EffectTemplateMissing,
                            $"Unknown effect template '{definitionId}'.",
                            definitionId)
                    }));
                return;
            }

            if (!IsHotEditableEffectField(fieldPath))
            {
                mapReload = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    definitionId,
                    LiveApplyMode.MapReloadRequired,
                    $"Field '{fieldPath}' is not NextCast-hot-editable.",
                    new[]
                    {
                        new LiveEditDiagnostic(
                            LiveEditDiagnosticSeverity.Warning,
                            LiveEditDiagnosticCodes.EffectFieldNotHotEditable,
                            $"Field '{fieldPath}' requires MapReload.",
                            definitionId)
                    }));
                return;
            }

            _stagedEffects.Add(new StagedEffectNumericCandidate
            {
                DefinitionId = definitionId,
                TemplateId = templateId,
                FieldPath = fieldPath,
                NumericValue = op.NumericValue
            });
            canNextCast = true;
            items.Add(new LiveApplyClassificationItem(
                op.Kind,
                definitionId,
                LiveApplyMode.NextCastLiveApply,
                $"Effect field '{fieldPath}' will apply on the next safe frame (NextCast).",
                Array.Empty<LiveEditDiagnostic>()));
        }

        private void ClassifyGraphBody(
            in LiveDebugPatchOperation op,
            List<LiveApplyClassificationItem> items,
            ref bool canNextCast,
            ref bool mapReload,
            ref bool engineRestart)
        {
            string graphKey = op.DefinitionId ?? string.Empty;
            var diags = new List<LiveEditDiagnostic>(2);

            JsonObject? obj;
            try
            {
                obj = JsonNode.Parse(op.DocumentJson!) as JsonObject;
            }
            catch (Exception ex)
            {
                engineRestart = false;
                mapReload = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.GraphCompileFailed,
                    $"Invalid graph JSON: {ex.Message}",
                    graphKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    graphKey,
                    LiveApplyMode.MapReloadRequired,
                    "Graph JSON failed to parse; live registry untouched.",
                    diags));
                return;
            }

            if (obj == null)
            {
                mapReload = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.MissingGraphDocument,
                    "Graph document must be a JSON object.",
                    graphKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    graphKey,
                    LiveApplyMode.MapReloadRequired,
                    "Graph document missing.",
                    diags));
                return;
            }

            GraphControlFlowCompileResult compile =
                GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphKey, _jsonOptions);

            bool hasCompileErrors = false;
            for (int d = 0; d < compile.Diagnostics.Count; d++)
            {
                if (compile.Diagnostics[d].Severity == GraphDiagnosticSeverity.Error)
                {
                    hasCompileErrors = true;
                    diags.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.GraphCompileFailed,
                        compile.Diagnostics[d].Message,
                        graphKey));
                }
            }

            if (compile.Package == null || hasCompileErrors)
            {
                mapReload = true;
                if (diags.Count == 0)
                {
                    diags.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.GraphCompileFailed,
                        "Graph compile returned null package.",
                        graphKey));
                }

                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    graphKey,
                    LiveApplyMode.MapReloadRequired,
                    "Graph candidate compile failed; live registry untouched.",
                    diags));
                return;
            }

            GraphProgramPackage package = compile.Package.Value;
            int graphId = GraphIdRegistry.GetId(graphKey);
            if (graphId == GraphIdRegistry.InvalidId)
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.GraphIdentityChanged,
                    $"Graph key '{graphKey}' is not registered; new graph ids require EngineRestart.",
                    graphKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    graphKey,
                    LiveApplyMode.EngineRestartRequired,
                    "New graph identity cannot be hot-applied.",
                    diags));
                return;
            }

            if (!_graphs.TryGetKind(graphId, out GraphKind liveKind))
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.GraphIdentityChanged,
                    $"Graph id {graphId} ('{graphKey}') has no live program; EngineRestart required.",
                    graphKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    graphKey,
                    LiveApplyMode.EngineRestartRequired,
                    "Missing live program for graph id.",
                    diags));
                return;
            }

            if (liveKind != package.Kind)
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.GraphIdentityChanged,
                    $"Graph kind change '{liveKind}' → '{package.Kind}' is forbidden on hot path.",
                    graphKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    graphKey,
                    LiveApplyMode.EngineRestartRequired,
                    "Graph kind identity changed.",
                    diags));
                return;
            }

            _stagedGraphs.Add(new StagedGraphCandidate
            {
                GraphKey = graphKey,
                GraphId = graphId,
                Kind = package.Kind,
                Program = package.Program,
                SourceMap = compile.SourceMap
            });
            canNextCast = true;
            items.Add(new LiveApplyClassificationItem(
                op.Kind,
                graphKey,
                LiveApplyMode.NextCastLiveApply,
                "Graph body candidate compiled; will ReplaceProgram on safe frame (NextCast).",
                Array.Empty<LiveEditDiagnostic>()));
        }

        private void ClassifyTagRuleBody(
            in LiveDebugPatchOperation op,
            List<LiveApplyClassificationItem> items,
            ref bool canNextCast,
            ref bool mapReload,
            ref bool engineRestart)
        {
            string tagKey = op.DefinitionId ?? string.Empty;
            var diags = new List<LiveEditDiagnostic>(2);

            if (_tagOps == null)
            {
                mapReload = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    tagKey,
                    LiveApplyMode.MapReloadRequired,
                    "TagOps is unavailable in this host; map reload is required.",
                    new[]
                    {
                        new LiveEditDiagnostic(
                            LiveEditDiagnosticSeverity.Error,
                            LiveEditDiagnosticCodes.TagRuleMissing,
                            "TagOps is null.",
                            tagKey)
                    }));
                return;
            }

            JsonObject? obj;
            try
            {
                obj = JsonNode.Parse(op.DocumentJson!) as JsonObject;
            }
            catch (Exception ex)
            {
                mapReload = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.TagRuleCompileFailed,
                    $"Invalid tag rule JSON: {ex.Message}",
                    tagKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    tagKey,
                    LiveApplyMode.MapReloadRequired,
                    "Tag rule JSON failed to parse; live registry untouched.",
                    diags));
                return;
            }

            if (obj == null)
            {
                mapReload = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.TagRuleCompileFailed,
                    "Tag rule document must be a JSON object.",
                    tagKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    tagKey,
                    LiveApplyMode.MapReloadRequired,
                    "Tag rule document missing.",
                    diags));
                return;
            }

            int tagId = TagRegistry.GetId(tagKey);
            if (tagId == TagRegistry.InvalidId)
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.TagRuleMissing,
                    $"Tag key '{tagKey}' is not registered; new tag identities require EngineRestart.",
                    tagKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    tagKey,
                    LiveApplyMode.EngineRestartRequired,
                    "New tag identity cannot be hot-applied.",
                    diags));
                return;
            }

            if (!_tagOps.HasTagRule(tagId))
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.TagRuleMissing,
                    $"Tag '{tagKey}' has no live rule set; EngineRestart required.",
                    tagKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    tagKey,
                    LiveApplyMode.EngineRestartRequired,
                    "Missing live tag rule for tag id.",
                    diags));
                return;
            }

            TagRuleSet ruleSet;
            try
            {
                ruleSet = TagRuleSetLoader.CompileRuleSetForHotApply(obj, tagKey, "live-edit://tag_rules");
            }
            catch (Exception ex)
            {
                // Unknown referenced tag names expand identity → EngineRestart.
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.TagRuleCompileFailed,
                    ex.Message,
                    tagKey));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    tagKey,
                    LiveApplyMode.EngineRestartRequired,
                    "Tag rule candidate compile failed (identity or reference invalid).",
                    diags));
                return;
            }

            _stagedTagRules.Add(new StagedTagRuleCandidate
            {
                TagKey = tagKey,
                TagId = tagId,
                RuleSet = ruleSet
            });
            canNextCast = true;
            items.Add(new LiveApplyClassificationItem(
                op.Kind,
                tagKey,
                LiveApplyMode.NextCastLiveApply,
                "Tag rule body candidate compiled; will ReplaceTagRuleSet on safe frame (NextCast).",
                Array.Empty<LiveEditDiagnostic>()));
        }

        private void ClassifyAttrConstraint(
            in LiveDebugPatchOperation op,
            List<LiveApplyClassificationItem> items,
            ref bool canNextCast,
            ref bool mapReload,
            ref bool engineRestart)
        {
            string attributeName = op.DefinitionId ?? string.Empty;
            string fieldPath = op.FieldPath ?? string.Empty;
            var diags = new List<LiveEditDiagnostic>(2);

            int attributeId = AttributeRegistry.GetId(attributeName);
            if (attributeId == AttributeRegistry.InvalidId)
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.AttrConstraintMissing,
                    $"Attribute '{attributeName}' is not registered; new attribute identities require EngineRestart.",
                    attributeName));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    attributeName,
                    LiveApplyMode.EngineRestartRequired,
                    "New attribute identity cannot be hot-applied.",
                    diags));
                return;
            }

            if (!AttributeRegistry.TryGetConstraints(attributeId, out AttributeRegistry.AttributeConstraints existing) ||
                !existing.HasAny)
            {
                engineRestart = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.AttrConstraintMissing,
                    $"Attribute '{attributeName}' has no authored constraints; introducing constraints requires EngineRestart.",
                    attributeName));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    attributeName,
                    LiveApplyMode.EngineRestartRequired,
                    "Attribute constraint schema missing.",
                    diags));
                return;
            }

            if (!TryBuildReplacedConstraints(in existing, fieldPath, op.NumericValue, out AttributeRegistry.AttributeConstraints next, out string? failReason))
            {
                mapReload = true;
                diags.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.AttrConstraintFieldInvalid,
                    failReason ?? "Attr constraint field is not hot-editable.",
                    attributeName));
                items.Add(new LiveApplyClassificationItem(
                    op.Kind,
                    attributeName,
                    LiveApplyMode.MapReloadRequired,
                    $"Field '{fieldPath}' is not NextCast-hot-editable for constraints.",
                    diags));
                return;
            }

            _stagedAttrConstraints.Add(new StagedAttrConstraintCandidate
            {
                AttributeName = attributeName,
                AttributeId = attributeId,
                Constraints = next
            });
            canNextCast = true;
            items.Add(new LiveApplyClassificationItem(
                op.Kind,
                attributeName,
                LiveApplyMode.NextCastLiveApply,
                $"Attribute constraint '{fieldPath}' will apply on the next safe frame (NextCast).",
                Array.Empty<LiveEditDiagnostic>()));
        }

        private static bool TryBuildReplacedConstraints(
            in AttributeRegistry.AttributeConstraints existing,
            string fieldPath,
            double numericValue,
            out AttributeRegistry.AttributeConstraints next,
            out string? failureReason)
        {
            next = default;
            failureReason = null;
            if (string.IsNullOrWhiteSpace(fieldPath))
            {
                failureReason = "fieldPath is required.";
                return false;
            }

            string path = fieldPath.Trim();
            float value = (float)numericValue;
            bool clamp = existing.ClampCurrentToBase;
            bool hasMin = existing.HasMin;
            float min = existing.Min;
            bool hasMax = existing.HasMax;
            float max = existing.Max;

            if (path.Equals("constraints.min", StringComparison.OrdinalIgnoreCase)
                || path.Equals("min", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasMin)
                {
                    failureReason = "Attribute has no min constraint to replace.";
                    return false;
                }

                min = value;
            }
            else if (path.Equals("constraints.max", StringComparison.OrdinalIgnoreCase)
                     || path.Equals("max", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasMax)
                {
                    failureReason = "Attribute has no max constraint to replace.";
                    return false;
                }

                max = value;
            }
            else
            {
                failureReason =
                    $"Field path '{fieldPath}' is not NextCast-hot-editable; use constraints.min or constraints.max.";
                return false;
            }

            if (hasMin && hasMax && min > max)
            {
                failureReason = $"Constraint min ({min}) cannot exceed max ({max}).";
                return false;
            }

            next = AttributeRegistry.AttributeConstraints.Create(clamp, hasMin, min, hasMax, max);
            return true;
        }

        private void ClassifyEffectRef(
            in LiveDebugPatchOperation op,
            List<LiveApplyClassificationItem> items,
            ref bool canNextCast,
            ref bool mapReload,
            ref bool engineRestart)
        {
            string definitionId = op.DefinitionId ?? string.Empty;
            string fieldPath = op.FieldPath ?? string.Empty;
            string targetName = op.DocumentJson ?? string.Empty;
            if (_effects == null)
            {
                mapReload = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind, definitionId, LiveApplyMode.MapReloadRequired,
                    "EffectTemplateRegistry unavailable.",
                    Array.Empty<LiveEditDiagnostic>()));
                return;
            }

            int templateId = EffectTemplateIdRegistry.GetId(definitionId);
            int targetId = EffectTemplateIdRegistry.GetId(targetName);
            if (templateId == EffectTemplateIdRegistry.InvalidId || targetId == EffectTemplateIdRegistry.InvalidId)
            {
                engineRestart = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind, definitionId, LiveApplyMode.EngineRestartRequired,
                    $"Unknown effect id '{definitionId}' or target '{targetName}'.",
                    Array.Empty<LiveEditDiagnostic>()));
                return;
            }

            _stagedEffectRefs.Add(new StagedEffectRefCandidate
            {
                DefinitionId = definitionId,
                TemplateId = templateId,
                FieldPath = fieldPath,
                TargetEffectTemplateId = targetId
            });
            canNextCast = true;
            items.Add(new LiveApplyClassificationItem(
                op.Kind, definitionId, LiveApplyMode.NextCastLiveApply,
                $"Projectile ref '{fieldPath}' -> '{targetName}' on NextCast.",
                Array.Empty<LiveEditDiagnostic>()));
        }

        private void ClassifyGrantedTag(
            in LiveDebugPatchOperation op,
            List<LiveApplyClassificationItem> items,
            ref bool canNextCast,
            ref bool mapReload,
            ref bool engineRestart)
        {
            string definitionId = op.DefinitionId ?? string.Empty;
            string tagName = op.DocumentJson ?? string.Empty;
            if (_effects == null)
            {
                mapReload = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind, definitionId, LiveApplyMode.MapReloadRequired,
                    "EffectTemplateRegistry unavailable.",
                    Array.Empty<LiveEditDiagnostic>()));
                return;
            }

            int templateId = EffectTemplateIdRegistry.GetId(definitionId);
            int tagId = TagRegistry.GetId(tagName);
            if (templateId == EffectTemplateIdRegistry.InvalidId)
            {
                engineRestart = true;
                items.Add(new LiveApplyClassificationItem(
                    op.Kind, definitionId, LiveApplyMode.EngineRestartRequired,
                    $"Unknown effect '{definitionId}'.",
                    Array.Empty<LiveEditDiagnostic>()));
                return;
            }

            if (tagId == TagRegistry.InvalidId)
            {
                // Allow Register only if not frozen; otherwise EngineRestart.
                if (!TagRegistry.IsFrozen)
                {
                    tagId = TagRegistry.Register(tagName);
                }
                else
                {
                    engineRestart = true;
                    items.Add(new LiveApplyClassificationItem(
                        op.Kind, definitionId, LiveApplyMode.EngineRestartRequired,
                        $"Unknown tag '{tagName}' while TagRegistry is frozen.",
                        Array.Empty<LiveEditDiagnostic>()));
                    return;
                }
            }

            _stagedGrantedTags.Add(new StagedEffectGrantedTagCandidate
            {
                DefinitionId = definitionId,
                TemplateId = templateId,
                TagId = tagId,
                Amount = (ushort)Math.Clamp((int)Math.Round(op.NumericValue), 1, 32)
            });
            canNextCast = true;
            items.Add(new LiveApplyClassificationItem(
                op.Kind, definitionId, LiveApplyMode.NextCastLiveApply,
                $"Granted tag '{tagName}' on NextCast.",
                Array.Empty<LiveEditDiagnostic>()));
        }

        private static bool IsHotEditableEffectField(string fieldPath)
        {
            if (string.IsNullOrWhiteSpace(fieldPath)) return false;
            string path = fieldPath.Trim();
            return path.Equals("duration.durationTicks", StringComparison.OrdinalIgnoreCase)
                || path.Equals("DurationTicks", StringComparison.OrdinalIgnoreCase)
                || path.Equals("duration.periodTicks", StringComparison.OrdinalIgnoreCase)
                || path.Equals("PeriodTicks", StringComparison.OrdinalIgnoreCase)
                || path.Equals("modifiers.0.value", StringComparison.OrdinalIgnoreCase)
                || path.Equals("modifiers[0].value", StringComparison.OrdinalIgnoreCase);
        }
    }
}
