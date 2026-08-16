using NUnit.Framework;
using Ludots.Raylib.Render;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibShaderContractTests
{
    [Test]
    public void SkyEnvironment_UsesDedicatedDayNightShaderContract()
    {
        string repoRoot = FindRepoRoot();
        string skyEnvironment = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Client.Raylib",
            "Rendering",
            "RaylibSkyEnvironment.cs"));
        string vertex = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "sky_daynight.vs"));
        string fragment = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "sky_daynight.fs"));

        Assert.That(skyEnvironment, Does.Contain("\"sky_daynight.vs\""));
        Assert.That(skyEnvironment, Does.Contain("\"sky_daynight.fs\""));
        Assert.That(vertex, Does.Contain("in vec3 vertexPosition"));
        Assert.That(vertex, Does.Contain("uniform mat4 matView"));
        Assert.That(vertex, Does.Contain("uniform mat4 matProjection"));
        Assert.That(fragment, Does.Contain("uniform sampler2D texture0"));
        Assert.That(fragment, Does.Contain("uniform float uDayPhase"));
        Assert.That(fragment, Does.Not.Contain("uSunDirection"));
        Assert.That(fragment, Does.Not.Contain("uTime"));
    }

    [Test]
    public void ProceduralSkybox_ShaderContractRemainsSeparateFromDayNightSky()
    {
        string repoRoot = FindRepoRoot();
        string skyboxRenderer = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Client.Raylib",
            "Rendering",
            "RaylibSkyboxRenderer.cs"));
        string vertex = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "skybox.vs"));
        string fragment = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "skybox.fs"));

        Assert.That(skyboxRenderer, Does.Contain("\"skybox.vs\""));
        Assert.That(skyboxRenderer, Does.Contain("\"skybox.fs\""));
        Assert.That(vertex, Does.Contain("uniform mat4 mvp"));
        Assert.That(vertex, Does.Contain("uniform mat4 matModel"));
        Assert.That(vertex, Does.Not.Contain("matView"));
        Assert.That(vertex, Does.Not.Contain("matProjection"));
        Assert.That(fragment, Does.Contain("uniform vec3 uSunDirection"));
        Assert.That(fragment, Does.Contain("uniform float uTime"));
        Assert.That(fragment, Does.Not.Contain("uDayPhase"));
        Assert.That(fragment, Does.Not.Contain("texture0"));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
