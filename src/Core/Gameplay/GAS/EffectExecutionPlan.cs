using System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Core.Gameplay.GAS
{
    public enum EffectOperationKind : byte
    {
        None = 0,
        Pure = 1,
        GasTransactional = 2,
        DelegatedBuiltin = 3,
        ExternalAtomicExclusive = 4,
        Unsupported = 5,
    }

    public enum EffectAtomicDomain : byte
    {
        None = 0,
        Displacement = 1,
        Progression = 2,
        Order = 3,
        Exchange = 4,
        Lifecycle = 5,
        Vision = 6,
        Relationship = 7,
    }

    public readonly struct EffectOperationMetadata
    {
        public EffectOperationMetadata(EffectOperationKind kind, EffectAtomicDomain domain, string name)
        {
            if (kind == EffectOperationKind.None)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Effect operation metadata requires a name.", nameof(name));
            }
            if (kind == EffectOperationKind.ExternalAtomicExclusive && domain == EffectAtomicDomain.None)
            {
                throw new ArgumentException("External atomic operations require a domain.", nameof(domain));
            }

            Kind = kind;
            Domain = domain;
            Name = name;
        }

        public EffectOperationKind Kind { get; }
        public EffectAtomicDomain Domain { get; }
        public string Name { get; }

        public static EffectOperationMetadata Pure(string name)
            => new(EffectOperationKind.Pure, EffectAtomicDomain.None, name);

        public static EffectOperationMetadata GasTransactional(string name)
            => new(EffectOperationKind.GasTransactional, EffectAtomicDomain.None, name);

        public static EffectOperationMetadata DelegatedBuiltin(string name)
            => new(EffectOperationKind.DelegatedBuiltin, EffectAtomicDomain.None, name);

        public static EffectOperationMetadata External(EffectAtomicDomain domain, string name)
            => new(EffectOperationKind.ExternalAtomicExclusive, domain, name);

        public static EffectOperationMetadata Unsupported(EffectAtomicDomain domain, string name)
            => new(EffectOperationKind.Unsupported, domain, name);
    }

    public enum EffectExecutionPlanKind : byte
    {
        Unfinalized = 0,
        GasTransactional = 1,
        ExternalAtomicExclusive = 2,
    }

    public readonly struct EffectWindowExecutionPlan
    {
        public EffectWindowExecutionPlan(
            EffectExecutionPlanKind kind,
            EffectAtomicDomain domain = EffectAtomicDomain.None,
            EffectPhaseId externalPhase = default,
            bool requiresListenerPreflight = false)
        {
            if (kind == EffectExecutionPlanKind.Unfinalized)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            Kind = kind;
            Domain = domain;
            ExternalPhase = externalPhase;
            RequiresListenerPreflight = requiresListenerPreflight;
        }

        public EffectExecutionPlanKind Kind { get; }
        public EffectAtomicDomain Domain { get; }
        public EffectPhaseId ExternalPhase { get; }
        public bool RequiresListenerPreflight { get; }
    }

    public readonly struct EffectExecutionPlanSet
    {
        public EffectExecutionPlanSet(
            in EffectWindowExecutionPlan activation,
            in EffectWindowExecutionPlan period,
            in EffectWindowExecutionPlan expire,
            in EffectWindowExecutionPlan remove)
        {
            Activation = activation;
            Period = period;
            Expire = expire;
            Remove = remove;
        }

        public EffectWindowExecutionPlan Activation { get; }
        public EffectWindowExecutionPlan Period { get; }
        public EffectWindowExecutionPlan Expire { get; }
        public EffectWindowExecutionPlan Remove { get; }
    }

    public static class EffectExecutionPlanCompiler
    {
        public const string InvalidCompositionError = "GAS.EFFECT_PLAN.ERR.InvalidComposition";
        public const string UnsupportedOperationError = "GAS.EFFECT_PLAN.ERR.UnsupportedOperation";
        public const string MissingOperationMetadataError = "GAS.EFFECT_PLAN.ERR.MissingOperationMetadata";

        public static void FinalizeAll(
            EffectTemplateRegistry templates,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GraphProgramRegistry graphPrograms,
            GasGraphOpHandlerTable graphHandlers,
            string assetPath = "GAS/effects.json")
        {
            ArgumentNullException.ThrowIfNull(templates);
            ArgumentNullException.ThrowIfNull(presetTypes);
            ArgumentNullException.ThrowIfNull(builtinHandlers);
            ArgumentNullException.ThrowIfNull(graphPrograms);
            ArgumentNullException.ThrowIfNull(graphHandlers);

            for (int templateId = 1; templateId < EffectTemplateRegistry.MaxTemplates; templateId++)
            {
                if (!templates.TryGetRef(templateId, out int templateIndex))
                {
                    continue;
                }

                ref readonly EffectTemplateData template = ref templates.GetRef(templateIndex);
                string effectName = EffectTemplateIdRegistry.GetName(templateId);
                if (string.IsNullOrEmpty(effectName))
                {
                    effectName = templateId.ToString();
                }

                CompilePurePhase(
                    templateId,
                    effectName,
                    assetPath,
                    in template,
                    EffectPhaseId.OnPropose,
                    presetTypes,
                    builtinHandlers,
                    graphPrograms,
                    graphHandlers);
                CompilePurePhase(
                    templateId,
                    effectName,
                    assetPath,
                    in template,
                    EffectPhaseId.OnCalculate,
                    presetTypes,
                    builtinHandlers,
                    graphPrograms,
                    graphHandlers);

                WindowAccumulator activation = default;
                AnalyzePhase(templateId, effectName, assetPath, in template, EffectPhaseId.OnResolve, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref activation);
                AnalyzePhase(templateId, effectName, assetPath, in template, EffectPhaseId.OnHit, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref activation);
                AnalyzePhase(templateId, effectName, assetPath, in template, EffectPhaseId.OnApply, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref activation);

                WindowAccumulator period = default;
                AnalyzePhase(templateId, effectName, assetPath, in template, EffectPhaseId.OnPeriod, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref period);
                WindowAccumulator expire = default;
                AnalyzePhase(templateId, effectName, assetPath, in template, EffectPhaseId.OnExpire, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref expire);
                WindowAccumulator remove = default;
                AnalyzePhase(templateId, effectName, assetPath, in template, EffectPhaseId.OnRemove, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref remove);

                EffectWindowExecutionPlan activationPlan = CompileWindow(
                    templateId,
                    effectName,
                    assetPath,
                    in template,
                    "Activation",
                    allowExternal: true,
                    in activation);
                EffectWindowExecutionPlan periodPlan = CompileWindow(templateId, effectName, assetPath, in template, "Period", allowExternal: false, in period);
                EffectWindowExecutionPlan expirePlan = CompileWindow(templateId, effectName, assetPath, in template, "Expire", allowExternal: false, in expire);
                EffectWindowExecutionPlan removePlan = CompileWindow(templateId, effectName, assetPath, in template, "Remove", allowExternal: false, in remove);
                var plans = new EffectExecutionPlanSet(in activationPlan, in periodPlan, in expirePlan, in removePlan);
                templates.FinalizeExecutionPlans(templateId, in plans);
            }

            templates.CompleteExecutionPlanFinalization();
        }

        private static void CompilePurePhase(
            int templateId,
            string effectName,
            string assetPath,
            in EffectTemplateData template,
            EffectPhaseId phase,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GraphProgramRegistry graphPrograms,
            GasGraphOpHandlerTable graphHandlers)
        {
            WindowAccumulator accumulator = default;
            AnalyzePhase(templateId, effectName, assetPath, in template, phase, presetTypes, builtinHandlers, graphPrograms, graphHandlers, ref accumulator);
            if (accumulator.NonPureCount > 0)
            {
                throw CompositionError(
                    InvalidCompositionError,
                    assetPath,
                    templateId,
                    effectName,
                    phase,
                    accumulator.LastOperationName,
                    "OnPropose and OnCalculate only allow pure operations.");
            }
        }

        private static EffectWindowExecutionPlan CompileWindow(
            int templateId,
            string effectName,
            string assetPath,
            in EffectTemplateData template,
            string windowName,
            bool allowExternal,
            in WindowAccumulator accumulator)
        {
            if (accumulator.ExternalCount == 0)
            {
                return new EffectWindowExecutionPlan(EffectExecutionPlanKind.GasTransactional);
            }

            if (!allowExternal || template.LifetimeKind != EffectLifetimeKind.Instant)
            {
                throw CompositionError(
                    InvalidCompositionError,
                    assetPath,
                    templateId,
                    effectName,
                    accumulator.ExternalPhase,
                    accumulator.ExternalOperationName,
                    $"External atomic operations are not allowed in the {windowName} window or persistent effects.");
            }
            if (accumulator.ExternalCount != 1 || accumulator.GasTransactionalCount != 0)
            {
                throw CompositionError(
                    InvalidCompositionError,
                    assetPath,
                    templateId,
                    effectName,
                    accumulator.ExternalPhase,
                    accumulator.ExternalOperationName,
                    "External atomic activation must contain exactly one external operation and no GAS transactional writes.");
            }
            if (accumulator.ExternalOperationIndex != accumulator.OperationCount - 1)
            {
                throw CompositionError(
                    InvalidCompositionError,
                    assetPath,
                    templateId,
                    effectName,
                    accumulator.ExternalPhase,
                    accumulator.ExternalOperationName,
                    "External atomic activation must be the final executable operation after Pre/Main/Post resolution.");
            }
            if (template.Modifiers.Count > 0 || template.GrantedTags.Count > 0 || template.ListenerSetup.Count > 0)
            {
                throw CompositionError(
                    InvalidCompositionError,
                    assetPath,
                    templateId,
                    effectName,
                    accumulator.ExternalPhase,
                    accumulator.ExternalOperationName,
                    "External atomic activation cannot be combined with modifiers, granted tags, or listener setup.");
            }

            return new EffectWindowExecutionPlan(
                EffectExecutionPlanKind.ExternalAtomicExclusive,
                accumulator.ExternalDomain,
                accumulator.ExternalPhase,
                requiresListenerPreflight: true);
        }

        private static void AnalyzePhase(
            int templateId,
            string effectName,
            string assetPath,
            in EffectTemplateData template,
            EffectPhaseId phase,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GraphProgramRegistry graphPrograms,
            GasGraphOpHandlerTable graphHandlers,
            ref WindowAccumulator accumulator)
        {
            GraphKind expectedGraphKind = phase == EffectPhaseId.OnPropose
                ? GraphKind.Validation
                : GraphKind.Effect;
            int preGraphId = template.PhaseGraphBindings.GetGraphId(phase, PhaseSlot.Pre);
            if (preGraphId > 0)
            {
                AnalyzeGraph(templateId, effectName, assetPath, phase, expectedGraphKind, preGraphId, builtinHandlers, graphPrograms, graphHandlers, ref accumulator);
            }

            int mainGraphId = template.PhaseGraphBindings.GetGraphId(phase, PhaseSlot.Main);
            if (mainGraphId > 0)
            {
                AnalyzeGraph(templateId, effectName, assetPath, phase, expectedGraphKind, mainGraphId, builtinHandlers, graphPrograms, graphHandlers, ref accumulator);
            }
            else if (!template.PhaseGraphBindings.IsSkipMain(phase) && presetTypes.IsRegistered(template.PresetType))
            {
                ref readonly PresetTypeDefinition preset = ref presetTypes.Get(template.PresetType);
                PhaseHandler handler = preset.DefaultPhaseHandlers[phase];
                if (handler.IsValid)
                {
                    if (handler.Kind == PhaseHandlerKind.Builtin)
                    {
                        AddBuiltin(templateId, effectName, assetPath, phase, (BuiltinHandlerId)handler.HandlerId, builtinHandlers, ref accumulator);
                    }
                    else if (handler.Kind == PhaseHandlerKind.Graph)
                    {
                        AnalyzeGraph(templateId, effectName, assetPath, phase, expectedGraphKind, handler.HandlerId, builtinHandlers, graphPrograms, graphHandlers, ref accumulator);
                    }
                    else
                    {
                        throw CompositionError(InvalidCompositionError, assetPath, templateId, effectName, phase, handler.Kind.ToString(), "Unsupported phase handler kind.");
                    }
                }
            }

            int postGraphId = template.PhaseGraphBindings.GetGraphId(phase, PhaseSlot.Post);
            if (postGraphId > 0)
            {
                AnalyzeGraph(templateId, effectName, assetPath, phase, expectedGraphKind, postGraphId, builtinHandlers, graphPrograms, graphHandlers, ref accumulator);
            }
        }

        private static void AnalyzeGraph(
            int templateId,
            string effectName,
            string assetPath,
            EffectPhaseId phase,
            GraphKind expectedGraphKind,
            int graphId,
            BuiltinHandlerRegistry builtinHandlers,
            GraphProgramRegistry graphPrograms,
            GasGraphOpHandlerTable graphHandlers,
            ref WindowAccumulator accumulator)
        {
            graphPrograms.RequireKind(graphId, expectedGraphKind);
            if (!graphPrograms.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw CompositionError(InvalidCompositionError, assetPath, templateId, effectName, phase, GraphIdRegistry.GetName(graphId), "Graph program is not registered.");
            }

            for (int i = 0; i < program.Length; i++)
            {
                ref readonly GraphInstruction instruction = ref program[i];
                if (instruction.Op == 0)
                {
                    continue;
                }

                GraphNodeOp op = (GraphNodeOp)instruction.Op;
                if (!graphHandlers.TryGetOperationMetadata(op, out EffectOperationMetadata metadata))
                {
                    throw CompositionError(MissingOperationMetadataError, assetPath, templateId, effectName, phase, op.ToString(), "Graph opcode has no operation metadata.");
                }
                if (metadata.Kind == EffectOperationKind.DelegatedBuiltin)
                {
                    AddBuiltin(templateId, effectName, assetPath, phase, (BuiltinHandlerId)instruction.Imm, builtinHandlers, ref accumulator);
                }
                else
                {
                    AddOperation(templateId, effectName, assetPath, phase, in metadata, ref accumulator);
                }
            }
        }

        private static void AddBuiltin(
            int templateId,
            string effectName,
            string assetPath,
            EffectPhaseId phase,
            BuiltinHandlerId handlerId,
            BuiltinHandlerRegistry builtinHandlers,
            ref WindowAccumulator accumulator)
        {
            if (!builtinHandlers.TryGetOperationMetadata(handlerId, out EffectOperationMetadata metadata))
            {
                throw CompositionError(MissingOperationMetadataError, assetPath, templateId, effectName, phase, handlerId.ToString(), "Builtin handler is not registered with operation metadata.");
            }
            AddOperation(templateId, effectName, assetPath, phase, in metadata, ref accumulator);
        }

        private static void AddOperation(
            int templateId,
            string effectName,
            string assetPath,
            EffectPhaseId phase,
            in EffectOperationMetadata metadata,
            ref WindowAccumulator accumulator)
        {
            if (metadata.Kind == EffectOperationKind.Unsupported)
            {
                throw CompositionError(UnsupportedOperationError, assetPath, templateId, effectName, phase, metadata.Name, $"Atomic domain '{metadata.Domain}' is not certified for Effect execution.");
            }

            int operationIndex = accumulator.OperationCount++;
            accumulator.LastOperationName = metadata.Name;
            switch (metadata.Kind)
            {
                case EffectOperationKind.Pure:
                    return;
                case EffectOperationKind.GasTransactional:
                    accumulator.GasTransactionalCount++;
                    accumulator.NonPureCount++;
                    return;
                case EffectOperationKind.ExternalAtomicExclusive:
                    accumulator.ExternalCount++;
                    accumulator.NonPureCount++;
                    accumulator.ExternalOperationIndex = operationIndex;
                    accumulator.ExternalDomain = metadata.Domain;
                    accumulator.ExternalPhase = phase;
                    accumulator.ExternalOperationName = metadata.Name;
                    return;
                default:
                    throw CompositionError(MissingOperationMetadataError, assetPath, templateId, effectName, phase, metadata.Name, $"Unsupported operation metadata kind '{metadata.Kind}'.");
            }
        }

        private static InvalidOperationException CompositionError(
            string code,
            string assetPath,
            int templateId,
            string effectName,
            EffectPhaseId phase,
            string operation,
            string reason)
        {
            return new InvalidOperationException(
                $"{code}: asset='{assetPath}', effect='{effectName}', templateId={templateId}, phase='{phase}', operation='{operation}'. {reason}");
        }

        private struct WindowAccumulator
        {
            public int OperationCount;
            public int NonPureCount;
            public int GasTransactionalCount;
            public int ExternalCount;
            public int ExternalOperationIndex;
            public EffectAtomicDomain ExternalDomain;
            public EffectPhaseId ExternalPhase;
            public string ExternalOperationName;
            public string LastOperationName;
        }
    }
}
