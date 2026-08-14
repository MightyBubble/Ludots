using System;

namespace Ludots.Core.Networking.Commands
{
    public enum NetworkGameplayCommandPhase : byte
    {
        WaitingForMatch = 0,
        Active = 1,
        Completed = 2,
    }

    public sealed class NetworkGameplayCommandGate
    {
        public NetworkGameplayCommandPhase Phase { get; private set; }

        public void StartMatch()
        {
            if (Phase == NetworkGameplayCommandPhase.WaitingForMatch)
            {
                Phase = NetworkGameplayCommandPhase.Active;
            }
        }

        public void CompleteMatch()
        {
            if (Phase == NetworkGameplayCommandPhase.WaitingForMatch)
            {
                throw new InvalidOperationException("A network match cannot complete before it starts.");
            }

            Phase = NetworkGameplayCommandPhase.Completed;
        }

        public bool TryAdmit(out NetworkCommandAdmissionCode rejection)
        {
            switch (Phase)
            {
                case NetworkGameplayCommandPhase.WaitingForMatch:
                    rejection = NetworkCommandAdmissionCode.NetworkMatchNotStarted;
                    return false;
                case NetworkGameplayCommandPhase.Active:
                    rejection = default;
                    return true;
                case NetworkGameplayCommandPhase.Completed:
                    rejection = NetworkCommandAdmissionCode.NetworkMatchCompleted;
                    return false;
                default:
                    throw new InvalidOperationException($"Unknown network gameplay command phase {Phase}.");
            }
        }
    }
}
