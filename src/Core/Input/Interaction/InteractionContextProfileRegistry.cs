using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Reference catalogs the profile install chain resolves <c>bindings[]</c>,
    /// <c>triggers[]</c>, and <c>whileActive</c> against (#1398 S2b / Case E §05): the graph
    /// program registry for trigger mounts and whileActive mounts (graphs that
    /// WriteCollection their preview collection), and the input action id space for
    /// semantic action bindings. A profile declaring any of those fields fails fast at install
    /// when its catalog is absent or incomplete.
    /// </summary>
    public sealed class InteractionContextProfileReferenceCatalog
    {
        public InteractionContextProfileReferenceCatalog(
            GraphProgramRegistry programs,
            IEnumerable<string> inputActionIds,
            GraphOutputSchemaRegistry? outputSchemas = null)
        {
            Programs = programs ?? throw new ArgumentNullException(nameof(programs));
            if (inputActionIds == null)
            {
                throw new ArgumentNullException(nameof(inputActionIds));
            }

            var actionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string actionId in inputActionIds)
            {
                if (!string.IsNullOrWhiteSpace(actionId))
                {
                    actionIds.Add(actionId);
                }
            }

            InputActionIds = actionIds;
            OutputSchemas = outputSchemas;
        }

        public GraphProgramRegistry Programs { get; }

        public IReadOnlyCollection<string> InputActionIds { get; }

        /// <summary>Required when any profile declares <c>whileActive</c>; null otherwise.</summary>
        public GraphOutputSchemaRegistry? OutputSchemas { get; }
    }

    /// <summary>
    /// InteractionContextProfile registry (RFC-0065 CTX-6, §5.3). Profiles are declared in
    /// <c>Input/interaction_context_profiles.json</c> and installed as immutable rows with
    /// every id field resolved up front: profile ids and input context ids register into this
    /// registry's own spaces, collection keys register into the shared
    /// <c>EntityCollectionStore</c> key space, and filter / command intent names must already
    /// be registered in their kernel registries (install those first — unknown names fail fast
    /// here). Context mounting (<see cref="TryCreateActiveContext"/>) is allocation free after
    /// install because it only copies pre-resolved ints.
    /// </summary>
    public sealed class InteractionContextProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly StringIntRegistry _inputContextIds;
        private InteractionContextProfileDefinition[] _profiles = new InteractionContextProfileDefinition[8];
        private int[] _collectionKeyIds = new int[8];
        private int[] _filterProfileIds = new int[8];
        private int[] _commandIntentProfileIds = new int[8];
        private int[] _inputContextIdsByProfile = new int[8];
        private int[] _whileActiveGraphIds = new int[8];

        public InteractionContextProfileRegistry(StringIntRegistry profileIdRegistry)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _inputContextIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        }

        /// <summary>Profile id space; ability declarations and mounts resolve profile names through it.</summary>
        public StringIntRegistry ProfileIdRegistry => _profileIds;

        /// <summary>
        /// IMC input context id space for profile-declared <c>inputContextId</c> values; the
        /// input context projection reads names back through it.
        /// </summary>
        public StringIntRegistry InputContextIdRegistry => _inputContextIds;

        /// <summary>
        /// Install every profile in the config, resolving id fields against the given spaces;
        /// fails fast on duplicates, unknown filter or command intent names. Profiles declaring
        /// <c>bindings[]</c> or <c>triggers[]</c> additionally require
        /// <paramref name="referenceCatalog"/> and resolve against it (unknown semantic action
        /// ids and trigger graph/event names fail fast).
        /// </summary>
        public void Install(
            InteractionContextProfilesConfig config,
            StringIntRegistry collectionKeyRegistry,
            StringIntRegistry filterProfileIdRegistry,
            StringIntRegistry commandIntentProfileIdRegistry,
            InteractionContextProfileReferenceCatalog? referenceCatalog = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (collectionKeyRegistry == null)
            {
                throw new ArgumentNullException(nameof(collectionKeyRegistry));
            }

            if (filterProfileIdRegistry == null)
            {
                throw new ArgumentNullException(nameof(filterProfileIdRegistry));
            }

            if (commandIntentProfileIdRegistry == null)
            {
                throw new ArgumentNullException(nameof(commandIntentProfileIdRegistry));
            }

            InteractionContextProfileConfigLoader.Validate(config, nameof(InteractionContextProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(
                    config.Profiles[i],
                    collectionKeyRegistry,
                    filterProfileIdRegistry,
                    commandIntentProfileIdRegistry,
                    referenceCatalog);
            }
        }

        /// <summary>True when the profile id has been installed.</summary>
        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        /// <summary>Installed profile row by id; false when not installed.</summary>
        public bool TryGetDefinition(int profileId, out InteractionContextProfileDefinition definition)
        {
            if (!IsInstalled(profileId))
            {
                definition = null!;
                return false;
            }

            definition = _profiles[profileId];
            return true;
        }

        /// <summary>
        /// Materialize the entity-mounted active context for the profile with
        /// <paramref name="contextEntity"/> as the carrier entity and
        /// <paramref name="source"/> as the mounting lifecycle. Returns false when the profile
        /// id is not installed. Allocation free after install.
        /// </summary>
        public bool TryCreateActiveContext(
            int profileId,
            Entity contextEntity,
            InteractionContextInstanceSource source,
            out InteractionContextInstance context)
        {
            if (!IsInstalled(profileId))
            {
                context = default;
                return false;
            }

            context = new InteractionContextInstance
            {
                ContextId = profileId,
                ContextEntity = contextEntity,
                ActiveCollectionKeyId = _collectionKeyIds[profileId],
                FilterProfileId = _filterProfileIds[profileId],
                CommandIntentProfileId = _commandIntentProfileIds[profileId],
                InputContextId = _inputContextIdsByProfile[profileId],
                Source = source,
            };
            return true;
        }

        /// <summary>
        /// WhileActive graph id for the profile (#1398 Case E §05); 0 when the profile
        /// declares no <c>whileActive</c>. Allocation free after install.
        /// </summary>
        public bool TryGetWhileActiveGraphId(int profileId, out int graphId)
        {
            if (!IsInstalled(profileId))
            {
                graphId = 0;
                return false;
            }

            graphId = _whileActiveGraphIds[profileId];
            return graphId > 0;
        }

        /// <summary>
        /// Steady-state routing anchor: the reserved default profile's resolved collection key
        /// and filter profile ids (the data-declared home of the retired engine default frame).
        /// Returns false when the default profile is not installed.
        /// </summary>
        public bool TryGetSteadyStateRouting(out int collectionKeyId, out int filterProfileId)
        {
            int defaultProfileId = _profileIds.GetId(InteractionContextIds.Default);
            if (!IsInstalled(defaultProfileId))
            {
                collectionKeyId = 0;
                filterProfileId = 0;
                return false;
            }

            collectionKeyId = _collectionKeyIds[defaultProfileId];
            filterProfileId = _filterProfileIds[defaultProfileId];
            return true;
        }

        private int _inputContextIdsFor(int profileId)
        {
            string inputContextId = _profiles[profileId].InputContextId;
            return string.IsNullOrWhiteSpace(inputContextId)
                ? _inputContextIds.InvalidId
                : _inputContextIds.Register(inputContextId);
        }

        private void InstallProfile(
            InteractionContextProfileDefinition definition,
            StringIntRegistry collectionKeyRegistry,
            StringIntRegistry filterProfileIdRegistry,
            StringIntRegistry commandIntentProfileIdRegistry,
            InteractionContextProfileReferenceCatalog? referenceCatalog)
        {
            int profileId = _profileIds.Register(definition.Id);
            if (profileId < _profiles.Length && _profiles[profileId] != null)
            {
                throw new InvalidOperationException($"Interaction context profile '{definition.Id}' is already installed.");
            }

            if (profileId >= _profiles.Length)
            {
                int next = _profiles.Length;
                while (next <= profileId)
                {
                    next *= 2;
                }

                Array.Resize(ref _profiles, next);
                Array.Resize(ref _collectionKeyIds, next);
                Array.Resize(ref _filterProfileIds, next);
                Array.Resize(ref _commandIntentProfileIds, next);
                Array.Resize(ref _inputContextIdsByProfile, next);
                Array.Resize(ref _whileActiveGraphIds, next);
            }

            int filterProfileId = ResolveDeclaredId(
                filterProfileIdRegistry,
                definition.FilterProfileId,
                definition.Id,
                nameof(definition.FilterProfileId),
                "filter profile");
            int commandIntentProfileId = ResolveDeclaredId(
                commandIntentProfileIdRegistry,
                definition.CommandIntentId,
                definition.Id,
                nameof(definition.CommandIntentId),
                "command intent profile");

            _profiles[profileId] = definition;
            _collectionKeyIds[profileId] = string.IsNullOrWhiteSpace(definition.ActiveCollectionKey)
                ? collectionKeyRegistry.InvalidId
                : collectionKeyRegistry.Register(definition.ActiveCollectionKey.Trim());
            _filterProfileIds[profileId] = filterProfileId;
            _commandIntentProfileIds[profileId] = commandIntentProfileId;
            _inputContextIdsByProfile[profileId] = _inputContextIdsFor(profileId);
            _whileActiveGraphIds[profileId] = ResolveWhileActiveGraphId(definition, referenceCatalog);
            ValidateBindings(definition, referenceCatalog);
            ValidateTriggers(definition, referenceCatalog);
        }

        private static int ResolveWhileActiveGraphId(
            InteractionContextProfileDefinition definition,
            InteractionContextProfileReferenceCatalog? referenceCatalog)
        {
            InteractionContextWhileActive? whileActive = definition.WhileActive;
            if (whileActive == null)
            {
                return 0;
            }

            if (referenceCatalog == null)
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{definition.Id}' declares whileActive but no reference catalog was provided at install; whileActive mounts require the graph program registry.");
            }

            string graphName = whileActive.Graph;
            int graphId = GraphIdRegistry.GetId(graphName);
            if (graphId == GraphIdRegistry.InvalidId ||
                !referenceCatalog.Programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{definition.Id}' whileActive.graph references unknown graph '{graphName}'.");
            }

            // Function-equivalence direction: continuous mount is host→function id, not a
            // GraphKind.Query privilege. Any kind is allowed if the program writes its preview
            // collection via WriteCollection (no GraphReturnWriter steal).
            if (!referenceCatalog.Programs.TryGetKind(graphId, out GraphKind mountedKind))
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{definition.Id}' whileActive.graph '{graphName}' has no registered kind.");
            }

            bool writesCollection = false;
            for (int i = 0; i < program.Length; i++)
            {
                if (program[i].Op == (ushort)GraphNodeOp.WriteCollection)
                {
                    writesCollection = true;
                    break;
                }
            }

            if (!writesCollection)
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{definition.Id}' whileActive.graph '{graphName}' must WriteCollection to write its preview collection; GraphReturnWriter output materialization is not the whileActive write path.");
            }

            GraphKindOperationPolicy.RequireAllowed(
                mountedKind,
                program,
                GasGraphOpHandlerTable.Instance,
                graphId,
                nameof(ResolveWhileActiveGraphId));

            return graphId;
        }

        private static void ValidateBindings(
            InteractionContextProfileDefinition definition,
            InteractionContextProfileReferenceCatalog? referenceCatalog)
        {
            if (definition.Bindings is not { Count: > 0 })
            {
                return;
            }

            if (referenceCatalog == null)
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{definition.Id}' declares bindings but no reference catalog was provided at install; semantic action bindings require the input action id space.");
            }

            for (int i = 0; i < definition.Bindings.Count; i++)
            {
                string actionId = definition.Bindings[i];
                if (!referenceCatalog.InputActionIds.Contains(actionId))
                {
                    throw new InvalidOperationException(
                        $"Interaction context profile '{definition.Id}' bindings[{i}] references unknown semantic action '{actionId}'.");
                }
            }
        }

        private static void ValidateTriggers(
            InteractionContextProfileDefinition definition,
            InteractionContextProfileReferenceCatalog? referenceCatalog)
        {
            if (definition.Triggers is not { Count: > 0 })
            {
                return;
            }

            if (referenceCatalog == null)
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{definition.Id}' declares triggers but no reference catalog was provided at install; trigger mounts require the graph program registry.");
            }

            string ownerLabel = $"Interaction context profile '{definition.Id}'";
            for (int i = 0; i < definition.Triggers.Count; i++)
            {
                Gameplay.MapTriggers.TriggerGraphMounting.ValidateContextTriggerMount(
                    referenceCatalog.Programs,
                    definition.Triggers[i],
                    ownerLabel);
            }
        }

        private static int ResolveDeclaredId(
            StringIntRegistry registry,
            string declaredName,
            string profileId,
            string fieldName,
            string kindLabel)
        {
            if (string.IsNullOrWhiteSpace(declaredName))
            {
                return registry.InvalidId;
            }

            if (!registry.TryGetId(declaredName, out int id))
            {
                throw new InvalidOperationException(
                    $"Interaction context profile '{profileId}' {fieldName} references unknown {kindLabel} '{declaredName}'.");
            }

            return id;
        }
    }
}
