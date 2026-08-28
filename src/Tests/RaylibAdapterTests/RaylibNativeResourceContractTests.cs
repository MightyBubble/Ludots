using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

/// <summary>
/// 源码契约：raylib 原生资源的加载/卸载在生产代码里只允许出现在 RaylibNativeResources 门面，
/// 保证 RaylibNativeResourceLedger 台账不漏登记（#1322 W0）。
/// 匹配是空白容忍的正则（Rl / Raylib / Raylib_cs.Raylib 三种限定形式），
/// 但文本契约无法封死任意别名 using——最终防线是门面记账邻接检查与评审。
/// </summary>
[TestFixture]
public sealed class RaylibNativeResourceContractTests
{
    private static readonly string[] WrappedMembers =
    {
        "LoadTexture(",
        "LoadTextureFromImage(",
        "LoadRenderTexture(",
        "LoadModel(",
        "LoadShader(",
        "LoadShaderFromMemory(",
        "LoadMaterialDefault(",
        "LoadSound(",
        "LoadSoundAlias(",
        "UnloadTexture(",
        "UnloadRenderTexture(",
        "UnloadModel(",
        "UnloadShader(",
        "UnloadMaterial(",
        "UnloadMesh(",
        "UnloadSound(",
        "UnloadSoundAlias(",
        "UploadMesh(",
        "GenMeshCube(",
        "GenMeshSphere(",
        "LoadTextureCubemap(",
    };

    [Test]
    public void ProductionCode_LoadsOrUnloadsNativeResources_OnlyThroughFacade()
    {
        string repoRoot = FindRepoRoot();
        string[] roots =
        {
            Path.Combine(repoRoot, "src", "Client", "Ludots.Raylib.Render"),
            Path.Combine(repoRoot, "src", "Client", "Ludots.Client.Raylib"),
            Path.Combine(repoRoot, "src", "Adapters", "Raylib"),
            Path.Combine(repoRoot, "src", "Apps", "Raylib"),
        };

        List<string> violations = new();
        foreach (string root in roots)
        {
            foreach (string file in EnumerateSourceFiles(root))
            {
                string relative = Path.GetRelativePath(repoRoot, file);
                if (relative.EndsWith("RaylibNativeResources.cs"))
                {
                    continue;
                }

                string content = File.ReadAllText(file);
                foreach (Regex pattern in BuildForbiddenPatterns())
                {
                    if (pattern.IsMatch(content))
                    {
                        violations.Add($"{relative}: {pattern}");
                    }
                }

                if (content.Contains("RaylibSkyIblInterop"))
                {
                    violations.Add($"{relative}: RaylibSkyIblInterop 绕过门面的本地 interop");
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "生产代码必须经 RaylibNativeResources 门面加载/卸载 raylib 原生资源，直连 Rl/Raylib/Raylib_cs.Raylib 会绕过驻留台账：\n" +
            string.Join("\n", violations));
    }

    [Test]
    public void Facade_WrapsEveryContractMember_AndAccountsAdjacentLedgerCall()
    {
        string facadePath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Client",
            "Ludots.Raylib.Render",
            "Rendering",
            "RaylibNativeResources.cs");
        Assert.That(File.Exists(facadePath), Is.True, facadePath);
        string facade = File.ReadAllText(facadePath);

        foreach (string member in WrappedMembers)
        {
            bool wrappedByRl = facade.Contains("Rl." + member);
            bool wrappedByNativeImport = member == "LoadTextureCubemap(" && facade.Contains("rlLoadTextureCubemap");
            Assert.That(wrappedByRl || wrappedByNativeImport, Is.True, $"门面缺少 {member.TrimEnd('(')} 的直连实现");

            Assert.That(HasAdjacentLedgerCall(facade, member), Is.True,
                $"{member.TrimEnd('(')} 的直连调用后 600 字符内没有记账调用（Track/Untrack）");
        }

        Assert.That(facade, Does.Contain("RaylibNativeResourceLedger.Track("));
        Assert.That(facade, Does.Contain("RaylibNativeResourceLedger.Untrack("));
    }

    private static IEnumerable<Regex> BuildForbiddenPatterns()
    {
        foreach (string member in WrappedMembers)
        {
            string name = Regex.Escape(member.TrimEnd('('));
            yield return new Regex(@"\bRl\s*\.\s*" + name + @"\s*\(", RegexOptions.None);
            yield return new Regex(@"\bRaylib\s*\.\s*" + name + @"\s*\(", RegexOptions.None);
        }
    }

    private static bool HasAdjacentLedgerCall(string facade, string member)
    {
        bool expectsTrack = member.StartsWith("Unload") == false;
        foreach (int bodyStart in EnumerateWrapperMethodBodies(facade, member.TrimEnd('(')))
        {
            string? body = ExtractMethodBody(facade, bodyStart);
            if (body == null)
            {
                continue;
            }

            bool accounted = expectsTrack
                ? body.Contains("TrackIfResident(") || body.Contains("RaylibNativeResourceLedger.Track(")
                : body.Contains("UntrackIfResident(") || body.Contains("RaylibNativeResourceLedger.Untrack(");
            if (accounted)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<int> EnumerateWrapperMethodBodies(string facade, string methodName)
    {
        int search = 0;
        while (true)
        {
            int signature = facade.IndexOf(methodName + "(", search, StringComparison.Ordinal);
            if (signature < 0)
            {
                yield break;
            }

            int openingBrace = facade.IndexOf('{', signature);
            int signatureLineStart = facade.LastIndexOf('\n', signature) + 1;
            string signatureLine = facade.Substring(signatureLineStart, signature - signatureLineStart);
            bool isMethodDeclaration = signatureLine.Contains("static") && openingBrace >= 0 && openingBrace - signature < 400;
            if (isMethodDeclaration)
            {
                yield return openingBrace;
            }

            search = signature + 1;
        }
    }

    private static string? ExtractMethodBody(string facade, int openingBrace)
    {
        int depth = 0;
        for (int i = openingBrace; i < facade.Length; i++)
        {
            if (facade[i] == '{')
            {
                depth++;
            }
            else if (facade[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return facade.Substring(openingBrace, i - openingBrace + 1);
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
            {
                continue;
            }

            yield return file;
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
