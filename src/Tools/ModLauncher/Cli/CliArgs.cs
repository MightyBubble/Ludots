using System;
using System.Collections.Generic;

namespace Ludots.ModLauncher.Cli
{
    internal sealed class CliArgs
    {
        public string Primary { get; private set; } = "";
        public string Secondary { get; private set; } = "";
        public string? PresetId { get; private set; }
        public string? ConfigPath { get; private set; }
        public List<string> ModNames { get; } = new List<string>();

        public static CliArgs Parse(string[] args)
        {
            var a = new CliArgs();
            if (args.Length == 0) return a;

            a.Primary = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            a.Secondary = args.Length > 1 ? args[1].ToLowerInvariant() : "";

            for (int i = 2; i < args.Length; i++)
            {
                var tok = args[i];
                if (tok == "--preset" && i + 1 < args.Length)
                {
                    a.PresetId = args[++i];
                    continue;
                }

                if (tok == "--config" && i + 1 < args.Length)
                {
                    a.ConfigPath = args[++i];
                    continue;
                }

                if (tok == "--mod" && i + 1 < args.Length)
                {
                    a.ModNames.Add(args[++i]);
                    continue;
                }

                if (tok == "--mods" && i + 1 < args.Length)
                {
                    a.ModNames.AddRange(args[++i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    continue;
                }
            }

            return a;
        }
    }
}

