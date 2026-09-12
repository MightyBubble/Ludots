using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.RaylibAdapter;

/// <summary>
/// GPU compute 姿势求值与 raylib UpdateModelAnimationBones 的逐元素等价合同：
/// 同一 (模型, clip, 离散帧) 下，SSBO 回读矩阵必须与 raylib CPU 求值的 mesh.boneMatrices
/// 在容差内一致——锁死 TRS 分解式、四元数约定与列主序布局的移植正确性。
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("NativeGraphics")]
public sealed unsafe class RaylibGpuSkinnedSsboEquivalenceTests
{
    [Test]
    public void ComputePoseMatrices_MatchRaylibBoneMatricesPerElement()
    {
        string modelPath = Path.Combine(
            FindRepoRoot(),
            "mods", "capabilities", "navigation", "MassNavigationMod", "assets", "Models",
            "mass_navigation_agent_soldier.glb");
        Assert.That(File.Exists(modelPath), Is.True, modelPath);

        const uint HiddenWindow = 0x80;
        Rl.SetConfigFlags(HiddenWindow);
        Rl.InitWindow(64, 64, "SSBO pose equivalence");
        try
        {
            Gl43.Initialize();
            Model model = RaylibNativeResources.LoadModel(modelPath);
            ModelAnimation* animations = Rl.LoadModelAnimations(modelPath, out int animCount);
            Assert.That(animCount, Is.GreaterThan(0), "soldier 模型必须携带动画");
            try
            {
                int modelBoneCount = model.boneCount;
                int firstMeshBoneCount = model.meshes[0].boneCount;
                int firstAnimBoneCount = animations[0].boneCount;
                TestContext.WriteLine(
                    $"骨骼数量合同：model.boneCount={modelBoneCount} mesh0.boneCount={firstMeshBoneCount} anim0.boneCount={firstAnimBoneCount} animCount={animCount} frames0={animations[0].frameCount}");
                for (int clip = 0; clip < animCount; clip++)
                {
                    if (animations[clip].boneCount != firstAnimBoneCount || animations[clip].boneCount != firstMeshBoneCount)
                    {
                        TestContext.WriteLine($"  clip {clip}: boneCount={animations[clip].boneCount} frames={animations[clip].frameCount}");
                    }
                }

                using var pipeline = new RaylibGpuSkinnedSsboPipeline(
                    maxPoseRows: animCount,
                    poseStride: 256,
                    maxInstances: 1);
                RaylibGpuSkinnedSsboModelBinding binding = pipeline.RegisterModel(999, model, animations, animCount);

                float[] gpuRow = new float[256 * 16];
                float[] expectedGl = new float[16];
                double maxAbsDiff = 0;
                string worst = "<none>";
                int compared = 0;
                long mismatched = 0;
                int firstBadClip = -1, firstBadFrame = -1;
                for (int clip = 0; clip < animCount; clip++)
                {
                    int frameCount = animations[clip].frameCount;
                    Assert.That(frameCount, Is.GreaterThan(0));
                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        Rl.UpdateModelAnimationBones(model, animations[clip], frame);
                        pipeline.WriteRowMeta(0, binding, clip, frame);
                        pipeline.DispatchPoseEvaluation(poseRowCount: 1, instanceCount: 0);
                        pipeline.ReadBackPoseRow(0, gpuRow);

                        for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                        {
                            Mesh mesh = model.meshes[meshIndex];
                            if (mesh.boneCount <= 0 || mesh.boneMatrices == null)
                            {
                                continue;
                            }

                            for (int bone = 0; bone < mesh.boneCount; bone++)
                            {
                                RaylibMatrix expected = mesh.boneMatrices[bone];
                                expectedGl[0] = expected.m0;
                                expectedGl[1] = expected.m1;
                                expectedGl[2] = expected.m2;
                                expectedGl[3] = 0f;
                                expectedGl[4] = expected.m4;
                                expectedGl[5] = expected.m5;
                                expectedGl[6] = expected.m6;
                                expectedGl[7] = 0f;
                                expectedGl[8] = expected.m8;
                                expectedGl[9] = expected.m9;
                                expectedGl[10] = expected.m10;
                                expectedGl[11] = 0f;
                                expectedGl[12] = expected.m12;
                                expectedGl[13] = expected.m13;
                                expectedGl[14] = expected.m14;
                                expectedGl[15] = 1f;
                                for (int element = 0; element < 16; element++)
                                {
                                    float actual = gpuRow[bone * 16 + element];
                                    double diff = Math.Abs(expectedGl[element] - actual);
                                    if (diff > maxAbsDiff)
                                    {
                                        maxAbsDiff = diff;
                                        worst = $"clip={clip} frame={frame} mesh={meshIndex} bone={bone} element={element} " +
                                                $"expected={expectedGl[element]} actual={actual}";
                                    }

                                    if (diff > 1e-3)
                                    {
                                        mismatched++;
                                        if (firstBadClip < 0)
                                        {
                                            firstBadClip = clip;
                                            firstBadFrame = frame;
                                        }
                                    }

                                    compared++;
                                }
                            }
                        }
                    }
                }

                TestContext.WriteLine($"SSBO 等价性：compared={compared} mismatched(>1e-3)={mismatched} 首个失败 clip={firstBadClip} frame={firstBadFrame} maxAbsDiff={maxAbsDiff:E3} worst=[{worst}]");
                Assert.That(compared, Is.GreaterThan(0), "至少要比较一组骨骼矩阵");
                Assert.That(maxAbsDiff, Is.LessThanOrEqualTo(1e-3),
                    $"GPU compute 姿势矩阵与 raylib CPU 求值不一致，最差项 {worst}");
            }
            finally
            {
                Rl.UnloadModelAnimations(animations, animCount);
                RaylibNativeResources.UnloadModel(model);
            }
        }
        finally
        {
            Rl.CloseWindow();
            Gl43.ResetForTests();
        }
    }

    private static string FindRepoRoot()
    {
        string? current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }
}
