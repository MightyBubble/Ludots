using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Navigation.GraphEcs;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Runtime
{
    [TestFixture]
    public sealed class GasRuntimeStateContractTests
    {
        [Test]
        public void HotPathComponentsRemainBlittableAndWithinConfiguredFootprints()
        {
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<OrderBuffer>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<OrderContinuationBuffer>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<OrderSpatialPayloadBuffer>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<AbilityExecInstance>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<DirtyFlags>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<GraphPathBuffer>(), Is.False);

            // OrderBuffer stores MAX_QUEUED_ORDERS + active + pending QueuedOrder slots, each padded
            // to a 256 B boundary, so the footprint steps in 256 B increments whenever Order gains a
            // field. Budget trails that alignment (8 slots x 256 B + 8 B header) rather than an
            // arbitrary byte count; real runaway growth still trips it.
            Assert.That(Marshal.SizeOf<OrderBuffer>(), Is.LessThanOrEqualTo(2_064));
            Assert.That(Marshal.SizeOf<AbilityExecInstance>(), Is.LessThanOrEqualTo(128));
            Assert.That(Marshal.SizeOf<DirtyFlags>(), Is.LessThanOrEqualTo(48));
            Assert.That(GraphPathBuffer.Capacity, Is.EqualTo(128));
        }

        [Test]
        public void TagOpsRequiresExplicitOwnedServices()
        {
            ConstructorInfo[] constructors = typeof(TagOps).GetConstructors();
            Assert.That(constructors, Has.Length.EqualTo(1));
            ConstructorInfo constructor = constructors[0];
            ParameterInfo[] parameters = constructor.GetParameters();

            Assert.That(parameters.Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(DirtyEntityQueue),
                typeof(TagRuleRegistry),
                typeof(GasBudget)
            }));
            Assert.That(parameters[0].IsOptional, Is.False);
            Assert.That(parameters[1].IsOptional, Is.False);
        }
    }
}
