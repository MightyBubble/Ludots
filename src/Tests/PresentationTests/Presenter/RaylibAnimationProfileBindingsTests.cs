using Ludots.Client.Raylib.Rendering;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;
using System.Runtime.CompilerServices;

namespace Ludots.Tests.Presentation;

[TestFixture]
public unsafe class RaylibAnimationProfileBindingsTests
{
    private readonly string _path = Path.GetFullPath("mannequin.glb");

    [TestCase(41, 0)]
    [TestCase(42, 3)]
    public void SemanticStates_SelectConfiguredClips_FromFourClipModel(int state, int expected)
    {
        var binding = Create();
        var animator = AnimatorPackedState.Create(1);
        animator.SetPrimaryStateIndex(state);
        animator.SetNormalizedTime01(0.5f);
        ModelAnimation* animations = stackalloc ModelAnimation[4];
        for (int i = 0; i < 4; i++) animations[i].frameCount = 62;
        var map = RaylibSkinnedPlayback.ResolveStateMap(1, _path, binding.Resolve);
        RaylibSkinnedPlayback.ResolveFromAnimator(in animator, animations, 4, map, out int clip, out int frame);
        Assert.That(clip, Is.EqualTo(expected));
        Assert.That(frame, Is.InRange(29, 31));
        Assert.Throws<InvalidOperationException>(() => RaylibSkinnedPlayback.MapStateToClipIndex(43, map));
    }

    [Test]
    public void ProfileCannotSilentlyUseNativeIndices_WhenResolverIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() => RaylibSkinnedPlayback.ResolveStateMap(1, _path, null));
        Assert.That(RaylibSkinnedPlayback.ResolveStateMap(0, _path, null), Is.Null);
    }

    [Test]
    public void ProfileCannotBindToAnotherModel()
    {
        var binding = Create();
        Assert.Throws<InvalidOperationException>(() => binding.Resolve(1, Path.GetFullPath("other.glb")));
        Assert.Throws<InvalidOperationException>(() => binding.Resolve(2, _path));
    }

    [TestCase("model.glb#anim:bad")]
    [TestCase("model.glb#anim:-1")]
    [TestCase("missing.glb#anim:0")]
    public void UnsupportedOrUnresolvedLocator_FailsWhenProfileIsUsed(string locator)
    {
        var binding = Create(locator);
        Assert.Throws<InvalidOperationException>(() => binding.Resolve(1, _path));
    }

    [Test]
    public void TenThousandRepeatedResolutions_AllocateZeroBytes()
    {
        var binding = Create();
        Func<int, string, IReadOnlyDictionary<int, int>?> resolver = binding.Resolve;
        for (int i = 0; i < 3; i++) MeasureResolutions(resolver, out _);
        long allocated = MeasureResolutions(resolver, out int sum);
        Assert.That(sum, Is.EqualTo(30000));
        Assert.That(allocated, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private long MeasureResolutions(Func<int, string, IReadOnlyDictionary<int, int>?> resolver, out int sum)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        sum = 0;
        for (int i = 0; i < 10000; i++)
            sum += RaylibSkinnedPlayback.MapStateToClipIndex(42, RaylibSkinnedPlayback.ResolveStateMap(1, _path, resolver));
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private RaylibAnimationProfileBindings Create(string locator = "model.glb#anim:0")
    {
        var profiles = new AnimationProfileRegistry();
        var clips = new AnimationClipRegistry();
        int idle = clips.Register("idle", new AnimationClipDefinition { Locators = [new("raylib", locator)] });
        int walk = clips.Register("walk", new AnimationClipDefinition { Locators = [new("raylib", "model.glb#anim:3")] });
        profiles.Register("locomotion", new AnimationProfileDefinition
        {
            StateClips = [new() { PackedStateIndex = 41, ClipAssetId = idle }, new() { PackedStateIndex = 42, ClipAssetId = walk }]
        });
        return new(profiles, clips, new Paths(_path));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SameNameConfigurationReplacement_InvalidatesCompiledBindings(bool replaceProfile)
    {
        var profiles = new AnimationProfileRegistry();
        var clips = new AnimationClipRegistry();
        int clip = clips.Register("walk", new AnimationClipDefinition { Locators = [new("raylib", "model.glb#anim:3")] });
        var profile = new AnimationProfileDefinition { StateClips = [new() { PackedStateIndex = 42, ClipAssetId = clip }] };
        int id = profiles.Register("locomotion", profile);
        var bindings = new RaylibAnimationProfileBindings(profiles, clips, new Paths(_path));
        Assert.That(bindings.Resolve(id, _path)![42], Is.EqualTo(3));
        if (replaceProfile)
            profiles.Register("locomotion", new AnimationProfileDefinition());
        else
            clips.Register("walk", new AnimationClipDefinition { Locators = [new("raylib", "model.glb#anim:1")] });
        Assert.Throws<InvalidOperationException>(() => bindings.Resolve(id, _path));
    }

    private sealed class Paths(string path) : IRenderAssetPathResolver
    {
        public bool TryResolveFullPath(string uri, out string fullPath)
        {
            fullPath = path;
            return uri == "model.glb";
        }
    }
}
