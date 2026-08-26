using System;
using System.Collections.Generic;
using System.Globalization;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    /// <summary>
    /// Brace scan shared by FormatText authoring sugar and editor auto-pins.
    /// Supports <c>{0}</c>… and named <c>{name}</c>; <c>{{</c>/<c>}}</c> escape.
    /// </summary>
    public static class GraphFormalTextTemplate
    {
        public const string ParseError = "GAS.GRAPH.ERR.FormatTextTemplate";

        public readonly struct Part
        {
            public Part(string? literal, string? portName, int argIndex)
            {
                Literal = literal;
                PortName = portName;
                ArgIndex = argIndex;
            }

            public string? Literal { get; }
            public string? PortName { get; }
            public int ArgIndex { get; }
            public bool IsLiteral => Literal != null;
        }

        public static bool TryParse(string? template, out Part[] parts, out string? error)
        {
            parts = Array.Empty<Part>();
            error = null;
            if (template == null)
            {
                error = $"{ParseError}: template text is required.";
                return false;
            }

            var list = new List<Part>(8);
            var literal = new System.Text.StringBuilder();
            var seenPorts = new Dictionary<string, int>(StringComparer.Ordinal);
            int nextIndex = 0;

            for (int i = 0; i < template.Length; i++)
            {
                char ch = template[i];
                if (ch == '{')
                {
                    if (i + 1 < template.Length && template[i + 1] == '{')
                    {
                        literal.Append('{');
                        i++;
                        continue;
                    }

                    FlushLiteral(list, literal);
                    int close = template.IndexOf('}', i + 1);
                    if (close < 0)
                    {
                        error = $"{ParseError}: unterminated '{{' in template.";
                        return false;
                    }

                    string raw = template.Substring(i + 1, close - i - 1);
                    if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf(':') >= 0)
                    {
                        error = $"{ParseError}: invalid placeholder '{{{raw}}}'.";
                        return false;
                    }

                    string portName;
                    int argIndex;
                    if (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int numeric))
                    {
                        portName = GraphControlFlowPorts.Arg(numeric);
                        if (!seenPorts.TryGetValue(portName, out argIndex))
                        {
                            argIndex = numeric;
                            seenPorts[portName] = argIndex;
                            if (numeric + 1 > nextIndex)
                            {
                                nextIndex = numeric + 1;
                            }
                        }
                    }
                    else
                    {
                        portName = GraphControlFlowPorts.Arg(raw);
                        if (!seenPorts.TryGetValue(portName, out argIndex))
                        {
                            argIndex = nextIndex++;
                            seenPorts[portName] = argIndex;
                        }
                    }

                    list.Add(new Part(literal: null, portName, argIndex));
                    i = close;
                    continue;
                }

                if (ch == '}')
                {
                    if (i + 1 < template.Length && template[i + 1] == '}')
                    {
                        literal.Append('}');
                        i++;
                        continue;
                    }

                    error = $"{ParseError}: unmatched '}}' in template.";
                    return false;
                }

                literal.Append(ch);
            }

            FlushLiteral(list, literal);
            parts = list.ToArray();
            return true;
        }

        public static string[] ListValuePorts(Part[] parts)
        {
            var ports = new List<string>(4);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].IsLiteral || parts[i].PortName == null)
                {
                    continue;
                }

                if (seen.Add(parts[i].PortName!))
                {
                    ports.Add(parts[i].PortName!);
                }
            }

            return ports.ToArray();
        }

        private static void FlushLiteral(List<Part> parts, System.Text.StringBuilder literal)
        {
            if (literal.Length == 0)
            {
                return;
            }

            parts.Add(new Part(literal.ToString(), portName: null, argIndex: -1));
            literal.Clear();
        }
    }
}
