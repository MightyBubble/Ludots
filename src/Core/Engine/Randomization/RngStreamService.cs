using System;
using System.Collections.Generic;

namespace Ludots.Core.Engine.Randomization
{
    public interface IRngStreamService
    {
        IReadOnlyCollection<string> DeclaredStreamIds { get; }

        void DeclareStream(string streamId, uint seed);

        RngStream GetStream(string streamId);
    }

    /// <summary>
    /// Registry of named deterministic random streams. Streams must be declared
    /// before use; lookups of undeclared ids fail closed instead of falling back.
    /// </summary>
    public sealed class RngStreamService : IRngStreamService
    {
        private readonly Dictionary<string, RngStream> _streams = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> DeclaredStreamIds => _streams.Keys;

        public void DeclareStream(string streamId, uint seed)
        {
            if (string.IsNullOrWhiteSpace(streamId))
            {
                throw new ArgumentException("Stream id is required.", nameof(streamId));
            }

            if (_streams.ContainsKey(streamId))
            {
                throw new InvalidOperationException($"Rng stream '{streamId}' is already declared.");
            }

            _streams.Add(streamId, new RngStream(streamId, seed));
        }

        public RngStream GetStream(string streamId)
        {
            if (string.IsNullOrEmpty(streamId))
            {
                throw new ArgumentException("Stream id is required.", nameof(streamId));
            }

            if (_streams.TryGetValue(streamId, out var stream))
            {
                return stream;
            }

            throw new InvalidOperationException(
                $"Rng stream '{streamId}' is not declared. Declare it explicitly before use.");
        }
    }
}
