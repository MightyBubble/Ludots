using System;
using System.Linq;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.RaylibAdapter;

/// <summary>
/// 真机探针：raylib 官方 GL 3.3 上下文上必须暴露 SSBO/compute 所需 ARB 扩展，
/// GPU 驱动蒙皮管线是硬合同——缺失即失败，不做纹理调色板回退。
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("NativeGraphics")]
public sealed class Gl43CapabilityProbeTests
{
    [Test]
    public void Raylib33Context_ExposesRequiredSsboComputeExtensions()
    {
        const uint HiddenWindow = 0x80;
        Rl.SetConfigFlags(HiddenWindow);
        Rl.InitWindow(64, 64, "Gl43 capability probe");
        try
        {
            Gl43Capabilities capabilities = Gl43.Initialize();
            TestContext.WriteLine(
                $"GL probe: version='{capabilities.Version}' renderer='{capabilities.Renderer}' " +
                $"ssbo={capabilities.HasShaderStorage} compute={capabilities.HasComputeShader} " +
                $"mdi={capabilities.HasMultiDrawIndirect}");

            Assert.That(capabilities.MissingRequired, Is.Empty,
                $"GPU-skinned SSBO 管线在当前 GL 3.3 上下文缺扩展: {string.Join(", ", capabilities.MissingRequired)}");
            Assert.That(capabilities.HasShaderStorage, Is.True);
            Assert.That(capabilities.HasComputeShader, Is.True);
        }
        finally
        {
            Rl.CloseWindow();
            Gl43.ResetForTests();
        }
    }

    [Test]
    public void Probe_MissingExtensionListedAsContractFailure()
    {
        Gl43Capabilities capabilities = Gl43Capabilities.Probe(
            "3.3 Core",
            "software rasterizer",
            new[] { "GL_ARB_compute_shader" });

        Assert.That(capabilities.MissingRequired, Is.EqualTo(new[] { Gl43.ExtShaderStorage }));
        Assert.That(capabilities.HasShaderStorage, Is.False);
        Assert.That(capabilities.HasComputeShader, Is.True);
    }
}
