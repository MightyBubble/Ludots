using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Runtime
{
    internal sealed class ChampionSkillCastModeCatalog
    {
        public const string Uri = "ChampionSkillSandboxMod:assets/UI/cast_mode_toolbar.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        private ChampionSkillCastModeCatalog(Mode[] modes)
        {
            Modes = modes;
        }

        public Mode[] Modes { get; }

        public readonly record struct Mode(string ButtonId, string Label, string ActionId, string CastModeId, string Accent);

        public static ChampionSkillCastModeCatalog Load(GameEngine engine)
        {
            using Stream stream = engine.VFS.GetStream(Uri);
            FileDto? file = JsonSerializer.Deserialize<FileDto>(stream, JsonOptions);
            if (file?.Modes == null || file.Modes.Length == 0)
            {
                throw new InvalidOperationException($"{Uri} must list at least one cast mode.");
            }

            var modes = new Mode[file.Modes.Length];
            for (int i = 0; i < file.Modes.Length; i++)
            {
                ModeDto dto = file.Modes[i] ?? throw new InvalidOperationException($"{Uri} modes[{i}] is missing.");
                modes[i] = new Mode(
                    RequireToken(dto.ButtonId, $"{Uri} modes[{i}].buttonId"),
                    RequireToken(dto.Label, $"{Uri} modes[{i}].label"),
                    RequireToken(dto.ActionId, $"{Uri} modes[{i}].actionId"),
                    RequireToken(dto.CastModeId, $"{Uri} modes[{i}].castModeId"),
                    RequireToken(dto.Accent, $"{Uri} modes[{i}].accent"));
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (string.Equals(modes[earlier].ButtonId, modes[i].ButtonId, StringComparison.Ordinal) ||
                        string.Equals(modes[earlier].CastModeId, modes[i].CastModeId, StringComparison.Ordinal) ||
                        string.Equals(modes[earlier].ActionId, modes[i].ActionId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"{Uri} modes[{i}] repeats a button, action, or cast mode.");
                    }
                }
            }

            return new ChampionSkillCastModeCatalog(modes);
        }

        public bool TryGetByButtonId(string buttonId, out Mode mode)
        {
            for (int i = 0; i < Modes.Length; i++)
            {
                if (string.Equals(Modes[i].ButtonId, buttonId, StringComparison.Ordinal))
                {
                    mode = Modes[i];
                    return true;
                }
            }

            mode = default;
            return false;
        }

        public int IndexOfButton(string? buttonId)
        {
            if (string.IsNullOrEmpty(buttonId))
            {
                return -1;
            }

            for (int i = 0; i < Modes.Length; i++)
            {
                if (string.Equals(Modes[i].ButtonId, buttonId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public string? ResolveActiveButtonId(GameEngine engine)
        {
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity rep))
            {
                return null;
            }

            StringIntRegistry registry = engine.GetService(CoreServiceKeys.CastModeIdRegistry)
                ?? throw new InvalidOperationException("ChampionSkillSandbox cast modes require CastModeIdRegistry.");
            if (!engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw new InvalidOperationException(
                    "ChampionSkillSandbox cast modes require InteractionPref on the possessed rep.");
            }

            if (pref.DefaultCastModeId <= 0)
            {
                throw new InvalidOperationException(
                    "ChampionSkillSandbox cast mode is not seeded on the possessed rep.");
            }

            string name = registry.GetName(pref.DefaultCastModeId);
            for (int i = 0; i < Modes.Length; i++)
            {
                if (string.Equals(Modes[i].CastModeId, name, StringComparison.Ordinal))
                {
                    return Modes[i].ButtonId;
                }
            }

            throw new InvalidOperationException(
                $"ChampionSkillSandbox possessed rep cast mode '{name}' is not listed in {Uri}.");
        }

        private static string RequireToken(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{field} must be a non-empty token without surrounding whitespace.");
            }

            return value;
        }

        private sealed class FileDto
        {
            public ModeDto[]? Modes { get; set; }
        }

        private sealed class ModeDto
        {
            public string? ButtonId { get; set; }
            public string? Label { get; set; }
            public string? ActionId { get; set; }
            public string? CastModeId { get; set; }
            public string? Accent { get; set; }
        }
    }
}
