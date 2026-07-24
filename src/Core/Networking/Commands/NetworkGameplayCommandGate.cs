using System;
using Ludots.Core.Gameplay.GAS.Orders;

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

        public bool TryAdmit(out OrderSubmitResult rejection)
        {
            rejection = Phase switch
            {
                NetworkGameplayCommandPhase.WaitingForMatch => OrderSubmitResult.NetworkMatchNotStarted,
                NetworkGameplayCommandPhase.Completed => OrderSubmitResult.NetworkMatchCompleted,
                _ => default,
            };
            return Phase == NetworkGameplayCommandPhase.Active;
        }
    }
}
