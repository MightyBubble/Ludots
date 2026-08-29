using System;
using System.Collections.Generic;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>Reserved interaction mode ids owned by the engine.</summary>
    public static class InteractionModeIds
    {
        /// <summary>Sparse default: entities without <see cref="InteractionMode"/> are in this mode; it activates no input contexts.</summary>
        public const string Normal = "mode.normal";
    }

    /// <summary>
    /// Sparse simulation-side interaction mode: present only while the entity is in a
    /// non-default mode, absent otherwise. <see cref="ModeId"/> lives in the
    /// <see cref="InteractionModeMap.ModeIdRegistry"/> id space and rides world saves.
    /// </summary>
    public struct InteractionMode
    {
        public int ModeId;
    }

    /// <summary>One activated input context of a mode, as authored in <c>Input/interaction_modes.json</c>.</summary>
    public readonly record struct InteractionModeContextBinding(string ContextId, int Priority);

    /// <summary>Merged root of <c>Input/interaction_modes.json</c>.</summary>
    public sealed class InteractionModesConfig
    {
        public List<InteractionModeDefinition> Modes { get; set; }
    }

    /// <summary>
    /// One interaction mode: the input contexts the local projection activates while an entity
    /// holds this mode. Mode ids like <c>mode.targeting</c> are mod data, never Core concepts
    /// (except the reserved <see cref="InteractionModeIds.Normal"/>).
    /// </summary>
    public sealed class InteractionModeDefinition
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>IMC context ids pushed by the projection while the mode is active (empty allowed; required empty for the reserved normal mode).</summary>
        public List<InteractionModeContextRef> Contexts { get; set; }
    }

    /// <summary>
    /// One context entry of a mode. <see cref="Priority"/> restates the IMC context's own stack
    /// priority for readable tables; <see cref="InteractionModeMap.Install"/> fails fast on drift
    /// because <see cref="Runtime.PlayerInputHandler"/> owns the effective stack order.
    /// </summary>
    public sealed class InteractionModeContextRef
    {
        public string ContextId { get; set; } = string.Empty;
        public int Priority { get; set; }
    }
}
