using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Godot
{
    /// <summary>
    /// IGameHost stub for Godot. Run() is not used — Godot drives the loop via _Process.
    /// Use GodotHostComposer.Compose + GodotHostLoop.Initialize + Tick instead.
    /// </summary>
    public sealed class GodotGameHost : IGameHost
    {
        public void Run()
        {
            throw new NotSupportedException("Godot drives the main loop. Use GodotHostComposer.Compose and GodotHostLoop instead.");
        }

        public void Dispose()
        {
        }
    }
}
