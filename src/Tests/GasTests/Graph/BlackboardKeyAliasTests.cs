using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class BlackboardKeyAliasTests
    {
        [Test]
        public void CustomBlackboardKey_DoesNotAliasBuiltinCastFacing()
        {
            ConfigKeyRegistry.Clear();
            OrderBlackboardKeyRegistry.ResetToBuiltins();
            try
            {
                for (int i = 1; i < OrderBlackboardKeys.Cast_Facing; i++)
                {
                    ConfigKeyRegistry.Register($"tests.config.pad.{i}");
                }

                int custom = OrderBlackboardKeyRegistry.Resolve("tests.bb.custom");
                var buffer = new BlackboardFloatBuffer();
                buffer.Set(OrderBlackboardKeys.Cast_Facing, 90f);
                buffer.Set(custom, 15f);
                buffer.TryGet(OrderBlackboardKeys.Cast_Facing, out float facing);

                Assert.Multiple(() =>
                {
                    Assert.That(custom, Is.Not.EqualTo(OrderBlackboardKeys.Cast_Facing),
                        $"custom id {custom} aliases Cast.Facing {OrderBlackboardKeys.Cast_Facing}; buffer facing became {facing}.");
                    Assert.That(custom, Is.GreaterThanOrEqualTo(10_000));
                    Assert.That(facing, Is.EqualTo(90f));
                    Assert.That(OrderBlackboardKeyRegistry.Resolve("Cast.Facing"), Is.EqualTo(OrderBlackboardKeys.Cast_Facing));
                });
            }
            finally
            {
                ConfigKeyRegistry.Clear();
                OrderBlackboardKeyRegistry.ResetToBuiltins();
            }
        }
    }
}
