using Ludots.Launcher.Backend;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class LauncherConfigServiceTests
{
    [Test]
    public void LoadRepoConfig_WhenRequiredFileIsMissing_ThrowsWithPath()
    {
        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);

            var exception = Assert.Throws<FileNotFoundException>(() => service.LoadRepoConfig());

            Assert.That(exception!.Message, Does.Contain(service.RepoConfigPath));
            Assert.That(exception.FileName, Is.EqualTo(service.RepoConfigPath));
        });
    }

    [Test]
    public void LoadPresets_WhenRequiredFileIsMissing_ThrowsWithPath()
    {
        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);

            var exception = Assert.Throws<FileNotFoundException>(() => service.LoadPresets());

            Assert.That(exception!.Message, Does.Contain(service.RepoPresetsPath));
            Assert.That(exception.FileName, Is.EqualTo(service.RepoPresetsPath));
        });
    }

    [Test]
    public void LoadMergedConfig_WhenExplicitUserOverlayIsMissing_UsesEmptyOverlay()
    {
        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);
            File.WriteAllText(service.RepoConfigPath, "{}");

            LauncherConfig config = service.LoadMergedConfig();

            Assert.That(service.UserConfigPath, Is.EqualTo(Path.Combine(root, "config.overlay.json")));
            Assert.That(config.ScanRoots, Has.Some.Property("Id").EqualTo("repo_mods"));
        });
    }

    [Test]
    public void LoadMergedConfig_WhenExistingUserOverlayHasInvalidJson_ThrowsWithPath()
    {
        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);
            File.WriteAllText(service.RepoConfigPath, "{}");
            File.WriteAllText(service.UserConfigPath, "{ invalid json");

            var exception = Assert.Throws<InvalidDataException>(() => service.LoadMergedConfig());

            Assert.That(exception!.Message, Does.Contain(service.UserConfigPath));
            Assert.That(exception.InnerException, Is.TypeOf<System.Text.Json.JsonException>());
        });
    }

    [Test]
    public void LoadRepoConfig_WhenDocumentDeserializesToNull_ThrowsWithPath()
    {
        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);
            File.WriteAllText(service.RepoConfigPath, "null");

            var exception = Assert.Throws<InvalidDataException>(() => service.LoadRepoConfig());

            Assert.That(exception!.Message, Does.Contain(service.RepoConfigPath));
            Assert.That(exception.InnerException, Is.TypeOf<System.Text.Json.JsonException>());
        });
    }

    [Test]
    public void LoadPreferences_WhenOptionalFileIsMissing_ReturnsEmptyPreferences()
    {
        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);

            LauncherPreferences preferences = service.LoadPreferences();

            Assert.That(preferences, Is.Not.Null);
        });
    }

    [Test]
    public void LoadPreferences_WhenExistingFileCannotBeRead_ThrowsWithPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The exclusive file-share read failure contract is Windows-specific.");
        }

        WithTemporaryDirectory(root =>
        {
            var service = CreateService(root);
            File.WriteAllText(service.PreferencesPath, "{}");
            using var lockedFile = new FileStream(
                service.PreferencesPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var exception = Assert.Throws<InvalidDataException>(() => service.LoadPreferences());

            Assert.That(exception!.Message, Does.Contain(service.PreferencesPath));
            Assert.That(exception.InnerException, Is.InstanceOf<IOException>());
        });
    }

    private static LauncherConfigService CreateService(string root)
    {
        return new LauncherConfigService(
            root,
            configPath: Path.Combine(root, "launcher.config.json"),
            presetsPath: Path.Combine(root, "launcher.presets.json"),
            preferencesPath: Path.Combine(root, "preferences.json"),
            userConfigPath: Path.Combine(root, "config.overlay.json"));
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "Ludots_LauncherConfig", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
