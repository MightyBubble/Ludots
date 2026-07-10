using System;
using System.Collections.Generic;
using System.Text.Json;
using Ludots.Core.Config;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/cast_commit_locks.json</c> (RFC-0065 CTX-8, §5.6 lock semantics).
    /// Follows the <c>CastCommitProfileConfigLoader</c> mounting pattern: catalog-declared
    /// DeepObject merge through the shared <see cref="ConfigPipeline"/>. Structural validation is
    /// here; cast commit id resolution (fail fast on uninstalled ids) happens at
    /// <see cref="ClientCastPreferenceStore.InstallLocks"/>.
    /// </summary>
    public sealed class ClientCastPreferenceConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public ClientCastPreferenceConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Load and validate the merged cast commit lock config.</summary>
        public CastCommitLocksConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Input/cast_commit_locks.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null)
            {
                throw new InvalidOperationException($"Missing required config '{relativePath}'.");
            }

            var config = mergedObject.Deserialize<CastCommitLocksConfig>(JsonOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize '{relativePath}'.");
            Validate(config, relativePath);
            return config;
        }

        /// <summary>Structural fail-fast validation of the lock declarations.</summary>
        public static void Validate(CastCommitLocksConfig config, string source)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.Locks == null)
            {
                throw new InvalidOperationException($"Cast commit lock config '{source}' must explicitly define locks.");
            }

            for (int i = 0; i < config.Locks.Count; i++)
            {
                CastCommitLockDefinition lockDefinition = config.Locks[i]
                    ?? throw new InvalidOperationException($"{source}.locks[{i}] must be an object.");
                string path = $"{source}.locks[{i}]";
                RequireTrimmedNonEmpty(lockDefinition.CastCommitId, $"{path}.castCommitId");
                switch (lockDefinition.Scope)
                {
                    case CastPreferenceScopeNames.Global:
                        if (!string.IsNullOrEmpty(lockDefinition.Key))
                        {
                            throw new InvalidOperationException($"{path}.key must be empty for scope 'global'.");
                        }

                        break;
                    case CastPreferenceScopeNames.Template:
                    case CastPreferenceScopeNames.FormSet:
                        RequireTrimmedNonEmpty(lockDefinition.Key, $"{path}.key");
                        break;
                    case CastPreferenceScopeNames.Slot:
                        RequireTrimmedNonEmpty(lockDefinition.Key, $"{path}.key");
                        ClientCastPreferenceStore.SplitSlotKey(lockDefinition.Key, $"{path}.key", out _, out _);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"{path}.scope must be one of '{CastPreferenceScopeNames.Global}', " +
                            $"'{CastPreferenceScopeNames.Template}', '{CastPreferenceScopeNames.FormSet}', " +
                            $"'{CastPreferenceScopeNames.Slot}'; got '{lockDefinition.Scope}'.");
                }
            }
        }

        private static void RequireTrimmedNonEmpty(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{path} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path} must not contain leading or trailing whitespace.");
            }
        }
    }
}
