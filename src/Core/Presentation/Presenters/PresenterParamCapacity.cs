using System;

namespace Ludots.Core.Presentation.Presenters
{
    internal static class PresenterParamCapacity
    {
        internal const string Error = "PRESENTATION.PRESENTER.ERR.ParamCapacity";

        internal static InvalidOperationException Exceeded(ParamLane lane, bool defaults, int capacity, int key)
        {
            return new InvalidOperationException(
                $"{Error}: lane={lane}, storage={(defaults ? "defaults" : "overrides")}, capacity={capacity}, key={key}.");
        }

        internal static void Validate(
            ReadOnlySpan<ParamDefault> entries,
            string context,
            PresenterFloatParams floats = default,
            PresenterIntParams ints = default,
            PresenterVectorParams vectors = default)
        {
            try
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    ref readonly ParamDefault entry = ref entries[i];
                    switch (entry.Lane)
                    {
                        case ParamLane.Float: floats.Set(entry.ParamKey, entry.FloatValue); break;
                        case ParamLane.Int: ints.Set(entry.ParamKey, entry.IntValue); break;
                        case ParamLane.Vector: vectors.Set(entry.ParamKey, entry.VectorValue); break;
                        default: throw new ArgumentOutOfRangeException(nameof(entry.Lane), entry.Lane, "Unknown presenter parameter lane.");
                    }
                }
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"{context}: {error.Message}", error);
            }
        }
    }
}
