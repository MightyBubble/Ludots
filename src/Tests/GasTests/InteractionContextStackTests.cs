using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class InteractionContextStackTests
    {
        private static InteractionContextStack CreateStack()
        {
            var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            return new InteractionContextStack(collectionKeys);
        }

        private static long PushDefaultFrame(InteractionContextStack stack)
        {
            return stack.Push(InteractionContextFrameDescriptor.Create(
                InteractionContextIds.Default,
                EntityCollectionKeys.CommandSource,
                EntityViewKeys.ControlPlaneCommand));
        }

        private static InteractionContextFrameDescriptor Descriptor(
            string contextId,
            Entity contextEntity = default,
            string inputContextId = "")
        {
            return InteractionContextFrameDescriptor.Create(
                contextId,
                "collection.tests." + contextId,
                "view.tests." + contextId,
                contextEntity,
                filterProfileId: "filter.tests." + contextId,
                commandIntentProfileId: "",
                inputContextId: inputContextId);
        }

        [Test]
        public void Push_TopIsLastActivated_AndTryGetAtEnumeratesBottomUp()
        {
            InteractionContextStack stack = CreateStack();
            PushDefaultFrame(stack);
            long tokenA = stack.Push(Descriptor("ctx.a"), out InteractionContextFrame frameA);
            long tokenB = stack.Push(Descriptor("ctx.b"), out InteractionContextFrame frameB);

            Assert.That(stack.Count, Is.EqualTo(3));
            Assert.That(tokenA, Is.Not.EqualTo(tokenB));
            Assert.That(frameA.OwnerToken, Is.EqualTo(tokenA));

            Assert.That(stack.TryPeek(out InteractionContextFrame top), Is.True);
            Assert.That(top.OwnerToken, Is.EqualTo(tokenB));
            Assert.That(top.ContextId, Is.EqualTo(frameB.ContextId));

            Assert.That(stack.TryGetAt(0, out InteractionContextFrame bottom), Is.True);
            Assert.That(bottom.ContextId, Is.EqualTo(stack.ContextIdRegistry.GetId(InteractionContextIds.Default)));
            Assert.That(stack.TryGetAt(1, out InteractionContextFrame middle), Is.True);
            Assert.That(middle.OwnerToken, Is.EqualTo(tokenA));
            Assert.That(stack.TryGetAt(3, out _), Is.False);
        }

        [Test]
        public void RemoveByToken_RemovesMiddleFrame_TopPeekUnchanged()
        {
            InteractionContextStack stack = CreateStack();
            PushDefaultFrame(stack);
            long tokenA = stack.Push(Descriptor("ctx.a"));
            long tokenB = stack.Push(Descriptor("ctx.b"));

            Assert.That(stack.RemoveByToken(tokenA), Is.True);
            Assert.That(stack.Count, Is.EqualTo(2));
            Assert.That(stack.TryPeek(out InteractionContextFrame top), Is.True);
            Assert.That(top.OwnerToken, Is.EqualTo(tokenB));

            Assert.That(stack.RemoveByToken(tokenA), Is.False);
        }

        [Test]
        public void RemoveByContextEntity_RemovesAllOwnedFrames()
        {
            using var world = World.Create();
            Entity exec = world.Create();
            Entity otherExec = world.Create();
            InteractionContextStack stack = CreateStack();
            PushDefaultFrame(stack);
            stack.Push(Descriptor("ctx.a", exec));
            long otherToken = stack.Push(Descriptor("ctx.b", otherExec));
            stack.Push(Descriptor("ctx.c", exec));

            Assert.That(stack.RemoveByContextEntity(exec), Is.EqualTo(2));
            Assert.That(stack.Count, Is.EqualTo(2));
            Assert.That(stack.TryPeek(out InteractionContextFrame top), Is.True);
            Assert.That(top.OwnerToken, Is.EqualTo(otherToken));

            Assert.That(stack.RemoveByContextEntity(exec), Is.EqualTo(0));
            Assert.That(stack.RemoveByContextEntity(default), Is.EqualTo(0));
        }

        [Test]
        public void DefaultFrame_CannotBeRemovedByToken()
        {
            InteractionContextStack stack = CreateStack();
            long defaultToken = PushDefaultFrame(stack);

            Assert.That(() => stack.RemoveByToken(defaultToken), Throws.InvalidOperationException);
            Assert.That(stack.Count, Is.EqualTo(1));
        }

        [Test]
        public void Revision_BumpsOnEveryMutation()
        {
            InteractionContextStack stack = CreateStack();
            uint initial = stack.Revision;
            PushDefaultFrame(stack);
            uint afterDefault = stack.Revision;
            long token = stack.Push(Descriptor("ctx.a"));
            uint afterPush = stack.Revision;
            stack.RemoveByToken(token);
            uint afterRemove = stack.Revision;

            Assert.That(afterDefault, Is.GreaterThan(initial));
            Assert.That(afterPush, Is.GreaterThan(afterDefault));
            Assert.That(afterRemove, Is.GreaterThan(afterPush));
        }

        [Test]
        public void TransitionListener_ReceivesPushAndRemoveCallbacks()
        {
            InteractionContextStack stack = CreateStack();
            var listener = new RecordingListener();
            stack.AddTransitionListener(listener);

            long token = stack.Push(Descriptor("ctx.a", inputContextId: "imc.tests.a"), out InteractionContextFrame pushed);
            stack.RemoveByToken(token);

            Assert.That(listener.Pushed, Has.Count.EqualTo(1));
            Assert.That(listener.Removed, Has.Count.EqualTo(1));
            Assert.That(listener.Pushed[0].OwnerToken, Is.EqualTo(token));
            Assert.That(listener.Removed[0].OwnerToken, Is.EqualTo(token));
            Assert.That(listener.Pushed[0].InputContextId, Is.EqualTo(pushed.InputContextId));
            Assert.That(stack.InputContextIdRegistry.GetName(pushed.InputContextId), Is.EqualTo("imc.tests.a"));

            Assert.That(stack.RemoveTransitionListener(listener), Is.True);
            stack.Push(Descriptor("ctx.b"));
            Assert.That(listener.Pushed, Has.Count.EqualTo(1));
        }

        [Test]
        public void Push_SameStringKeysResolveToSameIds()
        {
            InteractionContextStack stack = CreateStack();
            long first = stack.Push(Descriptor("ctx.a"), out InteractionContextFrame frameFirst);
            long second = stack.Push(Descriptor("ctx.a"), out InteractionContextFrame frameSecond);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(frameSecond.ContextId, Is.EqualTo(frameFirst.ContextId));
            Assert.That(frameSecond.ActiveCollectionKeyId, Is.EqualTo(frameFirst.ActiveCollectionKeyId));
            Assert.That(frameSecond.ActiveEntityViewKeyId, Is.EqualTo(frameFirst.ActiveEntityViewKeyId));
            Assert.That(frameSecond.FilterProfileId, Is.EqualTo(frameFirst.FilterProfileId));
            Assert.That(frameSecond.CommandIntentProfileId, Is.EqualTo(0));
            Assert.That(frameSecond.InputContextId, Is.EqualTo(0));
        }

        [Test]
        public void Push_CollectionKeyIdSharesInjectedRegistrySpace()
        {
            var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int existingId = collectionKeys.Register(EntityCollectionKeys.CommandSource);
            var stack = new InteractionContextStack(collectionKeys);

            stack.Push(InteractionContextFrameDescriptor.Create(
                InteractionContextIds.Default,
                EntityCollectionKeys.CommandSource,
                EntityViewKeys.ControlPlaneCommand), out InteractionContextFrame frame);

            Assert.That(frame.ActiveCollectionKeyId, Is.EqualTo(existingId));
        }

        private sealed class RecordingListener : IInteractionContextTransition
        {
            public List<InteractionContextFrame> Pushed { get; } = new();
            public List<InteractionContextFrame> Removed { get; } = new();

            public void OnFramePushed(in InteractionContextFrame frame) => Pushed.Add(frame);

            public void OnFrameRemoved(in InteractionContextFrame frame) => Removed.Add(frame);
        }
    }
}
