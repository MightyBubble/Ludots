namespace Ludots.Core.Gameplay.Audio
{
    /// <summary>
    /// Platform-agnostic audio service interface.
    /// Designed to cover capabilities of professional audio middleware (WalaWala/Wwise/FMOD).
    /// </summary>
    public interface IAudioService
    {
        // ── Event ────────────────────────────────────────────────

        /// <summary>Post a global event.</summary>
        /// <returns>PlayingId for later stop/control; 0 on failure.</returns>
        ulong PostEvent(string eventName);

        /// <summary>Post an event on a registered game object.</summary>
        ulong PostEvent(string eventName, ulong gameObjectId);

        /// <summary>Stop a playing instance by its PlayingId.</summary>
        void Stop(ulong playingId, uint fadeMs = 0);

        /// <summary>Stop all playing instances on a game object.</summary>
        void StopAllOn(ulong gameObjectId, uint fadeMs = 0);

        /// <summary>Stop all playing instances routed through a bus.</summary>
        void StopBus(string busName, uint fadeMs = 0);

        /// <summary>Stop everything.</summary>
        void StopAll(uint fadeMs = 0);

        // ── Game Object & 3D ─────────────────────────────────────

        /// <summary>Register a game object for 3D positioning and per-object state.</summary>
        void RegisterGameObject(ulong gameObjectId);

        /// <summary>Unregister a game object, stopping all its voices.</summary>
        void UnregisterGameObject(ulong gameObjectId);

        /// <summary>Set a game object's 3D position.</summary>
        void SetPosition(ulong gameObjectId, float x, float y, float z);

        /// <summary>Set multi-position array for area sources (flat [x,y,z,...]).</summary>
        void SetMultiPositions(ulong gameObjectId, float[] positions);

        /// <summary>Set semantic for multi-position playback. 0=SingleSource, 1=MultiSources, 2=MultiDirections.</summary>
        void SetMultiPositionType(ulong gameObjectId, uint multiPositionType);

        /// <summary>Set the global listener position and orientation.</summary>
        void SetListener(float posX, float posY, float posZ,
                         float fwdX, float fwdY, float fwdZ,
                         float upX, float upY, float upZ);

        /// <summary>Toggle listener-relative positioning for a game object.</summary>
        void SetListenerRelative(ulong gameObjectId, bool relative);

        // ── RTPC ─────────────────────────────────────────────────

        /// <summary>Set a global RTPC value (immediate).</summary>
        void SetRTPC(string rtpcName, float value);

        /// <summary>Set a global RTPC value with interpolation curve.</summary>
        void SetRTPCCurve(string rtpcName, float value, uint interpolationMs);

        /// <summary>Set a per-object RTPC value.</summary>
        void SetRTPCOn(ulong gameObjectId, string rtpcName, float value);

        // ── State / Switch ───────────────────────────────────────

        void SetState(string stateGroup, string stateValue);
        void SetSwitch(ulong gameObjectId, string switchGroup, string switchValue);

        // ── Suspend / Resume ─────────────────────────────────────

        /// <summary>Suspend a set of buses. Returns a token for later resume.</summary>
        ulong Suspend(string[] buses, uint fadeMs = 0);

        /// <summary>Resume a previously suspended frame by token.</summary>
        void Resume(ulong token, uint fadeMs = 0);

        // ── Schedule ─────────────────────────────────────────────

        /// <summary>Schedule an event to fire after a delay on a game object.</summary>
        void Schedule(string eventName, ulong gameObjectId, uint delayMs);

        /// <summary>Return the current engine snapshot as JSON, or null when unavailable.</summary>
        string? GetSnapshotJson();
    }
}
