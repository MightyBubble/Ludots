using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Persists the player's active control scheme id. Cast mode and command intent live on
    /// <see cref="InteractionPref"/>; this store does not keep a second preference chain.
    /// </summary>
    public sealed class ClientControlSchemePreferenceStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly string _persistencePath;
        private string _activeSchemeId = string.Empty;

        public ClientControlSchemePreferenceStore(string persistencePath)
        {
            if (string.IsNullOrWhiteSpace(persistencePath))
            {
                throw new ArgumentException("Preference persistence path is required.", nameof(persistencePath));
            }

            _persistencePath = persistencePath;
        }

        /// <summary>Path bound at construction. <see cref="Save()"/> writes this path.</summary>
        public string PersistencePath => _persistencePath;

        /// <summary>Bumped on every scheme mutation; consumers invalidate on change.</summary>
        public uint Revision { get; private set; }

        /// <summary>
        /// Active control scheme name. Written by <see cref="ControlSchemeRuntime.TrySwitch"/>;
        /// empty means the player has never switched.
        /// </summary>
        public string ActiveSchemeId => _activeSchemeId;

        /// <summary>Record the active control scheme name (persisted by <see cref="Save"/>).</summary>
        public void SetActiveScheme(string schemeId)
        {
            if (string.IsNullOrWhiteSpace(schemeId))
            {
                throw new ArgumentException("Active scheme id is required.", nameof(schemeId));
            }

            if (!string.Equals(schemeId, schemeId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Active scheme id must not contain leading or trailing whitespace.",
                    nameof(schemeId));
            }

            if (string.Equals(_activeSchemeId, schemeId, StringComparison.Ordinal))
            {
                return;
            }

            _activeSchemeId = schemeId;
            Revision++;
        }

        /// <summary>Persist the active scheme id to <see cref="PersistencePath"/>.</summary>
        public void Save()
        {
            Save(_persistencePath);
        }

        /// <summary>
        /// Persist the active scheme id as JSON. Parent directories are created on demand.
        /// <c>user://</c> is rejected here: expand it against the host user data root first.
        /// Saving before any scheme is active fails; the missing-file state is only produced
        /// by never having saved.
        /// </summary>
        public void Save(string path)
        {
            string effectivePath = RequireExpandedPath(path);
            if (string.IsNullOrEmpty(_activeSchemeId))
            {
                throw new InvalidOperationException(
                    $"Scheme preference '{effectivePath}' cannot be saved before a scheme is active.");
            }

            var file = new ClientControlSchemePreferenceFile
            {
                ActiveSchemeId = _activeSchemeId,
            };

            string json = JsonSerializer.Serialize(file, JsonWriteOptions);
            string? directory = Path.GetDirectoryName(effectivePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(effectivePath, json);
        }

        /// <summary>
        /// Load the active scheme id, replacing the in-memory value. A missing file is the
        /// never-switched state and clears the in-memory id. A present file must contain only
        /// <c>activeSchemeId</c>, a non-empty id with no surrounding whitespace. Bumps
        /// <see cref="Revision"/> on a successful load or a missing file; a rejected file leaves
        /// the in-memory id unchanged.
        /// </summary>
        public void Load(string path)
        {
            string effectivePath = RequireExpandedPath(path);
            if (!File.Exists(effectivePath))
            {
                _activeSchemeId = string.Empty;
                Revision++;
                return;
            }

            ClientControlSchemePreferenceFile? file;
            try
            {
                file = JsonSerializer.Deserialize<ClientControlSchemePreferenceFile>(
                    File.ReadAllText(effectivePath), JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Scheme preference '{effectivePath}' could not be read: {exception.Message}",
                    exception);
            }

            _activeSchemeId = RequireStoredSchemeId(effectivePath, file);
            Revision++;
        }

        private static string RequireStoredSchemeId(string path, ClientControlSchemePreferenceFile? file)
        {
            string? schemeId = file?.ActiveSchemeId;
            if (string.IsNullOrWhiteSpace(schemeId))
            {
                throw new InvalidOperationException(
                    $"Scheme preference '{path}' activeSchemeId must be a non-empty string.");
            }

            if (!string.Equals(schemeId, schemeId.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Scheme preference '{path}' activeSchemeId must not contain leading or trailing whitespace.");
            }

            return schemeId;
        }

        /// <summary>
        /// Resolve <c>game.json</c> <c>controlSchemePreferencePath</c>. A blank path fails by that
        /// field name. <c>user://</c> expands against <paramref name="userDataRoot"/>; a blank root
        /// fails by <c>userDataRoot</c>. Other paths are kept as given.
        /// </summary>
        public static string ResolveConfiguredPath(string? configuredPath, string userDataRoot)
        {
            if (string.IsNullOrWhiteSpace(userDataRoot))
            {
                throw new ArgumentException("userDataRoot is required.", nameof(userDataRoot));
            }

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException("game.json controlSchemePreferencePath is required.");
            }

            if (!configuredPath.StartsWith("user://", StringComparison.Ordinal))
            {
                return configuredPath;
            }

            string relative = configuredPath.Substring("user://".Length).TrimStart('/');
            if (relative.Length == 0)
            {
                throw new InvalidOperationException(
                    "game.json controlSchemePreferencePath user:// has no path after the prefix.");
            }

            return Path.Combine(userDataRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RequireExpandedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Preference persistence path is required.", nameof(path));
            }

            if (path.StartsWith("user://", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Preference path '{path}' still uses user://. Expand it against userDataRoot before saving or loading.");
            }

            return path;
        }

        private sealed class ClientControlSchemePreferenceFile
        {
            public string? ActiveSchemeId { get; set; }
        }
    }
}
