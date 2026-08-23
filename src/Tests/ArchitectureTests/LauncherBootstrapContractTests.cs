using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Ludots.Core.Hosting;
using Ludots.Core.Networking.Runtime;
using Ludots.Launcher.Backend;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public class LauncherBootstrapContractTests
    {
        [Test]
        public void Launcher_ResolvesFormalRtsShowcase_AsStrictThreeProcessGroup()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-rts-process-group-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var launcher = CreateLauncher(repoRoot, tempDirectory);
                LauncherResolvedProcessGroup group = launcher.ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);

                Assert.That(group.Topology, Is.EqualTo(LauncherProcessGroupTopologies.LocalAuthoritative));
                Assert.That(group.Processes.Select(process => process.Id), Is.EqualTo(new[]
                {
                    "authoritative-server",
                    "client-one",
                    "client-two"
                }));
                Assert.That(group.Processes[0].Application.HostKind, Is.EqualTo(LauncherProcessHostKinds.DedicatedServer));
                Assert.That(group.Processes.Skip(1).All(process =>
                    process.Application.HostKind == LauncherProcessHostKinds.Raylib), Is.True);
                Assert.That(group.Processes.Select(process => process.NetworkHost.ProcessRole), Is.EqualTo(new[]
                {
                    "authoritativeServer",
                    "replicatedClient",
                    "replicatedClient"
                }));
                Assert.That(group.Processes.Skip(1).Select(process => process.NetworkHost.ClientInstanceId),
                    Is.EqualTo(new[] { 1, 2 }));
                Assert.That(group.ClientCount, Is.EqualTo(2));
                Assert.That(group.ReadinessTimeoutMilliseconds, Is.EqualTo(45000));
                Assert.That(group.Processes.Skip(1).All(process =>
                    process.MinimumReplicatedMirrorCount == 8 &&
                    process.MinimumRenderableMirrorCount == 7), Is.True);
                Assert.That(group.Processes.Select(process => process.NetworkHost.FaultSeed), Is.Unique);
                Assert.That(group.Processes.All(process => process.NetworkHost.Port == 27777), Is.True);
                Assert.That(group.Processes.Skip(1).All(process => process.NetworkHost.Host == "127.0.0.1"), Is.True);
                Assert.That(group.Processes.All(process => process.NetworkHost.ConnectionKey == "rts-frontline-local"), Is.True);
                Assert.That(group.LaunchPlan.OrderedModIds, Does.Contain("RtsMultiplayerFrontlineNetworkedMod"));

                using JsonDocument registry = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(repoRoot, "showcase.registry.json")));
                JsonElement showcase = registry.RootElement.GetProperty("showcases")
                    .EnumerateArray()
                    .Single(entry => entry.GetProperty("id").GetString() == "rts_multiplayer_frontline");
                Assert.That(showcase.GetProperty("binding").ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(showcase.GetProperty("preset").GetString(),
                    Is.EqualTo("rts_multiplayer_frontline_networked_raylib"));

                string gallery = File.ReadAllText(Path.Combine(repoRoot, "docs", "gallery.html"));
                int launchFunction = gallery.IndexOf("function launchCmd(sc)", StringComparison.Ordinal);
                Assert.That(launchFunction, Is.GreaterThanOrEqualTo(0));
                int presetBranch = gallery.IndexOf("if (sc.preset)", launchFunction, StringComparison.Ordinal);
                int bindingBranch = gallery.IndexOf("if (sc.binding)", launchFunction, StringComparison.Ordinal);
                Assert.That(presetBranch, Is.GreaterThan(launchFunction));
                Assert.That(bindingBranch, Is.GreaterThan(presetBranch),
                    "The generated gallery command must prefer a process-group preset over a single binding.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void NetworkRoleArtifactGenerator_WritesOneStrictBootstrapPerResolvedRole()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-rts-role-artifacts-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var launcher = CreateLauncher(repoRoot, tempDirectory);
                LauncherResolvedProcessGroup resolved = launcher.ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                var generator = new LauncherNetworkRoleArtifactGenerator();
                LauncherProcessGroupArtifacts artifacts = generator.Generate(
                    resolved with { ArtifactDirectory = Path.Combine(tempDirectory, "roles") });

                Assert.That(artifacts.Processes, Has.Count.EqualTo(3));
                Assert.That(artifacts.Processes.Select(process => process.BootstrapPath), Is.Unique);
                Assert.That(JsonSerializer.Serialize(artifacts), Does.Not.Contain("rts-frontline-local"),
                    "CLI-facing artifact metadata must not print the connection key.");
                foreach (LauncherNetworkRoleArtifact process in artifacts.Processes)
                {
                    Assert.That(File.Exists(process.GraphPath), Is.True);
                    Assert.That(File.Exists(process.BootstrapPath), Is.True);
                    using JsonDocument bootstrap = JsonDocument.Parse(File.ReadAllText(process.BootstrapPath));
                    JsonElement networkHost = bootstrap.RootElement.GetProperty("NetworkHost");
                    Assert.That(networkHost.GetProperty("ProcessRole").GetString(), Is.EqualTo(process.ProcessRole));
                    Assert.That(networkHost.GetProperty("Port").GetInt32(), Is.EqualTo(27777));
                    Assert.That(networkHost.GetProperty("FaultSeed").GetInt32(), Is.Positive);
                    Assert.That(networkHost.GetProperty("ReadinessArtifactPath").GetString(),
                        Is.EqualTo(Path.GetFileName(process.ReadinessArtifactPath)));
                    Assert.That(bootstrap.RootElement.TryGetProperty("ReadinessArtifactPath", out _), Is.False);
                    Assert.That(bootstrap.RootElement.GetProperty("PlanFingerprint").GetString(),
                        Is.EqualTo(resolved.LaunchPlan.PlanFingerprint));
                }

                generator.DeleteSensitiveBootstrapArtifacts(artifacts);
                Assert.That(artifacts.Processes.All(process => !File.Exists(process.BootstrapPath)), Is.True);
                Assert.That(artifacts.Processes.All(process => File.Exists(process.GraphPath)), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void ProcessGroupResolver_RejectsDuplicateClientIdentityBeforeWritingArtifacts()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-rts-duplicate-client-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var launcher = CreateLauncher(repoRoot, tempDirectory);
                LauncherResolvedProcessGroup valid = launcher.ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                LauncherResolvedProcessApplication server = valid.Applications.Single(application =>
                    application.HostKind == LauncherProcessHostKinds.DedicatedServer);
                LauncherResolvedProcessApplication client = valid.Applications.Single(application =>
                    application.HostKind == LauncherProcessHostKinds.Raylib);
                string invalidArtifactDirectory = Path.Combine(tempDirectory, "invalid-group");
                var invalid = new LauncherProcessGroupDefinition
                {
                    Topology = LauncherProcessGroupTopologies.LocalAuthoritative,
                    ArtifactDirectory = Path.GetRelativePath(repoRoot, invalidArtifactDirectory),
                    Host = "127.0.0.1",
                    Port = 27777,
                    ConnectionKey = "test-key",
                    ClientCount = 2,
                    Readiness = new LauncherProcessGroupReadinessDefinition
                    {
                        TimeoutMilliseconds = 1000,
                        PollIntervalMilliseconds = 10
                    },
                    Applications = new()
                    {
                        new LauncherProcessApplicationDefinition
                        {
                            Id = "server",
                            HostKind = LauncherProcessHostKinds.DedicatedServer,
                            ProjectPath = server.ProjectPath,
                            AssemblyPath = server.AssemblyPath
                        },
                        new LauncherProcessApplicationDefinition
                        {
                            Id = "client",
                            HostKind = LauncherProcessHostKinds.Raylib,
                            ProjectPath = client.ProjectPath,
                            AssemblyPath = client.AssemblyPath
                        }
                    },
                    Processes = new()
                    {
                        new LauncherNetworkProcessDefinition
                        {
                            Id = "server",
                            ApplicationId = "server",
                            ProcessRole = "authoritativeServer",
                            FaultProfile = "normal",
                            FaultSeed = 1,
                            Readiness = Readiness("server.ready.json", 0, 0)
                        },
                        new LauncherNetworkProcessDefinition
                        {
                            Id = "client-a",
                            ApplicationId = "client",
                            ProcessRole = "replicatedClient",
                            ClientInstanceId = 7,
                            CredentialPath = "credentials/a.session",
                            FaultProfile = "normal",
                            FaultSeed = 2,
                            Readiness = Readiness("client-a.ready.json", 1, 1)
                        },
                        new LauncherNetworkProcessDefinition
                        {
                            Id = "client-b",
                            ApplicationId = "client",
                            ProcessRole = "replicatedClient",
                            ClientInstanceId = 7,
                            CredentialPath = "credentials/b.session",
                            FaultProfile = "normal",
                            FaultSeed = 3,
                            Readiness = Readiness("client-b.ready.json", 1, 1)
                        }
                    }
                };

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    LauncherNetworkProcessGroupResolver.Resolve(
                        repoRoot,
                        "invalid",
                        invalid,
                        valid.LaunchPlan));
                Assert.That(exception!.Message, Does.Contain("duplicate clientInstanceId 7"));
                Assert.That(Directory.Exists(invalidArtifactDirectory), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void LauncherPresetLoader_RejectsUnknownProcessGroupFields()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-rts-unknown-field-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            string presetsPath = Path.Combine(tempDirectory, "launcher.presets.json");

            try
            {
                File.WriteAllText(
                    presetsPath,
                    """
                    {
                      "schemaVersion": 1,
                      "presets": [
                        {
                          "id": "invalid",
                          "name": "Invalid",
                          "selectors": ["mod:LudotsCoreMod"],
                          "processGroup": {
                            "topology": "localAuthoritative",
                            "artifactDirectory": "artifacts/tests/invalid",
                            "host": "127.0.0.1",
                            "port": 27777,
                            "connectionKey": "test",
                            "clientCount": 2,
                            "readiness": {
                              "timeoutMilliseconds": 1000,
                              "pollIntervalMilliseconds": 10
                            },
                            "applications": [],
                            "processes": [],
                            "silentFallback": true
                          }
                        }
                      ]
                    }
                    """);
                var config = new LauncherConfigService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    presetsPath,
                    Path.Combine(tempDirectory, "preferences.json"),
                    Path.Combine(tempDirectory, "overlay.json"));

                var exception = Assert.Throws<InvalidOperationException>(() => config.LoadPresets());
                Assert.That(exception!.Message, Does.Contain("silentFallback"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void NetworkRoleArtifactGenerator_RejectsEscapingRoleDirectoryBeforeDeletion()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-rts-role-path-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var launcher = CreateLauncher(repoRoot, tempDirectory);
                LauncherResolvedProcessGroup valid = launcher.ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                LauncherResolvedNetworkProcess escaping = valid.Processes[0] with { Id = "../outside" };
                LauncherResolvedProcessGroup invalid = valid with
                {
                    ArtifactDirectory = Path.Combine(tempDirectory, "roles"),
                    Processes = new[] { escaping }.Concat(valid.Processes.Skip(1)).ToArray()
                };

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    new LauncherNetworkRoleArtifactGenerator().Generate(invalid));
                Assert.That(exception!.Message, Does.Contain("cannot be used as a role artifact directory"));
                Assert.That(Directory.Exists(Path.Combine(tempDirectory, "outside")), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Given_LocalAuthoritativeMatchHasStaleCredentials_When_PreparingLaunch_Then_AllClientCredentialsAreDeleted()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-local-credentials-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                LauncherResolvedProcessGroup resolved = CreateLauncher(repoRoot, tempDirectory).ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                string artifactDirectory = Path.Combine(tempDirectory, "roles");
                LauncherResolvedNetworkProcess[] processes = resolved.Processes
                    .Select(process => process.NetworkHost.ResolveRole() == NetworkProcessRole.ReplicatedClient
                        ? process with
                        {
                            CredentialPath = Path.Combine(artifactDirectory, "credentials", $"{process.Id}.session")
                        }
                        : process)
                    .ToArray();
                LauncherResolvedProcessGroup localMatch = resolved with
                {
                    ArtifactDirectory = artifactDirectory,
                    Processes = processes
                };
                string[] credentials = processes
                    .Where(process => process.NetworkHost.ResolveRole() == NetworkProcessRole.ReplicatedClient)
                    .Select(process => process.CredentialPath)
                    .ToArray();
                Directory.CreateDirectory(Path.Combine(artifactDirectory, "credentials"));
                foreach (string credential in credentials)
                {
                    File.WriteAllBytes(credential, new byte[] { 1, 2, 3 });
                }

                new LauncherNetworkRoleArtifactGenerator().PrepareCredentialsForLaunch(localMatch);

                Assert.That(credentials.All(credential => !File.Exists(credential)), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Given_ExternalJoinHasReconnectCredential_When_PreparingLaunch_Then_CredentialIsPreserved()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-external-credential-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                LauncherResolvedProcessGroup resolved = CreateLauncher(repoRoot, tempDirectory).ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_client_join_raylib" },
                    LauncherBuildMode.Never);
                string artifactDirectory = Path.Combine(tempDirectory, "roles");
                string credentialPath = Path.Combine(artifactDirectory, "credentials", "debug-client.session");
                Directory.CreateDirectory(Path.GetDirectoryName(credentialPath)!);
                byte[] reconnectCredential = { 7, 0, 9 };
                File.WriteAllBytes(credentialPath, reconnectCredential);
                LauncherResolvedProcessGroup externalJoin = resolved with
                {
                    ArtifactDirectory = artifactDirectory,
                    Processes = new[] { resolved.Processes.Single() with { CredentialPath = credentialPath } }
                };

                new LauncherNetworkRoleArtifactGenerator().PrepareCredentialsForLaunch(externalJoin);

                Assert.That(File.ReadAllBytes(credentialPath), Is.EqualTo(reconnectCredential));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Given_LocalAuthoritativeCredentialCannotBeDeleted_When_PreparingLaunch_Then_LaunchIsRejected()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-credential-delete-failure-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                LauncherResolvedProcessGroup resolved = CreateLauncher(repoRoot, tempDirectory).ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                string artifactDirectory = Path.Combine(tempDirectory, "roles");
                string invalidCredentialPath = Path.Combine(artifactDirectory, "credentials", "client-one.session");
                Directory.CreateDirectory(invalidCredentialPath);
                LauncherResolvedNetworkProcess[] processes = resolved.Processes
                    .Select(process => process.NetworkHost.ResolveRole() != NetworkProcessRole.ReplicatedClient
                        ? process
                        : process with
                        {
                            CredentialPath = process.Id == "client-one"
                                ? invalidCredentialPath
                                : Path.Combine(artifactDirectory, "credentials", $"{process.Id}.session")
                        })
                    .ToArray();
                LauncherResolvedProcessGroup localMatch = resolved with
                {
                    ArtifactDirectory = artifactDirectory,
                    Processes = processes
                };

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    new LauncherNetworkRoleArtifactGenerator().PrepareCredentialsForLaunch(localMatch));

                Assert.That(exception!.Message, Does.Contain("client-one"));
                Assert.That(exception.Message, Does.Contain("launch was aborted"));
                Assert.That(Directory.Exists(invalidCredentialPath), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Given_LocalAuthoritativeCredentialEscapesArtifactDirectory_When_PreparingLaunch_Then_PathIsRejectedBeforeDeletion()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-credential-owned-path-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                LauncherResolvedProcessGroup resolved = CreateLauncher(repoRoot, tempDirectory).ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                string artifactDirectory = Path.Combine(tempDirectory, "roles");
                string outsideCredentialPath = Path.Combine(tempDirectory, "outside.session");
                File.WriteAllBytes(outsideCredentialPath, new byte[] { 7, 0, 9 });
                LauncherResolvedNetworkProcess[] processes = resolved.Processes
                    .Select(process => process.Id == "client-one"
                        ? process with { CredentialPath = outsideCredentialPath }
                        : process)
                    .ToArray();
                LauncherResolvedProcessGroup localMatch = resolved with
                {
                    ArtifactDirectory = artifactDirectory,
                    Processes = processes
                };

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    new LauncherNetworkRoleArtifactGenerator().PrepareCredentialsForLaunch(localMatch));

                Assert.That(exception!.Message, Does.Contain("must stay within"));
                Assert.That(File.Exists(outsideCredentialPath), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void ActiveLaunchRecordPath_UsesRepositoryScopedCollisionResistantKey()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-active-key-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                LauncherService launcher = CreateLauncher(repoRoot, tempDirectory);
                string first = launcher.GetActiveProcessRecordPath("ray-lib");
                string second = launcher.GetActiveProcessRecordPath("raylib");
                string sameAdapterDifferentRepository = new LauncherService(
                        tempDirectory,
                        Path.Combine(repoRoot, "launcher.config.json"),
                        Path.Combine(repoRoot, "launcher.presets.json"),
                        Path.Combine(tempDirectory, "other-preferences.json"),
                        Path.Combine(tempDirectory, "other-overlay.json"))
                    .GetActiveProcessRecordPath("raylib");

                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(second, Is.Not.EqualTo(sameAdapterDifferentRepository));
                Assert.That(Path.GetFileName(second), Does.Match("^launch-[0-9a-f]{64}\\.json$"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void ProcessGroupReadiness_RequiresServerSeatsAndRenderableClientMirrors()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-readiness-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                LauncherResolvedProcessGroup resolved = CreateLauncher(repoRoot, tempDirectory).ResolveProcessGroup(
                    new[] { "preset:rts_multiplayer_frontline_networked_raylib" },
                    LauncherBuildMode.Never);
                LauncherProcessGroupArtifacts artifacts = new LauncherNetworkRoleArtifactGenerator().Generate(
                    resolved with { ArtifactDirectory = Path.Combine(tempDirectory, "roles") });
                LauncherNetworkRoleArtifact server = artifacts.Processes[0];
                LauncherNetworkRoleArtifact client = artifacts.Processes[1] with
                {
                    CredentialPath = Path.Combine(tempDirectory, "client.session")
                };
                File.WriteAllBytes(client.CredentialPath, new byte[] { 1 });

                var serverStarting = new LauncherNetworkProcessReadinessArtifact
                {
                    SchemaVersion = 1,
                    ProcessRole = "authoritativeServer",
                    RuntimeReady = true,
                    SessionEpoch = 71,
                    UpdatedAtUtc = DateTime.UtcNow,
                    ConnectedSeatCount = 1
                };
                Assert.That(LauncherNetworkProcessReadinessEvaluator.IsServerRuntimeReady(server, serverStarting), Is.True);
                Assert.That(LauncherNetworkProcessReadinessEvaluator.IsGroupReady(server, serverStarting, 2), Is.False);
                serverStarting.ConnectedSeatCount = 2;
                Assert.That(LauncherNetworkProcessReadinessEvaluator.IsGroupReady(server, serverStarting, 2), Is.True);

                var clientStarting = new LauncherNetworkProcessReadinessArtifact
                {
                    SchemaVersion = 1,
                    ProcessRole = "replicatedClient",
                    RuntimeReady = true,
                    SessionEstablished = true,
                    SessionEpoch = 71,
                    UpdatedAtUtc = DateTime.UtcNow,
                    ReplicatedMirrorCount = 8,
                    RenderableMirrorCount = 0
                };
                Assert.That(LauncherNetworkProcessReadinessEvaluator.IsGroupReady(client, clientStarting, 2), Is.False);
                clientStarting.RenderableMirrorCount = 7;
                Assert.That(LauncherNetworkProcessReadinessEvaluator.IsGroupReady(client, clientStarting, 2), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void ProcessGroupReadinessReader_ConsumesTheCoreProducerContract_AndRejectsStaleArtifacts()
        {
            string tempDirectory = Path.Combine(
                Path.GetTempPath(),
                $"ludots-launcher-readiness-contract-{Guid.NewGuid():N}");
            string artifactPath = Path.Combine(tempDirectory, "client.readiness.json");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                DateTime publishedAfterUtc = DateTime.UtcNow.AddSeconds(-1);
                using (var producer = new AtomicJsonNetworkReadinessArtifact(artifactPath))
                {
                    var snapshot = new NetworkReadinessSnapshot(
                        NetworkProcessRole.ReplicatedClient,
                        runtimeReady: true,
                        sessionEstablished: true,
                        sessionEpoch: 709,
                        replicatedMirrorCount: 8,
                        renderableMirrorCount: 7,
                        connectedSeatCount: 2);
                    producer.Publish(in snapshot);

                    var reader = new LauncherNetworkProcessReadinessReader();
                    Assert.That(
                        reader.TryRead(artifactPath, publishedAfterUtc, out LauncherNetworkProcessReadinessArtifact artifact),
                        Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(artifact.SchemaVersion, Is.EqualTo(NetworkReadinessSnapshot.CurrentSchemaVersion));
                        Assert.That(artifact.ProcessRole, Is.EqualTo("replicatedClient"));
                        Assert.That(artifact.RuntimeReady, Is.True);
                        Assert.That(artifact.SessionEstablished, Is.True);
                        Assert.That(artifact.SessionEpoch, Is.EqualTo(709));
                        Assert.That(artifact.ConnectedSeatCount, Is.EqualTo(2));
                        Assert.That(artifact.ReplicatedMirrorCount, Is.EqualTo(8));
                        Assert.That(artifact.RenderableMirrorCount, Is.EqualTo(7));
                        Assert.That(artifact.UpdatedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
                    });
                    Assert.That(
                        reader.TryRead(artifactPath, DateTime.UtcNow.AddMinutes(1), out _),
                        Is.False);
                }
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public async Task Given_CoreWriterAtomicallyReplacesReadiness_When_LauncherPollsConcurrently_Then_NoFileLockBlocksPublication()
        {
            string tempDirectory = Path.Combine(
                Path.GetTempPath(),
                $"ludots-launcher-readiness-concurrency-{Guid.NewGuid():N}");
            string artifactPath = Path.Combine(tempDirectory, "client.readiness.json");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                using var producer = new AtomicJsonNetworkReadinessArtifact(artifactPath);
                var reader = new LauncherNetworkProcessReadinessReader();
                for (int i = 1; i <= 25; i++)
                {
                    NetworkReadinessSnapshot sequentialSnapshot = CreateReadinessSnapshot((ulong)i);
                    producer.Publish(in sequentialSnapshot);
                }
                Assert.That(
                    reader.TryRead(artifactPath, out LauncherNetworkProcessReadinessArtifact sequentialArtifact),
                    Is.True);
                Assert.That(sequentialArtifact.SessionEpoch, Is.EqualTo(25));

                using var start = new ManualResetEventSlim(initialState: false);
                int publishCount = 0;
                int readCount = 0;
                Task writer = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 26; i <= 125; i++)
                    {
                        NetworkReadinessSnapshot snapshot = CreateReadinessSnapshot((ulong)i);
                        producer.Publish(in snapshot);
                        Interlocked.Increment(ref publishCount);
                    }
                });
                Task polling = Task.Run(() =>
                {
                    start.Wait();
                    while (!writer.IsCompleted || Volatile.Read(ref readCount) == 0)
                    {
                        if (!reader.TryRead(artifactPath, out LauncherNetworkProcessReadinessArtifact artifact))
                        {
                            continue;
                        }

                        Assert.That(artifact.SchemaVersion, Is.EqualTo(NetworkReadinessSnapshot.CurrentSchemaVersion));
                        Assert.That(artifact.ProcessRole, Is.EqualTo("replicatedClient"));
                        Assert.That(artifact.SessionEstablished, Is.True);
                        Assert.That(artifact.ReplicatedMirrorCount, Is.EqualTo(8));
                        Assert.That(artifact.RenderableMirrorCount, Is.EqualTo(7));
                        Interlocked.Increment(ref readCount);
                    }
                });

                start.Set();
                await Task.WhenAll(writer, polling);

                Assert.That(publishCount, Is.EqualTo(100));
                Assert.That(readCount, Is.Positive);
                Assert.That(
                    reader.TryRead(artifactPath, out LauncherNetworkProcessReadinessArtifact finalArtifact),
                    Is.True);
                Assert.That(finalArtifact.SessionEpoch, Is.EqualTo(125));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }

            static NetworkReadinessSnapshot CreateReadinessSnapshot(ulong sessionEpoch)
            {
                return new NetworkReadinessSnapshot(
                    NetworkProcessRole.ReplicatedClient,
                    runtimeReady: true,
                    sessionEstablished: true,
                    sessionEpoch,
                    replicatedMirrorCount: 8,
                    renderableMirrorCount: 7,
                    connectedSeatCount: 2);
            }
        }

        [Test]
        public async Task RunProcessAsync_ReturnsWithoutHanging_WhenDescendantKeepsRedirectedOutputOpen()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("The redirected-handle regression uses Windows cmd/start process inheritance.");
            }

            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-output-drain-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var scriptPath = Path.Combine(tempDirectory, "leave-output-open.cmd");

            try
            {
                File.WriteAllText(
                    scriptPath,
                    "@echo off\r\n" +
                    "start \"\" /b /d \"%SystemRoot%\" powershell.exe -NoProfile -NonInteractive -Command \"Start-Sleep -Milliseconds 1500\"\r\n" +
                    "echo parent-done\r\n" +
                    "exit /b 0\r\n");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await LauncherService.RunProcessAsync(
                    "cmd.exe",
                    $"/c \"{scriptPath}\"",
                    tempDirectory,
                    timeoutMs: 5_000,
                    outputDrainTimeoutMs: 250);
                stopwatch.Stop();

                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(result.Output, Does.Contain("parent-done"));
                Assert.That(result.Output, Does.Contain("Redirected output remained open"));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            }
            finally
            {
                await DeleteDirectoryWithRetryAsync(tempDirectory, TimeSpan.FromSeconds(5));
            }
        }

        private static async Task DeleteDirectoryWithRetryAsync(string directory, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            Exception? lastFailure = null;
            while (DateTime.UtcNow <= deadline)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }

                    return;
                }
                catch (IOException ex)
                {
                    lastFailure = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastFailure = ex;
                }

                await Task.Delay(100);
            }

            throw new IOException($"Failed to delete temporary test directory '{directory}'.", lastFailure);
        }

        [Test]
        public void GameBootstrapper_PrefersLaunchGraphMetadata_WhenPresent()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "planFingerprint": "test-fingerprint",
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}"
                        }
                      ]
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "test-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");
                using var engine = result.Engine;

                Assert.That(engine, Is.Not.Null);
                Assert.That(result.Config, Is.Not.Null);
                Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_AcceptsFullLauncherGraphMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-full-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "full-graph-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json"
                      },
                      "buildMode": "auto",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(modRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(modRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanSelectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "PlanRootModIds": [
                        "LudotsCoreMod"
                      ],
                      "PlanOrderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "PlanFingerprint": "full-graph-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");
                using var engine = result.Engine;

                Assert.That(engine, Is.Not.Null);
                Assert.That(result.Config, Is.Not.Null);
                Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsOfficialGraph_WhenBootstrapOmitsFreshnessMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-missing-freshness-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "missing-freshness-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json"
                      },
                      "buildMode": "auto",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(modRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(modRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "missing-freshness-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("missing plan freshness metadata"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_ReadsGraphEmittedByOfficialLauncherBackend()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-official-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.Raylib",
                "bin",
                "Release",
                "net8.0",
                "launcher.runtime.json");
            var originalGraph = CaptureFile(graphPath);
            var originalBootstrap = CaptureFile(bootstrapPath);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var resolve = launcher.Resolve(new[] { "mod:LudotsCoreMod" }, LauncherPlatformIds.Raylib, LauncherBuildMode.Never);
                var writtenBootstrapPath = launcher.WriteBootstrap(resolve.Plan);

                Assert.That(resolve.Plan.GraphArtifactPath, Is.EqualTo(graphPath));
                Assert.That(writtenBootstrapPath, Is.EqualTo(bootstrapPath));
                Assert.That(File.Exists(graphPath), Is.True);
                Assert.That(File.Exists(bootstrapPath), Is.True);
                var bootstrapJson = File.ReadAllText(bootstrapPath);
                Assert.That(bootstrapJson, Does.Contain("PlanSelectors"));
                Assert.That(bootstrapJson, Does.Contain("PlanRootModIds"));
                Assert.That(bootstrapJson, Does.Contain("PlanOrderedModIds"));

                var result = GameBootstrapper.InitializeFromBaseDirectory(resolve.Plan.AppOutputDirectory, bootstrapPath);

                try
                {
                    Assert.That(result.Engine.ModLoader.LoadedModIds, Is.EqualTo(resolve.Plan.OrderedModIds));
                    Assert.That(result.Config, Is.Not.Null);
                    Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
                }
                finally
                {
                    result.Engine.Dispose();
                }
            }
            finally
            {
                RestoreFile(graphPath, originalGraph);
                RestoreFile(bootstrapPath, originalBootstrap);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsStaleGraph_WhenBootstrapPlanDiffers()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-stale-plan-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var coreInputRoot = Path.Combine(repoRoot, "mods", "CoreInputMod");
            var coreRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "stale-plan-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json"
                      },
                      "buildMode": "never",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{coreRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(coreRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(coreRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanSelectors": [
                        "mod:CoreInputMod"
                      ],
                      "PlanRootModIds": [
                        "CoreInputMod"
                      ],
                      "PlanOrderedModIds": [
                        "LudotsCoreMod",
                        "CoreInputMod"
                      ],
                      "PlanFingerprint": "stale-plan-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                Assert.That(coreInputRoot, Does.Exist);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Stale launch graph rejected"));
                Assert.That(ex.Message, Does.Contain("selectors"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesCefBrowserRuntime_FromProviderPackageRoot()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-cef-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            var originalGraph = CaptureFile(graphPath);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var plan = launcher.Resolve(
                    new[] { "preset:browser_react_flow_cef_raylib" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                var runtime = plan.BrowserRuntime;
                string packageRootPath = Path.Combine(repoRoot, "BrowserRuntime", "cef");

                Assert.That(runtime, Is.Not.Null);
                Assert.That(runtime!.Provider, Is.EqualTo("cef"));
                Assert.That(runtime.ProviderAssemblyPath, Is.EqualTo(Path.Combine(packageRootPath, "Ludots.UI.Browser.Cef.dll")));
                Assert.That(runtime.RuntimeRootPath, Is.EqualTo(packageRootPath));
                Assert.That(runtime.ProviderProjectPath, Is.EqualTo(Path.Combine(
                    repoRoot,
                    "src",
                    "Libraries",
                    "Ludots.UI.Browser.Cef",
                    "Ludots.UI.Browser.Cef.csproj")));
            }
            finally
            {
                RestoreFile(graphPath, originalGraph);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesCapabilityStandardShowcases_AsOnlyAcceptanceRoots()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-capability-standard-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_static_presenter_30k" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardStaticPresenter30kMod",
                    expectedStartupMapId: "capability_standard_static_presenter_30k_showcase",
                    allowedModIds: new[] { "LudotsCoreMod", "CoreInputMod", "CapabilityStandardStaticPresenter30kMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_mass_navigation_large_world_10k" },
                        LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardMassNavigationLargeWorld10kMod",
                    expectedStartupMapId: "mass_navigation",
                    allowedModIds: new[]
                    {
                        "LudotsCoreMod",
                        "CoreInputMod",
                        "MassNavigationMod",
                        "CapabilityStandardMassNavigationLargeWorld10kMod"
                    },
                    requiredModIds: new[] { "LudotsCoreMod", "CoreInputMod", "MassNavigationMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$formation_capability_showcase" },
                        LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan,
                    expectedRootModId: "FormationCapabilityShowcaseMod",
                    expectedStartupMapId: "formation_capability_showcase",
                    allowedModIds: new[]
                    {
                        "LudotsCoreMod",
                        "CoreInputMod",
                        "CameraProfilesMod",
                        "MassNavigationMod",
                        "FormationCapabilityShowcaseMod"
                    },
                    requiredModIds: new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "MassNavigationMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_participant_views" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardParticipantViewsMod",
                    expectedStartupMapId: "capability_standard_participant_views",
                    allowedModIds: new[]
                    {
                        "LudotsCoreMod",
                        "CoreInputMod",
                        "ParticipantViewCapabilityMod",
                        "CapabilityStandardParticipantViewsMod"
                    },
                    requiredModIds: new[] { "LudotsCoreMod", "CoreInputMod", "ParticipantViewCapabilityMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_transport_network" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardTransportNetworkMod",
                    expectedStartupMapId: "capability_standard_transport_network",
                    allowedModIds: new[] { "LudotsCoreMod", "CoreInputMod", "CapabilityStandardTransportNetworkMod" });
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesProgressionScopeShowcase_AsSingleFeatureRoot()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-progression-scope-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var plan = launcher.Resolve(
                    new[] { "$progression_scope" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;

                Assert.That(plan.RootModIds, Is.EqualTo(new[] { "ProgressionScopeShowcaseMod" }));
                Assert.That(plan.OrderedModIds, Is.SubsetOf(new[]
                {
                    "LudotsCoreMod",
                    "CoreInputMod",
                    "EntityCommandPanelMod",
                    "ProgressionScopeShowcaseMod"
                }));
                Assert.That(plan.OrderedModIds, Does.Contain("ProgressionScopeShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsDemoMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsWar3TrainingShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsCncTrainingShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsSc2TrainingShowcaseMod"));

                var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
                Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo("progression_scope_showcase"));
                Assert.That(startupMapSetting.EffectiveSource, Does.Contain("ProgressionScopeShowcaseMod"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesAiShowcases_AsSingleFeatureRoots()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-ai-showcases-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var utilityPlan = launcher.Resolve(
                    new[] { "$utility_autocast_showcase" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                AssertAiShowcasePlan(
                    utilityPlan,
                    "UtilityAutocastShowcaseMod",
                    "utility_autocast_showcase",
                    new[]
                    {
                        "LudotsCoreMod",
                        "AIInspectorMod",
                        "UtilityAutocastShowcaseMod"
                    });

                var combatStancePlan = launcher.Resolve(
                    new[] { "$combat_stance_showcase" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                AssertAiShowcasePlan(
                    combatStancePlan,
                    "CombatStanceShowcaseMod",
                    "combat_stance_showcase",
                    new[]
                    {
                        "LudotsCoreMod",
                        "CombatStanceBehaviorMod",
                        "CombatStanceShowcaseMod"
                    });
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsUnknownLauncherMetadataFields()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-unknown-metadata-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "unknown-metadata-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json",
                        "unexpectedAdapterField": "must not be silently ignored"
                      },
                      "buildMode": "auto",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(modRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(modRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "unknown-metadata-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<Exception>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("unexpectedAdapterField"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsLaunchGraph_WhenDependencyOrderIsInvalid()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-graph-invalid-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var coreInputRoot = Path.Combine(repoRoot, "mods", "CoreInputMod");
            var coreRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "planFingerprint": "invalid-order-fingerprint",
                      "orderedModIds": [
                        "CoreInputMod",
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "CoreInputMod",
                          "rootPath": "{{coreInputRoot.Replace("\\", "\\\\")}}"
                        },
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{coreRoot.Replace("\\", "\\\\")}}"
                        }
                      ]
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "invalid-order-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Launch plan order is invalid"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_UsesGraphPlanOrder_AsRuntimeLoadOrder()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var lowPriorityMod = CreateTestMod(tempDirectory, "LowPriorityMod", priority: 0);
                var highPriorityMod = CreateTestMod(tempDirectory, "HighPriorityMod", priority: 100);
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-order-fingerprint",
                    orderedModIds: new[] { "LudotsCoreMod", "LowPriorityMod", "HighPriorityMod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "LowPriorityMod",
                        "rootPath": "{{lowPriorityMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "HighPriorityMod",
                        "rootPath": "{{highPriorityMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-order-fingerprint");

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");
                using var engine = result.Engine;

                Assert.That(engine.ModLoader.LoadedModIds, Is.EqualTo(new[] { "LudotsCoreMod", "LowPriorityMod", "HighPriorityMod" }),
                    "Graph-planned order should remain the runtime load order even when priority would have reordered an ad-hoc resolve path.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsBootstrapWithoutLaunchGraphMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-missing-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                File.WriteAllText(
                    Path.Combine(tempDirectory, "launcher.runtime.json"),
                    """
                    {
                      "PlanFingerprint": "graph-required-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("missing launch graph metadata"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsGraphDependencyOrderViolations()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-invalid-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var baseMod = CreateTestMod(tempDirectory, "BaseMod", priority: 0);
                var featureMod = CreateTestMod(
                    tempDirectory,
                    "FeatureMod",
                    priority: 0,
                    dependenciesJson: """
                    {
                      "BaseMod": "^1.0.0"
                    }
                    """);
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-invalid-order-fingerprint",
                    orderedModIds: new[] { "LudotsCoreMod", "FeatureMod", "BaseMod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "FeatureMod",
                        "rootPath": "{{featureMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "BaseMod",
                        "rootPath": "{{baseMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-invalid-order-fingerprint");

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Launch plan order is invalid"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsLaunchGraphModIdCaseAliases()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-case-alias-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-case-alias-fingerprint",
                    orderedModIds: new[] { "ludotscoremod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-case-alias-fingerprint");

                var ex = Assert.Throws<Exception>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("does not match plannedMods"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesRaylibClientParity_AsPresenterEraContract()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-raylib-client-parity-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var plan = launcher.Resolve(
                    new[] { "$raylib_client_parity" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;

                Assert.That(plan.RootModIds, Is.EqualTo(new[] { "RaylibClientParityShowcaseMod" }));
                Assert.That(plan.OrderedModIds, Does.Contain("RaylibClientParityShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Contain("RaylibPlatformMeshesMod"),
                    "raylib_client_parity must pull its building meshes from the platform mesh fixture, not the presenter showcase.");
                Assert.That(plan.OrderedModIds, Does.Not.Contain("PerformerBlacksmithShowcaseMod"),
                    "raylib_client_parity must not pull the legacy Performer blacksmith mod.");

                var modRoot = Path.Combine(
                    repoRoot,
                    "mods",
                    "showcases",
                    "raylib_client_parity",
                    "RaylibClientParityShowcaseMod");
                var presentationRoot = Path.Combine(modRoot, "assets", "Presentation");

                Assert.That(
                    File.Exists(Path.Combine(presentationRoot, "performers.json")),
                    Is.False,
                    "Legacy performers.json must no longer ship in the raylib_client_parity showcase.");
                Assert.That(
                    File.Exists(Path.Combine(presentationRoot, "presenters.json")),
                    Is.True,
                    "raylib_client_parity must ship a Presenter-era presenters.json.");

                string presentersJson = File.ReadAllText(Path.Combine(presentationRoot, "presenters.json"));
                Assert.That(presentersJson, Does.Contain("\"CreatePresenter\""));
                Assert.That(presentersJson, Does.Contain("\"DestroyPresenterScope\""));
                Assert.That(presentersJson, Does.Not.Contain("CreatePerformer"));
                Assert.That(presentersJson, Does.Not.Contain("DestroyPerformerScope"));

                string hostAssetsJson = File.ReadAllText(Path.Combine(presentationRoot, "host_assets.json"));
                Assert.That(hostAssetsJson, Does.Contain("PresenterBlacksmithShowcaseMod:"));
                Assert.That(hostAssetsJson, Does.Not.Contain("PerformerBlacksmithShowcaseMod:"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesEngineGallery_AsExecutableTargetWithoutModClosure()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-engine-gallery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            var originalGraph = CaptureFile(graphPath);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var plan = launcher.Resolve(
                    new[] { "$engine_gallery" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;

                Assert.That(plan.IsExecutableTarget, Is.True);
                Assert.That(plan.RootModIds, Is.Empty);
                Assert.That(plan.OrderedModIds, Is.Empty);
                Assert.That(plan.Mods, Is.Empty);
                Assert.That(plan.BootstrapArtifactStrategy, Is.EqualTo("none"));
                Assert.That(plan.BootstrapArtifactPath, Is.Empty);
                Assert.That(plan.ExecutableArgs, Is.Empty);
                Assert.That(plan.ExecutableProjectPath, Is.EqualTo(Path.Combine(
                    repoRoot,
                    "src", "Apps", "Raylib", "Ludots.App.RaylibEngineGallery",
                    "Ludots.App.RaylibEngineGallery.csproj")));
                Assert.That(plan.AppAssemblyPath, Is.EqualTo(Path.Combine(
                    repoRoot,
                    "src", "Apps", "Raylib", "Ludots.App.RaylibEngineGallery",
                    "bin", "Release", "net8.0", "Ludots.App.RaylibEngineGallery.dll")));
            }
            finally
            {
                RestoreFile(graphPath, originalGraph);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_PresetArgs_ReplaceBindingArgs_OnExecutableTarget()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-executable-args-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            var originalGraph = CaptureFile(graphPath);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                var configPath = Path.Combine(tempDirectory, "launcher.config.json");
                var presetsPath = Path.Combine(tempDirectory, "launcher.presets.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");
                File.WriteAllText(configPath, $$"""
                {
                  "schemaVersion": 1,
                  "bindings": [
                    {
                      "name": "executable_fixture",
                      "target": {
                        "type": "project",
                        "value": "src/Apps/Raylib/Ludots.App.RaylibEngineGallery/Ludots.App.RaylibEngineGallery.csproj",
                        "args": ["--scene", "terrain"]
                      }
                    }
                  ],
                  "adapters": { "default": "raylib" }
                }
                """);
                File.WriteAllText(presetsPath, $$"""
                {
                  "schemaVersion": 1,
                  "presets": [
                    {
                      "id": "executable_fixture_skybox",
                      "name": "Executable Fixture Skybox",
                      "selectors": ["$executable_fixture"],
                      "adapterId": "raylib",
                      "buildMode": "auto",
                      "args": ["--scene", "skybox"]
                    }
                  ]
                }
                """);

                var launcher = new LauncherService(repoRoot, configPath, presetsPath, preferencesPath, userConfigPath);

                var presetPlan = launcher.Resolve(
                    new[] { "preset:executable_fixture_skybox" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                Assert.That(presetPlan.IsExecutableTarget, Is.True);
                Assert.That(presetPlan.ExecutableArgs, Is.EqualTo(new[] { "--scene", "skybox" }),
                    "Preset args must fully replace binding args when present.");

                var bindingPlan = launcher.Resolve(
                    new[] { "$executable_fixture" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                Assert.That(bindingPlan.IsExecutableTarget, Is.True);
                Assert.That(bindingPlan.ExecutableArgs, Is.EqualTo(new[] { "--scene", "terrain" }),
                    "Binding args apply when the preset defines no args.");
            }
            finally
            {
                RestoreFile(graphPath, originalGraph);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_FailsLoud_WhenProjectBindingPointsToMissingCsproj()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-executable-missing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                var configPath = Path.Combine(tempDirectory, "launcher.config.json");
                var presetsPath = Path.Combine(tempDirectory, "launcher.presets.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");
                File.WriteAllText(configPath, $$"""
                {
                  "schemaVersion": 1,
                  "bindings": [
                    {
                      "name": "missing_executable_fixture",
                      "target": {
                        "type": "project",
                        "value": "src/Apps/Raylib/DoesNotExist/DoesNotExist.csproj"
                      }
                    }
                  ],
                  "adapters": { "default": "raylib" }
                }
                """);
                File.WriteAllText(presetsPath, "{ \"schemaVersion\": 1, \"presets\": [] }");

                var launcher = new LauncherService(repoRoot, configPath, presetsPath, preferencesPath, userConfigPath);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => launcher.Resolve(new[] { "$missing_executable_fixture" }, LauncherPlatformIds.Raylib, LauncherBuildMode.Never));

                Assert.That(ex!.Message, Does.Contain("Executable project target not found"));
                Assert.That(ex.Message, Does.Contain("DoesNotExist.csproj"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        private static string CreateTestMod(string root, string modName, int priority, string? dependenciesJson = null)
        {
            var modDir = Path.Combine(root, modName);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(
                Path.Combine(modDir, "mod.json"),
                $$"""
                {
                  "name": "{{modName}}",
                  "version": "1.0.0",
                  "description": "test",
                  "main": "",
                  "priority": {{priority}},
                  "dependencies": {{dependenciesJson ?? "{}"}}
                }
                """);
            return modDir;
        }

        private static void WriteLaunchGraph(string graphPath, string planFingerprint, string[] orderedModIds, string plannedModsJson)
        {
            var orderedIdsJson = string.Join(
                "," + Environment.NewLine + "    ",
                orderedModIds.Select(id => $"\"{id}\""));
            File.WriteAllText(
                graphPath,
                $$"""
                {
                  "schemaVersion": 1,
                  "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                  "planFingerprint": "{{planFingerprint}}",
                  "orderedModIds": [
                    {{orderedIdsJson}}
                  ],
                  "plannedMods": {{plannedModsJson}}
                }
                """);
        }

        private static void WriteBootstrap(string bootstrapPath, string fingerprint)
        {
            File.WriteAllText(
                bootstrapPath,
                $$"""
                {
                  "LaunchGraphPath": "raylib.launch.graph.json",
                  "PlanFingerprint": "{{fingerprint}}",
                  "PlanSchemaVersion": 1
                }
                """);
        }

        private static void AssertCapabilityStandardPlan(
            LauncherLaunchPlan plan,
            string expectedRootModId,
            string expectedStartupMapId,
            string[] allowedModIds,
            string[]? requiredModIds = null)
        {
            Assert.That(plan.RootModIds, Is.EqualTo(new[] { expectedRootModId }));
            Assert.That(plan.OrderedModIds, Does.Contain(expectedRootModId));
            Assert.That(plan.OrderedModIds, Is.SubsetOf(allowedModIds));
            if (requiredModIds is not null)
            {
                foreach (var requiredModId in requiredModIds)
                {
                    Assert.That(plan.OrderedModIds, Does.Contain(requiredModId));
                }
            }

            Assert.That(plan.OrderedModIds, Does.Not.Contain("PresenterBlacksmithShowcaseMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("PresenterBlacksmithScatterHudTextBenchmarkEntryMod"));

            var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo(expectedStartupMapId));
            Assert.That(startupMapSetting.EffectiveSource, Does.Contain(expectedRootModId));
        }

        private static void AssertAiShowcasePlan(
            LauncherLaunchPlan plan,
            string expectedRootModId,
            string expectedStartupMapId,
            string[] allowedModIds)
        {
            Assert.That(plan.RootModIds, Is.EqualTo(new[] { expectedRootModId }));
            Assert.That(plan.OrderedModIds, Is.SubsetOf(allowedModIds));
            Assert.That(plan.OrderedModIds, Does.Contain(expectedRootModId));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("AIDemoMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsDemoMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("RelationshipShowcaseMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("FourXAssociationShowcaseMod"));

            var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo(expectedStartupMapId));
            Assert.That(startupMapSetting.EffectiveSource, Does.Contain(expectedRootModId));
        }

        private static FileSnapshot CaptureFile(string path)
        {
            return File.Exists(path)
                ? new FileSnapshot(true, File.ReadAllText(path))
                : new FileSnapshot(false, string.Empty);
        }

        private static void RestoreFile(string path, FileSnapshot snapshot)
        {
            if (snapshot.Exists)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, snapshot.Contents);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static LauncherService CreateLauncher(string repoRoot, string tempDirectory)
        {
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");
            return new LauncherService(
                repoRoot,
                Path.Combine(repoRoot, "launcher.config.json"),
                Path.Combine(repoRoot, "launcher.presets.json"),
                preferencesPath,
                userConfigPath);
        }

        private static LauncherNetworkProcessReadinessDefinition Readiness(
            string artifactFileName,
            int minimumReplicatedMirrorCount,
            int minimumRenderableMirrorCount)
        {
            return new LauncherNetworkProcessReadinessDefinition
            {
                ArtifactFileName = artifactFileName,
                MinimumReplicatedMirrorCount = minimumReplicatedMirrorCount,
                MinimumRenderableMirrorCount = minimumRenderableMirrorCount
            };
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private readonly record struct FileSnapshot(bool Exists, string Contents);
    }
}
