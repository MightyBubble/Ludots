using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ExchangeRuntimeTests
    {
        private const int CreditDef = 1;
        private const int ArtifactDef = 2;
        private const int GemDef = 3;
        private const int TokenDef = 4;
        private const int LargeDef = 5;

        [SetUp]
        public void SetUp()
        {
            EffectParamKeys.Initialize();
        }

        [Test]
        public void TryExecute_StaticOperation_ConsumesInputsAndCreatesOutputAtomically()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 3, stashHeight: 2);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 20);
            Put(fixture.Inventory, ArtifactDef, stash, 1, 0);

            int operationId = fixture.Operations.Register("test.exchange", new ExchangeOperationDefinition
            {
                Id = "test.exchange",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 10),
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, ArtifactDef, 1)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(actor));

            That(result.Succeeded, Is.True);
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(10));
            That(fixture.Inventory.CountStackUnits(actor, ArtifactDef), Is.EqualTo(0));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(1));
        }

        [Test]
        public void TryExecute_OutputBlocked_DoesNotConsumeInputs()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 2, stashHeight: 1);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 20);
            Put(fixture.Inventory, ArtifactDef, stash, 1, 0);

            int operationId = fixture.Operations.Register("test.blocked", new ExchangeOperationDefinition
            {
                Id = "test.blocked",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 10)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(actor));

            That(result.Status, Is.EqualTo(ExchangeExecutionStatus.OutputBlocked));
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(20));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
        }

        [Test]
        public void TryExecute_MoveOutputFailure_RollsBackConsumedInputAndPriorMove()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 2, stashHeight: 2, targetWidth: 2, targetHeight: 1);
            Entity source = world.Create();
            Entity target = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(source, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Entity targetStash = fixture.Inventory.CreateContainer(target, ItemContainerOwnerKind.Actor, fixture.TargetLayout, ItemContainerPurpose.Stash);
            Entity artifact = Put(fixture.Inventory, ArtifactDef, stash, 0, 0);
            Put(fixture.Inventory, CreditDef, stash, 1, 0, stack: 10);
            Put(fixture.Inventory, TokenDef, targetStash, 0, 0);

            int operationId = fixture.Operations.Register("test.move.rollback", new ExchangeOperationDefinition
            {
                Id = "test.move.rollback",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 5)
                },
                Outputs = new[]
                {
                    MoveItem(RoleSlot.Target, ItemContainerPurpose.Stash, ArtifactDef, RoleSlot.Source),
                    CreateItem(RoleSlot.Target, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(source, target));

            That(result.Status, Is.EqualTo(ExchangeExecutionStatus.OutputBlocked));
            That(fixture.Inventory.CountStackUnits(source, CreditDef), Is.EqualTo(10));
            That(world.Get<ItemLocationCm>(artifact).Container, Is.EqualTo(stash));
            That(fixture.Inventory.CountStackUnits(target, GemDef), Is.EqualTo(0));
        }

        [Test]
        public void TryExecute_CumulativeOutputReservation_BlocksBeforeMutation()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 2, stashHeight: 2, targetWidth: 1, targetHeight: 1);
            Entity source = world.Create();
            Entity target = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(source, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Entity targetStash = fixture.Inventory.CreateContainer(target, ItemContainerOwnerKind.Actor, fixture.TargetLayout, ItemContainerPurpose.Stash);
            Entity artifact = Put(fixture.Inventory, ArtifactDef, stash, 0, 0);
            Put(fixture.Inventory, CreditDef, stash, 1, 0, stack: 10);

            int operationId = fixture.Operations.Register("test.cumulative.output.blocked", new ExchangeOperationDefinition
            {
                Id = "test.cumulative.output.blocked",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 5)
                },
                Outputs = new[]
                {
                    MoveItem(RoleSlot.Target, ItemContainerPurpose.Stash, ArtifactDef, RoleSlot.Source),
                    CreateItem(RoleSlot.Target, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(source, target));

            That(result.Status, Is.EqualTo(ExchangeExecutionStatus.OutputBlocked));
            That(fixture.Inventory.CountStackUnits(source, CreditDef), Is.EqualTo(10));
            That(world.Get<ItemLocationCm>(artifact).Container, Is.EqualTo(stash));
            That(fixture.Inventory.CountStackUnits(target, GemDef), Is.EqualTo(0));
            That(CountItemsInContainer(world, targetStash), Is.EqualTo(0));
        }

        [Test]
        public void ConsumeStackUnits_AppendsRollbackRecordsAcrossCalls()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 3, stashHeight: 2);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 20);
            Put(fixture.Inventory, ArtifactDef, stash, 1, 0);

            var consumed = new System.Collections.Generic.List<ItemConsumptionRecord>(4);

            That(fixture.Inventory.ConsumeStackUnits(actor, CreditDef, 20, consumed), Is.True);
            That(fixture.Inventory.ConsumeStackUnits(actor, ArtifactDef, 1, consumed), Is.True);
            That(consumed.Count, Is.EqualTo(2));
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(0));
            That(fixture.Inventory.CountStackUnits(actor, ArtifactDef), Is.EqualTo(0));

            fixture.Inventory.RestoreConsumedUnits(consumed);

            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(20));
            That(fixture.Inventory.CountStackUnits(actor, ArtifactDef), Is.EqualTo(1));
        }

        [Test]
        public void TryExecute_ScopedOperation_UsesOperationIdAndScopeKeyBeforeStaticDefinition()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 4, stashHeight: 1);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 30);

            int operationId = fixture.Operations.Register("test.dynamic", new ExchangeOperationDefinition
            {
                Id = "test.dynamic",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 20)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });
            fixture.Scoped.Set(operationId, ScopeKey.Named(77), new ExchangeOperationDefinition
            {
                Id = "test.dynamic#77",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 5)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, TokenDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(
                new ExchangeOperationKey(operationId, ScopeKey.Named(77)),
                new ExchangeExecutionContext(actor, scope: ScopeKey.Named(77)));

            That(result.Succeeded, Is.True);
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(25));
            That(fixture.Inventory.CountStackUnits(actor, TokenDef), Is.EqualTo(1));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
        }

        [Test]
        public void TryExecute_SameScopeDifferentOperation_DoesNotUseUnrelatedScopedDefinition()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 4, stashHeight: 1);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 30);

            int operationA = fixture.Operations.Register("test.dynamic.a", new ExchangeOperationDefinition
            {
                Id = "test.dynamic.a",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 20)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });
            int operationB = fixture.Operations.Register("test.dynamic.b", new ExchangeOperationDefinition
            {
                Id = "test.dynamic.b",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 7)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, LargeDef, 1)
                }
            });
            fixture.Scoped.Set(operationA, ScopeKey.Named(77), new ExchangeOperationDefinition
            {
                Id = "test.dynamic.a#77",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 5)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, TokenDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(
                new ExchangeOperationKey(operationB, ScopeKey.Named(77)),
                new ExchangeExecutionContext(actor, scope: ScopeKey.Named(77)));

            That(result.Succeeded, Is.True);
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(23));
            That(fixture.Inventory.CountStackUnits(actor, LargeDef), Is.EqualTo(1));
            That(fixture.Inventory.CountStackUnits(actor, TokenDef), Is.EqualTo(0));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
        }

        [Test]
        public void BuiltinExecuteExchange_UsesMergedParamsAndRuntimeContext()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 3, stashHeight: 1);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 10);

            int operationId = fixture.Operations.Register("test.gas.exchange", new ExchangeOperationDefinition
            {
                Id = "test.gas.exchange",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 3)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            var parameters = new EffectConfigParams();
            That(parameters.TryAddInt(EffectParamKeys.ExchangeOperationId, operationId), Is.True);
            var context = new EffectContext { Source = actor, Target = actor };
            var runtimeContext = new BuiltinHandlerExecutionContext { Exchange = fixture.Runtime };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);
            registry.Invoke(BuiltinHandlerId.ExecuteExchange, world, world.Create(), ref context, in parameters, new EffectTemplateData(), runtimeContext);

            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(7));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(1));
        }

        [Test]
        public void BuiltinExecuteExchange_NormalFailure_DoesNotThrowAndRecordsResult()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 2, stashHeight: 1);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 2);

            int operationId = fixture.Operations.Register("test.gas.exchange.failure", new ExchangeOperationDefinition
            {
                Id = "test.gas.exchange.failure",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 3)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            var parameters = new EffectConfigParams();
            That(parameters.TryAddInt(EffectParamKeys.ExchangeOperationId, operationId), Is.True);
            var context = new EffectContext { Source = actor, Target = actor };
            var runtimeContext = new BuiltinHandlerExecutionContext { Exchange = fixture.Runtime };

            var registry = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(registry);

            DoesNotThrow(() =>
                registry.Invoke(BuiltinHandlerId.ExecuteExchange, world, world.Create(), ref context, in parameters, new EffectTemplateData(), runtimeContext));

            That(runtimeContext.HasExchangeResult, Is.True);
            That(runtimeContext.LastExchangeResult.Status, Is.EqualTo(ExchangeExecutionStatus.InsufficientInput));
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(2));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
        }

        [Test]
        public void TryExecute_KeyScopeOverridesContextScope()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 4, stashHeight: 1);
            Entity actor = world.Create();
            Entity stash = fixture.Inventory.CreateContainer(actor, ItemContainerOwnerKind.Actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0, stack: 30);

            int operationId = fixture.Operations.Register("test.scope.override", new ExchangeOperationDefinition
            {
                Id = "test.scope.override",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 20)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });
            fixture.Scoped.Set(operationId, ScopeKey.Named(77), new ExchangeOperationDefinition
            {
                Id = "test.scope.override#77",
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 5)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, TokenDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(
                new ExchangeOperationKey(operationId, ScopeKey.Named(77)),
                new ExchangeExecutionContext(actor, scope: ScopeKey.Named(12)));

            That(result.Succeeded, Is.True);
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(25));
            That(fixture.Inventory.CountStackUnits(actor, TokenDef), Is.EqualTo(1));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
        }

        private static ExchangeFixture CreateFixture(World world, int stashWidth, int stashHeight, int targetWidth = 0, int targetHeight = 0)
        {
            var shapes = new ItemShapeRegistry();
            var layouts = new ItemLayoutRegistry();
            var definitions = new ItemDefinitionRegistry();
            int oneByOne = shapes.Register("shape_1x1", new ItemShapeDefinition
            {
                Id = "shape_1x1",
                Rotations = new[] { new ItemShapeRotation(1, 1, new[] { true }) }
            });
            int twoByOne = shapes.Register("shape_2x1", new ItemShapeDefinition
            {
                Id = "shape_2x1",
                Rotations = new[] { new ItemShapeRotation(2, 1, new[] { true, true }) }
            });

            int stashLayout = layouts.Register("layout_stash", new ItemLayoutDefinition
            {
                Id = "layout_stash",
                Purpose = ItemContainerPurpose.Stash,
                Width = stashWidth,
                Height = stashHeight
            }.InitializeBlockedMask(new bool[stashWidth * stashHeight]));
            int targetLayout = targetWidth > 0
                ? layouts.Register("layout_target", new ItemLayoutDefinition
                {
                    Id = "layout_target",
                    Purpose = ItemContainerPurpose.Stash,
                    Width = targetWidth,
                    Height = targetHeight
                }.InitializeBlockedMask(new bool[targetWidth * targetHeight]))
                : stashLayout;

            definitions.Register("credit", new ItemDefinition { Id = "credit", DisplayName = "Credit", ShapeId = oneByOne, MaxStack = 999 });
            definitions.Register("artifact", new ItemDefinition { Id = "artifact", DisplayName = "Artifact", ShapeId = oneByOne });
            definitions.Register("gem", new ItemDefinition { Id = "gem", DisplayName = "Gem", ShapeId = oneByOne });
            definitions.Register("token", new ItemDefinition { Id = "token", DisplayName = "Token", ShapeId = oneByOne });
            definitions.Register("large", new ItemDefinition { Id = "large", DisplayName = "Large", ShapeId = twoByOne });

            var inventory = new InventoryRuntimeService(world, shapes, layouts, definitions);
            var operations = new ExchangeOperationRegistry();
            var scoped = new ExchangeScopedOperationStore();
            var effects = new EffectRequestQueue();
            var runtime = new ExchangeRuntime(world, operations, scoped, inventory, effects);
            return new ExchangeFixture(inventory, operations, scoped, runtime, stashLayout, targetLayout);
        }

        private static Entity Put(InventoryRuntimeService inventory, int definitionId, Entity container, int x, int y, int stack = 1)
        {
            Entity item = inventory.CreateItem(definitionId, stack);
            That(inventory.TryMoveItemToGrid(item, container, x, y), Is.True);
            return item;
        }

        private static int CountItemsInContainer(World world, Entity container)
        {
            int count = 0;
            world.Query(in new QueryDescription().WithAll<ItemInstanceCm, ItemLocationCm>(), (Entity _, ref ItemInstanceCm _, ref ItemLocationCm location) =>
            {
                if (location.Container == container)
                {
                    count++;
                }
            });
            return count;
        }

        private static ExchangeOutputDefinition CreateItem(RoleSlot actor, ItemContainerPurpose purpose, int definitionId, int quantity)
        {
            return new ExchangeOutputDefinition(
                ExchangeOutputKind.CreateItem,
                actor,
                purpose,
                definitionId,
                quantity,
                0,
                0,
                RoleSlot.None,
                ItemContainerPurpose.None,
                0,
                RoleSlot.None,
                RoleSlot.None,
                RoleSlot.None);
        }

        private static ExchangeOutputDefinition MoveItem(
            RoleSlot actor,
            ItemContainerPurpose purpose,
            int definitionId,
            RoleSlot fromActor,
            ItemContainerPurpose fromPurpose = ItemContainerPurpose.None)
        {
            return new ExchangeOutputDefinition(
                ExchangeOutputKind.MoveItem,
                actor,
                purpose,
                definitionId,
                1,
                0,
                0,
                fromActor,
                fromPurpose,
                0,
                RoleSlot.None,
                RoleSlot.None,
                RoleSlot.None);
        }

        private readonly record struct ExchangeFixture(
            InventoryRuntimeService Inventory,
            ExchangeOperationRegistry Operations,
            ExchangeScopedOperationStore Scoped,
            ExchangeRuntime Runtime,
            int StashLayout,
            int TargetLayout);
    }
}
