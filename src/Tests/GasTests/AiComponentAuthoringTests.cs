using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using CombatStanceBehaviorMod;
using CombatStanceBehaviorMod.Components;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class AiComponentAuthoringTests
    {
        private const string CombatStanceModId = "CombatStanceBehaviorMod";

        [SetUp]
        public void SetUp()
        {
            Ludots.Core.Config.ComponentRegistry.SetUtilityAiAuthoringCatalog(CreateAuthoringCatalog());
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(CombatStanceModId);
        }

        [TearDown]
        public void TearDown()
        {
            Ludots.Core.Config.ComponentRegistry.SetUtilityAiAuthoringCatalog(null);
            Ludots.Core.Config.ComponentRegistry.UnregisterSource(CombatStanceModId);
        }

        [Test]
        public void EntityBuilder_AppliesUtilityAiAgentProfileKeyFromTemplate()
        {
            using var world = World.Create();
            var templates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal)
            {
                ["mage"] = new EntityTemplate
                {
                    Id = "mage",
                    Components = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
                    {
                        ["UtilityAiAgent"] = JsonNode.Parse("""{ "profile": "Profile.Basic" }""")!,
                        ["UtilityAiState"] = JsonNode.Parse("""{}""")!,
                    },
                },
            };
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mage"] = "ModA:Entities/templates.json",
            };

            Entity entity = new EntityBuilder(world, templates, sources)
                .UseTemplate("mage")
                .WithEntityContext("Map 'ai_authoring' entity 'mage_01'")
                .Build();

            That(world.Get<UtilityAiAgent>(entity).ProfileId, Is.EqualTo(0));
            That(world.Get<UtilityAiState>(entity).CurrentDecisionId, Is.EqualTo(-1));
        }

        [Test]
        public void EntityBuilder_UnknownUtilityAiProfileFailsFastWithTemplateContext()
        {
            using var world = World.Create();
            var templates = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal)
            {
                ["mage"] = new EntityTemplate
                {
                    Id = "mage",
                    Components = new Dictionary<string, JsonNode>(StringComparer.Ordinal)
                    {
                        ["UtilityAiAgent"] = JsonNode.Parse("""{ "profile": "Profile.Missing" }""")!,
                    },
                },
            };
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mage"] = "ModA:Entities/templates.json",
            };

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                new EntityBuilder(world, templates, sources)
                    .UseTemplate("mage")
                    .WithEntityContext("Map 'ai_authoring' entity 'mage_01'")
                    .Build())!;

            That(ex.Message, Does.Contain("Map 'ai_authoring' entity 'mage_01'"));
            That(ex.Message, Does.Contain("ModA:Entities/templates.json"));
            That(ex.Message, Does.Contain("unknown Utility AI profile 'Profile.Missing'"));
        }

        [Test]
        public void ComponentRegistry_RejectsUtilityAiNumericProfileAuthoring()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    "UtilityAiAgent",
                    JsonNode.Parse("""{ "profileId": 0 }""")!,
                    "ModA:Entities/templates.json template 'mage'"))!;

            That(ex.Message, Does.Contain("profileId"));
            That(ex.Message, Does.Contain("Use 'profile'"));
        }

        [Test]
        public void ComponentRegistry_AppliesUtilityAiPriorityAndActuatorKeys()
        {
            using var world = World.Create();
            Entity target = world.Create();
            Entity actor = world.Create();

            Ludots.Core.Config.ComponentRegistry.Apply(
                target,
                "UtilityAiTargetPriority",
                JsonNode.Parse("""{ "bucket": "High" }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(
                actor,
                "ActuatorReadiness",
                JsonNode.Parse("""{ "actuator": "Actuator.Primary", "initialReady01": 0.5, "requiresPreparation": true }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(
                actor,
                "AimGate",
                JsonNode.Parse("""{ "actuator": "Actuator.Primary", "initialReady01": 1 }""")!);
            Ludots.Core.Config.ComponentRegistry.Apply(
                actor,
                "UtilityAiDecisionTrace",
                JsonNode.Parse("""{}""")!);

            That(world.Get<UtilityAiTargetPriority>(target).Bucket, Is.EqualTo((int)UtilityAiTargetPriorityBucket.High));
            That(world.Get<ActuatorReadiness>(actor).ActuatorId, Is.EqualTo(0));
            That(world.Get<ActuatorReadiness>(actor).Ready01, Is.EqualTo(0.5f));
            That(world.Get<ActuatorReadiness>(actor).RequiresPreparation, Is.EqualTo(1));
            That(world.Get<AimGate>(actor).ActuatorId, Is.EqualTo(0));
            That(world.Get<AimGate>(actor).Ready01, Is.EqualTo(1f));
            That(world.Has<UtilityAiDecisionTrace>(actor), Is.True);
        }

        [Test]
        public void ComponentRegistry_RejectsNumericPriorityAndUnknownActuatorAuthoring()
        {
            using var world = World.Create();
            Entity entity = world.Create();

            InvalidOperationException priority = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    "UtilityAiTargetPriority",
                    JsonNode.Parse("""{ "Bucket": 3 }""")!))!;
            That(priority.Message, Does.Contain("Bucket"));
            That(priority.Message, Does.Contain("bucket"));

            InvalidOperationException actuator = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    "AimGate",
                    JsonNode.Parse("""{ "actuator": "Actuator.Missing" }""")!))!;
            That(actuator.Message, Does.Contain("unknown Utility AI actuator 'Actuator.Missing'"));
        }

        [Test]
        public void CombatStanceState_AppliesStanceKeyFromModAuthoring()
        {
            RegisterCombatStanceAuthoring();
            using var world = World.Create();
            Entity entity = world.Create();

            Ludots.Core.Config.ComponentRegistry.Apply(
                entity,
                "CombatStanceState",
                JsonNode.Parse("""{ "stance": "ReturnFire", "leashRadiusCm": 900, "retaliationTtlSteps": 30 }""")!,
                "CombatStanceBehaviorMod:Entities/templates.json template 'guard'");

            var state = world.Get<CombatStanceState>(entity);
            That(state.Stance, Is.EqualTo(CombatStances.ReturnFire));
            That(state.LeashRadiusCm, Is.EqualTo(900));
            That(state.RetaliationTtlSteps, Is.EqualTo(30));
        }

        [Test]
        public void CombatStanceState_RejectsNumericAndUnknownStanceAuthoring()
        {
            RegisterCombatStanceAuthoring();
            using var world = World.Create();
            Entity entity = world.Create();

            InvalidOperationException numeric = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    "CombatStanceState",
                    JsonNode.Parse("""{ "stanceId": 1, "leashRadiusCm": 900, "retaliationTtlSteps": 30 }""")!))!;
            That(numeric.Message, Does.Contain("stanceId"));
            That(numeric.Message, Does.Contain("Use 'stance'"));

            InvalidOperationException unknown = Throws<InvalidOperationException>(() =>
                Ludots.Core.Config.ComponentRegistry.Apply(
                    entity,
                    "CombatStanceState",
                    JsonNode.Parse("""{ "stance": "Aggressive", "leashRadiusCm": 900, "retaliationTtlSteps": 30 }""")!,
                    "CombatStanceBehaviorMod:Entities/templates.json template 'guard'"))!;
            That(unknown.Message, Does.Contain("CombatStanceBehaviorMod:Entities/templates.json"));
            That(unknown.Message, Does.Contain("unknown combat stance 'Aggressive'"));
        }

        private static UtilityAiAuthoringCatalog CreateAuthoringCatalog()
        {
            return new UtilityAiAuthoringCatalog(
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Profile.Basic"] = 0,
                },
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Stance.Default"] = 0,
                },
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["Actuator.Primary"] = 0,
                });
        }

        private static void RegisterCombatStanceAuthoring()
        {
            new CombatStanceBehaviorModEntry().OnLoad(new TestModContext(CombatStanceModId));
        }

        private sealed class TestModContext : IModContext
        {
            public TestModContext(string modId)
            {
                ModId = modId;
            }

            public string ModId { get; }
            public IVirtualFileSystem VFS => null!;
            public FunctionRegistry FunctionRegistry => null!;
            public SystemFactoryRegistry SystemFactoryRegistry => null!;
            public TriggerDecoratorRegistry TriggerDecorators => null!;
            public IModExtensionRegistration Extensions { get; } = RejectingModExtensionRegistration.Instance;
            public LogChannel LogChannel => default;

            public void OnEvent(EventKey eventKey, Func<ScriptContext, Task> handler)
            {
            }

            public void Log(string message)
            {
            }

            public void Log(LogLevel level, string message)
            {
            }

            public Stream GetResource(string uri)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class RejectingModExtensionRegistration : IModExtensionRegistration
        {
            public static readonly RejectingModExtensionRegistration Instance = new();

            private RejectingModExtensionRegistration()
            {
            }

            public IGasModExtensionRegistration Gas { get; } = new RejectingGasRegistration();
            public IPresentationModExtensionRegistration Presentation { get; } = new RejectingPresentationRegistration();

            private sealed class RejectingGasRegistration : IGasModExtensionRegistration
            {
                public int RegisterBuiltinHandler(string key, BuiltinHandlerFn handler)
                {
                    throw new NotSupportedException("This test mod context does not support extension registration.");
                }

                public int RegisterGraphOp(
                    string key,
                    GraphValueType outputType,
                    GasGraphOpHandler handler,
                    params GraphValueType[] inputTypes)
                {
                    throw new NotSupportedException("This test mod context does not support extension registration.");
                }

                public int RegisterGraphOp(
                    string key,
                    GraphValueType outputType,
                    byte? fixedRegister,
                    GasGraphOpHandler handler,
                    params GraphValueType[] inputTypes)
                {
                    throw new NotSupportedException("This test mod context does not support extension registration.");
                }
            }

            private sealed class RejectingPresentationRegistration : IPresentationModExtensionRegistration
            {
                public int RegisterPerformerCommand(
                    string key,
                    in PerformerCommandExtensionDescriptor descriptor)
                {
                    throw new NotSupportedException("This test mod context does not support extension registration.");
                }

                public int RegisterPerformerBehavior(
                    string key,
                    in PerformerBehaviorExtensionDescriptor descriptor)
                {
                    throw new NotSupportedException("This test mod context does not support extension registration.");
                }
            }
        }
    }
}
