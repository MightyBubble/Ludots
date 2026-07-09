using System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Modding
{
    public interface IModExtensionRegistration
    {
        IGasModExtensionRegistration Gas { get; }
        IPresentationModExtensionRegistration Presentation { get; }
    }

    public interface IGasModExtensionRegistration
    {
        int RegisterBuiltinHandler(string key, BuiltinHandlerFn handler);

        int RegisterGraphOp(
            string key,
            GraphValueType outputType,
            GasGraphOpHandler handler,
            params GraphValueType[] inputTypes);

        int RegisterGraphOp(
            string key,
            GraphValueType outputType,
            byte? fixedRegister,
            GasGraphOpHandler handler,
            params GraphValueType[] inputTypes);
    }

    public interface IPresentationModExtensionRegistration
    {
        int RegisterPerformerCommand(
            string key,
            in PerformerCommandExtensionDescriptor descriptor);

        int RegisterPerformerBehavior(
            string key,
            in PerformerBehaviorExtensionDescriptor descriptor);
    }

    /// <summary>
    /// Startup-only extension surface exposed to code mods during IMod.OnLoad.
    /// Registrations are frozen before config compilation starts.
    /// </summary>
    public sealed class ModExtensionHub
    {
        public ModExtensionHub()
        {
            Gas = new GasModExtensions();
            Presentation = new PresentationModExtensions();
            Reset();
        }

        internal GasModExtensions Gas { get; }
        internal PresentationModExtensions Presentation { get; }
        public bool IsFrozen { get; private set; }

        internal IModExtensionRegistration CreateRegistrationFacade(string modId)
        {
            return new ModExtensionRegistration(modId, this);
        }

        internal void Reset()
        {
            Gas.Reset();
            Presentation.Reset();
            IsFrozen = false;
        }

        internal void Freeze()
        {
            Gas.Freeze();
            Presentation.Freeze();
            IsFrozen = true;
        }
    }

    internal sealed class GasModExtensions
    {
        public BuiltinHandlerRegistry BuiltinHandlers { get; } = new();
        public GasGraphOpRegistry GraphOps { get; } = new();

        internal void Reset()
        {
            BuiltinHandlers.Clear();
            Ludots.Core.Gameplay.GAS.BuiltinHandlers.RegisterAll(BuiltinHandlers);
            GraphOps.Clear();
        }

        internal void Freeze()
        {
            BuiltinHandlers.Freeze();
            GraphOps.Freeze();
        }
    }

    internal sealed class PresentationModExtensions
    {
        public PerformerCommandKindRegistry PerformerCommands { get; } = new();
        public PerformerBehaviorKindRegistry PerformerBehaviors { get; } = new();

        internal void Reset()
        {
            PerformerCommands.Clear();
            PerformerBehaviors.Clear();
        }

        internal void Freeze()
        {
            PerformerCommands.Freeze();
            PerformerBehaviors.Freeze();
        }
    }

    internal sealed class ModExtensionRegistration : IModExtensionRegistration
    {
        public ModExtensionRegistration(string modId, ModExtensionHub hub)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Mod id must not be null or whitespace.", nameof(modId));
            }

            Gas = new GasModExtensionRegistration(modId, hub);
            Presentation = new PresentationModExtensionRegistration(modId, hub);
        }

        public IGasModExtensionRegistration Gas { get; }
        public IPresentationModExtensionRegistration Presentation { get; }
    }

    internal sealed class GasModExtensionRegistration : IGasModExtensionRegistration
    {
        private readonly string _modId;
        private readonly ModExtensionHub _hub;

        public GasModExtensionRegistration(string modId, ModExtensionHub hub)
        {
            _modId = modId;
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        }

        public int RegisterBuiltinHandler(string key, BuiltinHandlerFn handler)
        {
            ModExtensionKeyOwnership.RequireOwnedKey(_modId, key);
            return _hub.Gas.BuiltinHandlers.Register(key, handler);
        }

        public int RegisterGraphOp(
            string key,
            GraphValueType outputType,
            GasGraphOpHandler handler,
            params GraphValueType[] inputTypes)
        {
            ModExtensionKeyOwnership.RequireOwnedKey(_modId, key);
            return _hub.Gas.GraphOps.Register(key, outputType, handler, inputTypes);
        }

        public int RegisterGraphOp(
            string key,
            GraphValueType outputType,
            byte? fixedRegister,
            GasGraphOpHandler handler,
            params GraphValueType[] inputTypes)
        {
            ModExtensionKeyOwnership.RequireOwnedKey(_modId, key);
            return _hub.Gas.GraphOps.Register(key, outputType, fixedRegister, handler, inputTypes);
        }
    }

    internal sealed class PresentationModExtensionRegistration : IPresentationModExtensionRegistration
    {
        private readonly string _modId;
        private readonly ModExtensionHub _hub;

        public PresentationModExtensionRegistration(string modId, ModExtensionHub hub)
        {
            _modId = modId;
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        }

        public int RegisterPerformerCommand(
            string key,
            in PerformerCommandExtensionDescriptor descriptor)
        {
            ModExtensionKeyOwnership.RequireOwnedKey(_modId, key);
            return _hub.Presentation.PerformerCommands.Register(key, in descriptor);
        }

        public int RegisterPerformerBehavior(
            string key,
            in PerformerBehaviorExtensionDescriptor descriptor)
        {
            ModExtensionKeyOwnership.RequireOwnedKey(_modId, key);
            return _hub.Presentation.PerformerBehaviors.Register(key, in descriptor);
        }
    }

    internal static class ModExtensionKeyOwnership
    {
        public static void RequireOwnedKey(string modId, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Extension key must not be null or whitespace.", nameof(key));
            }

            string prefix = modId + ".";
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Extension key '{key}' must be prefixed with the loading mod id '{prefix}'.");
            }
        }
    }

}
