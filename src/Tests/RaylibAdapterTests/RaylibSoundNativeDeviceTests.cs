using System;
using System.IO;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.RaylibAdapter;

/// <summary>
/// 真实音频设备路径的冒烟验收：初始化失败或设备不可用（无声卡的 CI）时显式
/// Assert.Ignore 报告 skip，不允许静默通过或伪装成通过。
/// </summary>
[TestFixture]
public sealed class RaylibSoundNativeDeviceTests
{
    [Test]
    public void NativeAudioDevice_LoadPlayStopUnloadToneWave_RoundTrips()
    {
        string wavPath = FindShowcaseToneWav();
        Assert.That(File.Exists(wavPath), Is.True, $"expected generated tone asset at {wavPath}");

        try
        {
            Rl.InitAudioDevice();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"SKIP (explicit): raylib InitAudioDevice threw on this host ({ex.GetType().Name}: {ex.Message}); native audio round-trip cannot run.");
            return;
        }

        if (!Rl.IsAudioDeviceReady())
        {
            Assert.Ignore("SKIP (explicit): raylib audio device is not ready on this host (no audio device); native audio round-trip cannot run.");
        }

        try
        {
            Sound sound = Rl.LoadSound(wavPath);
            Assert.That(Rl.IsSoundValid(sound), Is.True, "LoadSound must return a valid sound for the generated tone WAV");

            Rl.SetSoundVolume(sound, 0.5f);
            Rl.PlaySound(sound);
            Assert.That(Rl.IsSoundPlaying(sound), Is.True, "played sound must report playing");

            Rl.StopSound(sound);
            Assert.That(Rl.IsSoundPlaying(sound), Is.False, "stopped sound must report not playing");

            Rl.UnloadSound(sound);
        }
        finally
        {
            Rl.CloseAudioDevice();
        }
    }

    [Test]
    public void NativeAudioDevice_SoundAndAliasLifecycle_BalancesNativeResourceLedger()
    {
        string wavPath = FindShowcaseToneWav();
        Assert.That(File.Exists(wavPath), Is.True, $"expected generated tone asset at {wavPath}");

        try
        {
            Rl.InitAudioDevice();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"SKIP (explicit): raylib InitAudioDevice threw on this host ({ex.GetType().Name}: {ex.Message}); native audio round-trip cannot run.");
            return;
        }

        if (!Rl.IsAudioDeviceReady())
        {
            Assert.Ignore("SKIP (explicit): raylib audio device is not ready on this host (no audio device); native audio round-trip cannot run.");
        }

        try
        {
            RaylibNativeResourceLedger.Reset();
            Sound sound = RaylibNativeResources.LoadSound(wavPath);
            Assert.That(Rl.IsSoundValid(sound), Is.True, "LoadSound must return a valid sound for the generated tone WAV");
            Sound firstAlias = RaylibNativeResources.LoadSoundAlias(sound);
            Sound secondAlias = RaylibNativeResources.LoadSoundAlias(sound);

            RaylibNativeResourceSnapshot loaded = RaylibNativeResourceLedger.Snapshot();
            Assert.That(loaded.OutstandingByKind[(int)RaylibNativeResourceKind.Sound], Is.EqualTo(1),
                "sound base must be tracked exactly once");
            Assert.That(loaded.OutstandingByKind[(int)RaylibNativeResourceKind.SoundAlias], Is.EqualTo(2),
                "two aliases of one sound must hold distinct ledger identities (raylib allocates a distinct alias buffer per LoadSoundAlias)");
            Assert.That(loaded.RetrackedCount, Is.EqualTo(0));
            Assert.That(loaded.UnknownUntrackCount, Is.EqualTo(0));

            RaylibNativeResources.UnloadSoundAlias(firstAlias);
            RaylibNativeResources.UnloadSoundAlias(secondAlias);
            RaylibNativeResources.UnloadSound(sound);

            RaylibNativeResourceSnapshot unloaded = RaylibNativeResourceLedger.Snapshot();
            Assert.That(unloaded.OutstandingCount, Is.EqualTo(0));
            Assert.That(unloaded.ResidentBytes, Is.EqualTo(0));
            Assert.That(unloaded.RetrackedCount, Is.EqualTo(0));
            Assert.That(unloaded.UnknownUntrackCount, Is.EqualTo(0));
        }
        finally
        {
            RaylibNativeResourceLedger.Reset();
            Rl.CloseAudioDevice();
        }
    }

    private static string FindShowcaseToneWav()
    {
        string repoRoot = FindRepoRoot();
        return Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "capability_standard",
            "CapabilityStandardSoundShowcaseMod",
            "assets",
            "Sounds",
            "tone_440hz.wav");
    }

    private static string FindRepoRoot()
    {
        string directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "showcase.registry.json")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
    }
}
