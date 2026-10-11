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

        private string _activeSchemeId = string.Empty;

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

        /// <summary>
        /// Persist the active scheme id as JSON. <c>user://</c> expands to the per-user Ludots
        /// data directory; parent directories are created on demand. Saving before any scheme
        /// is active fails; the missing-file state is only produced by never having saved.
        /// </summary>
        public void Save(string path)
        {
            string effectivePath = ExpandUserPath(path);
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
            string effectivePath = ExpandUserPath(path);
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

        private static string ExpandUserPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Preference persistence path is required.", nameof(path));
            }

            if (path.StartsWith("user://", StringComparison.Ordinal))
            {
                return path.Replace("user://",
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/Ludots/");
            }

            return path;
        }

        private sealed class ClientControlSchemePreferenceFile
        {
            public string? ActiveSchemeId { get; set; }
        }
    }
}
