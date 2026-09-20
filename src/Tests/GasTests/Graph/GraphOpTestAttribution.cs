using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Ludots.Tests.GAS
{
    internal sealed class GraphOpTestAttribution
    {
        internal const string GalleryPrefix = "GraphOpsNodeGallery";
        internal const string PerOpShowcasePrefix = "capability_standard_graph_op_";
        internal const string FamilyShowcasePrefix = "capability_standard_graph_ops_";

        internal static readonly string[] UniversalGalleryMethods =
        [
            "EveryExecutableOp_HasVignetteGraphAndUniqueShowcaseId",
            "EveryVignette_TicksOnce_WithChineseCaption",
            "ExistingVignettes_CompileWithFeaturedOp",
            "GeneratedMaps_SpawnEveryVignetteActor"
        ];

        private static readonly Regex ClassPattern = new(
            @"(?:public\s+)?(?:sealed\s+)?class\s+(\w+)",
            RegexOptions.Compiled);
        private static readonly Regex MethodPattern = new(
            @"public\s+void\s+([A-Za-z0-9_]+)\s*\(",
            RegexOptions.Compiled);
        private static readonly Regex StringLitPattern = new(
            @"""([A-Za-z][A-Za-z0-9_]*)""",
            RegexOptions.Compiled);
        private static readonly Regex ArrayPattern = new(
            @"(?:readonly\s+)?string\[\s*\]\s+(\w+)\s*=\s*\[(.*?)\];",
            RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex TestCasePattern = new(
            @"\[TestCase\(\s*""([A-Za-z][A-Za-z0-9_]*)""",
            RegexOptions.Compiled);
        private static readonly Regex TestCaseSourcePattern = new(
            @"\[TestCaseSource\(\s*nameof\((\w+)\)\s*\)\]",
            RegexOptions.Compiled);
        private static readonly Regex GraphOpEnumPattern = new(
            @"GraphNodeOp\.([A-Za-z][A-Za-z0-9_]*)",
            RegexOptions.Compiled);
        private static readonly Regex JsonOpPattern = new(
            @"""op""\s*:\s*""([A-Za-z][A-Za-z0-9_]*)""",
            RegexOptions.Compiled);
        private static readonly Regex BindLitPattern = new(
            @"(?:BindOp|Play|TickOp|BindAndTick)\(\s*""([A-Za-z][A-Za-z0-9_]*)""\s*\)",
            RegexOptions.Compiled);
        private static readonly Regex BindParamPattern = new(
            @"(?:BindOp|Play|TickOp|BindAndTick)\(\s*(\w+)\s*\)",
            RegexOptions.Compiled);
        private static readonly Regex ForeachOpPattern = new(
            @"foreach\s*\(\s*string\s+(\w+)\s+in\s+(\w+)",
            RegexOptions.Compiled);
        private static readonly Regex EnumAllPattern = new(
            @"Enum\.GetValues(?:<GraphNodeOp>|\(\s*typeof\s*\(\s*GraphNodeOp\s*\)\s*\))",
            RegexOptions.Compiled);
        private static readonly Regex GetFilesPattern = new(@"GetFiles\s*\(", RegexOptions.Compiled);

        private readonly HashSet<string> _ops;
        private readonly Dictionary<(string ClassName, string Method), HashSet<string>> _executed;

        private GraphOpTestAttribution(
            HashSet<string> ops,
            Dictionary<(string ClassName, string Method), HashSet<string>> executed)
        {
            _ops = ops;
            _executed = executed;
        }

        internal static GraphOpTestAttribution Load(string repoRoot, IEnumerable<string> ops)
        {
            var opSet = new HashSet<string>(ops, StringComparer.Ordinal);
            var executed = new Dictionary<(string, string), HashSet<string>>();
            string testsRoot = Path.Combine(repoRoot, "src", "Tests", "GasTests");
            foreach (string file in Directory.EnumerateFiles(testsRoot, "*Tests.cs", SearchOption.AllDirectories))
            {
                string text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                Match classMatch = ClassPattern.Match(text);
                if (!classMatch.Success)
                {
                    continue;
                }

                string className = classMatch.Groups[1].Value;
                Dictionary<string, HashSet<string>> arrays = LoadStringArrays(text, opSet);
                foreach (Match methodMatch in MethodPattern.Matches(text))
                {
                    string method = methodMatch.Groups[1].Value;
                    string attrs = ExtractLeadingAttributes(text, methodMatch.Index);
                    int brace = text.IndexOf('{', methodMatch.Index + methodMatch.Length);
                    string body = brace >= 0 ? ExtractBalanced(text, brace) : string.Empty;
                    executed[(className, method)] = OpsForMethod(method, attrs, body, arrays, opSet);
                }
            }

            return new GraphOpTestAttribution(opSet, executed);
        }

        internal bool HasMethod(string className, string method)
        {
            return _executed.ContainsKey((className, method));
        }

        internal bool Executes(string className, string method, string op)
        {
            return _executed.TryGetValue((className, method), out HashSet<string>? ops) && ops.Contains(op);
        }

        internal static bool IsUniversalGallery(string className, string method)
        {
            return className.StartsWith(GalleryPrefix, StringComparison.Ordinal)
                   && Array.IndexOf(UniversalGalleryMethods, method) >= 0;
        }

        internal static bool IsGallerySpecific(string className, string method)
        {
            return className.StartsWith(GalleryPrefix, StringComparison.Ordinal)
                   && !IsUniversalGallery(className, method);
        }

        internal static bool IsPerOpShowcaseId(string id)
        {
            return id.StartsWith(PerOpShowcasePrefix, StringComparison.Ordinal)
                   && !id.StartsWith(FamilyShowcasePrefix, StringComparison.Ordinal);
        }

        private static Dictionary<string, HashSet<string>> LoadStringArrays(string text, HashSet<string> opSet)
        {
            var arrays = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Match match in ArrayPattern.Matches(text))
            {
                arrays[match.Groups[1].Value] = CollectOps(match.Groups[2].Value, StringLitPattern, opSet);
            }

            return arrays;
        }

        private static HashSet<string> OpsForMethod(
            string method,
            string attrs,
            string body,
            Dictionary<string, HashSet<string>> arrays,
            HashSet<string> opSet)
        {
            if (EnumAllPattern.IsMatch(body) || IteratesVignetteFiles(body))
            {
                return new HashSet<string>(opSet, StringComparer.Ordinal);
            }

            var executed = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in TestCasePattern.Matches(attrs))
            {
                if (opSet.Contains(match.Groups[1].Value))
                {
                    executed.Add(match.Groups[1].Value);
                }
            }

            foreach (Match match in TestCaseSourcePattern.Matches(attrs))
            {
                if (arrays.TryGetValue(match.Groups[1].Value, out HashSet<string>? sourceOps))
                {
                    executed.UnionWith(sourceOps);
                }
            }

            executed.UnionWith(CollectOps(body, BindLitPattern, opSet));
            executed.UnionWith(CollectOps(body, GraphOpEnumPattern, opSet));
            executed.UnionWith(CollectOps(body, JsonOpPattern, opSet));

            Dictionary<string, HashSet<string>> localArrays = LoadStringArrays(body, opSet);
            var foreachVars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in ForeachOpPattern.Matches(body))
            {
                foreachVars[match.Groups[1].Value] = match.Groups[2].Value;
            }

            foreach (Match match in BindParamPattern.Matches(body))
            {
                string param = match.Groups[1].Value;
                if (localArrays.TryGetValue(param, out HashSet<string>? local))
                {
                    executed.UnionWith(local);
                }

                if (arrays.TryGetValue(param, out HashSet<string>? field))
                {
                    executed.UnionWith(field);
                }

                if (foreachVars.TryGetValue(param, out string? source))
                {
                    if (localArrays.TryGetValue(source, out HashSet<string>? localSource))
                    {
                        executed.UnionWith(localSource);
                    }

                    if (arrays.TryGetValue(source, out HashSet<string>? fieldSource))
                    {
                        executed.UnionWith(fieldSource);
                    }
                }
            }

            foreach (string op in opSet)
            {
                if (method.StartsWith(op + "_", StringComparison.Ordinal))
                {
                    executed.Add(op);
                }
            }

            return executed;
        }

        private static bool IteratesVignetteFiles(string body)
        {
            return GetFilesPattern.IsMatch(body)
                   && body.Contains("*.json", StringComparison.Ordinal)
                   && body.Contains("vignette", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> CollectOps(string text, Regex pattern, HashSet<string> opSet)
        {
            var ops = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in pattern.Matches(text))
            {
                string value = match.Groups[1].Value;
                if (opSet.Contains(value))
                {
                    ops.Add(value);
                }
            }

            return ops;
        }

        private static string ExtractLeadingAttributes(string text, int methodStart)
        {
            int i = methodStart;
            while (i > 0 && char.IsWhiteSpace(text[i - 1]))
            {
                i--;
            }

            var chunks = new List<string>();
            while (i > 0 && text[i - 1] == ']')
            {
                int depth = 0;
                bool found = false;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (text[j] == ']')
                    {
                        depth++;
                    }
                    else if (text[j] == '[')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            chunks.Add(text.Substring(j, i - j));
                            i = j;
                            while (i > 0 && char.IsWhiteSpace(text[i - 1]))
                            {
                                i--;
                            }

                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    break;
                }
            }

            chunks.Reverse();
            return string.Concat(chunks);
        }

        private static string ExtractBalanced(string text, int start)
        {
            if (start >= text.Length || text[start] != '{')
            {
                return string.Empty;
            }

            int depth = 0;
            int i = start;
            char? inStr = null;
            while (i < text.Length)
            {
                char ch = text[i];
                if (inStr is null)
                {
                    if (i + 1 < text.Length && ch == '/' && text[i + 1] == '/')
                    {
                        int nl = text.IndexOf('\n', i);
                        i = nl < 0 ? text.Length : nl + 1;
                        continue;
                    }

                    if (i + 1 < text.Length && ch == '/' && text[i + 1] == '*')
                    {
                        int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        i = end < 0 ? text.Length : end + 2;
                        continue;
                    }

                    if (i + 2 < text.Length && ch == '"' && text[i + 1] == '"' && text[i + 2] == '"')
                    {
                        int end = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                        i = end < 0 ? text.Length : end + 3;
                        continue;
                    }

                    if (ch is '"' or '\'')
                    {
                        inStr = ch;
                        i++;
                        continue;
                    }

                    if (ch == '{')
                    {
                        depth++;
                    }
                    else if (ch == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return text.Substring(start, i - start + 1);
                        }
                    }
                }
                else
                {
                    if (ch == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (ch == inStr)
                    {
                        inStr = null;
                    }
                }

                i++;
            }

            return text.Substring(start);
        }
    }
}
