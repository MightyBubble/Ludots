using System;
using Ludots.Core.Networking.Runtime;

namespace Ludots.Core.Hosting
{
    public sealed class NetworkHostBootstrapConfig
    {
        public string ProcessRole { get; set; } = string.Empty;

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; }

        public string ConnectionKey { get; set; } = string.Empty;

        public int ClientInstanceId { get; set; }

        public string CredentialPath { get; set; } = string.Empty;

        /// <summary>Deterministic fault-injection PRNG seed (parsed as uint).</summary>
        public string FaultInjectionSeed { get; set; } = "0";

        /// <summary>When set, adapter writes NetworkRuntimeProofDocument JSON periodically.</summary>
        public string ProofPath { get; set; } = string.Empty;

        public int ProofIntervalMilliseconds { get; set; } = 500;

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

        public uint ParseFaultInjectionSeed()
        {
            if (!uint.TryParse(FaultInjectionSeed, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint seed))
            {
                throw new InvalidOperationException(
                    $"Network host faultInjectionSeed must be a uint; got '{FaultInjectionSeed}'.");
            }

            return seed;
        }

        public void Validate()
        {
            NetworkProcessRole role = ResolveRole();
            if ((uint)(Port - 1) >= ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Network host port must be between 1 and {ushort.MaxValue}; got {Port}.");
            }

            if (string.IsNullOrWhiteSpace(ConnectionKey))
            {
                throw new InvalidOperationException("Network host connectionKey is required.");
            }

            _ = ParseFaultInjectionSeed();

            if (ProofIntervalMilliseconds <= 0)
            {
                throw new InvalidOperationException(
                    $"Network host proofIntervalMilliseconds must be positive; got {ProofIntervalMilliseconds}.");
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
    }
}
