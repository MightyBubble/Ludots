using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation.Animation
{
    [TestFixture]
    public sealed class ClipAssetLocatorSelectorTests
    {
        [Test]
        public void Parse_NamedAnimSelector_ResolvesName()
        {
            ClipAssetLocatorSelector selector = ClipAssetLocatorSelector.Parse("MassNavigationMod:assets/Models/hero.glb#anim:Idle");

            Assert.That(selector.HasFragment, Is.True);
            Assert.That(selector.Kind, Is.EqualTo(ClipAssetLocatorSelector.AnimKind));
            Assert.That(selector.IsName, Is.True);
            Assert.That(selector.IsIndex, Is.False);
            Assert.That(selector.Selector, Is.EqualTo("Idle"));
            Assert.That(selector.Index, Is.EqualTo(-1));
            Assert.That(selector.AssetPath, Is.EqualTo("MassNavigationMod:assets/Models/hero.glb"));
        }

        [Test]
        public void Parse_NumericAnimSelector_ResolvesZeroBasedIndex()
        {
            ClipAssetLocatorSelector selector = ClipAssetLocatorSelector.Parse("RaylibClientParityShowcaseMod:assets/Models/mannequin_large_walk.glb#anim:3");

            Assert.That(selector.HasFragment, Is.True);
            Assert.That(selector.Kind, Is.EqualTo(ClipAssetLocatorSelector.AnimKind));
            Assert.That(selector.IsIndex, Is.True);
            Assert.That(selector.IsName, Is.False);
            Assert.That(selector.Index, Is.EqualTo(3));
            Assert.That(selector.Selector, Is.EqualTo("3"));
        }

        [Test]
        public void Parse_GraphAndPoseKinds_ResolveNames()
        {
            ClipAssetLocatorSelector graph = ClipAssetLocatorSelector.Parse("animations/acceptance/tank_locomotion_cycle.glb#graph:humanoid_locomotion_cycle");
            Assert.That(graph.Kind, Is.EqualTo(ClipAssetLocatorSelector.GraphKind));
            Assert.That(graph.IsName, Is.True);
            Assert.That(graph.Selector, Is.EqualTo("humanoid_locomotion_cycle"));

            ClipAssetLocatorSelector pose = ClipAssetLocatorSelector.Parse("animations/acceptance/humanoid_aim_offsets.json#pose:humanoid_aim_yaw");
            Assert.That(pose.Kind, Is.EqualTo(ClipAssetLocatorSelector.PoseKind));
            Assert.That(pose.IsName, Is.True);
            Assert.That(pose.Selector, Is.EqualTo("humanoid_aim_yaw"));
        }

        [Test]
        public void Parse_BackendRefWithoutFragment_IsPathOnly()
        {
            ClipAssetLocatorSelector selector = ClipAssetLocatorSelector.Parse("/Game/Ludots/Acceptance/Humanoid/AN_Humanoid_Idle");

            Assert.That(selector.HasFragment, Is.False);
            Assert.That(selector.Kind, Is.EqualTo(string.Empty));
            Assert.That(selector.IsName, Is.False);
            Assert.That(selector.IsIndex, Is.False);
            Assert.That(selector.Index, Is.EqualTo(-1));
            Assert.That(selector.AssetPath, Is.EqualTo("/Game/Ludots/Acceptance/Humanoid/AN_Humanoid_Idle"));
        }

        [Test]
        public void Parse_ToString_RoundTripsFragmentForm()
        {
            const string assetRef = "MassNavigationMod:assets/Models/hero.glb#anim:Walking_A";
            ClipAssetLocatorSelector selector = ClipAssetLocatorSelector.Parse(assetRef);
            Assert.That(selector.ToString(), Is.EqualTo(assetRef));
            Assert.That(ClipAssetLocatorSelector.Parse(selector.ToString()).Equals(selector), Is.True);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("asset.glb#")]
        [TestCase("#anim:Idle")]
        [TestCase("asset.glb#anim")]
        [TestCase("asset.glb#anim:")]
        [TestCase("asset.glb#anim:  ")]
        [TestCase("asset.glb#foo:Idle")]
        [TestCase("asset.glb#anim:Idle#extra")]
        [TestCase("asset.glb#anim:-1")]
        [TestCase("asset.glb#anim:2147483648")]
        public void Parse_MalformedLocator_Throws(string assetRef)
        {
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => ClipAssetLocatorSelector.Parse(assetRef))!;
            Assert.That(ex.Message, Does.Contain(assetRef));
        }

        [Test]
        public void Parse_Null_ThrowsArgumentNull()
        {
            Assert.Throws<ArgumentNullException>(() => ClipAssetLocatorSelector.Parse(null!));
        }

        [Test]
        public void AnimationClipConfigLoader_MalformedLocatorFragment_FailsAtLoad()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ClipLocatorMalformed", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animation_clips.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animation_clips.json"), """
[
  {
    "id": "malformed.clip",
    "assetKind": "Clip",
    "locators": [
      { "backendId": "raylib", "assetRef": "Mod:assets/Models/hero.glb#anim:" }
    ]
  }
]
""");

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var clips = new AnimationClipRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new AnimationClipConfigLoader(pipeline, clips).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("malformed"));
        }

        [Test]
        public void AnimationClipConfigLoader_UnknownFragmentKind_FailsAtLoad()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ClipLocatorKind", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animation_clips.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animation_clips.json"), """
[
  {
    "id": "unknown.kind.clip",
    "assetKind": "Clip",
    "locators": [
      { "backendId": "raylib", "assetRef": "Mod:assets/Models/hero.glb#clip:Idle" }
    ]
  }
]
""");

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var clips = new AnimationClipRegistry();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => new AnimationClipConfigLoader(pipeline, clips).Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("malformed"));
        }

        [Test]
        public void AnimationClipConfigLoader_ValidLocatorForms_Load()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_ClipLocatorValid", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            Directory.CreateDirectory(core);
            Directory.CreateDirectory(Path.Combine(core, "Presentation"));
            File.WriteAllText(Path.Combine(core, "config_catalog.json"), """
[
  { "Path": "Presentation/animation_clips.json", "Policy": "ArrayById", "IdField": "id" }
]
""");
            File.WriteAllText(Path.Combine(core, "Presentation", "animation_clips.json"), """
[
  {
    "id": "valid.named.clip",
    "assetKind": "Clip",
    "locators": [
      { "backendId": "raylib", "assetRef": "Mod:assets/Models/hero.glb#anim:Idle" },
      { "backendId": "ue5", "assetRef": "/Game/Hero/AN_Hero_Idle" }
    ]
  },
  {
    "id": "valid.indexed.clip",
    "assetKind": "Clip",
    "locators": [
      { "backendId": "raylib", "assetRef": "Mod:assets/Models/hero.glb#anim:3" }
    ]
  },
  {
    "id": "valid.graph.clip",
    "assetKind": "BlendTree",
    "locators": [
      { "backendId": "raylib", "assetRef": "Mod:assets/Models/hero.glb#graph:locomotion" }
    ]
  },
  {
    "id": "valid.pose.clip",
    "assetKind": "PoseAsset",
    "locators": [
      { "backendId": "raylib", "assetRef": "Mod:assets/Models/aim_offsets.json#pose:aim_yaw" }
    ]
  }
]
""");

            var vfs = new Ludots.Core.Modding.VirtualFileSystem();
            vfs.Mount("Core", core);
            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            var clips = new AnimationClipRegistry();

            Assert.DoesNotThrow(() => new AnimationClipConfigLoader(pipeline, clips).Load(catalog));
            Assert.That(clips.GetId("valid.named.clip"), Is.GreaterThan(0));
            Assert.That(clips.GetId("valid.indexed.clip"), Is.GreaterThan(0));
            Assert.That(clips.GetId("valid.graph.clip"), Is.GreaterThan(0));
            Assert.That(clips.GetId("valid.pose.clip"), Is.GreaterThan(0));
        }
    }
}
