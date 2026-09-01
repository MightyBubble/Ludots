using System;
using Ludots.Core.Networking.Configuration;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Core.Hosting
{
    public sealed class NetworkHostBootstrapConfig
    {
        public const string NormalFaultProfile = "normal";
        public const string UnstableFaultProfile = "unstable";

        public string ProcessRole { get; set; } = string.Empty;

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; }

        public string ConnectionKey { get; set; } = string.Empty;

        public int ClientInstanceId { get; set; }

        public string CredentialPath { get; set; } = string.Empty;

        public string ReadinessArtifactPath { get; set; } = string.Empty;

        public string FaultProfile { get; set; } = string.Empty;

        public int FaultSeed { get; set; }

        public NetworkProcessRole ResolveRole()
        {
            return ProcessRole switch
            {
                "authoritativeServer" => NetworkProcessRole.AuthoritativeServer,
                "replicatedClient" => NetworkProcessRole.ReplicatedClient,
                _ => throw new InvalidOperationException(
                    $"Unknown network processRole '{ProcessRole}'. Expected authoritativeServer or replicatedClient."),
            };
        }

        public NetworkFaultProfileConfig ResolveFaultProfile(NetworkRuntimeConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return FaultProfile switch
            {
                NormalFaultProfile => config.NormalConnection,
                UnstableFaultProfile => config.UnstableConnection,
                _ => throw new InvalidOperationException(
                    $"Unknown network faultProfile '{FaultProfile}'. Expected {NormalFaultProfile} or {UnstableFaultProfile}."),
            };
        }

        public void Validate()
        {
            NetworkProcessRole role = ResolveRole();
            ValidateReadinessArtifactPath();
            if ((uint)(Port - 1) >= ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Network host port must be between 1 and {ushort.MaxValue}; got {Port}.");
            }

            if (string.IsNullOrWhiteSpace(ConnectionKey))
            {
                throw new InvalidOperationException("Network host connectionKey is required.");
            }

            if (FaultProfile != NormalFaultProfile && FaultProfile != UnstableFaultProfile)
            {
                throw new InvalidOperationException(
                    $"Network host faultProfile must be '{NormalFaultProfile}' or '{UnstableFaultProfile}'.");
            }

            if (FaultSeed <= 0)
            {
                throw new InvalidOperationException("Network host faultSeed must be positive.");
            }

            if (role == NetworkProcessRole.AuthoritativeServer)
            {
                if (ClientInstanceId != 0 || !string.IsNullOrEmpty(CredentialPath))
                {
                    throw new InvalidOperationException(
                        "Authoritative server bootstrap must not declare clientInstanceId or credentialPath.");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new InvalidOperationException("Replicated client bootstrap host is required.");
            }

            if (ClientInstanceId <= 0)
            {
                throw new InvalidOperationException(
                    "Replicated client bootstrap clientInstanceId must be positive.");
            }

            if (string.IsNullOrWhiteSpace(CredentialPath))
            {
                throw new InvalidOperationException(
                    "Replicated client bootstrap credentialPath is required for reconnect continuity.");
            }
        }

        public string? ResolveReadinessArtifactPath(string runtimeBaseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeBaseDirectory);
            ValidateReadinessArtifactPath();
            if (string.IsNullOrEmpty(ReadinessArtifactPath))
            {
                return null;
            }

            string baseDirectory = Path.GetFullPath(runtimeBaseDirectory);
            string resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, ReadinessArtifactPath));
            string relativeToBase = Path.GetRelativePath(baseDirectory, resolvedPath);
            if (Path.IsPathRooted(relativeToBase) ||
                relativeToBase.Equals("..", StringComparison.Ordinal) ||
                relativeToBase.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relativeToBase.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Network readinessArtifactPath must resolve inside runtimeBaseDirectory.");
            }

            return resolvedPath;
        }

        private void ValidateReadinessArtifactPath()
        {
            if (string.IsNullOrEmpty(ReadinessArtifactPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ReadinessArtifactPath) ||
                Path.IsPathRooted(ReadinessArtifactPath))
            {
                throw new InvalidOperationException(
                    "Network readinessArtifactPath must be a non-empty relative path when configured.");
            }

            string fileName = Path.GetFileName(ReadinessArtifactPath);
            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(stem) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Network readinessArtifactPath must end in a valid named .json file.");
            }
        }
    }
}
