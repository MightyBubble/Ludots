using System;
using System.Collections.Generic;

namespace Ludots.Core.Networking.Runtime
{
    public enum NetworkProcessRole : byte
    {
        Standalone = 0,
        AuthoritativeServer = 1,
        ReplicatedClient = 2,
    }

    public enum NetworkTransportPortOwnership : byte
    {
        Borrowed = 0,
        Owned = 1,
    }

    /// <summary>
    /// Platform-neutral lifecycle driven by the engine. Construction explicitly declares whether
    /// the runtime borrows transport ports or owns their disposal.
    /// </summary>
    public interface INetworkRuntimePort : IDisposable
    {
        NetworkProcessRole Role { get; }

        void PumpTransport();

        void BeforeAuthoritativeTick(uint executingTick);

        void AfterAuthoritativeCommit(uint committedTick);

        void PumpReplicatedClient(float frameDeltaTime);
    }

    internal static class NetworkTransportPortLifetime
    {
        public static void Validate(
            NetworkTransportPortOwnership ownership,
            object first,
            object second,
            object third)
        {
            if ((uint)ownership > (uint)NetworkTransportPortOwnership.Owned)
            {
                throw new ArgumentOutOfRangeException(nameof(ownership));
            }

            if (ownership == NetworkTransportPortOwnership.Borrowed)
            {
                return;
            }

            RequireDisposable(first, nameof(first));
            RequireDisposable(second, nameof(second));
            RequireDisposable(third, nameof(third));
        }

        public static void DisposeOwned(
            NetworkTransportPortOwnership ownership,
            object first,
            object second,
            object third)
        {
            if (ownership == NetworkTransportPortOwnership.Borrowed)
            {
                return;
            }

            List<Exception>? failures = null;
            DisposeAndCollect((IDisposable)first, ref failures);
            if (!ReferenceEquals(second, first))
            {
                DisposeAndCollect((IDisposable)second, ref failures);
            }

            if (!ReferenceEquals(third, first) && !ReferenceEquals(third, second))
            {
                DisposeAndCollect((IDisposable)third, ref failures);
            }

            if (failures != null)
            {
                throw new AggregateException("One or more owned network transport ports failed to dispose.", failures);
            }
        }

        private static void DisposeAndCollect(IDisposable port, ref List<Exception>? failures)
        {
            try
            {
                port.Dispose();
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>(capacity: 3);
                failures.Add(exception);
            }
        }

        private static void RequireDisposable(object port, string parameterName)
        {
            if (port is not IDisposable)
            {
                throw new ArgumentException(
                    $"Owned network transport port '{port.GetType().FullName}' must implement IDisposable.",
                    parameterName);
            }
        }
    }
}
