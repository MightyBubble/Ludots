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
            "Ludots.Raylib.Render",
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
        Assert.That(fragment, Does.Contain("uniform vec3 uSunDirection"));
        Assert.That(fragment, Does.Contain("uniform vec3 uSunColor"));
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
            "Ludots.Raylib.Render",
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

    [Test]
    public void LitModel_UsesRaylibModelMatrixShaderContract()
    {
        string repoRoot = FindRepoRoot();
        string vertex = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "model_lit.vs"));
        string renderer = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Raylib.Render",
            "Rendering",
            "RaylibLitModel.cs"));

        Assert.That(vertex, Does.Contain("uniform mat4 matModel"));
        Assert.That(vertex, Does.Not.Contain("uniform mat4 model"));
        Assert.That(renderer, Does.Contain("SHADER_LOC_MATRIX_MODEL"));
        Assert.That(renderer, Does.Contain("\"matModel\""));
    }

    [Test]
    public void ShadowDepth_UsesOpaqueRgbPackingAcrossAllReceivers()
    {
        string repoRoot = FindRepoRoot();
        string shaderRoot = Path.Combine(repoRoot, "src", "Platforms", "Desktop");
        string depth = File.ReadAllText(Path.Combine(shaderRoot, "shadow_depth.fs"));

        Assert.That(depth, Does.Contain("finalColor = vec4(enc, 1.0)"));
        Assert.That(depth, Does.Not.Contain("enc.a"));

        foreach (string receiver in new[] { "model_lit.fs", "instancing.fs", "skinning_instanced.fs", "terrain.fs" })
        {
            string fragment = File.ReadAllText(Path.Combine(shaderRoot, receiver));
            Assert.That(
                fragment,
                Does.Contain("dot(packed.rgb, vec3(1.0, 1.0 / 255.0, 1.0 / 65025.0))"),
                receiver);
            Assert.That(fragment, Does.Contain("vec2 shadowUv = proj.xy"), receiver);
            Assert.That(fragment, Does.Not.Contain("1.0 - proj.y"), receiver);
        }
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
