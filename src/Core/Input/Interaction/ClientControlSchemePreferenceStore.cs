using System;
using System.Text.Json;

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
        };

        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
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
            schemeId ??= string.Empty;
            if (string.Equals(_activeSchemeId, schemeId, StringComparison.Ordinal))
            {
                return;
            }

            _activeSchemeId = schemeId;
            Revision++;
        }

        /// <summary>
        /// Persist the active scheme id as JSON. <c>user://</c> expands to the per-user Ludots
        /// data directory; parent directories are created on demand.
        /// </summary>
        public void Save(string path)
        {
            string effectivePath = ExpandUserPath(path);
            var file = new ClientControlSchemePreferenceFile
            {
                ActiveSchemeId = string.IsNullOrEmpty(_activeSchemeId) ? null : _activeSchemeId,
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
        /// Load the active scheme id, replacing the in-memory value. A missing file clears it.
        /// Bumps <see cref="Revision"/>.
        /// </summary>
        public void Load(string path)
        {
            string effectivePath = ExpandUserPath(path);
            _activeSchemeId = string.Empty;
            Revision++;

            if (!File.Exists(effectivePath))
            {
                return;
            }

            var file = JsonSerializer.Deserialize<ClientControlSchemePreferenceFile>(
                File.ReadAllText(effectivePath), JsonOptions);
            if (file == null || string.IsNullOrWhiteSpace(file.ActiveSchemeId))
            {
                return;
            }

            SetActiveScheme(file.ActiveSchemeId.Trim());
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
