using NUnit.Framework;
using Ludots.Platform.Abstractions;
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
        string shaderRoot = Path.Combine(repoRoot, "src", "Platforms", "Desktop");
        string vertex = File.ReadAllText(Path.Combine(shaderRoot, "sky_daynight.vs"));
        string fragment = File.ReadAllText(Path.Combine(shaderRoot, "sky_daynight.fs"));
        string fragmentExpanded = RaylibShaderLoader.ExpandIncludes(fragment, shaderRoot, "sky_daynight.fs", 0);

        Assert.That(skyEnvironment, Does.Contain("\"sky_daynight.vs\""));
        Assert.That(skyEnvironment, Does.Contain("\"sky_daynight.fs\""));
        Assert.That(vertex, Does.Contain("in vec3 vertexPosition"));
        Assert.That(vertex, Does.Contain("uniform mat4 matView"));
        Assert.That(vertex, Does.Contain("uniform mat4 matProjection"));
        Assert.That(fragment, Does.Contain("uniform sampler2D texture0"));
        Assert.That(fragment, Does.Contain("uniform float uDayPhase"));
        Assert.That(fragmentExpanded, Does.Contain("uniform vec3 uSunDirection"));
        Assert.That(fragmentExpanded, Does.Contain("uniform vec3 uSunColor"));
        Assert.That(fragment, Does.Not.Contain("uTime"));
    }

    [Test]
    public void TerrainLighting_UsesHemisphereSkyWithoutSquaringAlbedo()
    {
        string repoRoot = FindRepoRoot();
        string terrain = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "terrain.fs"));
        string litModel = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Raylib.Render",
            "Rendering",
            "RaylibVisualHeightmapRenderer.cs"));

        Assert.That(terrain, Does.Contain("uniform vec3 uSkyZenith"));
        Assert.That(terrain, Does.Contain("uniform vec3 uSkyGround"));
        Assert.That(terrain, Does.Contain("mix(uSkyGround, uSkyZenith, hemisphere)"));
        Assert.That(terrain, Does.Contain("skyIrradiance + (uAmbient.rgb * uAmbient.a)"));
        Assert.That(terrain, Does.Contain("albedo * (ambient + direct)"));
        Assert.That(terrain, Does.Not.Contain("skyIrradiance * albedo"));
        Assert.That(litModel, Does.Contain("SkyZenithColor"));
        Assert.That(litModel, Does.Contain("SkyGroundColor"));
        Assert.That(litModel, Does.Contain("uSkyZenith"));
        Assert.That(litModel, Does.Contain("uSkyGround"));
    }

    [Test]
    public void TerrainNavWalkability_UsesDedicatedSamplerAfterBaseAlbedo()
    {
        string repoRoot = FindRepoRoot();
        string terrain = File.ReadAllText(Path.Combine(repoRoot, "src", "Platforms", "Desktop", "terrain.fs"));
        string renderer = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Raylib.Render",
            "Rendering",
            "RaylibVisualHeightmapRenderer.cs"));

        Assert.That(terrain, Does.Contain("uniform sampler2D uNavWalkabilityMap"));
        Assert.That(terrain, Does.Contain("uniform int uUseNavWalkability"));
        Assert.That(terrain, Does.Contain("uniform vec4 uNavWalkabilityBounds"));
        Assert.That(terrain, Does.Contain("vec2 worldCm = fragPos.xz * 100.0"));
        Assert.That(terrain, Does.Contain("uv.y = 1.0 - uv.y"));
        Assert.That(terrain, Does.Contain("albedo = mix(albedo, navTint.rgb"));
        Assert.That(
            terrain.IndexOf("if (uUseNavWalkability != 0)", StringComparison.Ordinal),
            Is.GreaterThan(terrain.IndexOf("if (uUseTerrainAlbedo != 0)", StringComparison.Ordinal)));
        Assert.That(
            terrain.IndexOf("if (uUseNavWalkability != 0)", StringComparison.Ordinal),
            Is.LessThan(terrain.IndexOf("vec3 N = normalize(fragNormal)", StringComparison.Ordinal)));
        Assert.That(terrain, Does.Contain("uniform sampler2D uControlMap"));
        Assert.That(renderer, Does.Contain("\"uNavWalkabilityMap\""));
        Assert.That(renderer, Does.Contain("\"uUseNavWalkability\""));
        Assert.That(renderer, Does.Contain("\"uNavWalkabilityBounds\""));
        Assert.That(renderer, Does.Contain("NavWalkabilityMaterialSlot"));
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
        string shaderRoot = Path.Combine(repoRoot, "src", "Platforms", "Desktop");
        string vertex = File.ReadAllText(Path.Combine(shaderRoot, "skybox.vs"));
        string fragment = File.ReadAllText(Path.Combine(shaderRoot, "skybox.fs"));
        string fragmentExpanded = RaylibShaderLoader.ExpandIncludes(fragment, shaderRoot, "skybox.fs", 0);

        Assert.That(skyboxRenderer, Does.Contain("\"skybox.vs\""));
        Assert.That(skyboxRenderer, Does.Contain("\"skybox.fs\""));
        Assert.That(vertex, Does.Contain("uniform mat4 mvp"));
        Assert.That(vertex, Does.Contain("uniform mat4 matModel"));
        Assert.That(vertex, Does.Not.Contain("matView"));
        Assert.That(vertex, Does.Not.Contain("matProjection"));
        Assert.That(fragmentExpanded, Does.Contain("uniform vec3 uSunDirection"));
        Assert.That(fragment, Does.Contain("uniform float uTime"));
        Assert.That(fragment, Does.Not.Contain("uDayPhase"));
        Assert.That(fragment, Does.Not.Contain("texture0"));
    }

    [Test]
    public void SunHalo_SingleSourceAcrossSkyShaders()
    {
        string repoRoot = FindRepoRoot();
        string shaderRoot = Path.Combine(repoRoot, "src", "Platforms", "Desktop");
        string rendererRoot = Path.Combine(repoRoot, "src", "Client", "Ludots.Raylib.Render", "Rendering");
        string[] sunUniforms = { "uSunDiskSharpness", "uSunDiskIntensity", "uSunGlowSharpness", "uSunGlowIntensity" };

        foreach (string fileName in new[] { "skybox.fs", "sky_daynight.fs" })
        {
            string raw = File.ReadAllText(Path.Combine(shaderRoot, fileName));
            Assert.That(raw, Does.Contain("// ludo:include sun_disk.glsl.inc"), fileName);
            Assert.That(raw, Does.Not.Contain("pow(sunDot,"), fileName);

            string expanded = RaylibShaderLoader.ExpandIncludes(raw, shaderRoot, fileName, 0);
            Assert.That(
                expanded.Replace("\r\n", "\n").Split("vec3 SunHalo(").Length - 1,
                Is.EqualTo(1),
                fileName);
            Assert.That(expanded, Does.Contain("normalize(uSunDirection)"), fileName);
            foreach (string uniform in sunUniforms)
            {
                Assert.That(expanded, Does.Contain(uniform), $"{fileName} missing '{uniform}'");
            }
        }

        string skyboxRenderer = File.ReadAllText(Path.Combine(rendererRoot, "RaylibSkyboxRenderer.cs"));
        string skyEnvironment = File.ReadAllText(Path.Combine(rendererRoot, "RaylibSkyEnvironment.cs"));
        foreach (string uniform in sunUniforms)
        {
            Assert.That(skyboxRenderer, Does.Contain(uniform), $"RaylibSkyboxRenderer.cs missing '{uniform}'");
            Assert.That(skyEnvironment, Does.Contain(uniform), $"RaylibSkyEnvironment.cs missing '{uniform}'");
        }
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
        string include = File.ReadAllText(Path.Combine(shaderRoot, "shadow_sampling.glsl.inc"));

        Assert.That(depth, Does.Contain("finalColor = vec4(enc, 1.0)"));
        Assert.That(depth, Does.Not.Contain("enc.a"));

        Assert.That(
            include,
            Does.Contain("dot(packedDepthRgb.rgb, vec3(1.0, 1.0 / 255.0, 1.0 / 65025.0))"));
        Assert.That(include, Does.Contain("float UnpackDepth(vec4 packedDepthRgb)"));
        Assert.That(include, Does.Not.Contain("vec4 packed)"));
        Assert.That(include, Does.Contain("vec2 shadowUv = proj.xy"));
        Assert.That(include, Does.Not.Contain("1.0 - proj.y"));
        Assert.That(include, Does.Contain("uniform float uShadowBias"));
        Assert.That(include, Does.Contain("stored + uShadowBias"));
        Assert.That(include, Does.Contain("uniform float uShadowMapTexel"));
        Assert.That(include, Does.Not.Contain("1.0 / 2048.0"));

        foreach (string receiver in new[] { "model_lit.fs", "model_file_lit.fs", "instancing.fs", "skinning_instanced.fs", "terrain.fs" })
        {
            string fragment = File.ReadAllText(Path.Combine(shaderRoot, receiver));
            Assert.That(fragment, Does.Contain("// ludo:include shadow_sampling.glsl.inc"), receiver);
            Assert.That(fragment, Does.Not.Contain("float UnpackDepth("), receiver);
            Assert.That(fragment, Does.Not.Contain("uniform float uShadowBias"), receiver);
        }
    }

    [Test]
    public void ShadowDepthCutout_DiscardsBelowCutoffAndPacksIdenticallyToOpaqueDepth()
    {
        string repoRoot = FindRepoRoot();
        string shaderRoot = Path.Combine(repoRoot, "src", "Platforms", "Desktop");
        string opaque = File.ReadAllText(Path.Combine(shaderRoot, "shadow_depth.fs"));
        string cutout = File.ReadAllText(Path.Combine(shaderRoot, "shadow_depth_cutout.fs"));
        string cutoutVertex = File.ReadAllText(Path.Combine(shaderRoot, "shadow_depth_cutout.vs"));

        Assert.That(cutout, Does.Contain("uniform sampler2D texture0"));
        Assert.That(cutout, Does.Contain("uniform float alphaCutoff"));
        Assert.That(cutout, Does.Contain("discard"));
        Assert.That(cutoutVertex, Does.Contain("in vec2 vertexTexCoord"));
        Assert.That(cutoutVertex, Does.Contain("uniform mat4 mvp"));

        // 深度打包必须与实体 pass 逐字一致：接收端只解 RGB24 这一种编码。
        Assert.That(ExtractDepthPackingBlock(cutout), Is.EqualTo(ExtractDepthPackingBlock(opaque)));

        string shadowMap = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Raylib.Render",
            "Rendering",
            "RaylibDirectionalShadowMap.cs"));
        Assert.That(shadowMap, Does.Contain("\"shadow_depth_cutout.vs\""));
        Assert.That(shadowMap, Does.Contain("\"shadow_depth_cutout.fs\""));
        Assert.That(shadowMap, Does.Contain("DrawMeshShadowCutout"));
    }

    [Test]
    public void BillboardShadow_CutoutAlphaTestsAndTransparentCastsNothing()
    {
        string repoRoot = FindRepoRoot();
        string renderer = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "Client",
            "Ludots.Raylib.Render",
            "Rendering",
            "RaylibPrimitiveRenderer.cs"));

        int method = renderer.IndexOf("private void DrawBillboardShadow", StringComparison.Ordinal);
        Assert.That(method, Is.GreaterThanOrEqualTo(0));
        int next = renderer.IndexOf("private void DrawProceduralMesh(", method, StringComparison.Ordinal);
        Assert.That(next, Is.GreaterThan(method));
        string body = renderer[method..next];
        Assert.That(body, Does.Contain("DrawMeshShadowCutout"));
        Assert.That(body, Does.Contain("DefaultVegetationAlphaCutoff"));

        int leaf = renderer.IndexOf("private void DrawShadowLeafAsset", StringComparison.Ordinal);
        Assert.That(leaf, Is.GreaterThanOrEqualTo(0));
        string leafBody = renderer[leaf..(leaf + 4000)];
        Assert.That(leafBody, Does.Contain("RaylibMaterialDrawState.CastsShadow"));
    }

    [Test]
    public void ShadowCastEligibility_OnlyOpaqueAndCutoutCast()
    {
        Assert.That(RaylibMaterialDrawState.CastsShadow(MaterialBlendMode.Opaque), Is.True);
        Assert.That(RaylibMaterialDrawState.CastsShadow(MaterialBlendMode.Cutout), Is.True);
        Assert.That(RaylibMaterialDrawState.CastsShadow(MaterialBlendMode.AlphaBlend), Is.False);
        Assert.That(RaylibMaterialDrawState.CastsShadow(MaterialBlendMode.Additive), Is.False);
    }

    private static string ExtractDepthPackingBlock(string fragmentShader)
    {
        string text = fragmentShader.Replace("\r\n", "\n");
        int start = text.IndexOf("float depth = gl_FragCoord.z;", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "Could not locate depth packing start.");
        int end = text.IndexOf("finalColor = vec4(enc, 1.0);", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThanOrEqualTo(0), "Could not locate depth packing end.");
        return text[start..(end + "finalColor = vec4(enc, 1.0);".Length)].Trim();
    }

    [Test]
    public void RaylibShadowConfig_ValidatesMapSizeAndBias()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibShadowConfig(MapSize: 1000, ReceiverBiasWorld: 0.04f).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibShadowConfig(MapSize: 128, ReceiverBiasWorld: 0.04f).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibShadowConfig(MapSize: 2048, ReceiverBiasWorld: 0f).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new RaylibShadowConfig(MapSize: 2048, ReceiverBiasWorld: -0.1f).Validate());

        RaylibShadowConfig defaults = RaylibShadowConfig.CreateDefault();
        Assert.DoesNotThrow(() => defaults.Validate());
        Assert.That(defaults.MapSize, Is.EqualTo(2048));
    }

    [Test]
    public void ReceiverShaders_ExpandToIdenticalShadowSamplingBlock()
    {
        string repoRoot = FindRepoRoot();
        string shaderRoot = Path.Combine(repoRoot, "src", "Platforms", "Desktop");
        string baseline = ExpandAndExtract(shaderRoot, "model_lit.fs");

        foreach (string receiver in new[] { "instancing.fs", "skinning_instanced.fs", "terrain.fs" })
        {
            Assert.That(ExpandAndExtract(shaderRoot, receiver), Is.EqualTo(baseline), receiver);
        }
    }

    private static string ExpandAndExtract(string shaderRoot, string fileName)
    {
        string raw = File.ReadAllText(Path.Combine(shaderRoot, fileName));
        string expanded = RaylibShaderLoader.ExpandIncludes(raw, shaderRoot, fileName, 0);
        Assert.That(
            expanded.Replace("\r\n", "\n").Split("float UnpackDepth(").Length - 1,
            Is.EqualTo(1),
            fileName);
        return ExtractShadowSamplingBlock(expanded);
    }

    private static string ExtractShadowSamplingBlock(string shaderText)
    {
        string text = shaderText.Replace("\r\n", "\n");

        int blockStart = text.IndexOf("float UnpackDepth(", StringComparison.Ordinal);
        Assert.That(blockStart, Is.GreaterThanOrEqualTo(0), "Could not locate UnpackDepth in shader.");

        int sampleShadow = text.IndexOf("float SampleShadow(", StringComparison.Ordinal);
        Assert.That(sampleShadow, Is.GreaterThanOrEqualTo(0), "Could not locate SampleShadow in shader.");

        int bodyOpen = text.IndexOf('{', sampleShadow);
        Assert.That(bodyOpen, Is.GreaterThanOrEqualTo(0), "Could not locate SampleShadow body opening brace.");

        int braceDepth = 0;
        int blockEnd = -1;
        for (int i = bodyOpen; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                braceDepth++;
            }
            else if (text[i] == '}')
            {
                braceDepth--;
                if (braceDepth == 0)
                {
                    blockEnd = i;
                    break;
                }
            }
        }

        Assert.That(blockEnd, Is.GreaterThanOrEqualTo(0), "Could not locate SampleShadow closing brace.");

        return text.Substring(blockStart, blockEnd - blockStart + 1).Trim();
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
