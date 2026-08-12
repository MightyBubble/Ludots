using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [Category("ci-gate")]
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(source, fixture.StashLayout, ItemContainerPurpose.Stash);
            Entity targetStash = fixture.Inventory.CreateContainer(target, fixture.TargetLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(source, fixture.StashLayout, ItemContainerPurpose.Stash);
            Entity targetStash = fixture.Inventory.CreateContainer(target, fixture.TargetLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
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

        [Test]
        public void TryExecute_RelationshipRequirementDeniesBeforeOutputReservationAndAllowsQualifiedRelationship()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 3, stashHeight: 1, targetWidth: 1, targetHeight: 1);
            Entity source = world.Create();
            Entity target = world.Create();
            Entity sourceStash = fixture.Inventory.CreateContainer(source, fixture.StashLayout, ItemContainerPurpose.Stash);
            Entity targetStash = fixture.Inventory.CreateContainer(target, fixture.TargetLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, sourceStash, 0, 0, stack: 20);
            Put(fixture.Inventory, TokenDef, targetStash, 0, 0);

            int operationId = fixture.Operations.Register("test.relationship.gated", new ExchangeOperationDefinition
            {
                Id = "test.relationship.gated",
                RelationshipRequirements = new[]
                {
                    new ExchangeRelationshipRequirement(
                        RoleSlot.Source,
                        RoleSlot.Target,
                        fixture.DiplomacyTypeId,
                        fixture.TrustMetricId,
                        minimumMetric: 50,
                        maximumMetric: null,
                        fixture.EmbargoFlagId,
                        requiredFlagValue: false)
                },
                Inputs = new[]
                {
                    new ExchangeInputDefinition(ExchangeInputKind.ItemStack, RoleSlot.Source, CreditDef, 5)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Target, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult noRelationship = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(source, target));

            That(noRelationship.Status, Is.EqualTo(ExchangeExecutionStatus.RelationshipDenied));
            That(fixture.Inventory.CountStackUnits(source, CreditDef), Is.EqualTo(20));
            That(fixture.Inventory.CountStackUnits(target, GemDef), Is.EqualTo(0));

            fixture.Relationships.SetMetric(source, target, fixture.DiplomacyTypeId, fixture.TrustMetricId, 60);
            That(fixture.Inventory.ConsumeStackUnits(target, TokenDef, 1), Is.True);

            ExchangeExecutionResult allowed = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(source, target));

            That(allowed.Succeeded, Is.True);
            That(fixture.Inventory.CountStackUnits(source, CreditDef), Is.EqualTo(15));
            That(fixture.Inventory.CountStackUnits(target, GemDef), Is.EqualTo(1));

            fixture.Relationships.SetFlag(source, target, fixture.DiplomacyTypeId, fixture.EmbargoFlagId, enabled: true);

            ExchangeExecutionResult embargoed = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(source, target));

            That(embargoed.Status, Is.EqualTo(ExchangeExecutionStatus.RelationshipDenied));
            That(fixture.Inventory.CountStackUnits(source, CreditDef), Is.EqualTo(15));
            That(fixture.Inventory.CountStackUnits(target, GemDef), Is.EqualTo(1));
        }

        [Test]
        public void TryExecute_AttributeCostInput_UsesGasAttributeAndBlocksWhenInsufficient()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 3, stashHeight: 1);
            Entity actor = world.Create(AttributeBuffer.CreateAttached());
            fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            int goldId = AttributeRegistry.Register("Exchange.Tests.Gold");
            world.Get<AttributeBuffer>(actor).SetCurrent(goldId, 8f);

            int operationId = fixture.Operations.Register("test.attribute.cost.insufficient", new ExchangeOperationDefinition
            {
                Id = "test.attribute.cost.insufficient",
                Inputs = new[]
                {
                    ExchangeInputDefinition.AttributeCost(RoleSlot.Source, goldId, 10)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(actor));

            That(result.Status, Is.EqualTo(ExchangeExecutionStatus.InsufficientInput));
            That(result.DetailIndex, Is.EqualTo(0));
            That(world.Get<AttributeBuffer>(actor).GetCurrent(goldId), Is.EqualTo(8f));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
        }

        [Test]
        public void TryExecute_AttributeCostInput_SubtractsAttributeAndCreatesOutput()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 3, stashHeight: 1);
            Entity actor = world.Create(AttributeBuffer.CreateAttached());
            fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            int goldId = AttributeRegistry.Register("Exchange.Tests.SpendableGold");
            world.Get<AttributeBuffer>(actor).SetCurrent(goldId, 30f);

            int operationId = fixture.Operations.Register("test.attribute.cost.success", new ExchangeOperationDefinition
            {
                Id = "test.attribute.cost.success",
                Inputs = new[]
                {
                    ExchangeInputDefinition.AttributeCost(RoleSlot.Source, goldId, 12)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(actor));

            That(result.Succeeded, Is.True);
            That(world.Get<AttributeBuffer>(actor).GetCurrent(goldId), Is.EqualTo(18f));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(1));
        }

        [Test]
        public void TryExecute_OutputFailure_RollsBackAttributeCostAndPriorCreatedItems()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 2, stashHeight: 1);
            Entity actor = world.Create(AttributeBuffer.CreateAttached());
            Entity stash = fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            Put(fixture.Inventory, CreditDef, stash, 0, 0);
            int goldId = AttributeRegistry.Register("Exchange.Tests.RollbackGold");
            world.Get<AttributeBuffer>(actor).SetCurrent(goldId, 25f);

            int operationId = fixture.Operations.Register("test.attribute.cost.rollback", new ExchangeOperationDefinition
            {
                Id = "test.attribute.cost.rollback",
                Inputs = new[]
                {
                    ExchangeInputDefinition.AttributeCost(RoleSlot.Source, goldId, 7)
                },
                Outputs = new[]
                {
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, GemDef, 1),
                    CreateItem(RoleSlot.Source, ItemContainerPurpose.Stash, TokenDef, 1)
                }
            });

            ExchangeExecutionResult result = fixture.Runtime.TryExecute(operationId, new ExchangeExecutionContext(actor));

            That(result.Status, Is.EqualTo(ExchangeExecutionStatus.OutputBlocked));
            That(world.Get<AttributeBuffer>(actor).GetCurrent(goldId), Is.EqualTo(25f));
            That(fixture.Inventory.CountStackUnits(actor, GemDef), Is.EqualTo(0));
            That(fixture.Inventory.CountStackUnits(actor, TokenDef), Is.EqualTo(0));
            That(fixture.Inventory.CountStackUnits(actor, CreditDef), Is.EqualTo(1));
        }

        [Test]
        public void TryExecute_AttributeCostInput_HotPathAllocatesZeroAfterWarmup()
        {
            using World world = World.Create();
            var fixture = CreateFixture(world, stashWidth: 100, stashHeight: 1);
            Entity actor = world.Create(AttributeBuffer.CreateAttached());
            fixture.Inventory.CreateContainer(actor, fixture.StashLayout, ItemContainerPurpose.Stash);
            int goldId = AttributeRegistry.Register("Exchange.Tests.HotPathGold");
            world.Get<AttributeBuffer>(actor).SetCurrent(goldId, 10_000f);

            int operationId = fixture.Operations.Register("test.attribute.cost.hotpath", new ExchangeOperationDefinition
            {
                Id = "test.attribute.cost.hotpath",
                Inputs = new[]
                {
                    ExchangeInputDefinition.AttributeCost(RoleSlot.Source, goldId, 1)
                },
                Outputs = Array.Empty<ExchangeOutputDefinition>()
            });

            var context = new ExchangeExecutionContext(actor);
            for (int i = 0; i < 32; i++)
            {
                That(fixture.Runtime.TryExecute(operationId, in context).Succeeded, Is.True);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocated = MeasureAttributeCostExecutionAllocations(
                fixture.Runtime,
                operationId,
                in context,
                out int succeeded);
            That(succeeded, Is.EqualTo(512));
            That(allocated, Is.EqualTo(0), "Exchange AttributeCost hot path must allocate 0 bytes after warmup.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureAttributeCostExecutionAllocations(
            ExchangeRuntime runtime,
            int operationId,
            in ExchangeExecutionContext context,
            out int succeeded)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            succeeded = 0;
            for (int i = 0; i < 512; i++)
            {
                if (runtime.TryExecute(operationId, in context).Succeeded)
                {
                    succeeded++;
                }
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [Test]
        public void ExchangeConfigLoader_CompilesRelationshipRequirementsFromRegistryNames()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ExchangeRelGate", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Core", "Configs", "Exchange"));
                File.WriteAllText(
                    Path.Combine(root, "Core", "Configs", "config_catalog.json"),
                    """
                    [
                      { "Path": "Exchange/operations.json", "Policy": "ArrayById", "IdField": "id" }
                    ]
                    """);
                File.WriteAllText(
                    Path.Combine(root, "Core", "Configs", "Exchange", "operations.json"),
                    """
                    [
                      {
                        "id": "test.relationship.config",
                        "relationshipRequirements": [
                          {
                            "source": "Source",
                            "target": "Target",
                            "type": "Diplomacy",
                            "metric": "Trust",
                            "minimumMetric": 50,
                            "flag": "Embargo",
                            "flagValue": false
                          }
                        ],
                        "inputs": [
                          { "kind": "ItemStack", "actor": "Source", "item": "credit", "quantity": 5 }
                        ],
                        "outputs": [
                          { "kind": "CreateItem", "actor": "Target", "purpose": "Stash", "item": "gem", "quantity": 1 }
                        ]
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", Path.Combine(root, "Core"));
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

                var items = new ItemDefinitionRegistry();
                int creditId = items.Register("credit", new ItemDefinition { Id = "credit", DisplayName = "Credit", ShapeId = 1 });
                int gemId = items.Register("gem", new ItemDefinition { Id = "gem", DisplayName = "Gem", ShapeId = 1 });
                var relationshipTypes = new RelationshipTypeRegistry();
                var relationshipMetrics = new RelationshipMetricRegistry();
                var relationshipFlags = new RelationshipFlagRegistry();
                int diplomacyId = relationshipTypes.Register("Diplomacy");
                int trustId = relationshipMetrics.Register("Trust");
                int embargoId = relationshipFlags.Register("Embargo");
                var operations = new ExchangeOperationRegistry();
                var loader = new ExchangeConfigLoader(
                    pipeline,
                    operations,
                    items,
                    relationshipTypes,
                    relationshipMetrics,
                    relationshipFlags);

                loader.Load(catalog);

                int operationId = operations.GetId("test.relationship.config");
                That(operations.TryGet(operationId, out ExchangeOperationDefinition operation), Is.True);
                That(operation.RelationshipRequirements.Length, Is.EqualTo(1));
                ExchangeRelationshipRequirement requirement = operation.RelationshipRequirements[0];
                That(requirement.Source, Is.EqualTo(RoleSlot.Source));
                That(requirement.Target, Is.EqualTo(RoleSlot.Target));
                That(requirement.TypeId, Is.EqualTo(diplomacyId));
                That(requirement.MetricId, Is.EqualTo(trustId));
                That(requirement.MetricComparison, Is.EqualTo(ExchangeRelationshipMetricComparison.GreaterOrEqual));
                That(requirement.MinimumMetric, Is.EqualTo(50));
                That(requirement.FlagId, Is.EqualTo(embargoId));
                That(requirement.RequiredFlagValue, Is.False);
                That(operation.Inputs[0].ItemDefinitionId, Is.EqualTo(creditId));
                That(operation.Outputs[0].ItemDefinitionId, Is.EqualTo(gemId));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
        }

        [Test]
        public void ExchangeConfigLoader_CompilesAttributeCostInputFromAttributeRegistryName()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ExchangeAttributeCost", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Core", "Configs", "Exchange"));
                File.WriteAllText(
                    Path.Combine(root, "Core", "Configs", "config_catalog.json"),
                    """
                    [
                      { "Path": "Exchange/operations.json", "Policy": "ArrayById", "IdField": "id" }
                    ]
                    """);
                File.WriteAllText(
                    Path.Combine(root, "Core", "Configs", "Exchange", "operations.json"),
                    """
                    [
                      {
                        "id": "test.attribute.config",
                        "inputs": [
                          { "kind": "AttributeCost", "actor": "Source", "attribute": "Gold", "quantity": 25 }
                        ],
                        "outputs": [
                          { "kind": "CreateItem", "actor": "Source", "purpose": "Stash", "item": "gem", "quantity": 1 }
                        ]
                      }
                    ]
                    """);

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", Path.Combine(root, "Core"));
                var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);

                var items = new ItemDefinitionRegistry();
                int gemId = items.Register("gem", new ItemDefinition { Id = "gem", DisplayName = "Gem", ShapeId = 1 });
                var operations = new ExchangeOperationRegistry();
                var loader = new ExchangeConfigLoader(
                    pipeline,
                    operations,
                    items,
                    new RelationshipTypeRegistry(),
                    new RelationshipMetricRegistry(),
                    new RelationshipFlagRegistry());

                loader.Load(catalog);

                int operationId = operations.GetId("test.attribute.config");
                That(operations.TryGet(operationId, out ExchangeOperationDefinition operation), Is.True);
                That(operation.Inputs.Length, Is.EqualTo(1));
                int goldId = AttributeRegistry.GetId("Gold");
                That(goldId, Is.GreaterThanOrEqualTo(0));
                That(operation.Inputs[0].Kind, Is.EqualTo(ExchangeInputKind.AttributeCost));
                That(operation.Inputs[0].Actor, Is.EqualTo(RoleSlot.Source));
                That(operation.Inputs[0].AttributeId, Is.EqualTo(goldId));
                That(operation.Inputs[0].Quantity, Is.EqualTo(25));
                That(operation.Outputs[0].ItemDefinitionId, Is.EqualTo(gemId));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                }
            }
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

            var relationshipTypes = new RelationshipTypeRegistry();
            var relationshipMetrics = new RelationshipMetricRegistry();
            var relationshipFlags = new RelationshipFlagRegistry();
            var relationshipBands = new RelationshipBandRegistry();
            var relationshipReasons = new RelationshipReasonRegistry();
            var relationships = new RelationshipRuntime(
                world,
                relationshipTypes,
                relationshipMetrics,
                relationshipFlags,
                relationshipBands,
                new RelationshipChangeBuffer(capacity: 8),
                new RelationshipReverseIndex(world));
            RelationshipCatalogInstaller.RegisterCatalog(
                new RelationshipCatalogConfig
                {
                    Types =
                    {
                        new RelationshipTypeConfig { Id = "Owns" },
                        new RelationshipTypeConfig { Id = "Diplomacy" }
                    },
                    Metrics =
                    {
                        new RelationshipMetricConfig { Id = "Trust", MinValue = -100, MaxValue = 100, DefaultValue = 0 }
                    },
                    Flags =
                    {
                        new RelationshipFlagConfig { Id = "Embargo" }
                    }
                },
                relationshipTypes,
                relationshipMetrics,
                relationshipFlags,
                relationshipBands,
                relationshipReasons);
            int ownsTypeId = relationshipTypes.GetId("Owns");
            int diplomacyTypeId = relationshipTypes.GetId("Diplomacy");
            int trustMetricId = relationshipMetrics.GetId("Trust");
            int embargoFlagId = relationshipFlags.GetId("Embargo");
            var ownership = new OwnershipResolver(relationships, ownsTypeId);
            var inventory = new InventoryRuntimeService(world, shapes, layouts, definitions, ownership);
            var operations = new ExchangeOperationRegistry();
            var scoped = new ExchangeScopedOperationStore();
            var effects = new EffectRequestQueue();
            var runtime = new ExchangeRuntime(world, operations, scoped, inventory, effects, relationships);
            return new ExchangeFixture(inventory, operations, scoped, runtime, relationships, stashLayout, targetLayout, diplomacyTypeId, trustMetricId, embargoFlagId);
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
            RelationshipRuntime Relationships,
            int StashLayout,
            int TargetLayout,
            int DiplomacyTypeId,
            int TrustMetricId,
            int EmbargoFlagId);
    }
}
