using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// ClientCastPreference scope chain store (RFC-0065 CTX-8, §5.6, DEC-15). Holds the local
    /// player's cast commit preferences at four scopes plus the mod-declared lock set from
    /// <c>Input/cast_commit_locks.json</c>. Resolution is perSlot &gt; perFormSet &gt; perTemplate &gt;
    /// global, and a lock at any scope overrides every player layer (most specific lock wins).
    /// Cast commit ids live in <see cref="CastCommitProfileRegistry.ProfileIdRegistry"/>; template
    /// and form set ids resolve through injected key resolvers so the store shares the caller id
    /// spaces (<c>EntityTemplateKeyRegistry</c> / <c>AbilityFormSetIdRegistry</c>). Steady-state
    /// resolution is allocation free. Persistence follows the
    /// <c>InputOrderMappingSystem.SaveUserPreferences</c> file pattern (<c>user://</c> expansion,
    /// pretty-printed camelCase JSON). No UI is wired here — settings screens call
    /// <see cref="TrySetPreference"/> / <see cref="Save"/> directly.
    /// </summary>
    public sealed class ClientCastPreferenceStore
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

        private readonly CastCommitProfileRegistry _castCommitProfiles;
        private readonly PreferenceScopeKeyResolver _templateKeyResolver;
        private readonly PreferenceScopeKeyName _templateKeyName;
        private readonly PreferenceScopeKeyResolver _formSetKeyResolver;
        private readonly PreferenceScopeKeyName _formSetKeyName;

        private readonly Dictionary<int, int> _perTemplate = new();
        private readonly Dictionary<int, int> _perFormSet = new();
        private readonly Dictionary<long, int> _perSlot = new();
        private int _global;

        private readonly Dictionary<int, int> _templateLocks = new();
        private readonly Dictionary<int, int> _formSetLocks = new();
        private readonly Dictionary<long, int> _slotLocks = new();
        private int _globalLock;

        private string _activeSchemeId = string.Empty;

        public ClientCastPreferenceStore(
            CastCommitProfileRegistry castCommitProfiles,
            PreferenceScopeKeyResolver templateKeyResolver,
            PreferenceScopeKeyName templateKeyName,
            PreferenceScopeKeyResolver formSetKeyResolver,
            PreferenceScopeKeyName formSetKeyName)
        {
            _castCommitProfiles = castCommitProfiles ?? throw new ArgumentNullException(nameof(castCommitProfiles));
            _templateKeyResolver = templateKeyResolver ?? throw new ArgumentNullException(nameof(templateKeyResolver));
            _templateKeyName = templateKeyName ?? throw new ArgumentNullException(nameof(templateKeyName));
            _formSetKeyResolver = formSetKeyResolver ?? throw new ArgumentNullException(nameof(formSetKeyResolver));
            _formSetKeyName = formSetKeyName ?? throw new ArgumentNullException(nameof(formSetKeyName));
        }

        /// <summary>Bumped on every preference/lock/scheme mutation; consumers invalidate on change.</summary>
        public uint Revision { get; private set; }

        /// <summary>
        /// Active control scheme name persisted alongside cast preferences (RFC-0065 INT-5).
        /// Written by <see cref="ControlSchemeRuntime.TrySwitch"/>; empty = never switched.
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
        /// Resolve the effective cast commit profile id for a (template, form set, slot) triple.
        /// Locks override every player layer; within each side the most specific scope wins.
        /// Returns 0 when neither locks nor preferences declare a value (the caller's mod default
        /// applies — the store never invents one).
        /// </summary>
        public int ResolveCastCommit(int templateId, int formSetId, int slotIndex)
        {
            if (_slotLocks.TryGetValue(SlotKey(templateId, slotIndex), out int locked) ||
                _formSetLocks.TryGetValue(formSetId, out locked) ||
                _templateLocks.TryGetValue(templateId, out locked))
            {
                return locked;
            }

            if (_globalLock != 0)
            {
                return _globalLock;
            }

            if (_perSlot.TryGetValue(SlotKey(templateId, slotIndex), out int preferred) ||
                _perFormSet.TryGetValue(formSetId, out preferred) ||
                _perTemplate.TryGetValue(templateId, out preferred))
            {
                return preferred;
            }

            return _global;
        }

        /// <summary>
        /// Write a player preference at a scope. <paramref name="castCommitId"/> = 0 clears the
        /// entry; non-zero ids must be installed (fail fast — an uninstalled id is a wiring error,
        /// not a player mistake). Returns false when a mod lock pins that exact scope key: locks
        /// win in resolution regardless, so the refusal is the UI signal ("显示锁定"), never a
        /// silent divergence. Bumps <see cref="Revision"/> on success.
        /// </summary>
        public bool TrySetPreference(CastPreferenceScope scope, int templateId, int formSetId, int slotIndex, int castCommitId)
        {
            if (castCommitId != 0 && !_castCommitProfiles.IsInstalled(castCommitId))
            {
                throw new InvalidOperationException(
                    $"Cast preference references cast commit profile id {castCommitId} which is not installed.");
            }

            if (IsLocked(scope, templateId, formSetId, slotIndex))
            {
                return false;
            }

            switch (scope)
            {
                case CastPreferenceScope.Global:
                    _global = castCommitId;
                    break;
                case CastPreferenceScope.PerTemplate:
                    WriteEntry(_perTemplate, templateId, castCommitId);
                    break;
                case CastPreferenceScope.PerFormSet:
                    WriteEntry(_perFormSet, formSetId, castCommitId);
                    break;
                case CastPreferenceScope.PerSlot:
                    WriteEntry(_perSlot, SlotKey(templateId, slotIndex), castCommitId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scope));
            }

            Revision++;
            return true;
        }

        /// <summary>True when a mod lock pins the exact scope key (settings UI shows the lock).</summary>
        public bool IsLocked(CastPreferenceScope scope, int templateId, int formSetId, int slotIndex)
        {
            return scope switch
            {
                CastPreferenceScope.Global => _globalLock != 0,
                CastPreferenceScope.PerTemplate => _templateLocks.ContainsKey(templateId),
                CastPreferenceScope.PerFormSet => _formSetLocks.ContainsKey(formSetId),
                CastPreferenceScope.PerSlot => _slotLocks.ContainsKey(SlotKey(templateId, slotIndex)),
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            };
        }

        /// <summary>
        /// Install the mod lock set. Fails fast on unknown scopes, malformed keys, duplicate locks,
        /// and cast commit ids that are not installed.
        /// </summary>
        public void InstallLocks(CastCommitLocksConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            ClientCastPreferenceConfigLoader.Validate(config, nameof(CastCommitLocksConfig));
            for (int i = 0; i < config.Locks.Count; i++)
            {
                CastCommitLockDefinition lockDefinition = config.Locks[i];
                int castCommitId = RequireInstalledCastCommit(lockDefinition.CastCommitId, $"locks[{i}]");
                switch (lockDefinition.Scope)
                {
                    case CastPreferenceScopeNames.Global:
                        if (_globalLock != 0)
                        {
                            throw new InvalidOperationException("Duplicate global cast commit lock.");
                        }

                        _globalLock = castCommitId;
                        break;
                    case CastPreferenceScopeNames.Template:
                        AddLock(_templateLocks, _templateKeyResolver(lockDefinition.Key), castCommitId, lockDefinition.Key, i);
                        break;
                    case CastPreferenceScopeNames.FormSet:
                        AddLock(_formSetLocks, _formSetKeyResolver(lockDefinition.Key), castCommitId, lockDefinition.Key, i);
                        break;
                    case CastPreferenceScopeNames.Slot:
                        SplitSlotKey(lockDefinition.Key, $"locks[{i}].key", out string templateKey, out int slotIndex);
                        AddLock(_slotLocks, SlotKey(_templateKeyResolver(templateKey), slotIndex), castCommitId, lockDefinition.Key, i);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Cast commit lock locks[{i}] declares unknown scope '{lockDefinition.Scope}'.");
                }
            }

            Revision++;
        }

        /// <summary>
        /// Persist player preferences (and the active scheme id) as JSON. Follows the
        /// <c>InputOrderMappingSystem</c> persistence pattern: <c>user://</c> expands to the
        /// per-user Ludots data directory, parent directories are created on demand.
        /// </summary>
        public void Save(string path)
        {
            string effectivePath = ExpandUserPath(path);
            var file = new ClientCastPreferenceFile
            {
                Global = _global != 0 ? new CastPreferenceEntry { CastCommitId = CastCommitName(_global) } : null,
                PerTemplate = SnapshotEntries(_perTemplate, _templateKeyName),
                PerFormSet = SnapshotEntries(_perFormSet, _formSetKeyName),
                PerSlot = SnapshotSlotEntries(),
                ActiveSchemeId = string.IsNullOrEmpty(_activeSchemeId) ? null : _activeSchemeId,
            };

            string json = JsonSerializer.Serialize(file, JsonWriteOptions);
            string directory = System.IO.Path.GetDirectoryName(effectivePath);
            if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(effectivePath, json);
        }

        /// <summary>
        /// Load player preferences from JSON, replacing every player layer (locks are untouched —
        /// they are mod data, never persisted per player). Unknown cast commit ids fail fast;
        /// entries pinned by a lock are dropped (the lock wins either way). A missing file is an
        /// empty preference set. Bumps <see cref="Revision"/>.
        /// </summary>
        public void Load(string path)
        {
            string effectivePath = ExpandUserPath(path);
            _global = 0;
            _perTemplate.Clear();
            _perFormSet.Clear();
            _perSlot.Clear();
            _activeSchemeId = string.Empty;
            Revision++;

            if (!System.IO.File.Exists(effectivePath))
            {
                return;
            }

            var file = JsonSerializer.Deserialize<ClientCastPreferenceFile>(
                System.IO.File.ReadAllText(effectivePath), JsonOptions);
            if (file == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(file.Global?.CastCommitId))
            {
                TrySetPreference(CastPreferenceScope.Global, 0, 0, 0,
                    RequireInstalledCastCommit(file.Global.CastCommitId, "global"));
            }

            if (file.PerTemplate != null)
            {
                foreach (KeyValuePair<string, CastPreferenceEntry> entry in file.PerTemplate)
                {
                    if (string.IsNullOrWhiteSpace(entry.Value?.CastCommitId))
                    {
                        continue;
                    }

                    TrySetPreference(CastPreferenceScope.PerTemplate, _templateKeyResolver(entry.Key), 0, 0,
                        RequireInstalledCastCommit(entry.Value.CastCommitId, $"perTemplate['{entry.Key}']"));
                }
            }

            if (file.PerFormSet != null)
            {
                foreach (KeyValuePair<string, CastPreferenceEntry> entry in file.PerFormSet)
                {
                    if (string.IsNullOrWhiteSpace(entry.Value?.CastCommitId))
                    {
                        continue;
                    }

                    TrySetPreference(CastPreferenceScope.PerFormSet, 0, _formSetKeyResolver(entry.Key), 0,
                        RequireInstalledCastCommit(entry.Value.CastCommitId, $"perFormSet['{entry.Key}']"));
                }
            }

            if (file.PerSlot != null)
            {
                foreach (KeyValuePair<string, CastPreferenceEntry> entry in file.PerSlot)
                {
                    if (string.IsNullOrWhiteSpace(entry.Value?.CastCommitId))
                    {
                        continue;
                    }

                    SplitSlotKey(entry.Key, $"perSlot['{entry.Key}']", out string templateKey, out int slotIndex);
                    TrySetPreference(CastPreferenceScope.PerSlot, _templateKeyResolver(templateKey), 0, slotIndex,
                        RequireInstalledCastCommit(entry.Value.CastCommitId, $"perSlot['{entry.Key}']"));
                }
            }

            if (!string.IsNullOrWhiteSpace(file.ActiveSchemeId))
            {
                SetActiveScheme(file.ActiveSchemeId.Trim());
            }
        }

        private static long SlotKey(int templateId, int slotIndex)
        {
            return ((long)templateId << 32) | (uint)slotIndex;
        }

        private static void WriteEntry(Dictionary<int, int> entries, int key, int castCommitId)
        {
            if (castCommitId == 0)
            {
                entries.Remove(key);
            }
            else
            {
                entries[key] = castCommitId;
            }
        }

        private static void WriteEntry(Dictionary<long, int> entries, long key, int castCommitId)
        {
            if (castCommitId == 0)
            {
                entries.Remove(key);
            }
            else
            {
                entries[key] = castCommitId;
            }
        }

        private static void AddLock(Dictionary<int, int> locks, int keyId, int castCommitId, string key, int index)
        {
            if (!locks.TryAdd(keyId, castCommitId))
            {
                throw new InvalidOperationException(
                    $"Cast commit lock locks[{index}] duplicates the lock for key '{key}'.");
            }
        }

        private static void AddLock(Dictionary<long, int> locks, long keyId, int castCommitId, string key, int index)
        {
            if (!locks.TryAdd(keyId, castCommitId))
            {
                throw new InvalidOperationException(
                    $"Cast commit lock locks[{index}] duplicates the lock for key '{key}'.");
            }
        }

        private int RequireInstalledCastCommit(string castCommitId, string path)
        {
            if (!_castCommitProfiles.ProfileIdRegistry.TryGetId(castCommitId, out int id) ||
                !_castCommitProfiles.IsInstalled(id))
            {
                throw new InvalidOperationException(
                    $"Cast preference {path} references cast commit profile '{castCommitId}' which is not installed.");
            }

            return id;
        }

        internal static void SplitSlotKey(string key, string path, out string templateKey, out int slotIndex)
        {
            int separator = key?.LastIndexOf('/') ?? -1;
            if (separator <= 0 || separator >= key.Length - 1 ||
                !int.TryParse(key[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out slotIndex))
            {
                throw new InvalidOperationException(
                    $"{path} must use the '<templateKey>/<slotIndex>' slot key format; got '{key}'.");
            }

            templateKey = key[..separator];
        }

        private string CastCommitName(int castCommitId)
        {
            return _castCommitProfiles.ProfileIdRegistry.GetName(castCommitId);
        }

        private Dictionary<string, CastPreferenceEntry> SnapshotEntries(Dictionary<int, int> entries, PreferenceScopeKeyName keyName)
        {
            if (entries.Count == 0)
            {
                return null;
            }

            var snapshot = new Dictionary<string, CastPreferenceEntry>(entries.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<int, int> entry in entries)
            {
                snapshot[keyName(entry.Key)] = new CastPreferenceEntry { CastCommitId = CastCommitName(entry.Value) };
            }

            return snapshot;
        }

        private Dictionary<string, CastPreferenceEntry> SnapshotSlotEntries()
        {
            if (_perSlot.Count == 0)
            {
                return null;
            }

            var snapshot = new Dictionary<string, CastPreferenceEntry>(_perSlot.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<long, int> entry in _perSlot)
            {
                int templateId = (int)(entry.Key >> 32);
                int slotIndex = (int)(entry.Key & 0xFFFFFFFF);
                string key = _templateKeyName(templateId) + "/" + slotIndex.ToString(CultureInfo.InvariantCulture);
                snapshot[key] = new CastPreferenceEntry { CastCommitId = CastCommitName(entry.Value) };
            }

            return snapshot;
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
    }
}
