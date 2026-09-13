using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Components;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Effect
{
    /// <summary>
    /// ISSUE-1521 临时刻度测量（交付前删除）：World.Add 结构搬迁成本按组件集大小分档。
    /// </summary>
    [TestFixture]
    public sealed class Issue1521ScratchTests
    {
        [Test]
        public void WorldAddCost_ThinVsFatEntity()
        {
            var world = World.Create();
            const int count = 2000;
            var thin = new Entity[count];
            var fat = new Entity[count];
            for (int i = 0; i < count; i++)
            {
                thin[i] = world.Create(new AttributeBuffer(), new DirtyFlags());
                fat[i] = world.Create(
                    new AttributeBuffer(),
                    new DirtyFlags(),
                    new ActiveEffectContainer(),
                    new GameplayTagContainer(),
                    new TagCountContainer(),
                    new WorldPositionCm(),
                    new PreviousWorldPositionCm(),
                    new FacingDirection(),
                    new EffectModifiers());
            }

            for (int round = 0; round < 5; round++)
            {
                for (int i = 0; i < count; i++)
                {
                    if (world.Has<GameplayAttributeChangedBits>(thin[i])) world.Remove<GameplayAttributeChangedBits>(thin[i]);
                    if (world.Has<GameplayAttributeChangedBits>(fat[i])) world.Remove<GameplayAttributeChangedBits>(fat[i]);
                }

                var swThin = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    world.Add(thin[i], new GameplayAttributeChangedBits());
                }

                swThin.Stop();

                var swFat = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    world.Add(fat[i], new GameplayAttributeChangedBits());
                }

                swFat.Stop();
                TestContext.Out.WriteLine(
                    $"round={round} thinPerAddUs={swThin.Elapsed.TotalMilliseconds * 1000 / count:F2} fatPerAddUs={swFat.Elapsed.TotalMilliseconds * 1000 / count:F2}");
            }

            World.Destroy(world);
        }
    }
}
