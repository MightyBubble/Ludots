using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance
{
    [Category("ci-gate")]
    [Category("arch-guard")]
    public sealed class TerminologyGovernanceTests
    {
        [Test]
        public void ProductionCode_DoesNotJuxtaposeClientWithMachineInIdentifiers()
        {
            string repoRoot = FindRepoRoot();
            var rules = new[]
            {
                new ForbiddenPattern("MachineClient* identifier", @"\bMachineClient\w*"),
                new ForbiddenPattern("ClientMachine* identifier", @"\bClientMachine\w*"),
            };
            var hits = new List<string>();

            foreach (string file in EnumerateTerminologyScanFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                for (int i = 0; i < rules.Length; i++)
                {
                    foreach (Match match in Regex.Matches(text, rules[i].Pattern, RegexOptions.CultureInvariant))
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}: {match.Value} ({rules[i].Name})");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Terminology rule 1 (#902 3.5): 'client' must never denote a machine; identifiers must not juxtapose Client with Machine. See gitbook/architecture/terminology.md:\n" +
                string.Join("\n", hits));
        }

        /// <summary>
        /// Terminology rule 2 guard. Adapters may expose stateless device observation ports
        /// (enumeration / hot-plug; stable identity + device kind only) into the App-level
        /// container, classified explicitly by registered surface type. Writable device
        /// instance handles and device-to-Seat binding state belong to the Seat domain.
        /// The legality criterion is what the service carries — observation versus
        /// interactive state — never the key naming.
        /// </summary>
        [Test]
        public void Adapters_ExposeOnlyDeviceObservationPortsIntoAppLevelContainer()
        {
            string repoRoot = FindRepoRoot();
            var keySurfaces = FindCoreServiceKeySurfaces(ReadCoreServiceKeysSource(repoRoot));
            string[] writeFaceMembers = ReadSyntheticInputDeviceWriteFaceMembers(repoRoot);
            HashSet<string> writableDeviceTypes = FindWritableDeviceTypeNames(EnumerateWritableDeviceTypeSourceTexts(repoRoot), writeFaceMembers);
            var hits = FindObservationPortClassificationViolations(keySurfaces);

            foreach (string file in EnumerateAdapterCodeFiles(repoRoot))
            {
                hits.AddRange(FindAdapterDeviceServiceRuleViolations(
                    File.ReadAllText(file),
                    ToRepoRelativePath(repoRoot, file),
                    writableDeviceTypes,
                    DeviceServiceAllowlist));
            }

            Assert.That(
                hits,
                Is.Empty,
                "Terminology rule 2 (#902 3.5): Adapters expose device capability into the App-level container only as stateless observation ports " +
                "(enumeration / hot-plug; stable identity + device kind only), classified explicitly by registered surface type in DeviceObservationPorts; " +
                "any other Device-named service key is rejected until explicitly classified. Registering a writable device instance " +
                "(SyntheticInputDevice or a type implementing its device write face) is rejected regardless of key name — writable device handles and " +
                "device-to-Seat binding state belong to the Seat domain. The legacy SyntheticInput allowlist is shrink-only; its collapse target is " +
                "per-seat mock devices held by the Seat domain:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void DeviceServiceGuard_ClassifiesObservationPortsByRegisteredSurface()
        {
            string typedKeysSource = """
                public static readonly ServiceKey<IInputDeviceWatcher> InputDeviceWatcher = new("InputDeviceWatcher");
                """;
            Assert.That(
                FindObservationPortClassificationViolations(FindCoreServiceKeySurfaces(typedKeysSource)),
                Is.Empty,
                "An observation port declared with its port interface as the registered surface is the legal shape and must pass classification.");

            string mistypedKeysSource = """
                public static readonly ServiceKey<SyntheticInputDevice> InputDeviceWatcher = new("InputDeviceWatcher");
                """;
            var mistyped = FindObservationPortClassificationViolations(FindCoreServiceKeySurfaces(mistypedKeysSource));
            Assert.That(mistyped, Has.Count.EqualTo(1));
            Assert.That(
                mistyped[0],
                Does.Contain("ServiceKey<IInputDeviceWatcher>"),
                "Observation-port legality is bound to the registered surface type; re-typing the key to a writable device instance must go red.");
        }

        [Test]
        public void DeviceServiceGuard_DetectsSyntheticDeviceInstanceRegistrations()
        {
            var writableDeviceTypes = FindWritableDeviceTypeNames(
                new[]
                {
                    """
                    public sealed class VirtualProbeGamepad
                    {
                        public void MovePointer(float x, float y) { }
                        public void PointerDown(int button) { }
                        public void TypeText(string text) { }
                    }
                    """,
                },
                ReadSyntheticInputDeviceWriteFaceMembers(FindRepoRoot()));

            string observationPortSource = """
                var deviceWatcher = new RaylibInputDeviceWatcher();
                engine.SetService(CoreServiceKeys.InputDeviceWatcher, deviceWatcher);
                engine.SetService(CoreServiceKeys.InputBackend, inputBackend);
                """;
            Assert.That(
                FindAdapterDeviceServiceRuleViolations(observationPortSource, "probe.cs", writableDeviceTypes, Array.Empty<DeviceServiceAllowance>()),
                Is.Empty,
                "Registering an explicitly classified observation port must pass; a hit here means the guard blocks its own legal classification.");

            string violationsSource = """
                var syntheticInput = new SyntheticInputDevice();
                engine.SetService(CoreServiceKeys.InputBackend, syntheticInput);
                engine.SetService(CoreServiceKeys.RumbleMotor, new VirtualProbeGamepad());
                engine.SetService(CoreServiceKeys.HapticDeviceEnumerator, haptics);
                """;
            var flagged = FindAdapterDeviceServiceRuleViolations(violationsSource, "probe.cs", writableDeviceTypes, Array.Empty<DeviceServiceAllowance>());

            Assert.That(
                flagged,
                Has.Count.EqualTo(3),
                "The rule 2 device guard must flag synthetic writable-instance and unclassified-key registrations; a miss here means the guard is dead.");
            Assert.That(
                flagged[0],
                Does.Contain("SyntheticInputDevice"),
                "A writable device instance must be rejected by the registered value's type, not by the key name.");
            Assert.That(
                flagged[1],
                Does.Contain("VirtualProbeGamepad"),
                "A type implementing the device write face is a writable device instance even under a Device-free key name.");
            Assert.That(
                flagged[2],
                Does.Contain("HapticDeviceEnumerator"),
                "New Device-named service keys are rejected until explicitly classified as observation ports.");
        }

        /// <summary>
        /// Save-side guard for terminology rule 4. The network-payload counterpart is
        /// deferred until the online replication line lands on main; when it does, this
        /// test class must gain an equivalent scan over the network payload codecs.
        /// </summary>
        [Test]
        public void SavePayloads_DoNotCarryLocalIoConceptKeys()
        {
            string repoRoot = FindRepoRoot();
            var hits = new List<string>();

            foreach (string file in EnumeratePersistenceCodeFiles(repoRoot))
            {
                string relativePath = ToRepoRelativePath(repoRoot, file);
                foreach (SaveKeyOccurrence occurrence in FindLocalIoSaveKeyOccurrences(File.ReadAllText(file)))
                {
                    SaveKeyAllowance allowance = LocalIoSaveKeyAllowlist
                        .FirstOrDefault(entry => entry.Path == relativePath && entry.Key == occurrence.Key);
                    if (allowance.Path is null)
                    {
                        hits.Add($"{relativePath}: save payload key \"{occurrence.Key}\" ({occurrence.Occurrences}x)");
                    }
                    else if (occurrence.Occurrences > allowance.AllowedCount)
                    {
                        hits.Add($"{relativePath}: save payload key \"{occurrence.Key}\" occurs {occurrence.Occurrences}x (allowed {allowance.AllowedCount})");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Terminology rule 4 (#902 3.5): local I/O concepts (seatId / controlSchemeId / device identifiers) must not enter save payloads; " +
                "saves carry participant / player and semantic order only. Scans save write/read paths under src/Core/Persistence. " +
                "The allowlist below is shrink-only and covers the adjudicated launchContext.localSeats launch snapshot " +
                "(cross-machine restore fail-fast is the accepted contract); re-review before the first player-facing release:\n" +
                string.Join("\n", hits));
        }

        [Test]
        public void SavePayloadGuard_DetectsSyntheticLocalIoKeyViolations()
        {
            string violatingSource = """
                var seat = new JsonObject
                {
                    ["seat_id"] = binding.SeatId,
                    ["controlSchemeId"] = binding.ControlSchemeId,
                    ["gamepadId"] = binding.GamepadId,
                    ["deviceName"] = binding.DeviceName,
                };
                """;

            var flagged = FindLocalIoSaveKeyOccurrences(violatingSource)
                .OrderBy(occurrence => occurrence.Key, StringComparer.Ordinal)
                .ToList();

            Assert.That(
                flagged.Select(occurrence => $"{occurrence.Key}={occurrence.Occurrences}"),
                Is.EqualTo(new[] { "controlSchemeId=1", "deviceName=1", "gamepadId=1", "seat_id=1" }),
                "The rule 4 save guard must flag synthetic local I/O payload keys; a miss here means the guard is dead.");

            string cleanSource = """
                var player = new JsonObject
                {
                    ["playerId"] = player.Id,
                    ["teamId"] = player.TeamId,
                    ["seatOrder"] = seat.Order,
                };
                """;

            Assert.That(
                FindLocalIoSaveKeyOccurrences(cleanSource),
                Is.Empty,
                "The rule 4 save guard must not flag participant / player / semantic-order payload keys.");
        }

        private static readonly DeviceServiceAllowance[] DeviceServiceAllowlist =
        {
            new("src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs", "SyntheticInput", 1),
        };

        // Stateless device observation ports (enumeration / hot-plug; stable identity +
        // device kind only) that Adapters may register into the App-level container.
        // Classification is explicit and typed: the declared ServiceKey surface must be
        // the port interface, so legality survives key renames but dies on re-typing.
        private static readonly DeviceObservationPort[] DeviceObservationPorts =
        {
            new("InputDeviceWatcher", "IInputDeviceWatcher"),
        };

        private static readonly Dictionary<string, string> DeviceObservationPortByKey =
            DeviceObservationPorts.ToDictionary(port => port.Key, port => port.PortType);

        // Adjudicated exception: launchContext.localSeats[] keeps its launch-snapshot entry
        // shape (seatId / playerId / controlSchemeId); cross-machine restore fail-fast is the
        // accepted contract. Shrink-only; re-review before the first player-facing release.
        private static readonly SaveKeyAllowance[] LocalIoSaveKeyAllowlist =
        {
            new("src/Core/Persistence/CoreSaveParticipants.cs", "seatId", 2),
            new("src/Core/Persistence/CoreSaveParticipants.cs", "controlSchemeId", 2),
        };

        private static IEnumerable<string> EnumerateTerminologyScanFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core"),
                Path.Combine(repoRoot, "mods"),
            };
            return EnumerateFiles(roots, "*.cs");
        }

        private static IEnumerable<string> EnumerateAdapterCodeFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Adapters"),
            };
            return EnumerateFiles(roots, "*.cs");
        }

        private static IEnumerable<string> EnumeratePersistenceCodeFiles(string repoRoot)
        {
            string[] roots =
            {
                Path.Combine(repoRoot, "src", "Core", "Persistence"),
            };
            return EnumerateFiles(roots, "*.cs");
        }

        private static string ReadCoreServiceKeysSource(string repoRoot)
        {
            string? path = EnumerateFiles(new[] { Path.Combine(repoRoot, "src", "Core") }, "CoreServiceKeys.cs").FirstOrDefault();
            return path is null
                ? throw new FileNotFoundException("CoreServiceKeys.cs not found under src/Core; the rule 2 guard reads the key surface table from it.")
                : File.ReadAllText(path);
        }

        private static string[] ReadSyntheticInputDeviceWriteFaceMembers(string repoRoot)
        {
            string path = Path.Combine(repoRoot, "src", "Core", "Input", "Runtime", "SyntheticInputDevice.cs");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "SyntheticInputDevice.cs not found at src/Core/Input/Runtime; the rule 2 guard reads the device write face from its declaration surface.",
                    path);
            }

            string source = File.ReadAllText(path);
            int writeStart = source.IndexOf(DeviceWriteSideMarker, StringComparison.Ordinal);
            int readStart = source.IndexOf(DeviceReadSideMarker, StringComparison.Ordinal);
            if (writeStart < 0 || readStart < 0 || readStart <= writeStart)
            {
                throw new InvalidOperationException(
                    $"SyntheticInputDevice.cs must delimit its write face with '{DeviceWriteSideMarker}' before '{DeviceReadSideMarker}'; the rule 2 guard extracts the member set from that slice.");
            }

            // The slice spans the write side plus the host-loop frame boundary; public instance
            // methods declared there are the write face — properties and read-only queries live
            // outside it, and statics never match.
            string[] members = Regex.Matches(
                    source[writeStart..readStart],
                    @"\bpublic\s+(?!static\b)[\w.]+(?:<[^>]+>)?\s+(?<name>\w+)\s*\(",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (members.Length < DeviceWriteFaceQuorum)
            {
                throw new InvalidOperationException(
                    $"Extracted device write face has only {members.Length} members; the declaration-surface extraction is broken.");
            }

            return members;
        }

        private static IEnumerable<string> EnumerateWritableDeviceTypeSourceTexts(string repoRoot)
        {
            string[] roots = { Path.Combine(repoRoot, "src"), Path.Combine(repoRoot, "mods") };
            foreach (string file in EnumerateFiles(roots, "*.cs"))
            {
                if (IsUnderTestDirectory(file))
                {
                    continue;
                }

                yield return string.Join("\n", SourceTextScanner.ReadCodeLines(file).Select(line => line.Text));
            }
        }

        private static bool IsUnderTestDirectory(string file)
        {
            string normalized = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.Equals("Tests", StringComparison.OrdinalIgnoreCase));
        }

        private static readonly Regex SaveKeyLiteralPattern = new(
            @"\[\s*""(?<key>[^""\r\n]+)""\s*\]",
            RegexOptions.CultureInvariant);

        private static readonly Regex DeviceServiceRegistrationPattern = new(
            @"\bSetService\s*\(\s*CoreServiceKeys\.(?<key>\w+)\s*,(?<value>.*?)\)\s*;",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        private static readonly Regex CoreServiceKeySurfacePattern = new(
            @"ServiceKey\s*<\s*(?<surface>[\w.]+)\s*>\s+(?<key>\w+)\s*=",
            RegexOptions.CultureInvariant);

        private static readonly Regex RegisteredValueCastPattern = new(
            @"\(\s*(?<type>[\w.]+)\s*\)",
            RegexOptions.CultureInvariant);

        private static readonly Regex RegisteredValueConstructionPattern = new(
            @"\bnew\s+(?<type>[\w.]+)\s*[\(<]",
            RegexOptions.CultureInvariant);

        private static readonly Regex BareIdentifierPattern = new(
            @"^\w+$",
            RegexOptions.CultureInvariant);

        private static readonly Regex TypeDeclarationPattern = new(
            @"\b(?:class|struct|interface|record(?:\s+(?:class|struct))?)\s+(?<name>\w+)[^{;=]*\{",
            RegexOptions.CultureInvariant);

        // The device write face is the interactive-state surface of SyntheticInputDevice — read
        // from its declaration surface, never hand-copied here; a type declaring a quorum of
        // these members carries interactive device state, whatever its name.
        private const string DeviceWriteSideMarker = "// ---- write side";
        private const string DeviceReadSideMarker = "// ---- read side";

        private const int DeviceWriteFaceQuorum = 3;

        // Matched against the key with underscores stripped and lower-cased, so camelCase
        // and snake_case spellings of the same concept cannot slip past the guard.
        private static readonly string[] LocalIoSaveKeyMarkers =
        {
            "seatid",
            "controlschemeid",
            "inputschemeid",
            "device",
            "gamepad",
            "joystick",
            "peripheral",
            "hardware",
            "keyboardid",
            "mouseid",
            "controllerid",
        };

        private static IEnumerable<SaveKeyOccurrence> FindLocalIoSaveKeyOccurrences(string text)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match match in SaveKeyLiteralPattern.Matches(text))
            {
                string key = match.Groups["key"].Value;
                if (!IsLocalIoConceptSaveKey(key))
                {
                    continue;
                }

                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }

            return counts.Select(pair => new SaveKeyOccurrence(pair.Key, pair.Value));
        }

        private static bool IsLocalIoConceptSaveKey(string key)
        {
            string normalized = key.Replace("_", string.Empty).ToLowerInvariant();
            return LocalIoSaveKeyMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
        }

        private static List<string> FindObservationPortClassificationViolations(IReadOnlyDictionary<string, string> keySurfaces)
        {
            var hits = new List<string>();
            foreach (DeviceObservationPort port in DeviceObservationPorts)
            {
                if (!keySurfaces.TryGetValue(port.Key, out string? surface))
                {
                    hits.Add($"CoreServiceKeys.{port.Key}: classified observation port key no longer exists; re-classify or drop the port entry");
                }
                else if (!string.Equals(surface, port.PortType, StringComparison.Ordinal))
                {
                    hits.Add($"CoreServiceKeys.{port.Key}: observation port must be declared ServiceKey<{port.PortType}> (found ServiceKey<{surface}>)");
                }
            }

            return hits;
        }

        private static List<string> FindAdapterDeviceServiceRuleViolations(
            string adapterSource,
            string relativePath,
            IReadOnlyCollection<string> writableDeviceTypes,
            IReadOnlyList<DeviceServiceAllowance> allowlist)
        {
            var hits = new List<string>();
            foreach (DeviceServiceRegistration registration in FindDeviceServiceRegistrations(adapterSource))
            {
                string? writableInstance = FindWritableDeviceInstanceType(adapterSource, registration.Value, writableDeviceTypes);
                if (DeviceObservationPortByKey.ContainsKey(registration.Key))
                {
                    if (writableInstance is not null)
                    {
                        hits.Add($"{relativePath}: CoreServiceKeys.{registration.Key} carries writable device instance {writableInstance} behind an observation-port key");
                    }

                    continue;
                }

                bool deviceNamedKey = registration.Key.Contains("Device", StringComparison.Ordinal) || registration.Key == "SyntheticInput";
                if (writableInstance is null && !deviceNamedKey)
                {
                    continue;
                }

                DeviceServiceAllowance allowance = allowlist
                    .FirstOrDefault(entry => entry.Path == relativePath && entry.Key == registration.Key);
                if (allowance.Path is null)
                {
                    hits.Add(writableInstance is null
                        ? $"{relativePath}: CoreServiceKeys.{registration.Key} (unclassified Device-named service key; classify it as an observation port or hold it in the Seat domain)"
                        : $"{relativePath}: CoreServiceKeys.{registration.Key} registers writable device instance {writableInstance}");
                }
                else if (CountOccurrences(adapterSource, registration.MatchText) > allowance.AllowedCount)
                {
                    hits.Add($"{relativePath}: CoreServiceKeys.{registration.Key} registered {CountOccurrences(adapterSource, registration.MatchText)}x (allowed {allowance.AllowedCount})");
                }
            }

            return hits;
        }

        private static IEnumerable<DeviceServiceRegistration> FindDeviceServiceRegistrations(string text)
        {
            foreach (Match match in DeviceServiceRegistrationPattern.Matches(text))
            {
                yield return new DeviceServiceRegistration(
                    match.Groups["key"].Value,
                    match.Groups["value"].Value.Trim(),
                    match.Value);
            }
        }

        private static IReadOnlyDictionary<string, string> FindCoreServiceKeySurfaces(string coreServiceKeysSource)
        {
            var surfaces = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in CoreServiceKeySurfacePattern.Matches(coreServiceKeysSource))
            {
                surfaces[match.Groups["key"].Value] = SimpleTypeName(match.Groups["surface"].Value);
            }

            return surfaces;
        }

        private static string? FindWritableDeviceInstanceType(
            string fileText,
            string valueExpression,
            IReadOnlyCollection<string> writableDeviceTypes)
        {
            foreach (string candidate in InferRegisteredValueTypes(fileText, valueExpression))
            {
                if (writableDeviceTypes.Contains(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static IEnumerable<string> InferRegisteredValueTypes(string fileText, string valueExpression)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match cast in RegisteredValueCastPattern.Matches(valueExpression))
            {
                candidates.Add(SimpleTypeName(cast.Groups["type"].Value));
            }

            foreach (Match construction in RegisteredValueConstructionPattern.Matches(valueExpression))
            {
                candidates.Add(SimpleTypeName(construction.Groups["type"].Value));
            }

            string trimmed = valueExpression.Trim();
            if (BareIdentifierPattern.IsMatch(trimmed))
            {
                string escaped = Regex.Escape(trimmed);
                foreach (Match declaration in Regex.Matches(fileText, $@"\b(?<type>[\w.]+)\??\s+{escaped}\s*[=;,)]"))
                {
                    candidates.Add(SimpleTypeName(declaration.Groups["type"].Value));
                }

                foreach (Match declaration in Regex.Matches(fileText, $@"\bvar\s+{escaped}\s*=\s*new\s+(?<type>[\w.]+)"))
                {
                    candidates.Add(SimpleTypeName(declaration.Groups["type"].Value));
                }
            }

            return candidates;
        }

        private static HashSet<string> FindWritableDeviceTypeNames(IEnumerable<string> sources, IReadOnlyCollection<string> writeFaceMembers)
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { "SyntheticInputDevice" };
            foreach (string source in sources)
            {
                foreach (TypeDeclarationSegment segment in EnumerateTypeDeclarationSegments(source))
                {
                    if (CountDeviceWriteFaceMemberDeclarations(segment.Body, writeFaceMembers) >= DeviceWriteFaceQuorum)
                    {
                        names.Add(segment.Name);
                    }
                }
            }

            return names;
        }

        private static IEnumerable<TypeDeclarationSegment> EnumerateTypeDeclarationSegments(string text)
        {
            foreach (Match declaration in TypeDeclarationPattern.Matches(text))
            {
                int bodyOpenIndex = declaration.Index + declaration.Length - 1;
                int bodyCloseIndex = FindBalancedBraceClose(text, bodyOpenIndex);
                if (bodyCloseIndex < 0)
                {
                    continue;
                }

                yield return new TypeDeclarationSegment(
                    declaration.Groups["name"].Value,
                    text[bodyOpenIndex..bodyCloseIndex]);
            }
        }

        private static int FindBalancedBraceClose(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int CountDeviceWriteFaceMemberDeclarations(string typeBody, IReadOnlyCollection<string> writeFaceMembers) =>
            writeFaceMembers.Count(member => Regex.IsMatch(
                typeBody,
                $@"\b(?:public|internal|protected|private)\b[^\n;{{}}]*\b{member}\s*\(",
                RegexOptions.CultureInvariant));

        private static string SimpleTypeName(string typeName)
        {
            int lastDot = typeName.LastIndexOf('.');
            return lastDot < 0 ? typeName : typeName[(lastDot + 1)..];
        }

        private static IEnumerable<string> EnumerateFiles(IEnumerable<string> roots, string pattern)
        {
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                    .Where(IsNotBuildArtifact))
                {
                    yield return file;
                }
            }
        }

        private static int CountOccurrences(string text, string value) =>
            Regex.Matches(text, Regex.Escape(value), RegexOptions.CultureInvariant).Count;

        private static bool IsNotBuildArtifact(string file)
        {
            string normalized = file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return !normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "gitbook")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private static string ToRepoRelativePath(string repoRoot, string absolutePath) =>
            Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');

        private readonly record struct ForbiddenPattern(string Name, string Pattern);

        private readonly record struct DeviceServiceAllowance(string Path, string Key, int AllowedCount);

        private readonly record struct DeviceObservationPort(string Key, string PortType);

        private readonly record struct DeviceServiceRegistration(string Key, string Value, string MatchText);

        private readonly record struct TypeDeclarationSegment(string Name, string Body);

        private readonly record struct SaveKeyAllowance(string Path, string Key, int AllowedCount);

        private readonly record struct SaveKeyOccurrence(string Key, int Occurrences);
    }
}
