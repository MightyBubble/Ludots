using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Engine;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Config
{
    /// <summary>
    /// Input-side collection key declarations (constitution §08: key semantics live in data,
    /// the engine holds no builtin key table). One string per named role; ids are resolved
    /// against the EntityCollectionStore key registry at load time and injected into the
    /// consumers (CollectionApplier, hover channel, shared graphs). Games and mods merge this
    /// file through ConfigPipeline to rename keys or add their own alongside.
    /// </summary>
    public sealed class InputCollectionKeyDeclarations
    {
        /// <summary>
        /// Authored stand-in for the command-source collection. It is not itself a collection
        /// name: load substitutes the merged <c>commandSource</c> value from
        /// Input/collection_keys.json. Shared graphs, cameras, and presenters author this token
        /// so they do not hard-code a game's key. An unbound token fails at that load site.
        /// </summary>
        public const string CommandSourceRole = "commandSource";

        public string CastRaw { get; set; } = string.Empty;
        public string Hover { get; set; } = string.Empty;
        public string CommandSource { get; set; } = string.Empty;

        public int CastRawKeyId { get; private set; }
        public int HoverKeyId { get; private set; }
        public int CommandSourceKeyId { get; private set; }

        public static InputCollectionKeyDeclarations LoadAndRegister(
            global::Ludots.Core.Config.ConfigPipeline configs,
            StringIntRegistry keyRegistry,
            global::Ludots.Core.Config.ConfigCatalog catalog = null,
            global::Ludots.Core.Config.ConfigConflictReport report = null)
        {
            var entry = global::Ludots.Core.Config.ConfigPipeline.RequireEntry(catalog, "Input/collection_keys.json", global::Ludots.Core.Config.ConfigMergePolicy.DeepObject);
            var mergedObject = configs.MergeDeepObjectFromCatalog(in entry, report)
                ?? throw new InvalidOperationException(
                    "INPUT.COLLECTION_KEYS.ERR.FileMissing: Input/collection_keys.json must declare the input collection key roles (constitution §08: no builtin key fallback).");

            var declarations = mergedObject.Deserialize<InputCollectionKeyDeclarations>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("INPUT.COLLECTION_KEYS.ERR.DeserializeFailed: Input/collection_keys.json did not deserialize.");

            declarations.CastRawKeyId = RegisterRequired(keyRegistry, declarations.CastRaw, "castRaw");
            declarations.HoverKeyId = RegisterRequired(keyRegistry, declarations.Hover, "hover");
            declarations.CommandSourceKeyId = RegisterRequired(keyRegistry, declarations.CommandSource, CommandSourceRole);
            return declarations;
        }

        public static string RequireCommandSourceKey(GameEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            return RequireCommandSourceKey(engine.GlobalContext);
        }

        public static string RequireCommandSourceKey(IReadOnlyDictionary<string, object> globals)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.InputCollectionKeys.Name, out object? raw) &&
                raw is InputCollectionKeyDeclarations declarations &&
                !string.IsNullOrWhiteSpace(declarations.CommandSource))
            {
                return declarations.CommandSource;
            }

            throw new InvalidOperationException(
                "INPUT.COLLECTION_KEYS.ERR.CommandSourceUnbound: Input/collection_keys.json role 'commandSource' is not bound.");
        }

        public static string ResolveAuthoredCollectionKey(string? authored, string? boundCommandSource, string site)
        {
            if (string.IsNullOrEmpty(authored) ||
                !string.Equals(authored, CommandSourceRole, StringComparison.Ordinal))
            {
                return authored ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(boundCommandSource))
            {
                throw new InvalidOperationException(
                    $"INPUT.COLLECTION_KEYS.ERR.CommandSourceUnbound: {site} uses collection role '{CommandSourceRole}', but Input/collection_keys.json did not bind it.");
            }

            return boundCommandSource;
        }

        private static int RegisterRequired(StringIntRegistry keyRegistry, string key, string role)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"INPUT.COLLECTION_KEYS.ERR.MissingRole: Input/collection_keys.json must declare '{role}' (constitution §08: no builtin key fallback).");
            }

            return keyRegistry.Register(key);
        }
    }
}
