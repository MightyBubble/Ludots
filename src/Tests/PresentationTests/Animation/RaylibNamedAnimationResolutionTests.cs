using System;
using System.IO;
using System.Text;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.Presentation.Animation;

[TestFixture]
public sealed unsafe class RaylibNamedAnimationResolutionTests
{
    [Test]
    public void KayKitSoldierResolvesIdleAndWalkingByName()
    {
        string modelPath = Path.Combine(FindRepoRoot(), "mods", "capabilities", "navigation", "MassNavigationMod", "assets", "Models", "mass_navigation_agent_soldier.glb");
        Assert.That(File.Exists(modelPath), Is.True, modelPath);

        ModelAnimation* animations = Rl.LoadModelAnimations(modelPath, out int animCount);
        Assert.That((nuint)animations, Is.Not.EqualTo(0u));
        Assert.That(animCount, Is.GreaterThan(2));
        try
        {
            string[] names = new string[animCount];
            for (int i = 0; i < animCount; i++)
            {
                names[i] = ReadName(animations[i]);
            }

            int idle = Array.IndexOf(names, "Idle");
            int walking = Array.IndexOf(names, "Walking_A");
            Assert.That(idle, Is.GreaterThanOrEqualTo(0));
            Assert.That(walking, Is.GreaterThanOrEqualTo(0));
            Assert.That(walking, Is.Not.EqualTo(idle));

            var resolver = new FixedSelectorResolver(
                new ClipAssetLocatorSelector[]
                {
                    ClipAssetLocatorSelector.Parse("MassNavigationMod:assets/Models/mass_navigation_agent_soldier.glb#anim:Idle"),
                    ClipAssetLocatorSelector.Parse("MassNavigationMod:assets/Models/mass_navigation_agent_soldier.glb#anim:Walking_A")
                });
            AnimatorPackedState idleState = AnimatorPackedState.Create(1);
            idleState.SetPrimaryStateIndex(41);
            AnimatorPackedState walkState = AnimatorPackedState.Create(1);
            walkState.SetPrimaryStateIndex(42);

            RaylibSkinnedPlayback.ResolveFromAnimator(in idleState, animations, animCount, 1, resolver, names, out int idleResolved, out _, out _);
            RaylibSkinnedPlayback.ResolveFromAnimator(in walkState, animations, animCount, 1, resolver, names, out int walkResolved, out _, out _);

            Assert.That(idleResolved, Is.EqualTo(idle));
            Assert.That(walkResolved, Is.EqualTo(walking));
        }
        finally
        {
            Rl.UnloadModelAnimations(animations, animCount);
        }
    }

    private static string ReadName(in ModelAnimation animation)
    {
        fixed (byte* name = animation.name)
        {
            int length = 0;
            while (length < 32 && name[length] != 0)
            {
                length++;
            }

            return length == 0 ? string.Empty : Encoding.UTF8.GetString(name, length);
        }
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) && File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
    }

    private sealed class FixedSelectorResolver : IRenderAnimationClipResolver
    {
        private readonly ClipAssetLocatorSelector[] _selectors;

        public FixedSelectorResolver(ClipAssetLocatorSelector[] selectors)
        {
            _selectors = selectors;
        }

        public bool TryResolve(int animationProfileId, int packedStateIndex, out ClipAssetLocatorSelector selector)
        {
            int index = packedStateIndex switch
            {
                41 => 0,
                42 => 1,
                _ => -1,
            };
            if (index >= 0)
            {
                selector = _selectors[index];
                return true;
            }

            selector = default;
            return false;
        }
    }
}
