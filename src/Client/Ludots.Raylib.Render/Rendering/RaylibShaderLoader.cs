using System;
using System.IO;
using System.Text.RegularExpressions;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    internal static class RaylibShaderLoader
    {
        private const int MaxIncludeDepth = 4;

        private static readonly Regex IncludePattern = new(
            @"^\s*//\s*ludo:include\s+(\S+)\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public static Shader Load(string shaderDirectory, string vsFileName, string fsFileName, string label)
        {
            string vsText = ExpandIncludes(
                File.ReadAllText(Path.Combine(shaderDirectory, vsFileName)),
                shaderDirectory,
                vsFileName,
                depth: 0);
            string fsText = ExpandIncludes(
                File.ReadAllText(Path.Combine(shaderDirectory, fsFileName)),
                shaderDirectory,
                fsFileName,
                depth: 0);

            Shader shader = RaylibNativeResources.LoadShaderFromMemory(vsText, fsText);
            if (shader.id == 0)
            {
                throw new InvalidOperationException(
                    $"Failed to load {label} shader ({vsFileName}, {fsFileName}): shader.id == 0.");
            }

            return shader;
        }

        internal static string ExpandIncludes(string text, string shaderDirectory, string fileName, int depth)
        {
            if (depth > MaxIncludeDepth)
            {
                throw new InvalidOperationException(
                    $"Shader include depth exceeds {MaxIncludeDepth} while expanding '{fileName}'.");
            }

            return IncludePattern.Replace(text, match =>
            {
                string includeName = match.Groups[1].Value;
                // Path.GetFileName 去掉目录成分，防 include 名带路径时逃出 shaderDirectory。
                string includePath = Path.Combine(shaderDirectory, Path.GetFileName(includeName));
                if (!File.Exists(includePath))
                {
                    throw new FileNotFoundException(
                        $"Shader include '{includeName}' referenced by '{fileName}' was not found under '{shaderDirectory}'.",
                        includePath);
                }

                return ExpandIncludes(File.ReadAllText(includePath), shaderDirectory, includeName, depth + 1);
            });
        }
    }
}
