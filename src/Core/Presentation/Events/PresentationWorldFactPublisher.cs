using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Events
{
    /// <summary>
    /// Publishes world-space presentation facts into the existing presenter rule pipeline.
    /// It never writes presenter commands or render buffers directly.
    /// </summary>
    public readonly struct PresentationWorldFactPublisher
    {
        private readonly PresentationEventStream _events;
        private readonly GameSession? _session;

        public PresentationWorldFactPublisher(PresentationEventStream events, GameSession? session = null)
        {
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _session = session;
        }

        public static bool TryCreate(Dictionary<string, object> globals, out PresentationWorldFactPublisher publisher)
        {
            if (globals == null)
            {
                throw new ArgumentNullException(nameof(globals));
            }

            if (!globals.TryGetValue(CoreServiceKeys.PresentationEventStream.Name, out object? eventsObj) ||
                eventsObj is not PresentationEventStream events)
            {
                publisher = default;
                return false;
            }

            GameSession? session = globals.TryGetValue(CoreServiceKeys.GameSession.Name, out object? sessionObj) &&
                sessionObj is GameSession resolvedSession
                    ? resolvedSession
                    : null;
            publisher = new PresentationWorldFactPublisher(events, session);
            return true;
        }

        public void PublishWorldOverlayUpdated(
            string key,
            Entity owner,
            int scopeTag,
            Vector3 position,
            float radiusOrLength,
            float innerRadiusOrWidth = 0f,
            float rotationDeg = 0f,
            float angleDeg = 0f,
            float borderWidth = 0f,
            Entity target = default,
            Entity viewer = default)
        {
            Publish(
                PresentationEventKind.WorldOverlayUpdated,
                key,
                owner,
                scopeTag,
                position,
                radiusOrLength,
                innerRadiusOrWidth,
                rotationDeg,
                angleDeg,
                borderWidth,
                target,
                viewer);
        }

        public void PublishWorldOverlayEnded(string key, Entity owner, int scopeTag, Entity target = default, Entity viewer = default)
        {
            Publish(
                PresentationEventKind.WorldOverlayEnded,
                key,
                owner,
                scopeTag,
                default,
                0f,
                0f,
                0f,
                0f,
                0f,
                target,
                viewer);
        }

        public void PublishWorldHudUpdated(
            string key,
            Entity owner,
            int scopeTag,
            Vector3 position,
            float value,
            Entity target = default,
            Entity viewer = default)
        {
            Publish(
                PresentationEventKind.WorldHudUpdated,
                key,
                owner,
                scopeTag,
                position,
                value,
                0f,
                0f,
                0f,
                0f,
                target,
                viewer);
        }

        public void PublishWorldHudEnded(string key, Entity owner, int scopeTag, Entity target = default, Entity viewer = default)
        {
            Publish(
                PresentationEventKind.WorldHudEnded,
                key,
                owner,
                scopeTag,
                default,
                0f,
                0f,
                0f,
                0f,
                0f,
                target,
                viewer);
        }

        public void PublishWorldSplineUpdated(
            string key,
            Entity owner,
            int scopeTag,
            Vector3 start,
            Vector3 end,
            float width,
            float borderWidth = 0f,
            Entity target = default,
            Entity viewer = default)
        {
            Publish(
                PresentationEventKind.WorldSplineUpdated,
                key,
                owner,
                scopeTag,
                start,
                end.X,
                end.Y,
                end.Z,
                width,
                borderWidth,
                target,
                viewer);
        }

        public void PublishWorldSplineEnded(string key, Entity owner, int scopeTag, Entity target = default, Entity viewer = default)
        {
            Publish(
                PresentationEventKind.WorldSplineEnded,
                key,
                owner,
                scopeTag,
                default,
                0f,
                0f,
                0f,
                0f,
                0f,
                target,
                viewer);
        }

        public static int ComposeScope(string key, Entity entity, int discriminator = 0)
        {
            int scope = HashCode.Combine(key, entity.Id, entity.WorldId, entity.Version, discriminator) & int.MaxValue;
            return scope == 0 ? 1 : scope;
        }

        public static int ComposeScope(string key, int a, int b = 0, int c = 0)
        {
            int scope = HashCode.Combine(key, a, b, c) & int.MaxValue;
            return scope == 0 ? 1 : scope;
        }

        private void Publish(
            PresentationEventKind kind,
            string key,
            Entity owner,
            int scopeTag,
            Vector3 position,
            float floatA,
            float floatB,
            float floatC,
            float floatD,
            float magnitude,
            Entity target,
            Entity viewer)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Presentation fact key must be a non-empty semantic string.", nameof(key));
            }

            if (scopeTag <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeTag), "Presentation fact scopeTag must be positive.");
            }

            Entity normalizedOwner = owner == default ? Entity.Null : owner;
            Entity normalizedTarget = target == default ? normalizedOwner : target;
            var evt = new PresentationEvent
            {
                LogicTickStamp = _session?.CurrentTick ?? 0,
                Kind = kind,
                KeyId = PresentationEventKeyRegistry.Register(key),
                Source = normalizedOwner,
                Target = normalizedTarget,
                Viewer = viewer,
                PayloadA = scopeTag,
                Position = position,
                FloatA = floatA,
                FloatB = floatB,
                FloatC = floatC,
                FloatD = floatD,
                Magnitude = magnitude,
            };

            if (!_events.TryAdd(in evt))
            {
                throw new InvalidOperationException(
                    $"PresentationEventStream is full while publishing world presentation fact kind={kind}, key='{key}'.");
            }
        }
    }
}
