using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Requests;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class PerformContractsAndLegacyLaneTests
    {
        private static readonly string[] ForbiddenPresenterCommandTokens =
        {
            "PresentationCommand",
            "PresentationCommandKind",
            "PresentationCommandBuffer",
            "PerformCommand",
            "PerformCommandBuffer",
        };

        [Test]
        public void PresenterCommand_IsTheOnlyCommandChainSsot()
        {
            Assembly assembly = typeof(PresenterCommand).Assembly;
            Type?[] requiredTypes =
            {
                assembly.GetType("Ludots.Core.Presentation.Presenters.PresenterCommand", throwOnError: false),
                assembly.GetType("Ludots.Core.Presentation.Presenters.PresenterCommandKind", throwOnError: false),
                assembly.GetType("Ludots.Core.Presentation.Presenters.PresenterCommandBuffer", throwOnError: false),
            };
            string[] missingTypes = requiredTypes
                .Where(static type => type == null)
                .Select((_, index) => index switch
                {
                    0 => "PresenterCommand",
                    1 => "PresenterCommandKind",
                    _ => "PresenterCommandBuffer",
                })
                .ToArray();

            Assert.That(
                missingTypes,
                Is.Empty,
                "T3 command-chain rewrite requires PresenterCommand, PresenterCommandKind, and PresenterCommandBuffer to land together.");
        }

        [Test]
        public void LegacyCommandTypes_AreRemovedFromCore()
        {
            Assembly assembly = typeof(PresenterCommand).Assembly;
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Commands.PresentationCommand", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Commands.PresentationCommandKind", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Commands.PresentationCommandBuffer", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Perform.PerformCommand", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Perform.PerformCommandBuffer", throwOnError: false), Is.Null);
        }

        [Test]
        public void PresenterCommandContract_ExposesT3Fields()
        {
            FieldInfo[] fields = typeof(PresenterCommand).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = fields.Select(static field => field.Name).ToArray();

            Assert.That(fieldNames, Does.Contain("CommandKind"));
            Assert.That(fieldNames, Does.Contain("PresenterDefinitionId"));
            Assert.That(fieldNames, Does.Contain("ParentEntity"));
            Assert.That(fieldNames, Does.Contain("ScopeTag"));
            Assert.That(fieldNames, Does.Contain("ParamKey"));
            Assert.That(fieldNames, Does.Contain("ParamLane"));
            Assert.That(fieldNames, Does.Contain("ParamValue"));
            Assert.That(fieldNames, Does.Contain("IntValue"));
            Assert.That(fieldNames, Does.Contain("VectorValue"));
            Assert.That(fieldNames, Does.Contain("ValueSource"));
            Assert.That(fieldNames, Does.Contain("TargetBehaviorSlot"));

            foreach (string forbiddenToken in ForbiddenPresenterCommandTokens)
            {
                Assert.That(fieldNames, Does.Not.Contain(forbiddenToken));
            }

            Assert.That(typeof(PresenterCommand).GetField("ScopeId", BindingFlags.Instance | BindingFlags.Public), Is.Null);
            Assert.That(typeof(PresenterCommand).GetField("BehaviorSlot", BindingFlags.Instance | BindingFlags.Public), Is.Null);
        }

        [Test]
        public void PrefabStack_MustNotExist()
        {
            Assembly assembly = typeof(MeshAssetRegistry).Assembly;
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Assets.PrefabPart"), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Assets.PrefabRegistry"), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Assets.PrefabFinalizationPipeline"), Is.Null);
        }

        [Test]
        public void PresenterRuntime_UsesExistingScopeAndParamFlow_InsteadOfDedicatedPartType()
        {
            Type presenterState = typeof(Ludots.Core.Presentation.Presenters.PresenterState);
            FieldInfo? scopeField = presenterState.GetField("ScopeId", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(scopeField, Is.Not.Null, "Presenter runtime should continue using ScopeId for grouping.");

            Assembly assembly = typeof(PresenterCommand).Assembly;
            Type? legacyPartType = assembly.GetType("Ludots.Core.Presentation.Presenters.PresenterPart", throwOnError: false);
            Assert.That(legacyPartType, Is.Null, "Runtime must not introduce a dedicated PresenterPart type.");
        }

        [Test]
        public void PresenterStateContract_ExposesIdentityAndBehaviorFields()
        {
            FieldInfo[] fields = typeof(Ludots.Core.Presentation.Presenters.PresenterState).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = fields.Select(static field => field.Name).ToArray();

            Assert.That(fieldNames, Does.Contain("DefId"));
            Assert.That(fieldNames, Does.Contain("ScopeId"));
            Assert.That(fieldNames, Does.Contain("OwnerEntity"));
            Assert.That(fieldNames, Does.Contain("BehaviorActiveMask"));
            Assert.That(fieldNames, Does.Contain("Version"));
            Assert.That(fieldNames, Does.Contain("StableId"));
        }

        [Test]
        public void PresenterDefinitionContract_ExposesWave2DefinitionFields()
        {
            FieldInfo[] fields = typeof(PresenterDefinition).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] fieldNames = fields.Select(static field => field.Name).ToArray();

            Assert.That(fieldNames, Does.Contain("Key"));
            Assert.That(fieldNames, Does.Contain("Extends"));
            Assert.That(fieldNames, Does.Contain("Children"));
            Assert.That(fieldNames, Does.Contain("Behaviors"));
            Assert.That(fieldNames, Does.Contain("ParamDefaults"));
        }

        [Test]
        public void PresenterScopeTagRegistry_ProvidesStringToIntSsot()
        {
            PresenterScopeTagRegistry.Clear();

            int working = PresenterScopeTagRegistry.Register("working");
            int structure = PresenterScopeTagRegistry.Register("structure");

            Assert.That(working, Is.GreaterThan(0));
            Assert.That(structure, Is.GreaterThan(0));
            Assert.That(PresenterScopeTagRegistry.GetId("working"), Is.EqualTo(working));
            Assert.That(PresenterScopeTagRegistry.GetName(structure), Is.EqualTo("structure"));
        }

        [Test]
        public void AnimationChannelRegistry_InternsExactNamedSlots()
        {
            AnimationChannelRegistry.Clear();

            int locomotion = AnimationChannelRegistry.Register(AnimationChannelRegistry.Locomotion);
            int aimYaw = AnimationChannelRegistry.Register(AnimationChannelRegistry.AimYaw);
            int recoil = AnimationChannelRegistry.Register(AnimationChannelRegistry.Recoil);

            Assert.That(locomotion, Is.GreaterThan(0));
            Assert.That(aimYaw, Is.GreaterThan(0));
            Assert.That(recoil, Is.GreaterThan(0));
            Assert.That(AnimationChannelRegistry.GetId(AnimationChannelRegistry.Locomotion), Is.EqualTo(locomotion));
            Assert.That(AnimationChannelRegistry.GetName(aimYaw), Is.EqualTo(AnimationChannelRegistry.AimYaw));
            Assert.That(
                () => AnimationChannelRegistry.Register(" locomotion "),
                Throws.ArgumentException);
        }

        [Test]
        public void TransformSourceContract_MatchesArchitectureValues()
        {
            Assert.That((byte)TransformSource.InheritParent, Is.EqualTo(0));
            Assert.That((byte)TransformSource.EntityTransform, Is.EqualTo(1));
            Assert.That((byte)TransformSource.SplineDriven, Is.EqualTo(2));
            Assert.That((byte)TransformSource.BoneAttached, Is.EqualTo(3));
            Assert.That((byte)TransformSource.AttachedToParent, Is.EqualTo(4));
            Assert.That((byte)TransformSource.WorldFixed, Is.EqualTo(5));
        }

        [Test]
        public void PresenterCommandValueSourceContract_MatchesArchitectureValues()
        {
            Assert.That((byte)PresenterCommandValueSource.Fixed, Is.EqualTo(0));
            Assert.That((byte)PresenterCommandValueSource.EventKeyId, Is.EqualTo(1));
            Assert.That((byte)PresenterCommandValueSource.EventPayloadA, Is.EqualTo(2));
            Assert.That((byte)PresenterCommandValueSource.EventPayloadB, Is.EqualTo(3));
            Assert.That((byte)PresenterCommandValueSource.EventMagnitude, Is.EqualTo(4));
        }

        [Test]
        public void PresentationRequest_RemainsAdapterNeutralOutputGate()
        {
            FieldInfo[] fields = typeof(PresentationRequest).GetFields(BindingFlags.Instance | BindingFlags.Public);

            string[] forbiddenTokens =
            {
                "PresenterRule",
                "PresenterCommand",
                "PresenterCommandBuffer",
                "PresentationBehaviorDefinition",
            };

            string[] offendingNames =
                fields.Where(field => forbiddenTokens.Any(token => field.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    .Select(static field => field.Name)
                    .ToArray();

            string[] offendingTypes =
                fields.Where(field =>
                    {
                        string? fullName = field.FieldType.FullName;
                        return fullName != null &&
                               forbiddenTokens.Any(token => fullName.Contains(token, StringComparison.OrdinalIgnoreCase));
                    })
                    .Select(static field => $"{field.Name}:{field.FieldType.FullName}")
                    .ToArray();

            Assert.That(offendingNames, Is.Empty, "PresentationRequest must stay an adapter-neutral output packet.");
            Assert.That(offendingTypes, Is.Empty, "PresentationRequest must not store orchestration or phase contracts.");
        }

        [Test]
        public void VisualRenderPayloadContract_ExposesSharedSurfaceAndCustomDataFields()
        {
            string[] fieldNames = typeof(VisualRenderPayload)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(static field => field.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            string[] expected =
            {
                "AnimationOverlay",
                "AnimationProfileId",
                "Animator",
                "AssetKind",
                "Color",
                "MaterialCustomData",
                "MaterialId",
                "MeshAssetId",
                "Position",
                "RenderPath",
                "Rotation",
                "Scale",
                "SortId",
                "StableId",
                "SurfaceLayerKey",
                "TemplateId",
                "Visibility",
            };

            Assert.That(fieldNames, Is.EqualTo(expected));
            Assert.That(typeof(PresentationVisualProxy).GetField("LOD", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(typeof(PrimitiveDrawItem).GetField("LOD", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
            Assert.That(typeof(SkinnedVisualBatchItem).GetField("LOD", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        }

        [Test]
        public void VisualRenderPayloadContainers_StoreSharedStateOnlyThroughPayload()
        {
            string[] sharedPayloadFields = typeof(VisualRenderPayload)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(static field => field.Name)
                .ToArray();

            AssertPayloadContainer(typeof(PresentationVisualProxy), sharedPayloadFields, "ProxyKind", "Payload", "Mobility", "Flags", "LOD");
            AssertPayloadContainer(typeof(PrimitiveDrawItem), sharedPayloadFields, "Payload", "Mobility", "Flags", "LOD");
            AssertPayloadContainer(typeof(SkinnedVisualBatchItem), sharedPayloadFields, "Payload", "LOD");
        }

        [Test]
        public void PresentationVisualProxyEmitter_AssignsPayloadAsWhole()
        {
            string repoRoot = FindRepoRoot();
            string sourcePath = Path.Combine(repoRoot, "src", "Core", "Presentation", "Rendering", "PresentationVisualProxyEmitter.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("Payload = proxy.Payload"));
        }

        [Test]
        public void LegacyPresenterVisualKindAndBuiltinDefinitions_AreRemovedFromCore()
        {
            Assembly assembly = typeof(PresenterCommand).Assembly;

            Assert.That(assembly.GetType("Ludots.Core.Presentation.Presenters.PresenterVisualKind", throwOnError: false), Is.Null);
            Assert.That(assembly.GetType("Ludots.Core.Presentation.Presenters.BuiltinPresenterDefinitions", throwOnError: false), Is.Null);
        }

        [Test]
        public void PresenterCompiledBindingContract_LandsAtDefinitionRegister()
        {
            FieldInfo[] compiledFields = typeof(CompiledBinding).GetFields(BindingFlags.Instance | BindingFlags.Public);
            string[] compiledFieldNames = compiledFields.Select(static field => field.Name).ToArray();
            Assert.That(compiledFieldNames, Does.Contain("SourceAttributeId"));
            Assert.That(compiledFieldNames, Does.Contain("SourceTagId"));
            Assert.That(compiledFieldNames, Does.Contain("TargetParamKey"));
            Assert.That(compiledFieldNames, Does.Contain("Mode"));
            Assert.That(compiledFieldNames, Does.Contain("Thresholds"));

            FieldInfo[] definitionFields = typeof(PresenterDefinition).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
            string[] definitionFieldNames = definitionFields.Select(static field => field.Name).ToArray();
            Assert.That(definitionFieldNames, Does.Contain("CompiledBindings"));
            Assert.That(definitionFieldNames, Does.Contain("BehaviorPresenceMask"));
        }

        [Test]
        public void PresenterDefinitionRegister_RejectsOutOfRangeBehaviorSlotIndex()
        {
            var registry = new PresenterDefinitionRegistry();
            var definition = new PresenterDefinition
            {
                Behaviors = new[]
                {
                    new BehaviorSlot
                    {
                        Kind = BehaviorKind.Sound,
                        SlotIndex = 32,
                    },
                },
            };

            Assert.That(
                () => registry.Register("bad.slot.index", definition),
                Throws.InvalidOperationException.With.Message.StartsWith("PRESENTATION.PRESENTER.ERR.InvalidBehaviorSlotIndex"));
        }

        [Test]
        public void PresenterMainlineSystems_ArePresentInCore()
        {
            Assembly assembly = typeof(PresenterCommand).Assembly;
            Assert.That(
                assembly.GetType("Ludots.Core.Presentation.Systems.PresenterRuntimeSystem", throwOnError: false),
                Is.Not.Null,
                "PresenterRuntimeSystem must exist as lifecycle SSOT.");
            Assert.That(
                assembly.GetType("Ludots.Core.Presentation.Systems.PresenterEmitSystem", throwOnError: false),
                Is.Not.Null,
                "PresenterEmitSystem must exist as emit SSOT.");
        }

        private static IEnumerable<string> EnumerateForbiddenMemberDependencies(Type contractType)
        {
            foreach (FieldInfo field in contractType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return field.Name;
                yield return field.FieldType.FullName ?? field.FieldType.Name;
            }

            foreach (PropertyInfo property in contractType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                yield return property.Name;
                yield return property.PropertyType.FullName ?? property.PropertyType.Name;
            }

            foreach (ConstructorInfo constructor in contractType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    yield return parameter.Name ?? string.Empty;
                    yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                }
            }

            foreach (MethodInfo method in contractType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return method.Name;
                yield return method.ReturnType.FullName ?? method.ReturnType.Name;
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return parameter.Name ?? string.Empty;
                    yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
                }
            }
        }

        private static void AssertPayloadContainer(Type containerType, string[] sharedPayloadFields, params string[] expectedPublicFields)
        {
            string[] fieldNames = containerType
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(static field => field.Name)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                fieldNames,
                Is.EqualTo(expectedPublicFields.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
                $"{containerType.Name} should expose only its payload wrapper fields.");

            string[] duplicatedSharedFields = fieldNames
                .Where(name => sharedPayloadFields.Contains(name, StringComparer.Ordinal))
                .ToArray();

            Assert.That(
                duplicatedSharedFields,
                Is.Empty,
                $"{containerType.Name} must not reintroduce duplicated VisualRenderPayload fields outside Payload.");
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
