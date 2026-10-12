using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Runtime
{
    /// <summary>
    /// Cast-style buttons inject the same actions as the F-keys.
    /// <c>graph.aim.set_cast_mode</c> writes the cast mode. This showcase's
    /// <c>graph.interaction.leave_context_scored</c> listens on those same actions:
    /// it leaves <c>context_scored</c> and switches to the tactical camera.
    /// WoW and LoL both inject <c>Aim.CastMode.Smart</c>; the catalog marks LoL
    /// as the button that represents that mode because the two styles resolve the same way.
    /// </summary>
    internal static class InteractionShowCastModes
    {
        public const string CatalogUri = "InteractionShowcaseMod:assets/UI/aim_pref_toolbar.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

        private static Catalog? _catalog;

        public static void Activate(GameEngine engine, string buttonId)
        {
            Mode mode = RequireCatalog(engine).RequireButton(buttonId);
            ResolveHandler(engine, mode.ActionId).InjectButtonPress(mode.ActionId);
        }

        public static string GetActive(GameEngine engine)
        {
            Catalog catalog = RequireCatalog(engine);
            Entity rep = RequireRep(engine);
            Mode? contextMode = catalog.ActiveContextMode(engine, rep);
            if (contextMode != null)
            {
                return contextMode.Value.ButtonId;
            }

            string prefValue = ReadPrefValue(engine, rep);
            return catalog.RepresentativeForPref(prefValue).ButtonId;
        }

        public static string GetDisplayName(GameEngine engine, string buttonId)
        {
            return RequireCatalog(engine).RequireButton(buttonId).Label;
        }

        private static Catalog RequireCatalog(GameEngine engine)
        {
            return _catalog ??= Catalog.Load(engine);
        }

        private static Entity RequireRep(GameEngine engine)
        {
            if (!InteractionShowcaseRuntime.TryGetShowcaseLocalPlayerRep(engine, out Entity rep))
            {
                throw new InvalidOperationException(
                    "Interaction showcase cast modes require a local seat possessing the hero player rep.");
            }

            return rep;
        }

        private static string ReadPrefValue(GameEngine engine, Entity rep)
        {
            InteractionPrefCatalog catalog = engine.GetService(CoreServiceKeys.InteractionPrefCatalog)
                ?? throw new InvalidOperationException("Interaction showcase preference buttons require InteractionPrefCatalog.");
            if (!engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw new InvalidOperationException(
                    "Interaction showcase preference buttons require InteractionPref on the possessed rep.");
            }

            const string prefKey = "pref.aim.cast_mode";
            int keyId = catalog.Keys.GetId(prefKey);
            if (keyId == catalog.Keys.InvalidId)
            {
                throw new InvalidOperationException($"Interaction showcase preference key '{prefKey}' is not declared.");
            }

            int valueId = pref.ResolvePref(0, keyId);
            if (valueId <= 0)
            {
                throw new InvalidOperationException(
                    $"Interaction showcase preference '{prefKey}' is not seeded on the possessed rep.");
            }

            string name = catalog.Values.GetName(valueId);
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException(
                    $"Interaction showcase preference '{prefKey}' value id {valueId} is not registered.");
            }

            return name;
        }

        private static PlayerInputHandler ResolveHandler(GameEngine engine, string actionId)
        {
            ClientLocalSeatRegistry seats = engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
                ?? throw new InvalidOperationException(
                    "Interaction showcase cast modes require ClientLocalSeatRegistry.");
            PlayerInputHandler? handler = null;
            if (seats.Count > 1)
            {
                ClientLocalSeatInputRuntime seatInput = engine.GetService(CoreServiceKeys.ClientLocalSeatInputRuntime)
                    ?? throw new InvalidOperationException(
                        "Interaction showcase cast modes require ClientLocalSeatInputRuntime.");
                IReadOnlyList<string> seatIds = seats.SeatIds;
                for (int i = 0; i < seatIds.Count; i++)
                {
                    ClientLocalSeat seat = seats.Require(seatIds[i]);
                    if (!seat.HasPossession)
                    {
                        continue;
                    }

                    if (!seatInput.TryGetChannel(seat.SeatId, out ClientLocalSeatInputChannel channel))
                    {
                        throw new InvalidOperationException(
                            $"Interaction showcase cast mode action '{actionId}' cannot attribute to seat '{seat.SeatId}'.");
                    }

                    handler = channel.Handler;
                    break;
                }
            }
            else
            {
                handler = engine.GetService(CoreServiceKeys.InputHandler);
            }

            if (handler == null)
            {
                throw new InvalidOperationException(
                    $"Interaction showcase cast mode action '{actionId}' has no input handler.");
            }

            if (!handler.HasAction(actionId))
            {
                throw new InvalidOperationException(
                    $"Interaction showcase cast mode action '{actionId}' is not a declared input action.");
            }

            return handler;
        }

        private readonly record struct Mode(
            string ButtonId,
            string Label,
            string ActionId,
            string? PrefValue,
            bool RepresentsPref,
            string? ContextId);

        private sealed class Catalog
        {
            private Catalog(Mode[] modes)
            {
                Modes = modes;
            }

            public Mode[] Modes { get; }

            public static Catalog Load(GameEngine engine)
            {
                using Stream stream = engine.VFS.GetStream(CatalogUri);
                FileDto? file = JsonSerializer.Deserialize<FileDto>(stream, JsonOptions);
                if (file?.Modes == null || file.Modes.Length == 0)
                {
                    throw new InvalidOperationException($"{CatalogUri} must list at least one cast mode.");
                }

                var modes = new Mode[file.Modes.Length];
                for (int i = 0; i < file.Modes.Length; i++)
                {
                    ModeDto dto = file.Modes[i] ?? throw new InvalidOperationException($"{CatalogUri} modes[{i}] is missing.");
                    bool hasPref = !string.IsNullOrWhiteSpace(dto.PrefValue);
                    bool hasContext = !string.IsNullOrWhiteSpace(dto.ContextId);
                    if (hasPref == hasContext)
                    {
                        throw new InvalidOperationException(
                            $"{CatalogUri} modes[{i}] must declare exactly one of prefValue or contextId.");
                    }

                    if (hasPref && dto.RepresentsPref == null)
                    {
                        throw new InvalidOperationException(
                            $"{CatalogUri} modes[{i}] must declare representsPref.");
                    }

                    if (hasContext && dto.RepresentsPref != null)
                    {
                        throw new InvalidOperationException(
                            $"{CatalogUri} modes[{i}] context row must not declare representsPref.");
                    }

                    modes[i] = new Mode(
                        RequireToken(dto.ButtonId, $"{CatalogUri} modes[{i}].buttonId"),
                        RequireToken(dto.Label, $"{CatalogUri} modes[{i}].label"),
                        RequireToken(dto.ActionId, $"{CatalogUri} modes[{i}].actionId"),
                        hasPref ? RequireToken(dto.PrefValue, $"{CatalogUri} modes[{i}].prefValue") : null,
                        dto.RepresentsPref ?? false,
                        hasContext ? RequireToken(dto.ContextId, $"{CatalogUri} modes[{i}].contextId") : null);
                    for (int earlier = 0; earlier < i; earlier++)
                    {
                        bool samePref = modes[i].PrefValue != null &&
                            string.Equals(modes[earlier].PrefValue, modes[i].PrefValue, StringComparison.Ordinal);
                        bool sameAction = string.Equals(modes[earlier].ActionId, modes[i].ActionId, StringComparison.Ordinal);
                        if (string.Equals(modes[earlier].ButtonId, modes[i].ButtonId, StringComparison.Ordinal) ||
                            (modes[i].ContextId != null &&
                             string.Equals(modes[earlier].ContextId, modes[i].ContextId, StringComparison.Ordinal)) ||
                            (sameAction && !samePref))
                        {
                            throw new InvalidOperationException(
                                $"{CatalogUri} modes[{i}] repeats a button or context, or reuses an action for a different cast mode.");
                        }
                    }
                }

                for (int i = 0; i < modes.Length; i++)
                {
                    if (modes[i].PrefValue == null)
                    {
                        continue;
                    }

                    int representatives = 0;
                    for (int other = 0; other < modes.Length; other++)
                    {
                        if (string.Equals(modes[other].PrefValue, modes[i].PrefValue, StringComparison.Ordinal) &&
                            modes[other].RepresentsPref)
                        {
                            representatives++;
                        }
                    }

                    if (representatives != 1)
                    {
                        throw new InvalidOperationException(
                            $"{CatalogUri} cast mode '{modes[i].PrefValue}' must have exactly one representsPref row.");
                    }
                }

                return new Catalog(modes);
            }

            public Mode RequireButton(string buttonId)
            {
                for (int i = 0; i < Modes.Length; i++)
                {
                    if (string.Equals(Modes[i].ButtonId, buttonId, StringComparison.Ordinal))
                    {
                        return Modes[i];
                    }
                }

                throw new InvalidOperationException($"{CatalogUri} has no button '{buttonId}'.");
            }

            public Mode RepresentativeForPref(string prefValue)
            {
                Mode? found = null;
                for (int i = 0; i < Modes.Length; i++)
                {
                    if (!string.Equals(Modes[i].PrefValue, prefValue, StringComparison.Ordinal) ||
                        !Modes[i].RepresentsPref)
                    {
                        continue;
                    }

                    if (found != null)
                    {
                        throw new InvalidOperationException(
                            $"{CatalogUri} cast mode '{prefValue}' has more than one representative button.");
                    }

                    found = Modes[i];
                }

                if (found == null)
                {
                    throw new InvalidOperationException(
                        $"{CatalogUri} does not represent cast mode '{prefValue}'.");
                }

                return found.Value;
            }

            public Mode? ActiveContextMode(GameEngine engine, Entity rep)
            {
                InteractionContextInstanceRuntime runtime = engine.GetService(CoreServiceKeys.InteractionContextInstances)
                    ?? throw new InvalidOperationException(
                        "Interaction showcase cast modes require InteractionContextInstances.");
                InteractionContextProfileRegistry profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                    ?? throw new InvalidOperationException(
                        "Interaction showcase cast modes require InteractionContextProfileRegistry.");
                Mode? active = null;
                for (int i = 0; i < Modes.Length; i++)
                {
                    string? contextId = Modes[i].ContextId;
                    if (contextId == null)
                    {
                        continue;
                    }

                    if (!runtime.IsActive(rep, profiles.ProfileIdRegistry.GetId(contextId)))
                    {
                        continue;
                    }

                    if (active != null)
                    {
                        throw new InvalidOperationException(
                            $"Interaction showcase rep carries both '{active.Value.ContextId}' and '{Modes[i].ContextId}'.");
                    }

                    active = Modes[i];
                }

                return active;
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
                public string? PrefValue { get; set; }
                public bool? RepresentsPref { get; set; }
                public string? ContextId { get; set; }
            }
        }
    }
}
