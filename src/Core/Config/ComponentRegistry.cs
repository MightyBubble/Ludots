using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.Core.Utils;
using Arch.Core.Extensions;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.ActionLoops;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Diagnostics;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Layers;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Physics;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Spatial;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Config
{
    public delegate void ComponentSetter(Entity entity, JsonNode data);
    public delegate void ComponentSetterWithContext(Entity entity, JsonNode data, ComponentAuthoringContext context);

    public static class ComponentRegistry
    {
        private static readonly Dictionary<string, ComponentSetterWithContext> _setters = new Dictionary<string, ComponentSetterWithContext>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ComponentType> _componentTypes = new Dictionary<string, ComponentType>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _registrationSource = new Dictionary<string, string>(StringComparer.Ordinal);
        private static RegistrationConflictReport _conflictReport;
        private static UtilityAiAuthoringCatalog _utilityAiAuthoring = UtilityAiAuthoringCatalog.Empty;

        static ComponentRegistry()
        {
            LayerRegistry.Register(MassNavigationLayerNames.Agent);

            Register<Position>("Position");
            Register<Velocity>("Velocity");
            Register<Health>("Health");
            Register<Name>("Name");
            Register<FacingDirection>("FacingDirection");
            Register<MapEntity>("MapEntity");
            Register("WorldPositionCm", SetWorldPositionCm);
            Register<SpatialPartitionExcluded>("SpatialPartitionExcluded");
            Register<Ludots.Core.Gameplay.Components.Team>("Team");
            Register<Ludots.Core.Gameplay.Components.PlayerOwner>("PlayerOwner");
            Register<Ludots.Core.Gameplay.Components.TeamIdentity>("TeamIdentity");
            Register<Ludots.Core.Gameplay.Components.PlayerIdentity>("PlayerIdentity");
            Register("ReplicationSchemaRef", SetReplicationSchemaRef, null, Component<ReplicationSchemaRef>.ComponentType);
            Register<Ludots.Core.Gameplay.Components.TeamEntityRef>("TeamEntityRef");
            Register<Ludots.Core.Gameplay.Components.EntityTriggerGraphAggregateRoot>("EntityTriggerGraphAggregateRoot");
            Register("EntityLayer", SetEntityLayer, null, Component<Ludots.Core.Gameplay.Components.EntityLayer>.ComponentType);
            Register("AttributeBuffer", SetAttributeBuffer);
            Register("EntityLocalClock", SetEntityLocalClock, null, Component<EntityLocalClock>.ComponentType);
            Register("AttributeDerivedGraphBinding", SetAttributeDerivedGraphBinding, null, Component<AttributeDerivedGraphBinding>.ComponentType);
            Register("AbilityStateBuffer", SetAbilityStateBuffer, null, Component<AbilityStateBuffer>.ComponentType);
            Register<GrantedSlotBuffer>("GrantedSlotBuffer");
            Register("AbilityProgressionRequirements", SetAbilityProgressionRequirements);
            Register("ProgressionStateBuffer", SetProgressionStateBuffer);
            Register("ProgressionScopeHost", SetProgressionScopeHost);
            Register("ProgressionScopeBinding", SetProgressionScopeBinding);
            Register("AbilityFormSetRef", SetAbilityFormSetRef);
            Register("ItemInstanceCm", SetItemInstanceCm, null, Component<ItemInstanceCm>.ComponentType);
            Register<ForceInput2D>("ForceInput2D");
            Register<GameplayTagContainer>("GameplayTagContainer", SetGameplayTagContainer);
            Register<TagCountContainer>("TagCountContainer");
            Register<DirtyFlags>("DirtyFlags");
            Register<TimedTagBuffer>("TimedTagBuffer");
            Register<AbilityTagGrantReceiver>("AbilityTagGrantReceiver");
            Register("OrderBuffer", SetOrderBuffer, null, Component<OrderBuffer>.ComponentType);
            ActionLoopComponentAuthoring.Register();
            Register<OrderSpatialPayloadBuffer>("OrderSpatialPayloadBuffer");
            Register<CommandSourceSelectableTag>("CommandSourceSelectableTag");
            Register("CommandSourceSelectableState", SetCommandSourceSelectableState, null, Component<CommandSourceSelectableState>.ComponentType);
            Register<CommandSourceDragState>("CommandSourceDragState");
            Register("VisionEmitterCm", SetVisionEmitterCm, null, Component<VisionEmitterCm>.ComponentType);
            Register("FogOccupantCm", SetFogOccupantCm, null, Component<FogOccupantCm>.ComponentType);
            Register("FieldTrackedCm", SetFieldTrackedCm, null, Component<FieldTrackedCm>.ComponentType);
            Register("SpatialBounds", SetSpatialBounds);
            Register("SpatialBox3D", SetSpatialBox3D);
            Register("SpatialFootprint2D", SetSpatialFootprint2D);
            Register<BlackboardSpatialBuffer>("BlackboardSpatialBuffer");
            Register<BlackboardEntityBuffer>("BlackboardEntityBuffer");
            Register<BlackboardIntBuffer>("BlackboardIntBuffer");
            Register<BlackboardFloatBuffer>("BlackboardFloatBuffer");
            Register("AbilityExecAimSync", SetAbilityExecAimSync);
            Register<VisualTransform>("VisualTransform");
            Register("CullState", SetCullState, null, Component<CullState>.ComponentType);
            Register<ContinuousHeightmapSampleState>("ContinuousHeightmapSampleState");
            Register("PresentationStaticTransform", SetPresentationStaticTransform);
            Register<PresentationStaticHeightPending>("PresentationStaticHeightPending");
            Register("ManifestationObstacleIntent2D", SetManifestationObstacleIntent2D);
            Register("ManifestationObstaclePolygon2D", SetManifestationObstaclePolygon2D);
            Register("CompoundObstacle2D", SetCompoundObstacle2D);
            Register<RuntimeNavMeshStructuralObstacle>("RuntimeNavMeshStructuralObstacle");
            Register("ManifestationMotion2D", SetManifestationMotion2D);
            Register("AttachedLocalPose", SetAttachedLocalPose);
            Register("DestroyWhenParentExecutionEnds", SetDestroyWhenParentExecutionEnds);
            Register<UtilityAiAgent>("UtilityAiAgent", SetUtilityAiAgent);
            Register<UtilityAiState>("UtilityAiState", SetUtilityAiState);
            Register<UtilityAiDecisionTrace>("UtilityAiDecisionTrace", SetUtilityAiDecisionTrace);
            Register<UtilityAiTargetPriority>("UtilityAiTargetPriority", SetUtilityAiTargetPriority);
            Register<UtilityAiCombatMemory>("UtilityAiCombatMemory", SetUtilityAiCombatMemory);
            Register<ActuatorReadiness>("ActuatorReadiness", SetActuatorReadiness);
            Register<AimGate>("AimGate", SetAimGate);
            Register("MassNavigationAgent", SetMassNavigationAgent, null, Component<MassNavigationAgent>.ComponentType);
            Register("MassNavigationBlocker", SetMassNavigationBlocker, null, Component<MassNavigationBlocker>.ComponentType);
            Register<MassNavigationHotspotMarker>("MassNavigationHotspotMarker");
            Register("MovementParticipation", SetMovementParticipation, null, Component<MovementParticipation>.ComponentType);
        }

        public static void Register<T>(string name, string modId = null)
        {
            Register(name, (entity, json, _) =>
            {
                T component;
                try
                {
                    component = json.Deserialize<T>(StrictJsonOptions.CreateExact(includeFields: true))
                        ?? throw new InvalidOperationException($"Component '{name}' failed to deserialize.");
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException($"Component '{name}' failed strict deserialization: {ex.Message}", ex);
                }

                entity.Add<T>(component);
            }, modId, Component<T>.ComponentType);
        }

        public static void SetConflictReport(RegistrationConflictReport report)
        {
            _conflictReport = report;
        }

        public static void SetUtilityAiAuthoringCatalog(UtilityAiAuthoringCatalog authoring)
        {
            _utilityAiAuthoring = authoring ?? UtilityAiAuthoringCatalog.Empty;
        }

        private static void SetReplicationSchemaRef(Entity entity, JsonNode data)
        {
            JsonObject obj = data as JsonObject
                ?? throw new InvalidOperationException("ReplicationSchemaRef authoring must be an object.");
            ValidateProperties(obj, "ReplicationSchemaRef", "SchemaId");
            int schemaId = ReadIntProperty(obj, "SchemaId", "ReplicationSchemaRef");
            if (schemaId <= 0)
            {
                throw new InvalidOperationException("ReplicationSchemaRef.SchemaId must be a positive integer.");
            }

            entity.Add(new ReplicationSchemaRef(schemaId));
        }

        public static void Register(string name, ComponentSetter setter, string modId = null)
        {
            if (setter == null)
            {
                throw new InvalidOperationException($"ComponentRegistry registration '{name}' requires a setter.");
            }

            Register(name, (entity, json, _) => setter(entity, json), modId, componentType: null);
        }

        public static void Register(string name, ComponentSetterWithContext setter, string modId = null)
        {
            Register(name, setter, modId, componentType: null);
        }

        public static void Register<T>(string name, ComponentSetter setter, string modId = null)
        {
            Register(name, setter, modId, Component<T>.ComponentType);
        }

        private static void Register(string name, ComponentSetter setter, string modId, ComponentType? componentType)
        {
            if (setter == null)
            {
                throw new InvalidOperationException($"ComponentRegistry registration '{name}' requires a setter.");
            }

            Register(name, (entity, json, _) => setter(entity, json), modId, componentType);
        }

        private static void Register(string name, ComponentSetterWithContext setter, string modId, ComponentType? componentType)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("ComponentRegistry registration requires a non-empty component name.");
            }

            if (setter == null)
            {
                throw new InvalidOperationException($"ComponentRegistry registration '{name}' requires a setter.");
            }

            if (_setters.TryGetValue(name, out var existingSetter))
            {
                string existingMod = _registrationSource.TryGetValue(name, out var em) ? em : "(core)";
                string newMod = modId ?? "(core)";
                if (IsSameRegistration(name, existingSetter, setter, componentType))
                {
                    return;
                }

                _conflictReport?.Add("ComponentRegistry", name, existingMod, newMod);
                throw new InvalidOperationException(
                    $"Component '{name}' already registered by '{existingMod}', cannot register duplicate from '{newMod}'.");
            }

            _setters[name] = setter;
            if (componentType.HasValue)
            {
                _componentTypes[name] = componentType.Value;
            }
            else
            {
                _componentTypes.Remove(name);
            }

            _registrationSource[name] = modId ?? "(core)";
        }

        private static bool IsSameRegistration(
            string name,
            ComponentSetterWithContext existingSetter,
            ComponentSetterWithContext newSetter,
            ComponentType? newComponentType)
        {
            bool hasExistingType = _componentTypes.TryGetValue(name, out var existingType);
            if (hasExistingType || newComponentType.HasValue)
            {
                return hasExistingType &&
                    newComponentType.HasValue &&
                    existingType.Equals(newComponentType.Value);
            }

            return existingSetter.Equals(newSetter) ||
                IsSameSetterDefinition(existingSetter, newSetter);
        }

        private static bool IsSameSetterDefinition(ComponentSetterWithContext existingSetter, ComponentSetterWithContext newSetter)
        {
            MethodInfo existingMethod = existingSetter.Method;
            MethodInfo newMethod = newSetter.Method;
            Type? existingDeclaringType = existingMethod.DeclaringType;
            Type? newDeclaringType = newMethod.DeclaringType;
            if (existingDeclaringType == null || newDeclaringType == null)
            {
                return false;
            }

            if (!string.Equals(existingDeclaringType.Assembly.GetName().Name, newDeclaringType.Assembly.GetName().Name, StringComparison.Ordinal) ||
                !Equals(existingMethod.Module.ModuleVersionId, newMethod.Module.ModuleVersionId) ||
                !string.Equals(existingDeclaringType.FullName, newDeclaringType.FullName, StringComparison.Ordinal) ||
                !string.Equals(existingMethod.Name, newMethod.Name, StringComparison.Ordinal) ||
                !Equals(existingMethod.ReturnType, newMethod.ReturnType))
            {
                return false;
            }

            ParameterInfo[] existingParameters = existingMethod.GetParameters();
            ParameterInfo[] newParameters = newMethod.GetParameters();
            if (existingParameters.Length != newParameters.Length)
            {
                return false;
            }

            if (!AreDelegateTargetsEquivalent(existingSetter.Target, newSetter.Target))
            {
                return false;
            }

            for (int i = 0; i < existingParameters.Length; i++)
            {
                if (!Equals(existingParameters[i].ParameterType, newParameters[i].ParameterType))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreDelegateTargetsEquivalent(object? existingTarget, object? newTarget)
        {
            if (ReferenceEquals(existingTarget, newTarget))
            {
                return true;
            }

            if (existingTarget == null || newTarget == null)
            {
                return false;
            }

            Type existingType = existingTarget.GetType();
            if (existingType != newTarget.GetType())
            {
                return false;
            }

            FieldInfo[] fields = existingType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                object? existingValue = fields[i].GetValue(existingTarget);
                object? newValue = fields[i].GetValue(newTarget);
                if (existingValue is Delegate existingDelegate && newValue is Delegate newDelegate)
                {
                    if (!existingDelegate.Equals(newDelegate))
                    {
                        return false;
                    }

                    continue;
                }

                if (!Equals(existingValue, newValue))
                {
                    return false;
                }
            }

            return true;
        }

        public static int UnregisterSource(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new InvalidOperationException("ComponentRegistry unregister requires a non-empty mod id.");
            }

            if (string.Equals(modId, "(core)", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ComponentRegistry cannot unregister core component authoring.");
            }

            var names = new List<string>();
            foreach (var kvp in _registrationSource)
            {
                if (string.Equals(kvp.Value, modId, StringComparison.Ordinal))
                {
                    names.Add(kvp.Key);
                }
            }

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                _setters.Remove(name);
                _componentTypes.Remove(name);
                _registrationSource.Remove(name);
            }

            return names.Count;
        }

        public static bool TryGetComponentType(string componentName, out ComponentType componentType)
        {
            return _componentTypes.TryGetValue(componentName, out componentType);
        }

        public static IReadOnlyDictionary<string, ComponentType> GetRegisteredComponentTypes()
        {
            var ordered = new SortedDictionary<string, ComponentType>(_componentTypes, StringComparer.Ordinal);
            return new ReadOnlyDictionary<string, ComponentType>(ordered);
        }

        public static void Apply(Entity entity, string componentName, JsonNode data)
        {
            Apply(entity, componentName, data, ComponentAuthoringContext.Empty, context: null);
        }

        public static void Apply(Entity entity, string componentName, JsonNode data, string context)
        {
            Apply(entity, componentName, data, ComponentAuthoringContext.Empty, context);
        }

        public static void Apply(Entity entity, string componentName, JsonNode data, ComponentAuthoringContext context)
        {
            Apply(entity, componentName, data, context, context: null);
        }

        public static void Apply(
            Entity entity,
            string componentName,
            JsonNode data,
            ComponentAuthoringContext authoringContext,
            string context)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                throw new InvalidOperationException("ComponentRegistry requires a non-empty component name.");
            }

            if (data == null)
            {
                throw new InvalidOperationException($"Component '{componentName}' requires non-null data.");
            }

            if (_setters.TryGetValue(componentName, out var setter))
            {
                try
                {
                    setter(entity, data, authoringContext ?? ComponentAuthoringContext.Empty);
                }
                catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(context))
                {
                    throw new InvalidOperationException($"{context}: {ex.Message}", ex);
                }

                return;
            }

            string message = $"Unknown component '{componentName}'.";
            if (!string.IsNullOrWhiteSpace(context))
            {
                message = $"{context}: {message}";
            }

            throw new InvalidOperationException(message);
        }

        private static void SetOrderBuffer(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "OrderBuffer");
            entity.Add(OrderBuffer.CreateEmpty());
        }

        private static void SetUtilityAiAgent(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("UtilityAiAgent requires an object payload.");
            }

            RejectNumericIdAuthoring(obj, "UtilityAiAgent", "profileId", "profile");
            RejectNumericIdAuthoring(obj, "UtilityAiAgent", "ProfileId", "profile");
            ValidateProperties(obj, "UtilityAiAgent", "profile");

            string profileKey = RequireStringProperty(obj, "profile", "UtilityAiAgent");
            if (!_utilityAiAuthoring.TryGetProfileId(profileKey, out int profileId) || profileId < 0)
            {
                throw new InvalidOperationException($"UtilityAiAgent.profile references unknown Utility AI profile '{profileKey}'.");
            }

            entity.Add(new UtilityAiAgent { ProfileId = profileId });
        }

        private static void SetUtilityAiState(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "UtilityAiState");
            entity.Add(new UtilityAiState { CurrentDecisionId = -1 });
        }

        private static void SetUtilityAiDecisionTrace(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "UtilityAiDecisionTrace");
            entity.Add(new UtilityAiDecisionTrace());
        }

        private static void SetUtilityAiCombatMemory(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "UtilityAiCombatMemory");
            entity.Add(new UtilityAiCombatMemory());
        }

        private static void SetUtilityAiTargetPriority(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("UtilityAiTargetPriority requires an object payload.");
            }

            RejectNumericIdAuthoring(obj, "UtilityAiTargetPriority", "Bucket", "bucket");
            ValidateProperties(obj, "UtilityAiTargetPriority", "bucket");

            string bucketKey = RequireStringProperty(obj, "bucket", "UtilityAiTargetPriority");
            entity.Add(new UtilityAiTargetPriority { Bucket = ParseUtilityAiTargetPriorityBucket(bucketKey, "UtilityAiTargetPriority.bucket") });
        }

        private static void SetActuatorReadiness(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ActuatorReadiness requires an object payload.");
            }

            RejectNumericIdAuthoring(obj, "ActuatorReadiness", "actuatorId", "actuator");
            RejectNumericIdAuthoring(obj, "ActuatorReadiness", "ActuatorId", "actuator");
            ValidateProperties(
                obj,
                "ActuatorReadiness",
                "actuator",
                "initialReady01",
                "initialBlockReason",
                "initialEtaSteps",
                "requiresPreparation");

            int actuatorId = ResolveUtilityAiActuator(RequireStringProperty(obj, "actuator", "ActuatorReadiness"), "ActuatorReadiness.actuator");
            float ready01 = TryReadFloatProperty(obj, "initialReady01", out float authoredReady)
                ? authoredReady
                : 1f;
            ValidateUnitFloat(ready01, "ActuatorReadiness.initialReady01");

            entity.Add(new ActuatorReadiness
            {
                ActuatorId = actuatorId,
                Ready01 = ready01,
                BlockReason = TryReadIntProperty(obj, out int blockReason, "initialBlockReason") ? blockReason : 0,
                EtaSteps = TryReadIntProperty(obj, out int etaSteps, "initialEtaSteps") ? etaSteps : 0,
                RequiresPreparation = TryReadBooleanByteProperty(obj, "requiresPreparation", out byte requiresPreparation) ? requiresPreparation : (byte)0,
            });
        }

        private static void SetAimGate(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AimGate requires an object payload.");
            }

            RejectNumericIdAuthoring(obj, "AimGate", "actuatorId", "actuator");
            RejectNumericIdAuthoring(obj, "AimGate", "ActuatorId", "actuator");
            ValidateProperties(obj, "AimGate", "actuator", "initialReady01", "initialBlockReason");

            int actuatorId = ResolveUtilityAiActuator(RequireStringProperty(obj, "actuator", "AimGate"), "AimGate.actuator");
            float ready01 = TryReadFloatProperty(obj, "initialReady01", out float authoredReady)
                ? authoredReady
                : 1f;
            ValidateUnitFloat(ready01, "AimGate.initialReady01");

            entity.Add(new AimGate
            {
                ActuatorId = actuatorId,
                Ready01 = ready01,
                BlockReason = TryReadIntProperty(obj, out int blockReason, "initialBlockReason") ? blockReason : 0,
            });
        }

        private static void SetCommandSourceSelectableState(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("CommandSourceSelectableState requires an object payload.");
            }

            ValidateProperties(obj, "CommandSourceSelectableState", "IsEnabled");
            JsonNode isEnabledNode = RequireProperty(obj, "IsEnabled", "CommandSourceSelectableState");
            byte enabled = ParseSelectableStateEnabled(isEnabledNode, "CommandSourceSelectableState.IsEnabled");
            entity.Add(new CommandSourceSelectableState { IsEnabled = enabled });
        }

        private static void SetVisionEmitterCm(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("VisionEmitterCm requires an object payload.");
            }

            ValidateProperties(
                obj,
                "VisionEmitterCm",
                "scope",
                "layers",
                "polarity",
                "aperture",
                "altitudeBand",
                "priority",
                "detectionStrength",
                "trueSightStrength");

            entity.Add(new VisionEmitterCm
            {
                ScopeKeyId = ResolveVisionScopeKeyId(context, RequireStringProperty(obj, "scope", "VisionEmitterCm")),
                LayerMask = ResolveFogLayerMask(context, RequireArrayProperty(obj, "layers", "VisionEmitterCm.layers"), "VisionEmitterCm.layers"),
                Polarity = ParseVisionPolarity(RequireStringProperty(obj, "polarity", "VisionEmitterCm")),
                Aperture = ParseVisionAperture(RequireObjectProperty(obj, "aperture", "VisionEmitterCm.aperture")),
                AltitudeBand = ReadOptionalIntProperty(obj, "altitudeBand"),
                Priority = ReadOptionalIntProperty(obj, "priority"),
                DetectionStrength = ReadOptionalByteProperty(obj, "detectionStrength", "VisionEmitterCm.detectionStrength"),
                TrueSightStrength = ReadOptionalByteProperty(obj, "trueSightStrength", "VisionEmitterCm.trueSightStrength"),
            });
        }

        private static void SetFogOccupantCm(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("FogOccupantCm requires an object payload.");
            }

            ValidateProperties(obj, "FogOccupantCm", "layers", "altitudeBand", "stealthLevel");
            entity.Add(new FogOccupantCm
            {
                ExposeLayerMask = ResolveFogLayerMask(context, RequireArrayProperty(obj, "layers", "FogOccupantCm.layers"), "FogOccupantCm.layers"),
                AltitudeBand = ReadOptionalIntProperty(obj, "altitudeBand"),
                StealthLevel = ReadOptionalByteProperty(obj, "stealthLevel", "FogOccupantCm.stealthLevel"),
            });
        }

        private static void SetFieldTrackedCm(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("FieldTrackedCm requires an object payload.");
            }

            ValidateProperties(obj, "FieldTrackedCm", "layer");
            string layerKey = RequireStringProperty(obj, "layer", "FieldTrackedCm");
            FieldLayerRegistry registry = context.Require<FieldLayerRegistry>(ComponentAuthoringServiceKeys.FieldLayerRegistry);
            FieldLayerId layerId = registry.GetId(layerKey);
            if (layerId.Value <= 0 || !registry.TryGet(layerId, out _))
            {
                throw new InvalidOperationException(
                    $"FieldTrackedCm.layer references unknown field layer '{layerKey}'. Declare it in Fields/layers.json.");
            }

            entity.Add(new FieldTrackedCm { LayerId = layerId });
        }

        private static void SetSpatialBounds(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SpatialBounds requires an object payload.");
            }
            ValidateProperties(obj, "SpatialBounds", "kind", "localCenterCm", "localCenterXCm", "localCenterYCm", "localCenterZCm");

            string kindRaw = RequireStringProperty(obj, "kind", "SpatialBounds");

            var bounds = new SpatialBounds
            {
                Kind = ParseSpatialBoundsKind(kindRaw),
            };

            bool hasLocalCenter = obj.TryGetPropertyValue("localCenterCm", out _);
            bool hasSplitLocalCenter =
                obj.TryGetPropertyValue("localCenterXCm", out _) ||
                obj.TryGetPropertyValue("localCenterZCm", out _);
            if (hasLocalCenter && hasSplitLocalCenter)
            {
                throw new InvalidOperationException("SpatialBounds must author either localCenterCm or localCenterXCm/localCenterZCm, not both.");
            }

            if (TryReadPointProperty(obj, out var localCenter, "localCenterCm", "SpatialBounds.localCenterCm"))
            {
                bounds.LocalCenterXCm = localCenter.X;
                bounds.LocalCenterZCm = localCenter.Y;
            }
            else
            {
                bounds.LocalCenterXCm = ReadIntProperty(obj, "localCenterXCm", "SpatialBounds");
                bounds.LocalCenterZCm = ReadIntProperty(obj, "localCenterZCm", "SpatialBounds");
            }

            bounds.LocalCenterYCm = ReadIntProperty(obj, "localCenterYCm", "SpatialBounds");
            entity.Add(bounds);
        }

        private static void SetSpatialBox3D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SpatialBox3D requires an object payload.");
            }
            ValidateProperties(obj, "SpatialBox3D", "halfSizeXCm", "halfSizeYCm", "halfSizeZCm");

            entity.Add(new SpatialBox3D
            {
                HalfSizeXCm = ReadIntProperty(obj, "halfSizeXCm", "SpatialBox3D"),
                HalfSizeYCm = ReadIntProperty(obj, "halfSizeYCm", "SpatialBox3D"),
                HalfSizeZCm = ReadIntProperty(obj, "halfSizeZCm", "SpatialBox3D"),
            });
        }

        private static void SetSpatialFootprint2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("SpatialFootprint2D requires an object payload.");
            }
            ValidateProperties(obj, "SpatialFootprint2D", "vertices", "polygons");

            JsonArray? polygons = obj["polygons"] as JsonArray;
            JsonArray? vertices = obj["vertices"] as JsonArray;

            var footprint = new SpatialFootprint2D();
            if (polygons != null)
            {
                if (polygons.Count == 0 || polygons.Count > SpatialFootprint2D.MaxPolygons)
                {
                    throw new InvalidOperationException($"SpatialFootprint2D polygons count must be between 1 and {SpatialFootprint2D.MaxPolygons}.");
                }

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    if (polygons[polygonIndex] is not JsonArray polygonVertices)
                    {
                        throw new InvalidOperationException("SpatialFootprint2D polygons entries must be vertex arrays.");
                    }

                    SetFootprintPolygon(ref footprint, polygonIndex, polygonVertices);
                }
            }
            else if (vertices != null)
            {
                SetFootprintPolygon(ref footprint, 0, vertices);
            }
            else
            {
                throw new InvalidOperationException("SpatialFootprint2D requires either 'vertices' or 'polygons'.");
            }

            entity.Add(footprint);
        }

        private static void SetAbilityStateBuffer(Entity entity, JsonNode data)
        {
            var buffer = default(AbilityStateBuffer);
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityStateBuffer requires an object payload.");
            }
            ValidateProperties(obj, "AbilityStateBuffer", "abilityIds");

            if (obj.TryGetPropertyValue("abilityIds", out var idsNode))
            {
                if (idsNode is not JsonArray arr)
                {
                    throw new InvalidOperationException("AbilityStateBuffer.abilityIds requires an array.");
                }

                if (arr.Count > AbilityStateBuffer.CAPACITY)
                {
                    throw new InvalidOperationException(
                        $"AbilityStateBuffer.abilityIds accepts at most {AbilityStateBuffer.CAPACITY} entries.");
                }

                for (int i = 0; i < arr.Count; i++)
                {
                    var elem = arr[i];
                    if (elem == null)
                    {
                        throw new InvalidOperationException($"AbilityStateBuffer.abilityIds[{i}] requires a non-null value.");
                    }

                    int id;
                    if (elem.GetValueKind() == JsonValueKind.String)
                    {
                        var abilityConfigId = elem.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(abilityConfigId))
                        {
                            throw new InvalidOperationException($"AbilityStateBuffer.abilityIds[{i}] requires a non-empty ability id.");
                        }
                        else
                        {
                            id = Ludots.Core.Gameplay.GAS.Registry.AbilityIdRegistry.GetId(abilityConfigId);
                            if (id <= 0)
                            {
                                throw new InvalidOperationException($"Unknown ability id '{abilityConfigId}' in AbilityStateBuffer config.");
                            }
                        }
                    }
                    else
                    {
                        id = elem.GetValue<int>();
                    }

                    if (id <= 0)
                    {
                        throw new InvalidOperationException($"AbilityStateBuffer.abilityIds[{i}] resolved to invalid id '{id}'.");
                    }

                    buffer.AddAbility(id);
                }
            }
            entity.Add(buffer);
        }

        private static void SetAbilityProgressionRequirements(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityProgressionRequirements requires an object payload.");
            }

            ValidateProperties(obj, "AbilityProgressionRequirements", "useRequirement", "showRequirement");
            var requirements = default(AbilityProgressionRequirements);
            if (obj.TryGetPropertyValue("useRequirement", out JsonNode useNode))
            {
                string requirementName = ReadStringNode(useNode, "AbilityProgressionRequirements.useRequirement");
                requirements.UseRequirementId = ResolveProgressionRequirementId(requirementName, "AbilityProgressionRequirements.useRequirement");
            }

            if (obj.TryGetPropertyValue("showRequirement", out JsonNode showNode))
            {
                string requirementName = ReadStringNode(showNode, "AbilityProgressionRequirements.showRequirement");
                requirements.ShowRequirementId = ResolveProgressionRequirementId(requirementName, "AbilityProgressionRequirements.showRequirement");
            }

            entity.Add(requirements);
        }

        private static void SetProgressionStateBuffer(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "ProgressionStateBuffer");
            entity.Add(new ProgressionStateBuffer());
        }

        private static void SetProgressionScopeHost(Entity entity, JsonNode data)
        {
            if (!entity.Has<ProgressionStateBuffer>())
            {
                entity.Add(new ProgressionStateBuffer());
            }

            if (!entity.Has<ScopeMembershipRevision>())
            {
                entity.Add(new ScopeMembershipRevision());
            }

            var authoring = entity.Has<ScopeHostAuthoring>()
                ? entity.Get<ScopeHostAuthoring>()
                : default;
            AddProgressionScopeEntries(ref authoring, data, "ProgressionScopeHost");
            if (entity.Has<ScopeHostAuthoring>())
            {
                entity.Set(authoring);
            }
            else
            {
                entity.Add(authoring);
            }
        }

        private static void SetProgressionScopeBinding(Entity entity, JsonNode data)
        {
            if (!entity.Has<ScopeRefBuffer>())
            {
                entity.Add(new ScopeRefBuffer());
            }

            if (!entity.Has<ScopeMemberTag>())
            {
                entity.Add(new ScopeMemberTag());
            }

            var authoring = entity.Has<ScopeBindingAuthoring>()
                ? entity.Get<ScopeBindingAuthoring>()
                : default;
            AddProgressionScopeEntries(ref authoring, data, "ProgressionScopeBinding");
            if (entity.Has<ScopeBindingAuthoring>())
            {
                entity.Set(authoring);
            }
            else
            {
                entity.Add(authoring);
            }
        }

        private static byte ParseSelectableStateEnabled(JsonNode node, string context)
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => throw new InvalidOperationException($"{context} requires a boolean enabled value."),
            };
        }

        private static SpatialBoundsKind ParseSpatialBoundsKind(string value)
        {
            return value switch
            {
                "Point" => SpatialBoundsKind.Point,
                "Footprint2D" => SpatialBoundsKind.Footprint2D,
                "Box3D" => SpatialBoundsKind.Box3D,
                _ => throw new InvalidOperationException($"Unsupported SpatialBounds kind '{value}'. Expected Point, Footprint2D, or Box3D."),
            };
        }

        private static void SetFootprintPolygon(ref SpatialFootprint2D footprint, int polygonIndex, JsonArray vertices)
        {
            if (vertices.Count < 3 || vertices.Count > SpatialFootprint2D.MaxVerticesPerPolygon)
            {
                throw new InvalidOperationException(
                    $"SpatialFootprint2D polygon vertex count must be between 3 and {SpatialFootprint2D.MaxVerticesPerPolygon}.");
            }

            footprint.SetPolygonVertexCount(polygonIndex, vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i] is not JsonObject pointObj)
                {
                    throw new InvalidOperationException("SpatialFootprint2D vertices entries must be objects with x/y.");
                }

                footprint.SetVertex(
                    polygonIndex,
                    i,
                    new WorldCmInt2(
                        ReadIntProperty(pointObj, "x", "SpatialFootprint2D vertex"),
                        ReadIntProperty(pointObj, "y", "SpatialFootprint2D vertex")));
            }
        }

        private static void SetWorldPositionCm(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("WorldPositionCm requires an object payload.");
            }
            ValidateProperties(obj, "WorldPositionCm", "Value");

            JsonNode valueNode = RequireProperty(obj, "Value", "WorldPositionCm");
            if (valueNode is not JsonObject valueObj)
            {
                throw new InvalidOperationException("WorldPositionCm.Value requires an object payload.");
            }
            ValidateProperties(valueObj, "WorldPositionCm.Value", "X", "Y");

            int x = ReadIntProperty(valueObj, "X", "WorldPositionCm.Value");
            int y = ReadIntProperty(valueObj, "Y", "WorldPositionCm.Value");
            var fix64Pos = Fix64Vec2.FromInt(x, y);
            SetOrAdd(entity, new WorldPositionCm { Value = fix64Pos });
            SetOrAdd(entity, new PreviousWorldPositionCm { Value = fix64Pos });
            if (!entity.Has<VisualTransform>())
            {
                entity.Add(VisualTransform.Default);
            }
            if (!entity.Has<CullState>())
            {
                entity.Add(new CullState { IsVisible = false, LOD = LODLevel.Low });
            }
        }

        private static void SetOrAdd<T>(Entity entity, in T component)
        {
            if (entity.Has<T>())
            {
                entity.Set(component);
            }
            else
            {
                entity.Add(component);
            }
        }

        private static void SetVisualTransform(Entity entity, JsonNode data)
        {
            SetOrAddAuthoredComponent<VisualTransform>(entity, data, "VisualTransform");
        }

        private static void SetCullState(Entity entity, JsonNode data)
        {
            SetOrAddAuthoredComponent<CullState>(entity, data, "CullState");
        }

        private static void SetOrAddAuthoredComponent<T>(Entity entity, JsonNode data, string componentName)
        {
            T component;
            try
            {
                component = data.Deserialize<T>(StrictJsonOptions.CreateExact(includeFields: true))
                    ?? throw new InvalidOperationException($"Component '{componentName}' failed to deserialize.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Component '{componentName}' failed strict deserialization: {ex.Message}",
                    ex);
            }

            if (entity.Has<T>())
            {
                entity.Set(component);
            }
            else
            {
                entity.Add(component);
            }
        }

        private static void SetPresentationStaticTransform(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "PresentationStaticTransform");
            entity.Add(new PresentationStaticTransform());
            entity.Add(new PresentationStaticVisualPending());
            entity.Add(new PresentationStaticCullPending());
        }

        private static void SetAbilityFormSetRef(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityFormSetRef requires an object payload.");
            }

            ValidateProperties(obj, "AbilityFormSetRef", "formSetId");
            string formSetName = RequireStringProperty(obj, "formSetId", "AbilityFormSetRef");
            if (string.IsNullOrWhiteSpace(formSetName))
            {
                throw new InvalidOperationException("AbilityFormSetRef requires a non-empty formSetId.");
            }

            int formSetId = AbilityFormSetIdRegistry.GetId(formSetName);
            if (formSetId <= 0)
            {
                throw new InvalidOperationException($"Unknown ability form set id '{formSetName}'.");
            }

            entity.Add(new AbilityFormSetRef { FormSetId = formSetId });
            if (!entity.Has<AbilityFormSlotBuffer>())
            {
                entity.Add(new AbilityFormSlotBuffer());
            }
        }

        private static void SetItemInstanceCm(Entity entity, JsonNode data, ComponentAuthoringContext context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ItemInstanceCm requires an object payload.");
            }

            RejectNumericIdAuthoring(obj, "ItemInstanceCm", "DefinitionId", "definitionId");
            ValidateProperties(obj, "ItemInstanceCm", "definitionId", "stackCount", "charges", "durability");

            string definitionKey = RequireStringProperty(obj, "definitionId", "ItemInstanceCm");
            ItemDefinitionRegistry definitions =
                context.Require<ItemDefinitionRegistry>(ComponentAuthoringServiceKeys.ItemDefinitionRegistry);
            int definitionId = definitions.GetId(definitionKey);
            if (definitionId <= 0 || !definitions.TryGet(definitionId, out _))
            {
                throw new InvalidOperationException(
                    $"ItemInstanceCm.definitionId references unknown item definition '{definitionKey}'. Declare it in Items/definitions.json.");
            }

            int stackCount = TryReadIntProperty(obj, out int authoredStackCount, "stackCount")
                ? authoredStackCount
                : 1;
            if (stackCount <= 0)
            {
                throw new InvalidOperationException("ItemInstanceCm.stackCount must be positive.");
            }

            entity.Add(new ItemInstanceCm
            {
                DefinitionId = definitionId,
                StackCount = stackCount,
                Charges = ReadOptionalIntProperty(obj, "charges"),
                Durability = ReadOptionalIntProperty(obj, "durability"),
            });
        }

        private static void SetEntityLayer(Entity entity, JsonNode data)
        {
            LayerMask layerMask = EntityLayerAuthoring.ReadLayerMask(data, "EntityLayer component");
            entity.Add(new Ludots.Core.Gameplay.Components.EntityLayer(layerMask));
        }

        private static unsafe void SetAttributeBuffer(Entity entity, JsonNode data)
        {
            var buffer = default(AttributeBuffer);
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AttributeBuffer requires an object payload.");
            }
            ValidateProperties(obj, "AttributeBuffer", "base", "current");

            if (obj.TryGetPropertyValue("base", out var baseNode))
            {
                if (baseNode is not JsonObject baseObj)
                {
                    throw new InvalidOperationException("AttributeBuffer.base requires an object payload.");
                }

                foreach (var kvp in baseObj)
                {
                    if (kvp.Value == null)
                    {
                        throw new InvalidOperationException($"AttributeBuffer.base.{kvp.Key} requires a non-null numeric value.");
                    }

                    float v = kvp.Value.GetValue<float>();
                    int attrId = ResolveAttributeBufferAttributeId(kvp.Key, $"AttributeBuffer.base.{kvp.Key}");
                    buffer.SetBase(attrId, v);
                }
            }

            if (obj.TryGetPropertyValue("current", out var currentNode))
            {
                if (currentNode is not JsonObject currentObj)
                {
                    throw new InvalidOperationException("AttributeBuffer.current requires an object payload.");
                }

                foreach (var kvp in currentObj)
                {
                    if (kvp.Value == null)
                    {
                        throw new InvalidOperationException($"AttributeBuffer.current.{kvp.Key} requires a non-null numeric value.");
                    }

                    float v = kvp.Value.GetValue<float>();
                    int attrId = ResolveAttributeBufferAttributeId(kvp.Key, $"AttributeBuffer.current.{kvp.Key}");
                    buffer.SetCurrent(attrId, v);
                }
            }

            var snapshot = default(AttributeLastSnapshot);
            ulong definedMask = buffer.DefinedMask;
            while (definedMask != 0UL)
            {
                int attributeId = System.Numerics.BitOperations.TrailingZeroCount(definedMask);
                definedMask &= definedMask - 1UL;
                snapshot.Values[attributeId] = buffer.GetCurrent(attributeId);
            }

            entity.Add(buffer);
            entity.Add(snapshot);
        }

        private static int ResolveAttributeBufferAttributeId(string attributeName, string context)
        {
            int attributeId = AttributeRegistry.GetId(attributeName);
            if (attributeId != AttributeRegistry.InvalidId)
            {
                return attributeId;
            }

            if (!AttributeRegistry.IsFrozen)
            {
                return AttributeRegistry.Register(attributeName);
            }

            throw new InvalidOperationException(
                $"{context} references unregistered attribute '{attributeName}'. Declare it in startup GAS attribute config before map loading.");
        }

        private static void SetGameplayTagContainer(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("GameplayTagContainer requires an object payload.");
            }
            ValidateProperties(obj, "GameplayTagContainer", "tags");

            var container = new GameplayTagContainer();
            var counts = new TagCountContainer();
            if (obj.TryGetPropertyValue("tags", out var tagsNode))
            {
                if (tagsNode is not JsonArray tags)
                {
                    throw new InvalidOperationException("GameplayTagContainer.tags requires an array payload.");
                }

                foreach (JsonNode? tag in tags)
                {
                    if (tag == null)
                    {
                        throw new InvalidOperationException("GameplayTagContainer.tags requires non-null string entries.");
                    }

                    string tagName = ReadStringNode(tag, "GameplayTagContainer.tags");
                    int tagId = ResolveGameplayTagId(tagName, $"GameplayTagContainer.tags.{tagName}");
                    container.AddTag(tagId);
                    if (!counts.AddCount(tagId))
                    {
                        throw new InvalidOperationException("GameplayTagContainer.tags exceeds TagCountContainer capacity.");
                    }
                }
            }

            entity.Add(container);
            if (counts.Count > 0)
            {
                entity.Add(counts);
            }
        }

        private static int ResolveGameplayTagId(string tagName, string context)
        {
            int tagId = TagRegistry.GetId(tagName);
            if (tagId != TagRegistry.InvalidId)
            {
                return tagId;
            }

            if (!TagRegistry.IsFrozen)
            {
                return TagRegistry.Register(tagName);
            }

            throw new InvalidOperationException(
                $"{context} references unregistered tag '{tagName}'. Declare it in startup GAS tag config before loading.");
        }

        private static void SetEntityLocalClock(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "EntityLocalClock");
            entity.Add(new EntityLocalClock());
        }

        private static unsafe void SetAttributeDerivedGraphBinding(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AttributeDerivedGraphBinding requires an object payload.");
            }

            if (obj.ContainsKey("graphProgramIds") || obj.ContainsKey("GraphProgramIds") ||
                obj.ContainsKey("graphProgramId") || obj.ContainsKey("GraphProgramId"))
            {
                throw new InvalidOperationException(
                    "AttributeDerivedGraphBinding numeric graph ids are internal only; author graphs by name via 'graphs'.");
            }

            ValidateProperties(obj, "AttributeDerivedGraphBinding", "graphs");
            JsonNode graphsNode = RequireProperty(obj, "graphs", "AttributeDerivedGraphBinding");
            if (graphsNode is not JsonArray graphs)
            {
                throw new InvalidOperationException("AttributeDerivedGraphBinding.graphs requires an array.");
            }

            var binding = new AttributeDerivedGraphBinding();
            for (int i = 0; i < graphs.Count; i++)
            {
                JsonNode graphNode = graphs[i];
                if (graphNode == null || graphNode.GetValueKind() == JsonValueKind.Null)
                {
                    throw new InvalidOperationException($"AttributeDerivedGraphBinding.graphs[{i}] requires a non-null string value.");
                }

                if (graphNode.GetValueKind() != JsonValueKind.String)
                {
                    throw new InvalidOperationException($"AttributeDerivedGraphBinding.graphs[{i}] requires a string graph name.");
                }

                string graphName = graphNode.GetValue<string>();
                if (string.IsNullOrWhiteSpace(graphName))
                {
                    throw new InvalidOperationException($"AttributeDerivedGraphBinding.graphs[{i}] requires a non-empty graph name.");
                }

                int graphId = GraphIdRegistry.GetId(graphName);
                if (graphId <= 0)
                {
                    throw new InvalidOperationException(
                        $"AttributeDerivedGraphBinding.graphs[{i}] references unknown graph '{graphName}'.");
                }

                binding.Add(graphId);
            }

            entity.Add(binding);
        }

        private static void SetManifestationObstacleIntent2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D requires an object payload.");
            }
            ValidateProperties(
                obj,
                "ManifestationObstacleIntent2D",
                "shape",
                "sinkPhysicsCollider",
                "sinkNavigationObstacle",
                "navRadiusCm",
                "radiusCm",
                "halfWidthCm",
                "halfHeightCm",
                "localOffsetCm",
                "localOffsetXCm",
                "localOffsetYCm");

            string shapeRaw = RequireStringProperty(obj, "shape", "ManifestationObstacleIntent2D");
            var intent = new ManifestationObstacleIntent2D
            {
                Shape = ParseManifestationObstacleShape(shapeRaw),
                SinkPhysicsCollider = ParseBooleanByte(RequireProperty(obj, "sinkPhysicsCollider", "ManifestationObstacleIntent2D"), "ManifestationObstacleIntent2D.sinkPhysicsCollider"),
                SinkNavigationObstacle = ParseBooleanByte(RequireProperty(obj, "sinkNavigationObstacle", "ManifestationObstacleIntent2D"), "ManifestationObstacleIntent2D.sinkNavigationObstacle"),
                NavRadiusCm = ReadIntProperty(obj, "navRadiusCm", "ManifestationObstacleIntent2D"),
            };

            if (intent.SinkPhysicsCollider == 0 && intent.SinkNavigationObstacle == 0)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D requires at least one sink intent.");
            }

            if (intent.Shape == ManifestationObstacleShape2D.Circle)
            {
                RequireAbsentProperties(obj, "ManifestationObstacleIntent2D Circle", "halfWidthCm", "halfHeightCm");
                intent.RadiusCm = ReadIntProperty(obj, "radiusCm", "ManifestationObstacleIntent2D");
            }
            else if (intent.Shape == ManifestationObstacleShape2D.Box)
            {
                RequireAbsentProperties(obj, "ManifestationObstacleIntent2D Box", "radiusCm");
                intent.HalfWidthCm = ReadIntProperty(obj, "halfWidthCm", "ManifestationObstacleIntent2D");
                intent.HalfHeightCm = ReadIntProperty(obj, "halfHeightCm", "ManifestationObstacleIntent2D");
            }
            else
            {
                RequireAbsentProperties(obj, "ManifestationObstacleIntent2D Polygon", "radiusCm", "halfWidthCm", "halfHeightCm");
            }

            bool hasLocalOffset = obj.TryGetPropertyValue("localOffsetCm", out _);
            bool hasSplitLocalOffset =
                obj.TryGetPropertyValue("localOffsetXCm", out _) ||
                obj.TryGetPropertyValue("localOffsetYCm", out _);
            if (hasLocalOffset && hasSplitLocalOffset)
            {
                throw new InvalidOperationException("ManifestationObstacleIntent2D must author either localOffsetCm or localOffsetXCm/localOffsetYCm, not both.");
            }

            if (TryReadPointProperty(obj, out var localOffset, "localOffsetCm", "ManifestationObstacleIntent2D.localOffsetCm"))
            {
                intent.LocalOffsetXCm = localOffset.X;
                intent.LocalOffsetYCm = localOffset.Y;
            }
            else
            {
                intent.LocalOffsetXCm = ReadIntProperty(obj, "localOffsetXCm", "ManifestationObstacleIntent2D");
                intent.LocalOffsetYCm = ReadIntProperty(obj, "localOffsetYCm", "ManifestationObstacleIntent2D");
            }

            entity.Add(intent);
        }

        private static void SetManifestationObstaclePolygon2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationObstaclePolygon2D requires an object payload.");
            }
            ValidateProperties(obj, "ManifestationObstaclePolygon2D", "vertices");

            JsonArray? vertices = obj["vertices"] as JsonArray;
            if (vertices == null)
            {
                throw new InvalidOperationException("ManifestationObstaclePolygon2D requires a vertices array.");
            }

            if (vertices.Count < 3 || vertices.Count > ManifestationObstaclePolygon2D.MaxVertices)
            {
                throw new InvalidOperationException($"ManifestationObstaclePolygon2D vertices count must be between 3 and {ManifestationObstaclePolygon2D.MaxVertices}.");
            }

            var polygon = new ManifestationObstaclePolygon2D
            {
                VertexCount = (byte)vertices.Count,
            };

            for (int i = 0; i < vertices.Count; i++)
            {
                if (vertices[i] is not JsonObject pointObj)
                {
                    throw new InvalidOperationException("ManifestationObstaclePolygon2D vertices entries must be objects with x/y.");
                }

                polygon.SetVertex(i, new WorldCmInt2(
                    ReadIntProperty(pointObj, "x", "ManifestationObstaclePolygon2D vertex"),
                    ReadIntProperty(pointObj, "y", "ManifestationObstaclePolygon2D vertex")));
            }

            entity.Add(polygon);
        }

        private static void SetCompoundObstacle2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("CompoundObstacle2D requires an object payload.");
            }
            ValidateProperties(obj, "CompoundObstacle2D", "sinkPhysicsCollider", "sinkNavigationObstacle", "pieces");

            var obstacle = new CompoundObstacle2D
            {
                SinkPhysicsCollider = ParseBooleanByte(RequireProperty(obj, "sinkPhysicsCollider", "CompoundObstacle2D"), "CompoundObstacle2D.sinkPhysicsCollider"),
                SinkNavigationObstacle = ParseBooleanByte(RequireProperty(obj, "sinkNavigationObstacle", "CompoundObstacle2D"), "CompoundObstacle2D.sinkNavigationObstacle"),
            };

            if (obstacle.SinkPhysicsCollider == 0 && obstacle.SinkNavigationObstacle == 0)
            {
                throw new InvalidOperationException("CompoundObstacle2D requires at least one sink intent.");
            }

            if (RequireProperty(obj, "pieces", "CompoundObstacle2D") is not JsonArray pieces)
            {
                throw new InvalidOperationException("CompoundObstacle2D.pieces requires an array.");
            }

            if (pieces.Count == 0 || pieces.Count > CompoundObstacle2D.MaxPieces)
            {
                throw new InvalidOperationException($"CompoundObstacle2D pieces count must be between 1 and {CompoundObstacle2D.MaxPieces}.");
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] is not JsonObject pieceObj)
                {
                    throw new InvalidOperationException($"CompoundObstacle2D.pieces[{i}] requires an object payload.");
                }

                SetCompoundObstaclePiece(ref obstacle, i, pieceObj);
            }

            entity.Add(obstacle);
        }

        private static void SetCompoundObstaclePiece(ref CompoundObstacle2D obstacle, int pieceIndex, JsonObject obj)
        {
            string context = $"CompoundObstacle2D.pieces[{pieceIndex}]";
            ValidateProperties(
                obj,
                context,
                "shape",
                "navRadiusCm",
                "radiusCm",
                "halfWidthCm",
                "halfHeightCm",
                "localOffsetCm",
                "localOffsetXCm",
                "localOffsetYCm",
                "vertices");

            ManifestationObstacleShape2D shape = ParseManifestationObstacleShape(
                RequireStringProperty(obj, "shape", context),
                context);

            int radiusCm = 0;
            int halfWidthCm = 0;
            int halfHeightCm = 0;
            if (shape == ManifestationObstacleShape2D.Circle)
            {
                RequireAbsentProperties(obj, $"{context} Circle", "halfWidthCm", "halfHeightCm", "vertices");
                radiusCm = ReadIntProperty(obj, "radiusCm", context);
            }
            else if (shape == ManifestationObstacleShape2D.Box)
            {
                RequireAbsentProperties(obj, $"{context} Box", "radiusCm", "vertices");
                halfWidthCm = ReadIntProperty(obj, "halfWidthCm", context);
                halfHeightCm = ReadIntProperty(obj, "halfHeightCm", context);
            }
            else
            {
                RequireAbsentProperties(obj, $"{context} Polygon", "radiusCm", "halfWidthCm", "halfHeightCm");
            }

            bool hasLocalOffset = obj.TryGetPropertyValue("localOffsetCm", out _);
            bool hasSplitLocalOffset =
                obj.TryGetPropertyValue("localOffsetXCm", out _) ||
                obj.TryGetPropertyValue("localOffsetYCm", out _);
            if (hasLocalOffset && hasSplitLocalOffset)
            {
                throw new InvalidOperationException($"{context} must author either localOffsetCm or localOffsetXCm/localOffsetYCm, not both.");
            }

            int localOffsetXCm;
            int localOffsetYCm;
            if (TryReadPointProperty(obj, out var localOffset, "localOffsetCm", $"{context}.localOffsetCm"))
            {
                localOffsetXCm = localOffset.X;
                localOffsetYCm = localOffset.Y;
            }
            else
            {
                localOffsetXCm = ReadIntProperty(obj, "localOffsetXCm", context);
                localOffsetYCm = ReadIntProperty(obj, "localOffsetYCm", context);
            }

            int navRadiusCm = ReadIntProperty(obj, "navRadiusCm", context);
            obstacle.SetPiece(
                pieceIndex,
                shape,
                radiusCm,
                halfWidthCm,
                halfHeightCm,
                localOffsetXCm,
                localOffsetYCm,
                navRadiusCm);

            if (shape == ManifestationObstacleShape2D.Polygon)
            {
                if (RequireProperty(obj, "vertices", context) is not JsonArray vertices)
                {
                    throw new InvalidOperationException($"{context}.vertices requires an array.");
                }

                if (vertices.Count < 3 || vertices.Count > CompoundObstacle2D.MaxVerticesPerPolygon)
                {
                    throw new InvalidOperationException(
                        $"{context}.vertices count must be between 3 and {CompoundObstacle2D.MaxVerticesPerPolygon}.");
                }

                obstacle.SetPolygonVertexCount(pieceIndex, vertices.Count);
                for (int i = 0; i < vertices.Count; i++)
                {
                    if (vertices[i] is not JsonObject pointObj)
                    {
                        throw new InvalidOperationException($"{context}.vertices entries must be objects with x/y.");
                    }
                    ValidateProperties(pointObj, $"{context}.vertices[{i}]", "x", "y");

                    obstacle.SetVertex(
                        pieceIndex,
                        i,
                        new WorldCmInt2(
                            ReadIntProperty(pointObj, "x", $"{context}.vertices[{i}]"),
                            ReadIntProperty(pointObj, "y", $"{context}.vertices[{i}]")));
                }
            }
        }

        private static void SetAbilityExecAimSync(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AbilityExecAimSync requires an object payload.");
            }
            ValidateProperties(obj, "AbilityExecAimSync", "abilitySlot", "syncFacing");

            entity.Add(new AbilityExecAimSync
            {
                AbilitySlot = ReadIntProperty(obj, "abilitySlot", "AbilityExecAimSync"),
                SyncFacing = ParseBooleanByte(RequireProperty(obj, "syncFacing", "AbilityExecAimSync"), "AbilityExecAimSync.syncFacing"),
            });
        }

        private static void SetManifestationMotion2D(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("ManifestationMotion2D requires an object payload.");
            }
            ValidateProperties(obj, "ManifestationMotion2D", "followParentPosition", "facingSource", "sweepDegreesPerSecond", "forwardOffsetCm");

            byte followParentPosition = ParseBooleanByte(RequireProperty(obj, "followParentPosition", "ManifestationMotion2D"), "ManifestationMotion2D.followParentPosition");
            int forwardOffsetCm = ReadIntProperty(obj, "forwardOffsetCm", "ManifestationMotion2D");
            entity.Add(new ManifestationMotion2D
            {
                FollowParentPosition = followParentPosition,
                FacingSource = ParseManifestationFacingSource(RequireStringProperty(obj, "facingSource", "ManifestationMotion2D")),
                SweepDegreesPerSecond = ReadFloatProperty(obj, "sweepDegreesPerSecond", "ManifestationMotion2D"),
                ForwardOffsetCm = forwardOffsetCm,
            });

            // 位置跟随职责已并入通用 AttachmentPositionSyncSystem：跟随父的 manifestation
            // 在装配期取得 AttachedLocalPose（前向偏移沿自身朝向旋转），朝向仍由
            // ManifestationMotion2DSystem 计算。既有模板 JSON 无需变更。
            if (followParentPosition != 0)
            {
                entity.Add(new Ludots.Core.Components.AttachedLocalPose
                {
                    OffsetCm = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(forwardOffsetCm, 0),
                    LocalFacingRad = Ludots.Core.Mathematics.FixedPoint.Fix64.Zero,
                    InheritParentFacing = 0,
                    OffsetRotation = Ludots.Core.Components.AttachedOffsetRotation.OwnFacing,
                });
            }
        }

        private static void SetAttachedLocalPose(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("AttachedLocalPose requires an object payload.");
            }
            ValidateProperties(obj, "AttachedLocalPose", "offsetXCm", "offsetYCm", "facingDeg", "inheritParentFacing", "offsetRotation");

            entity.Add(new Ludots.Core.Components.AttachedLocalPose
            {
                OffsetCm = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(
                    ReadIntProperty(obj, "offsetXCm", "AttachedLocalPose"),
                    ReadIntProperty(obj, "offsetYCm", "AttachedLocalPose")),
                LocalFacingRad = Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(
                    ReadIntProperty(obj, "facingDeg", "AttachedLocalPose")) * (Ludots.Core.Mathematics.FixedPoint.Fix64.Pi / Ludots.Core.Mathematics.FixedPoint.Fix64.FromInt(180)),
                InheritParentFacing = ParseBooleanByte(RequireProperty(obj, "inheritParentFacing", "AttachedLocalPose"), "AttachedLocalPose.inheritParentFacing"),
                OffsetRotation = Ludots.Core.Gameplay.Attachment.AttachedLocalPoseAuthoring.ParseOffsetRotation(
                    RequireStringProperty(obj, "offsetRotation", "AttachedLocalPose"),
                    "AttachedLocalPose"),
            });
        }

        private static void SetDestroyWhenParentExecutionEnds(Entity entity, JsonNode data)
        {
            RequireEmptyObject(data, "DestroyWhenParentExecutionEnds");
            entity.Add(new DestroyWhenParentExecutionEnds());
        }

        private static void SetMassNavigationAgent(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("MassNavigationAgent requires an object payload.");
            }

            ValidateProperties(obj, "MassNavigationAgent", "profileId");
            string profileId = RequireStringProperty(obj, "profileId", "MassNavigationAgent");
            int profileKey = MassNavigationProfileRegistry.Register(profileId);
            entity.Add(new MassNavigationAgent { ProfileId = profileKey });
        }

        private static void SetMassNavigationBlocker(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("MassNavigationBlocker requires an object payload.");
            }

            ValidateProperties(obj, "MassNavigationBlocker", "radiusCm");
            JsonNode radiusNode = RequireProperty(obj, "radiusCm", "MassNavigationBlocker");
            if (radiusNode is not JsonValue radiusValue || !radiusValue.TryGetValue(out float radiusCm))
            {
                throw new InvalidOperationException("MassNavigationBlocker.radiusCm requires a numeric value.");
            }

            if (!(radiusCm > 0f))
            {
                throw new InvalidOperationException("MassNavigationBlocker.radiusCm must be > 0.");
            }

            entity.Add(new MassNavigationBlocker { RadiusCm = radiusCm });
        }

        private static void SetMovementParticipation(Entity entity, JsonNode data)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException("MovementParticipation requires an object payload.");
            }

            ValidateProperties(obj, "MovementParticipation", "physicsPresence", "displacement");
            PhysicsPresenceKind physicsPresence = ParsePhysicsPresenceKind(
                RequireStringProperty(obj, "physicsPresence", "MovementParticipation"));

            if (RequireProperty(obj, "displacement", "MovementParticipation") is not JsonObject displacementObj)
            {
                throw new InvalidOperationException("MovementParticipation.displacement requires an object payload.");
            }

            ValidateProperties(
                displacementObj,
                "MovementParticipation.displacement",
                "allowed",
                "handbackSpeedThresholdCmPerSec",
                "maxDurationMs");
            bool displacementAllowed = ParseBooleanByte(
                RequireProperty(displacementObj, "allowed", "MovementParticipation.displacement"),
                "MovementParticipation.displacement.allowed") != 0;
            float handbackSpeedThresholdCmPerSec = ReadFloatProperty(
                displacementObj,
                "handbackSpeedThresholdCmPerSec",
                "MovementParticipation.displacement");
            if (!(handbackSpeedThresholdCmPerSec > 0f))
            {
                throw new InvalidOperationException(
                    "MovementParticipation.displacement.handbackSpeedThresholdCmPerSec must be > 0.");
            }

            int maxDurationMs = ReadIntProperty(displacementObj, "maxDurationMs", "MovementParticipation.displacement");
            if (maxDurationMs <= 0)
            {
                throw new InvalidOperationException("MovementParticipation.displacement.maxDurationMs must be > 0.");
            }

            entity.Add(new MovementParticipation
            {
                PhysicsPresence = physicsPresence,
                DisplacementAllowed = displacementAllowed,
                DisplacementHandbackSpeedThresholdCmPerSec = handbackSpeedThresholdCmPerSec,
                DisplacementMaxDurationMs = maxDurationMs,
            });

            // poseAuthority is runtime state, never authored: derive the initial owner from the
            // declared physics presence so the entity enters the world with exactly one pose writer.
            entity.Add(new PoseAuthority
            {
                Value = MovementParticipationRules.DeriveInitialPoseAuthority(physicsPresence),
            });
        }

        private static PhysicsPresenceKind ParsePhysicsPresenceKind(string raw)
        {
            return raw switch
            {
                "none" => PhysicsPresenceKind.None,
                "kinematic" => PhysicsPresenceKind.Kinematic,
                "dynamic" => PhysicsPresenceKind.Dynamic,
                _ => throw new InvalidOperationException(
                    $"MovementParticipation.physicsPresence '{raw}' is not configured (expected 'none', 'kinematic' or 'dynamic').")
            };
        }

        private static ManifestationObstacleShape2D ParseManifestationObstacleShape(string? raw)
        {
            return ParseManifestationObstacleShape(raw, "ManifestationObstacleIntent2D");
        }

        private static ManifestationObstacleShape2D ParseManifestationObstacleShape(string? raw, string context)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"{context} requires a non-empty shape.");
            }

            return raw switch
            {
                "Circle" => ManifestationObstacleShape2D.Circle,
                "Box" => ManifestationObstacleShape2D.Box,
                "Polygon" => ManifestationObstacleShape2D.Polygon,
                _ => throw new InvalidOperationException($"Unsupported {context} shape '{raw}'.")
            };
        }

        private static ManifestationFacingSource2D ParseManifestationFacingSource(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException("ManifestationMotion2D requires a non-empty facingSource.");
            }

            return raw switch
            {
                "None" => ManifestationFacingSource2D.None,
                "SweepVelocity" => ManifestationFacingSource2D.SweepVelocity,
                "ParentExecutionTarget" => ManifestationFacingSource2D.ParentExecutionTarget,
                _ => throw new InvalidOperationException($"Unsupported ManifestationMotion2D facingSource '{raw}'.")
            };
        }

        private static byte ParseBooleanByte(JsonNode node, string context)
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => throw new InvalidOperationException($"{context} requires a boolean value."),
            };
        }

        private static bool TryReadBooleanByteProperty(JsonObject obj, string name, out byte value)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                value = 0;
                return false;
            }

            if (node == null || node.GetValueKind() == JsonValueKind.Null)
            {
                throw new InvalidOperationException($"{name} requires a non-null boolean value.");
            }

            value = ParseBooleanByte(node, name);
            return true;
        }

        private static bool TryReadIntProperty(JsonObject obj, out int value, string name)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                value = 0;
                return false;
            }

            if (node == null)
            {
                throw new InvalidOperationException($"Property '{name}' requires a non-null integer value.");
            }

            if (node.GetValueKind() == JsonValueKind.Null)
            {
                throw new InvalidOperationException($"Property '{name}' requires a non-null integer value.");
            }

            if (node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"Property '{name}' requires an integer value.");
            }

            value = node.GetValue<int>();
            return true;
        }

        private static bool TryReadFloatProperty(JsonObject obj, string name, out float value)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                value = 0f;
                return false;
            }

            if (node == null)
            {
                throw new InvalidOperationException($"Property '{name}' requires a non-null numeric value.");
            }

            if (node.GetValueKind() == JsonValueKind.Null)
            {
                throw new InvalidOperationException($"Property '{name}' requires a non-null numeric value.");
            }

            if (node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"Property '{name}' requires a numeric value.");
            }

            value = node.GetValue<float>();
            return true;
        }

        private static void ValidateUnitFloat(float value, string context)
        {
            if (float.IsNaN(value) || value < 0f || value > 1f)
            {
                throw new InvalidOperationException($"{context} must be between 0 and 1.");
            }
        }

        private static int ResolveUtilityAiActuator(string actuatorKey, string context)
        {
            if (!_utilityAiAuthoring.TryGetActuatorId(actuatorKey, out int actuatorId) || actuatorId < 0)
            {
                throw new InvalidOperationException($"{context} references unknown Utility AI actuator '{actuatorKey}'.");
            }

            return actuatorId;
        }

        private static int ParseUtilityAiTargetPriorityBucket(string raw, string context)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException($"{context} requires a non-empty bucket key.");
            }

            return raw switch
            {
                "None" => (int)UtilityAiTargetPriorityBucket.None,
                "Low" => (int)UtilityAiTargetPriorityBucket.Low,
                "Normal" => (int)UtilityAiTargetPriorityBucket.Normal,
                "High" => (int)UtilityAiTargetPriorityBucket.High,
                "Critical" => (int)UtilityAiTargetPriorityBucket.Critical,
                _ => throw new InvalidOperationException($"{context} references unknown target priority bucket '{raw}'.")
            };
        }

        private static void RejectNumericIdAuthoring(JsonObject obj, string componentName, string numericProperty, string keyProperty)
        {
            if (obj.ContainsKey(numericProperty))
            {
                throw new InvalidOperationException($"{componentName} does not support '{numericProperty}'. Use '{keyProperty}' with a string key.");
            }
        }

        private static void AddProgressionScopeEntries(ref ScopeHostAuthoring authoring, JsonNode data, string context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object payload.");
            }

            ValidateProperties(obj, context, "scope", "hostKey", "entries");
            if (obj.TryGetPropertyValue("entries", out JsonNode entriesNode))
            {
                if (entriesNode is not JsonArray entries)
                {
                    throw new InvalidOperationException($"{context}.entries requires an array.");
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not JsonObject entryObj)
                    {
                        throw new InvalidOperationException($"{context}.entries[{i}] requires an object payload.");
                    }

                    AddProgressionScopeEntry(ref authoring, entryObj, $"{context}.entries[{i}]");
                }

                return;
            }

            AddProgressionScopeEntry(ref authoring, obj, context);
        }

        private static void AddProgressionScopeEntries(ref ScopeBindingAuthoring authoring, JsonNode data, string context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an object payload.");
            }

            ValidateProperties(obj, context, "scope", "hostKey", "entries");
            if (obj.TryGetPropertyValue("entries", out JsonNode entriesNode))
            {
                if (entriesNode is not JsonArray entries)
                {
                    throw new InvalidOperationException($"{context}.entries requires an array.");
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not JsonObject entryObj)
                    {
                        throw new InvalidOperationException($"{context}.entries[{i}] requires an object payload.");
                    }

                    AddProgressionScopeEntry(ref authoring, entryObj, $"{context}.entries[{i}]");
                }

                return;
            }

            AddProgressionScopeEntry(ref authoring, obj, context);
        }

        private static void AddProgressionScopeEntry(ref ScopeHostAuthoring authoring, JsonObject obj, string context)
        {
            ValidateProperties(obj, context, "scope", "hostKey");
            int scopeNameKeyId = ConfigKeyRegistry.Register(RequireStringProperty(obj, "scope", context));
            int hostKeyId = ConfigKeyRegistry.Register(RequireStringProperty(obj, "hostKey", context));
            if (!authoring.TryAdd(scopeNameKeyId, hostKeyId))
            {
                throw new InvalidOperationException($"{context} exceeds ProgressionScopeHost capacity.");
            }
        }

        private static void AddProgressionScopeEntry(ref ScopeBindingAuthoring authoring, JsonObject obj, string context)
        {
            ValidateProperties(obj, context, "scope", "hostKey");
            int scopeNameKeyId = ConfigKeyRegistry.Register(RequireStringProperty(obj, "scope", context));
            int hostKeyId = ConfigKeyRegistry.Register(RequireStringProperty(obj, "hostKey", context));
            if (!authoring.TryAdd(scopeNameKeyId, hostKeyId))
            {
                throw new InvalidOperationException($"{context} exceeds ProgressionScopeBinding capacity.");
            }
        }

        private static int ResolveProgressionRequirementId(string requirementName, string context)
        {
            int requirementId = ProgressionRequirementIdRegistry.GetId(requirementName);
            if (requirementId <= 0)
            {
                throw new InvalidOperationException($"{context} references unknown progression requirement '{requirementName}'.");
            }

            return requirementId;
        }

        private static int ResolveVisionScopeKeyId(ComponentAuthoringContext context, string scopeKey)
        {
            ScopeKeyRegistry scopeKeys = context.Require<ScopeKeyRegistry>(ComponentAuthoringServiceKeys.ScopeKeyRegistry);
            return scopeKeys.TryGetId(scopeKey, out int scopeKeyId) && scopeKeyId > 0
                ? scopeKeyId
                : throw new InvalidOperationException(
                    $"VisionEmitterCm.scope references unknown progression scope '{scopeKey}'. Declare it in Progression/scopes.json.");
        }

        private static uint ResolveFogLayerMask(ComponentAuthoringContext context, JsonArray layers, string contextName)
        {
            if (layers.Count == 0)
            {
                throw new InvalidOperationException($"{contextName} requires at least one fog layer.");
            }

            FogLayerRegistry registry = context.Require<FogLayerRegistry>(ComponentAuthoringServiceKeys.VisionFogLayerRegistry);
            uint mask = 0u;
            for (int i = 0; i < layers.Count; i++)
            {
                string layerKey = ReadStringNode(
                    layers[i] ?? throw new InvalidOperationException($"{contextName}[{i}] requires a non-null fog layer key."),
                    $"{contextName}[{i}]");
                FogLayerId layerId = registry.GetId(layerKey);
                if (layerId.Value <= 0)
                {
                    throw new InvalidOperationException($"{contextName}[{i}] references unknown fog layer '{layerKey}'.");
                }

                mask |= registry.ToMask(layerId);
            }

            return mask;
        }

        private static VisionPolarity ParseVisionPolarity(string value)
        {
            return value switch
            {
                "Reveal" => VisionPolarity.Reveal,
                "Deny" => VisionPolarity.Deny,
                _ => throw new InvalidOperationException($"Unsupported VisionEmitterCm.polarity '{value}'. Expected Reveal or Deny."),
            };
        }

        private static VisionAperture ParseVisionAperture(JsonObject obj)
        {
            ValidateProperties(obj, "VisionEmitterCm.aperture", "kind", "rangeCm", "halfAngleDeg", "halfWidthCm", "halfHeightCm", "lengthCm");
            string kind = RequireStringProperty(obj, "kind", "VisionEmitterCm.aperture");
            return kind switch
            {
                "Disk" => VisionAperture.Disk(ReadIntProperty(obj, "rangeCm", "VisionEmitterCm.aperture")),
                "Cone" => VisionAperture.Cone(
                    ReadIntProperty(obj, "rangeCm", "VisionEmitterCm.aperture"),
                    ReadIntProperty(obj, "halfAngleDeg", "VisionEmitterCm.aperture")),
                "Box" => VisionAperture.Box(
                    ReadIntProperty(obj, "halfWidthCm", "VisionEmitterCm.aperture"),
                    ReadIntProperty(obj, "halfHeightCm", "VisionEmitterCm.aperture")),
                "Line" => VisionAperture.Line(
                    ReadIntProperty(obj, "lengthCm", "VisionEmitterCm.aperture"),
                    ReadIntProperty(obj, "halfWidthCm", "VisionEmitterCm.aperture")),
                _ => throw new InvalidOperationException($"Unsupported VisionEmitterCm.aperture.kind '{kind}'. Expected Disk, Cone, Box, or Line."),
            };
        }

        private static string ReadStringNode(JsonNode node, string context)
        {
            if (node == null || node.GetValueKind() == JsonValueKind.Null)
            {
                throw new InvalidOperationException($"{context} requires a non-null string value.");
            }

            if (node.GetValueKind() != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{context} requires a string value.");
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} requires a non-empty string value.");
            }

            return value;
        }

        private static void RequireEmptyObject(JsonNode data, string context)
        {
            if (data is not JsonObject obj)
            {
                throw new InvalidOperationException($"{context} requires an empty object payload.");
            }

            if (obj.Count != 0)
            {
                throw new InvalidOperationException($"{context} does not accept authored fields.");
            }
        }

        private static void ValidateProperties(JsonObject obj, string context, params string[] allowedNames)
        {
            foreach (var kvp in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(kvp.Key, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException($"{context} contains unsupported property '{kvp.Key}'.");
                }
            }
        }

        private static void RequireAbsentProperties(JsonObject obj, string context, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (obj.ContainsKey(names[i]))
                {
                    throw new InvalidOperationException($"{context} must not author '{names[i]}'.");
                }
            }
        }

        private static int ReadIntProperty(JsonObject obj, string name, string context)
        {
            if (TryReadIntProperty(obj, out int value, name))
            {
                return value;
            }

            throw new InvalidOperationException($"{context} requires explicit '{name}'.");
        }

        private static byte ReadByteProperty(JsonObject obj, string name, string context)
        {
            int value = ReadIntProperty(obj, name, context);
            if ((uint)value > byte.MaxValue)
            {
                throw new InvalidOperationException($"{context}.{name} must be between 0 and {byte.MaxValue}.");
            }

            return (byte)value;
        }

        private static bool TryReadPointProperty(JsonObject obj, out WorldCmInt2 point, string name, string context)
        {
            if (!obj.TryGetPropertyValue(name, out var node))
            {
                point = WorldCmInt2.Zero;
                return false;
            }

            if (node is not JsonObject pointObj)
            {
                throw new InvalidOperationException($"{context} requires an object payload.");
            }

            ValidateProperties(pointObj, context, "x", "y");
            point = new WorldCmInt2(
                ReadIntProperty(pointObj, "x", context),
                ReadIntProperty(pointObj, "y", context));
            return true;
        }

        private static float ReadFloatProperty(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            if (node.GetValueKind() != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"{context}.{name} requires a numeric value.");
            }

            return node.GetValue<float>();
        }

        private static JsonNode RequireProperty(JsonObject obj, string name, string context)
        {
            if (!obj.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            {
                throw new InvalidOperationException($"{context} requires explicit '{name}'.");
            }

            return node;
        }

        private static JsonObject RequireObjectProperty(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            return node as JsonObject
                ?? throw new InvalidOperationException($"{context}.{name} requires an object payload.");
        }

        private static JsonArray RequireArrayProperty(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            return node as JsonArray
                ?? throw new InvalidOperationException($"{context} requires an array.");
        }

        private static int ReadOptionalIntProperty(JsonObject obj, string name)
        {
            return TryReadIntProperty(obj, out int value, name) ? value : 0;
        }

        private static byte ReadOptionalByteProperty(JsonObject obj, string name, string context)
        {
            if (!TryReadIntProperty(obj, out int value, name))
            {
                return 0;
            }

            if (value < byte.MinValue || value > byte.MaxValue)
            {
                throw new InvalidOperationException($"{context} must be between {byte.MinValue} and {byte.MaxValue}.");
            }

            return (byte)value;
        }

        private static string RequireStringProperty(JsonObject obj, string name, string context)
        {
            JsonNode node = RequireProperty(obj, name, context);
            if (node.GetValueKind() != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{context}.{name} requires a string value.");
            }

            string value = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context}.{name} requires a non-empty string value.");
            }

            return value;
        }
    }
}
