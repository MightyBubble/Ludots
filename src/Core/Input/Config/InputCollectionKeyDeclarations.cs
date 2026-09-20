using System;
using System.Text.Json;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Config
{
    /// <summary>
    /// Input-side collection key declarations (constitution §08: key semantics live in data,
    /// the engine holds no builtin key table). One string per named role; ids are resolved
    /// against the EntityCollectionStore key registry at load time and injected into the
    /// consumers (CollectionApplier, aim runtime, hover channel). Games and mods merge this
    /// file through ConfigPipeline to rename keys or add their own alongside.
    /// </summary>
    public sealed class InputCollectionKeyDeclarations
    {
        public string CastRaw { get; set; } = string.Empty;
        public string Hover { get; set; } = string.Empty;
        public string AbilityAimHover { get; set; } = string.Empty;
        public string AbilityAimAffected { get; set; } = string.Empty;

        public int CastRawKeyId { get; private set; }
        public int HoverKeyId { get; private set; }
        public int AbilityAimHoverKeyId { get; private set; }
        public int AbilityAimAffectedKeyId { get; private set; }

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
            declarations.AbilityAimHoverKeyId = RegisterRequired(keyRegistry, declarations.AbilityAimHover, "abilityAimHover");
            declarations.AbilityAimAffectedKeyId = RegisterRequired(keyRegistry, declarations.AbilityAimAffected, "abilityAimAffected");
            return declarations;
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
