using System;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Typed fail-fast for an algorithm that cannot accept the supplied triangle-surface input.
    /// Never used to switch algorithms; callers must surface algorithm/input/reason explicitly.
    /// </summary>
    public sealed class NavBakeUnsupportedInputException : InvalidOperationException
    {
        public NavBakeUnsupportedInputException(
            NavBakeAlgorithmKind algorithm,
            string inputOwner,
            string reason)
            : base(BuildMessage(algorithm, inputOwner, reason))
        {
            Algorithm = algorithm;
            InputOwner = inputOwner ?? throw new ArgumentNullException(nameof(inputOwner));
            Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        }

        public NavBakeAlgorithmKind Algorithm { get; }

        public string InputOwner { get; }

        public string Reason { get; }

        private static string BuildMessage(NavBakeAlgorithmKind algorithm, string inputOwner, string reason)
        {
            if (string.IsNullOrWhiteSpace(inputOwner))
            {
                throw new ArgumentException("inputOwner is required.", nameof(inputOwner));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("reason is required.", nameof(reason));
            }

            return $"NavBake algorithm '{NavBakeNames.FormatAlgorithm(algorithm)}' rejected input '{inputOwner}': {reason}";
        }
    }
}
