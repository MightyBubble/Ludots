using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Presenters
{
    public static class WorldTextArgs
    {
        public static bool UsesTemplateArgs(in WorldTextConfig worldText)
        {
            return worldText.Args != null && worldText.Args.Length > 0;
        }

        public static PresentationTextPacket Build(
            in WorldTextConfig worldText,
            int sentenceTokenId,
            Entity presenter,
            Entity owner,
            World world,
            PresenterEntityRuntime runtime,
            IDictionary<string, object> globals)
        {
            if (sentenceTokenId <= 0)
            {
                throw new InvalidOperationException("WorldText args require a positive text token id.");
            }

            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (globals == null) throw new ArgumentNullException(nameof(globals));

            PresentationTextCatalog catalog = RequireCatalog(globals);
            IEntityInfoTitleProfiles? profiles = FindTitleProfiles(globals);
            WorldTextArg[] args = worldText.Args ?? Array.Empty<WorldTextArg>();
            PresentationTextPacket packet = PresentationTextPacket.FromToken(sentenceTokenId);
            for (int i = 0; i < args.Length; i++)
            {
                packet.SetArg(i, ResolveArg(in args[i], i, presenter, owner, world, runtime, catalog, profiles));
            }

            return packet;
        }

        private static PresentationTextArg ResolveArg(
            in WorldTextArg arg,
            int index,
            Entity presenter,
            Entity owner,
            World world,
            PresenterEntityRuntime runtime,
            PresentationTextCatalog catalog,
            IEntityInfoTitleProfiles? profiles)
        {
            switch (arg.Source)
            {
                case WorldTextArgSource.Param:
                    if (!runtime.TryResolveFloat(presenter, arg.ParamKey, out float value))
                    {
                        throw new InvalidOperationException(
                            $"WorldText args[{index}].paramKey {arg.ParamKey} did not resolve to a float param value.");
                    }

                    return PresentationTextArg.FromInt32((int)value);

                case WorldTextArgSource.EntityInfoTitle:
                    return PresentationTextArg.FromTextToken(RequireTitleTokenId(owner, world, catalog, profiles));

                default:
                    throw new InvalidOperationException(
                        $"WorldText args[{index}] source '{arg.Source}' is not loaded.");
            }
        }

        private static int RequireTitleTokenId(
            Entity owner,
            World world,
            PresentationTextCatalog catalog,
            IEntityInfoTitleProfiles? profiles)
        {
            if (!EntityInfoTitles.TryGetTokenId(world, owner, catalog, profiles, out int tokenId))
            {
                throw new InvalidOperationException("WorldText entityInfoTitle has no title on this entity.");
            }

            if (!catalog.TryGetTokenDefinition(tokenId, out PresentationTextTokenDefinition definition) ||
                definition.ArgCount != 0)
            {
                throw new InvalidOperationException(
                    $"WorldText entityInfoTitle token id {tokenId} must be a zero-argument text token.");
            }

            return tokenId;
        }

        private static PresentationTextCatalog RequireCatalog(IDictionary<string, object> globals)
        {
            if (globals.TryGetValue(CoreServiceKeys.PresentationTextCatalog.Name, out object? value) &&
                value is PresentationTextCatalog catalog)
            {
                return catalog;
            }

            throw new InvalidOperationException("WorldText args require PresentationTextCatalog.");
        }

        private static IEntityInfoTitleProfiles? FindTitleProfiles(IDictionary<string, object> globals)
        {
            if (!globals.TryGetValue(CoreServiceKeys.EntityInfoTitleProfiles.Name, out object? value) || value == null)
            {
                return null;
            }

            if (value is IEntityInfoTitleProfiles profiles)
            {
                return profiles;
            }

            throw new InvalidOperationException(
                "EntityInfoTitleProfiles service is not IEntityInfoTitleProfiles.");
        }
    }
}
