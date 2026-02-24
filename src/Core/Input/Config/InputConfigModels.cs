using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Ludots.Core.Input.Config
{
    public enum InputActionType
    {
        Button,
        Axis1D,
        Axis2D,
        Axis3D
    }

    /// <summary>
    /// Definition of an Input Action (e.g., "Move", "Jump").
    /// </summary>
    public class InputActionDef
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public InputActionType Type { get; set; } = InputActionType.Button;
    }

    /// <summary>
    /// Definition of a parameter for processors or interactions.
    /// </summary>
    public class InputParameterDef
    {
        public string Name { get; set; }
        public float Value { get; set; }
    }

    /// <summary>
    /// Definition of a modifier (Processor or Interaction).
    /// </summary>
    public class InputModifierDef
    {
        public string Type { get; set; } // e.g. "Deadzone", "Invert"
        public List<InputParameterDef> Parameters { get; set; } = new List<InputParameterDef>();
    }

    /// <summary>
    /// Definition of a binding between a physical path and an action.
    /// </summary>
    public class InputBindingDef
    {
        public string ActionId { get; set; }
        public string Path { get; set; } // e.g. "<Keyboard>/w"
        
        // For Composites (e.g. 2D Vector from WASD)
        public string CompositeType { get; set; } // e.g. "2DVector"
        public List<InputBindingDef> CompositeParts { get; set; } // e.g. Up, Down, Left, Right parts

        public List<InputModifierDef> Processors { get; set; } = new List<InputModifierDef>();
        public List<InputModifierDef> Interactions { get; set; } = new List<InputModifierDef>();
    }

    /// <summary>
    /// Definition of an Input Mapping Context (IMC).
    /// A collection of bindings that can be enabled/disabled together.
    /// </summary>
    public class InputContextDef
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public int Priority { get; set; } = 0;
        public List<InputBindingDef> Bindings { get; set; } = new List<InputBindingDef>();
    }

    /// <summary>
    /// Root configuration file structure.
    /// </summary>
    public class InputConfigRoot
    {
        public List<InputActionDef> Actions { get; set; } = new List<InputActionDef>();
        public List<InputContextDef> Contexts { get; set; } = new List<InputContextDef>();
    }
}
