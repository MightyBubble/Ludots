using System;
using Arch.Core;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph
{
    /// <summary>
    /// #1126 AwaitCallback: registration-ordered resume (not completion order);
    /// invalid / double-complete / dead target fail closed.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TriggerGraphAwaitCallbackTests
    {
        private const string DialogConfirm = "DialogConfirm";

        [SetUp]
        public void SetUp()
        {
            RecordingTarget.ResetResumeSequence();
        }

        [Test]
        public void AwaitCallback_CompletesInRegistrationOrder_NotCompletionOrder()
        {
            var callbacks = new GraphCallbackService();
            var first = new RecordingTarget();
            var second = new RecordingTarget();

            int firstHandle = BeginAwait(callbacks, first, DialogConfirm, resultBoolRegister: 0);
            int secondHandle = BeginAwait(callbacks, second, DialogConfirm, resultBoolRegister: 1);

            // Complete second first — resume order must still be first then second.
            callbacks.Complete(secondHandle, confirmed: false);
            callbacks.Complete(firstHandle, confirmed: true);
            callbacks.Drain();

            Assert.That(first.ResumeOrder, Is.EqualTo(1));
            Assert.That(second.ResumeOrder, Is.EqualTo(2));
            Assert.That(first.Confirmed, Is.True);
            Assert.That(second.Confirmed, Is.False);
            Assert.That(first.ResultBoolRegister, Is.EqualTo(0));
            Assert.That(second.ResultBoolRegister, Is.EqualTo(1));
        }

        [Test]
        public void Complete_UnknownHandle_FailsClosed()
        {
            var callbacks = new GraphCallbackService();
            var ex = Assert.Throws<InvalidOperationException>(() => callbacks.Complete(99, true));
            Assert.That(ex!.Message, Does.Contain("GRAPH.CALLBACK.ERR.InvalidHandle"));
        }

        [Test]
        public void Complete_Twice_FailsClosed()
        {
            var callbacks = new GraphCallbackService();
            var target = new RecordingTarget();
            int handle = BeginAwait(callbacks, target, DialogConfirm, resultBoolRegister: 0);
            callbacks.Complete(handle, true);
            var ex = Assert.Throws<InvalidOperationException>(() => callbacks.Complete(handle, false));
            Assert.That(ex!.Message, Does.Contain("GRAPH.CALLBACK.ERR.DoubleComplete"));
        }

        [Test]
        public void Drain_DeadTarget_FailsClosed()
        {
            var callbacks = new GraphCallbackService();
            var target = new RecordingTarget();
            int handle = BeginAwait(callbacks, target, DialogConfirm, resultBoolRegister: 0);
            callbacks.Complete(handle, true);
            target.Kill();
            var ex = Assert.Throws<InvalidOperationException>(() => callbacks.Drain());
            Assert.That(ex!.Message, Does.Contain("GRAPH.CALLBACK.ERR.ResumeTargetDead"));
            Assert.That(target.ResumeOrder, Is.EqualTo(0));
        }

        [Test]
        public void NestedResumeTargets_BindInnermostForBeginAwait()
        {
            var callbacks = new GraphCallbackService();
            var outer = new RecordingTarget();
            var inner = new RecordingTarget();

            callbacks.PushResumeTarget(outer);
            callbacks.PushResumeTarget(inner);
            int handle = callbacks.BeginAwait(DialogConfirm, default, Entity.Null, 0);
            callbacks.PopResumeTarget(inner);
            callbacks.PopResumeTarget(outer);

            callbacks.Complete(handle, true);
            callbacks.Drain();

            Assert.That(inner.ResumeOrder, Is.EqualTo(1));
            Assert.That(outer.ResumeOrder, Is.EqualTo(0), "waiter must bind the innermost PushResumeTarget");
        }

        [Test]
        public void BeginAwait_WithoutResumeTarget_FailsClosed()
        {
            var callbacks = new GraphCallbackService();
            var ex = Assert.Throws<InvalidOperationException>(
                () => callbacks.BeginAwait(DialogConfirm, default, Entity.Null, 0));
            Assert.That(ex!.Message, Does.Contain("GRAPH.CALLBACK.ERR.NoResumeTarget"));
        }

        [Test]
        public void TryGetLiveHandleForTarget_FindsRegisteredWaiter()
        {
            var callbacks = new GraphCallbackService();
            var target = new RecordingTarget();
            int handle = BeginAwait(callbacks, target, DialogConfirm, resultBoolRegister: 3);
            Assert.That(callbacks.TryGetLiveHandleForTarget(target, out int found), Is.True);
            Assert.That(found, Is.EqualTo(handle));
            Assert.That(callbacks.HasLiveWaiterForTarget(target), Is.True);
        }

        private static int BeginAwait(
            GraphCallbackService callbacks,
            IGraphCallbackResumeTarget target,
            string callbackType,
            int resultBoolRegister)
        {
            callbacks.PushResumeTarget(target);
            try
            {
                return callbacks.BeginAwait(callbackType, default(MapId), Entity.Null, resultBoolRegister);
            }
            finally
            {
                callbacks.PopResumeTarget(target);
            }
        }

        private sealed class RecordingTarget : IGraphCallbackResumeTarget
        {
            private static int s_resumeSeq;

            public static void ResetResumeSequence() => s_resumeSeq = 0;

            public int ResumeOrder { get; private set; }
            public bool Confirmed { get; private set; }
            public int ResultBoolRegister { get; private set; } = -1;
            public bool IsCallbackResumeAlive { get; private set; } = true;

            public void Kill() => IsCallbackResumeAlive = false;

            public void ResumeAfterGraphCallback(int handleId, bool confirmed, int resultBoolRegister)
            {
                ResumeOrder = ++s_resumeSeq;
                Confirmed = confirmed;
                ResultBoolRegister = resultBoolRegister;
            }
        }
    }
}
