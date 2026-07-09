using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class ModExtensionRegistrationTests
    {
        [Test]
        public void ModExtensions_RegisterProviderKeysAndAllowConsumerGraphReferences()
        {
            var hub = new ModExtensionHub();
            ModContext provider = CreateContext("ProviderMod", hub);

            int builtinId = provider.Extensions.Gas.RegisterBuiltinHandler(
                "ProviderMod.ApplyCustom",
                NoopBuiltinHandler);
            int opCode = provider.Extensions.Gas.RegisterGraphOp(
                "ProviderMod.CustomOp",
                GraphValueType.Void,
                NoopGraphOp);
            int commandKindId = provider.Extensions.Presentation.RegisterPerformerCommand(
                "ProviderMod.CustomCommand",
                new PerformerCommandExtensionDescriptor(
                    PerformerCommandRouteStrategy.SingleRuntime,
                    NoopCommand));
            int behaviorKindId = provider.Extensions.Presentation.RegisterPerformerBehavior(
                "ProviderMod.CustomBehavior",
                new PerformerBehaviorExtensionDescriptor(
                    PerformerBehaviorExecutionLane.ContinuousTick,
                    NoopBehavior));

            hub.Freeze();

            Assert.That(builtinId, Is.GreaterThanOrEqualTo(BuiltinHandlerRegistry.FirstModHandlerId));
            Assert.That(opCode, Is.GreaterThanOrEqualTo(GasGraphOpRegistry.FirstModOpCode));
            Assert.That(commandKindId, Is.GreaterThanOrEqualTo(PerformerCommandKindRegistry.FirstModCommandKindId));
            Assert.That(behaviorKindId, Is.GreaterThanOrEqualTo(PerformerBehaviorKindRegistry.FirstModBehaviorKindId));

            Assert.That(hub.Gas.BuiltinHandlers.GetId("ProviderMod.ApplyCustom"), Is.EqualTo(builtinId));
            Assert.That(hub.Gas.GraphOps.TryGet("ProviderMod.CustomOp", out GasGraphOpDefinition definition), Is.True);
            Assert.That(definition.OpCode, Is.EqualTo(opCode));
            Assert.That(hub.Presentation.PerformerCommands.GetId("ProviderMod.CustomCommand"), Is.EqualTo(commandKindId));
            Assert.That(hub.Presentation.PerformerBehaviors.GetId("ProviderMod.CustomBehavior"), Is.EqualTo(behaviorKindId));

            var consumerGraph = new GraphConfig
            {
                Id = "ConsumerMod.UsesProviderOp",
                Entry = "custom",
                Nodes =
                {
                    new GraphNodeConfig
                    {
                        Id = "custom",
                        Op = "ProviderMod.CustomOp",
                    },
                },
            };

            var (package, diagnostics) = GraphCompiler.Compile(consumerGraph, hub.Gas.GraphOps);

            Assert.That(diagnostics.Exists(d => d.Severity == GraphDiagnosticSeverity.Error), Is.False);
            Assert.That(package.HasValue, Is.True);
            Assert.That(package!.Value.Program[0].Op, Is.EqualTo(opCode));
        }

        [Test]
        public void ModExtensions_RejectNamespaceImpersonationAndLateRegistration()
        {
            var hub = new ModExtensionHub();
            ModContext provider = CreateContext("ProviderMod", hub);
            ModContext consumer = CreateContext("ConsumerMod", hub);

            provider.Extensions.Gas.RegisterGraphOp(
                "ProviderMod.CustomOp",
                GraphValueType.Void,
                NoopGraphOp);

            InvalidOperationException impersonation = Assert.Throws<InvalidOperationException>(
                () => consumer.Extensions.Gas.RegisterGraphOp(
                    "ProviderMod.OtherOp",
                    GraphValueType.Void,
                    NoopGraphOp))!;
            Assert.That(impersonation.Message, Does.Contain("ConsumerMod."));

            hub.Freeze();

            InvalidOperationException lateRegistration = Assert.Throws<InvalidOperationException>(
                () => provider.Extensions.Gas.RegisterGraphOp(
                    "ProviderMod.AfterFreeze",
                    GraphValueType.Void,
                    NoopGraphOp))!;
            Assert.That(lateRegistration.Message, Does.Contain("frozen"));
        }

        [Test]
        public void ModExtensions_RejectDuplicateAndFrozenBuiltinHandlerRegistration()
        {
            var hub = new ModExtensionHub();
            ModContext provider = CreateContext("ProviderMod", hub);

            provider.Extensions.Gas.RegisterBuiltinHandler(
                "ProviderMod.ApplyCustom",
                NoopBuiltinHandler);

            InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(
                () => provider.Extensions.Gas.RegisterBuiltinHandler(
                    "ProviderMod.ApplyCustom",
                    NoopBuiltinHandler))!;
            Assert.That(duplicate.Message, Does.Contain("already registered"));

            hub.Freeze();

            InvalidOperationException frozen = Assert.Throws<InvalidOperationException>(
                () => provider.Extensions.Gas.RegisterBuiltinHandler(
                    "ProviderMod.AfterFreeze",
                    NoopBuiltinHandler))!;
            Assert.That(frozen.Message, Does.Contain("frozen"));
        }

        [Test]
        public void ModExtensions_RejectInvalidGraphOpShapeAtRegistration()
        {
            var hub = new ModExtensionHub();
            ModContext provider = CreateContext("ProviderMod", hub);

            InvalidOperationException targetListOutput = Assert.Throws<InvalidOperationException>(
                () => provider.Extensions.Gas.RegisterGraphOp(
                    "ProviderMod.TargetListOutput",
                    GraphValueType.TargetList,
                    NoopGraphOp))!;
            Assert.That(targetListOutput.Message, Does.Contain("unsupported output type"));

            InvalidOperationException voidInput = Assert.Throws<InvalidOperationException>(
                () => provider.Extensions.Gas.RegisterGraphOp(
                    "ProviderMod.VoidInput",
                    GraphValueType.Void,
                    NoopGraphOp,
                    GraphValueType.Void))!;
            Assert.That(voidInput.Message, Does.Contain("unsupported input type"));

            InvalidOperationException fixedRegister = Assert.Throws<InvalidOperationException>(
                () => provider.Extensions.Gas.RegisterGraphOp(
                    "ProviderMod.BadFixedRegister",
                    GraphValueType.Float,
                    (byte)GraphVmLimits.MaxFloatRegisters,
                    NoopGraphOp))!;
            Assert.That(fixedRegister.Message, Does.Contain("fixed register"));
        }

        [Test]
        public void ModExtensions_GraphOpInputTypesAreFrozenByValue()
        {
            var hub = new ModExtensionHub();
            ModContext provider = CreateContext("ProviderMod", hub);
            var inputTypes = new[] { GraphValueType.Float };

            provider.Extensions.Gas.RegisterGraphOp(
                "ProviderMod.ImmutableInputs",
                GraphValueType.Float,
                NoopGraphOp,
                inputTypes);

            inputTypes[0] = GraphValueType.Entity;

            Assert.That(hub.Gas.GraphOps.TryGet("ProviderMod.ImmutableInputs", out GasGraphOpDefinition definition), Is.True);
            Assert.That(definition.InputTypes, Is.EqualTo(new[] { GraphValueType.Float }));
        }

        private static ModContext CreateContext(string modId, ModExtensionHub hub)
        {
            return new ModContext(
                modId,
                new VirtualFileSystem(),
                new FunctionRegistry(),
                new TriggerManager(),
                new SystemFactoryRegistry(),
                new TriggerDecoratorRegistry(),
                hub);
        }

        private static void NoopBuiltinHandler(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
        }

        private static void NoopGraphOp(ref GraphExecutionState state, in GraphInstruction ins, ref int pc)
        {
        }

        private static void NoopCommand(in PerformerCommandExecutionContext context)
        {
        }

        private static void NoopBehavior(in PerformerBehaviorExecutionContext context)
        {
        }
    }
}
