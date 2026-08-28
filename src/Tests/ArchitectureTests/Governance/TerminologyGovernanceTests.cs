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

        [Test]
        public void Adapters_DoNotRegisterDeviceServicesIntoAppLevelContainer()
        {
            string repoRoot = FindRepoRoot();
            var deviceServiceRegistration = new Regex(
                @"SetService\s*\(\s*CoreServiceKeys\.(?<key>\w*Device\w*|SyntheticInput)\b",
                RegexOptions.CultureInvariant);
            var hits = new List<string>();

            foreach (string file in EnumerateAdapterCodeFiles(repoRoot))
            {
                string text = File.ReadAllText(file);
                string relativePath = ToRepoRelativePath(repoRoot, file);
                foreach (Match match in deviceServiceRegistration.Matches(text))
                {
                    string key = match.Groups["key"].Value;
                    DeviceServiceAllowance allowance = DeviceServiceAllowlist
                        .FirstOrDefault(entry => entry.Path == relativePath && entry.Key == key);
                    if (allowance.Path is null)
                    {
                        hits.Add($"{relativePath}: CoreServiceKeys.{key}");
                    }
                    else if (CountOccurrences(text, match.Value) > allowance.AllowedCount)
                    {
                        hits.Add($"{relativePath}: CoreServiceKeys.{key} registered {CountOccurrences(text, match.Value)}x (allowed {allowance.AllowedCount})");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Terminology rule 2 (#902 3.5): device instances are held by Seat; Adapters must not register device services into the App-level container. " +
                "The allowlist below is shrink-only (P3 collapses it to zero, see #1058); new device-service registrations are forbidden:\n" +
                string.Join("\n", hits));
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

        private static readonly Regex SaveKeyLiteralPattern = new(
            @"\[\s*""(?<key>[^""\r\n]+)""\s*\]",
            RegexOptions.CultureInvariant);

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

        private readonly record struct SaveKeyAllowance(string Path, string Key, int AllowedCount);

        private readonly record struct SaveKeyOccurrence(string Key, int Occurrences);
    }
}
