using Arch.Core;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class PerformerRetainedDirtyFastPathTests
{
    [Test]
    public void MarkKnownRetainedTransformDrivenEmitDirty_AppendsOnlyOnce()
    {
        using World world = World.Create();
        var runtime = new PerformerEntityRuntime(world);
        Entity performer = world.Create(
            new PerformerEmitCache(),
            new PerfRetainedPresentationRequest());
        ref PerformerEmitCache emitCache = ref world.Get<PerformerEmitCache>(performer);

        runtime.MarkKnownRetainedTransformDrivenEmitDirty(performer, ref emitCache);
        runtime.MarkKnownRetainedTransformDrivenEmitDirty(performer, ref emitCache);

        Assert.That(runtime.HasDirtyRetainedPresentationRequests, Is.True);
        Assert.That(emitCache.RetainedDirty, Is.EqualTo(1));
        Assert.That(runtime.RetainedPresentationDirtyEntities.Length, Is.EqualTo(1));
        Assert.That(runtime.RetainedPresentationDirtyEntities[0], Is.EqualTo(performer));
    }
}
