using System;

namespace Ludots.Core.Networking.Commands
{
    public readonly struct NetworkCommandIngressConfig
    {
        public NetworkCommandIngressConfig(
            int seatCapacity,
            int simulationTickRateHz,
            int maxBatchesPerSecond,
            int burstBatchCapacity,
            int maxActorsPerBatch,
            int sequenceHistoryCapacity,
            int maxPastTargetTicks,
            int maxFutureTargetTicks,
            int scheduledBatchCapacity,
            int commandCorrelationCapacity)
        {
            SeatCapacity = RequirePositive(seatCapacity, nameof(seatCapacity));
            SimulationTickRateHz = RequirePositive(simulationTickRateHz, nameof(simulationTickRateHz));
            MaxBatchesPerSecond = RequirePositive(maxBatchesPerSecond, nameof(maxBatchesPerSecond));
            BurstBatchCapacity = RequirePositive(burstBatchCapacity, nameof(burstBatchCapacity));
            MaxActorsPerBatch = RequirePositive(maxActorsPerBatch, nameof(maxActorsPerBatch));
            SequenceHistoryCapacity = RequirePositive(sequenceHistoryCapacity, nameof(sequenceHistoryCapacity));
            if (maxPastTargetTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPastTargetTicks));
            }

            if (maxFutureTargetTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFutureTargetTicks));
            }

            MaxPastTargetTicks = maxPastTargetTicks;
            MaxFutureTargetTicks = maxFutureTargetTicks;
            ScheduledBatchCapacity = RequirePositive(scheduledBatchCapacity, nameof(scheduledBatchCapacity));
            CommandCorrelationCapacity = RequirePositive(commandCorrelationCapacity, nameof(commandCorrelationCapacity));
            if (SequenceHistoryCapacity < ScheduledBatchCapacity)
            {
                throw new ArgumentException(
                    "Sequence history capacity must cover every scheduled batch slot so pending outcomes cannot be overwritten.",
                    nameof(sequenceHistoryCapacity));
            }
        }

        public int SeatCapacity { get; }
        public int SimulationTickRateHz { get; }
        public int MaxBatchesPerSecond { get; }
        public int BurstBatchCapacity { get; }
        public int MaxActorsPerBatch { get; }
        public int SequenceHistoryCapacity { get; }
        public int MaxPastTargetTicks { get; }
        public int MaxFutureTargetTicks { get; }
        public int ScheduledBatchCapacity { get; }
        public int CommandCorrelationCapacity { get; }

        private static int RequirePositive(int value, string name)
        {
            return value > 0
                ? value
                : throw new ArgumentOutOfRangeException(name, value, $"{name} must be positive.");
        }
    }
}
