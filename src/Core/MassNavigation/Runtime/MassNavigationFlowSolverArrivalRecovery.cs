namespace Ludots.Core.MassNavigation.Runtime;

public sealed partial class MassNavigationFlowSolverState
{
    private void UpdateUnitStuckTimer(int index, float px, float py, float dt)
    {
        int i2 = index << 1;
        float dx = px - _unitProgressAnchorCm[i2];
        float dy = py - _unitProgressAnchorCm[i2 + 1];
        float progressDistanceCm = ArrivalTuning.ProgressDistanceCm;
        if ((dx * dx) + (dy * dy) >= progressDistanceCm * progressDistanceCm)
        {
            _unitProgressAnchorCm[i2] = px;
            _unitProgressAnchorCm[i2 + 1] = py;
            _unitStuckSeconds[index] = 0f;
            return;
        }

        _unitStuckSeconds[index] += dt;
    }

    private bool ShouldRetryTargetAfterPush(int index, float px, float py, float targetDistSq, float unitTargetStopThresholdSq)
    {
        if (_unitRetryCounts[index] >= ArrivalTuning.MaxRetryCount || targetDistSq <= unitTargetStopThresholdSq)
        {
            return false;
        }

        int i2 = index << 1;
        float dx = px - _unitSettledAnchorCm[i2];
        float dy = py - _unitSettledAnchorCm[i2 + 1];
        float wakeDistanceCm = ArrivalTuning.WakePushDistanceCm;
        return (dx * dx) + (dy * dy) >= wakeDistanceCm * wakeDistanceCm;
    }

    private float ResolveUnitTargetStopThresholdSq(int index, float configuredStopThresholdSq)
    {
        float explicitStopThresholdCm = _unitTargetStopThresholdsCm[index];
        return explicitStopThresholdCm > 0f
            ? explicitStopThresholdCm * explicitStopThresholdCm
            : configuredStopThresholdSq;
    }

    // Runs inside parallel step jobs: must only touch per-agent slots. Arrival events are
    // enqueued later by the single-threaded post-step scan (UpdateSettledUnitCount + EnqueueArrivalEvent).
    private void EnterSettledState(int index, float px, float py)
    {
        int i2 = index << 1;
        _unitSettledFlags[index] = 1;
        _unitSettledAnchorCm[i2] = px;
        _unitSettledAnchorCm[i2 + 1] = py;
        _unitProgressAnchorCm[i2] = px;
        _unitProgressAnchorCm[i2 + 1] = py;
        _unitStuckSeconds[index] = 0f;
        _velocitiesCm[i2] = 0f;
        _velocitiesCm[i2 + 1] = 0f;
    }

    private void ExitSettledState(int index, float px, float py)
    {
        _unitSettledFlags[index] = 0;
        if (_unitRetryCounts[index] < byte.MaxValue)
        {
            _unitRetryCounts[index]++;
        }

        ResetUnitProgressAnchor(index, px, py);
    }

    private void ResetUnitArrivalState(int index, bool clearRetryCount)
    {
        if ((uint)index >= (uint)UnitCount)
        {
            return;
        }

        int i2 = index << 1;
        float px = _positionsCm.Length > i2 ? _positionsCm[i2] : 0f;
        float py = _positionsCm.Length > i2 + 1 ? _positionsCm[i2 + 1] : 0f;
        _unitStuckSeconds[index] = 0f;
        _unitSettledFlags[index] = 0;
        _arrivalEventEmittedFlags[index] = 0;
        _unitProgressAnchorCm[i2] = px;
        _unitProgressAnchorCm[i2 + 1] = py;
        _unitSettledAnchorCm[i2] = px;
        _unitSettledAnchorCm[i2 + 1] = py;
        if (clearRetryCount)
        {
            _unitRetryCounts[index] = 0;
        }
    }

    private void EnqueueArrivalEvent(int index)
    {
        if (_arrivalEventEmittedFlags[index] != 0 || _arrivalEventCount >= UnitCount)
        {
            return;
        }

        _arrivalEventAgentIndices[_arrivalEventCount++] = index;
        _arrivalEventEmittedFlags[index] = 1;
    }

    private void ResetUnitProgressAnchor(int index, float px, float py)
    {
        int i2 = index << 1;
        _unitProgressAnchorCm[i2] = px;
        _unitProgressAnchorCm[i2 + 1] = py;
        _unitStuckSeconds[index] = 0f;
    }
}
