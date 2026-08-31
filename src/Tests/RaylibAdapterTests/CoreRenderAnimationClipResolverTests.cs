using Ludots.Adapter.Raylib;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class CoreRenderAnimationClipResolverTests
{
    [Test]
    public void ResolvesProfileStateToConfiguredNamedRaylibClip()
    {
        var profiles = new AnimationProfileRegistry();
        var clips = new AnimationClipRegistry();
        var controllers = new AnimatorControllerRegistry();
        int controllerId = controllers.Register("nav.controller");
        int idleClipId = clips.Register("nav.clip.idle", new AnimationClipDefinition
        {
            AssetKind = AnimationClipAssetKind.Clip,
            Locators = new[]
            {
                new AnimationClipLocatorDefinition(
                    "raylib",
                    "NavShowcase:assets/Models/Knight.glb#anim:Idle")
            }
        });
        profiles.Register("nav.profile", new AnimationProfileDefinition
        {
            AnimatorControllerId = controllerId,
            StateClips = new[]
            {
                new AnimationStateClipBinding { PackedStateIndex = 41, ClipAssetId = idleClipId }
            }
        });

        var resolver = new CoreRenderAnimationClipResolver(profiles, clips);

        Assert.That(resolver.TryResolve(profiles.GetId("nav.profile"), 41, out ClipAssetLocatorSelector selector), Is.True);
        Assert.That(selector.AssetPath, Is.EqualTo("NavShowcase:assets/Models/Knight.glb"));
        Assert.That(selector.Kind, Is.EqualTo(ClipAssetLocatorSelector.AnimKind));
        Assert.That(selector.Selector, Is.EqualTo("Idle"));
    }

    [Test]
    public void MissingProfileStateFailsLoudly()
    {
        var profiles = new AnimationProfileRegistry();
        var clips = new AnimationClipRegistry();
        var controllers = new AnimatorControllerRegistry();
        int controllerId = controllers.Register("nav.controller");
        int clipId = clips.Register("nav.clip", new AnimationClipDefinition
        {
            AssetKind = AnimationClipAssetKind.Clip,
            Locators = new[]
            {
                new AnimationClipLocatorDefinition("raylib", "NavShowcase:assets/Models/Knight.glb#anim:Idle")
            }
        });
        int profileId = profiles.Register("nav.profile", new AnimationProfileDefinition
        {
            AnimatorControllerId = controllerId,
            StateClips = new[]
            {
                new AnimationStateClipBinding { PackedStateIndex = 41, ClipAssetId = clipId }
            }
        });

        var resolver = new CoreRenderAnimationClipResolver(profiles, clips);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => resolver.TryResolve(profileId, 42, out _))!;
        Assert.That(ex.Message, Does.Contain("packed state 42"));
    }

    [Test]
    public void MissingRaylibLocatorFailsLoudly()
    {
        var profiles = new AnimationProfileRegistry();
        var clips = new AnimationClipRegistry();
        var controllers = new AnimatorControllerRegistry();
        int controllerId = controllers.Register("nav.controller");
        int clipId = clips.Register("nav.clip", new AnimationClipDefinition
        {
            AssetKind = AnimationClipAssetKind.Clip,
            Locators = new[]
            {
                new AnimationClipLocatorDefinition("ue5", "/Game/Nav/Idle")
            }
        });
        int profileId = profiles.Register("nav.profile", new AnimationProfileDefinition
        {
            AnimatorControllerId = controllerId,
            StateClips = new[]
            {
                new AnimationStateClipBinding { PackedStateIndex = 41, ClipAssetId = clipId }
            }
        });

        var resolver = new CoreRenderAnimationClipResolver(profiles, clips);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => resolver.TryResolve(profileId, 41, out _))!;
        Assert.That(ex.Message, Does.Contain("raylib"));
    }
}
