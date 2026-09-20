using System;
using System.Collections.Generic;
using Ludots.Core.Hosting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Hosting
{
    [TestFixture]
    public sealed class AppHostLifecycleTests
    {
        [Test]
        public void LifecycleStateMachine_AdvancesCreatedThroughTerminated()
        {
            var lifecycle = new AppHostLifecycle(TestDescriptor());

            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Created));

            lifecycle.TransitionTo(AppLifecyclePhase.Configuring);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Configuring));
            lifecycle.TransitionTo(AppLifecyclePhase.Initialized);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Initialized));
            lifecycle.TransitionTo(AppLifecyclePhase.Running);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Running));
            lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.ShuttingDown));
            lifecycle.TransitionTo(AppLifecyclePhase.Terminated);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Terminated));
        }

        [Test]
        public void PhaseChanged_ReportsTransitionsInOrderWithDescriptor()
        {
            var lifecycle = new AppHostLifecycle(TestDescriptor());
            var events = new List<AppStateChangedEventArgs>();
            lifecycle.PhaseChanged += events.Add;

            lifecycle.TransitionTo(AppLifecyclePhase.Configuring);
            lifecycle.TransitionTo(AppLifecyclePhase.Initialized);
            lifecycle.TransitionTo(AppLifecyclePhase.Running);
            lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
            lifecycle.TransitionTo(AppLifecyclePhase.Terminated);

            var expected = new[]
            {
                (AppLifecyclePhase.Created, AppLifecyclePhase.Configuring),
                (AppLifecyclePhase.Configuring, AppLifecyclePhase.Initialized),
                (AppLifecyclePhase.Initialized, AppLifecyclePhase.Running),
                (AppLifecyclePhase.Running, AppLifecyclePhase.ShuttingDown),
                (AppLifecyclePhase.ShuttingDown, AppLifecyclePhase.Terminated)
            };
            Assert.That(events, Has.Count.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(events[i].PreviousPhase, Is.EqualTo(expected[i].Item1), $"previous of event {i}");
                Assert.That(events[i].NewPhase, Is.EqualTo(expected[i].Item2), $"new phase of event {i}");
                Assert.That(events[i].App, Is.EqualTo(lifecycle.Descriptor));
            }
        }

        [Test]
        public void Transition_RejectsDuplicateAndBackwardPhases()
        {
            var lifecycle = new AppHostLifecycle(TestDescriptor());
            AdvanceToRunning(lifecycle);

            Assert.That(() => lifecycle.TransitionTo(AppLifecyclePhase.Running), Throws.InvalidOperationException);
            Assert.That(() => lifecycle.TransitionTo(AppLifecyclePhase.Created), Throws.InvalidOperationException);
            Assert.That(() => lifecycle.TransitionTo(AppLifecyclePhase.Configuring), Throws.InvalidOperationException);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Running));
        }

        [Test]
        public void Transition_AllowsSuspendResumeCycleAndShutdownFromSuspend()
        {
            var lifecycle = new AppHostLifecycle(TestDescriptor());
            AdvanceToRunning(lifecycle);

            lifecycle.TransitionTo(AppLifecyclePhase.Suspending);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Suspending));
            lifecycle.TransitionTo(AppLifecyclePhase.Running);
            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Running));
            lifecycle.TransitionTo(AppLifecyclePhase.Suspending);
            lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
            lifecycle.TransitionTo(AppLifecyclePhase.Terminated);

            Assert.That(lifecycle.Phase, Is.EqualTo(AppLifecyclePhase.Terminated));
        }

        [Test]
        public void AppHostContract_InitializeThenRunEndsTerminated()
        {
            var host = new FakeAppHost("contract-app");
            var phases = new List<AppLifecyclePhase>();
            host.PhaseChanged += args => phases.Add(args.NewPhase);

            host.Initialize(new AppInitContext(@"C:\app", Array.Empty<string>(), AssetsRoot: null));
            Assert.That(host.Phase, Is.EqualTo(AppLifecyclePhase.Initialized));

            host.Run();
            Assert.That(host.Phase, Is.EqualTo(AppLifecyclePhase.Terminated));
            Assert.That(
                phases,
                Is.EqualTo(new[]
                {
                    AppLifecyclePhase.Configuring,
                    AppLifecyclePhase.Initialized,
                    AppLifecyclePhase.Running,
                    AppLifecyclePhase.ShuttingDown,
                    AppLifecyclePhase.Terminated
                }));
        }

        [Test]
        public void Registry_HoldsSingleHostAndRejectsDoubleRegistration()
        {
            var registry = new AppHostRegistry();
            var first = new FakeAppHost("app-one");

            registry.Register(first);

            Assert.That(registry.Current, Is.SameAs(first));
            Assert.That(registry.CurrentDescriptor, Is.Not.Null);
            Assert.That(registry.CurrentDescriptor!.AppId, Is.EqualTo("app-one"));

            var second = new FakeAppHost("app-two");
            Assert.That(() => registry.Register(second), Throws.InvalidOperationException);
            Assert.That(registry.Current, Is.SameAs(first));
        }

        [Test]
        public void Registry_RejectsNullHost()
        {
            var registry = new AppHostRegistry();

            Assert.That(() => registry.Register(null!), Throws.ArgumentNullException);
        }

        private static void AdvanceToRunning(AppHostLifecycle lifecycle)
        {
            lifecycle.TransitionTo(AppLifecyclePhase.Configuring);
            lifecycle.TransitionTo(AppLifecyclePhase.Initialized);
            lifecycle.TransitionTo(AppLifecyclePhase.Running);
        }

        private static AppDescriptor TestDescriptor()
        {
            return new AppDescriptor(
                "lifecycle-test-app",
                "desktop",
                "raylib",
                new Dictionary<string, string>());
        }

        private sealed class FakeAppHost : IAppHost
        {
            private readonly AppHostLifecycle _lifecycle;

            public FakeAppHost(string appId)
            {
                _lifecycle = new AppHostLifecycle(
                    new AppDescriptor(appId, "desktop", "raylib", new Dictionary<string, string>()));
            }

            public AppDescriptor Descriptor => _lifecycle.Descriptor;

            public AppLifecyclePhase Phase => _lifecycle.Phase;

            public event Action<AppStateChangedEventArgs>? PhaseChanged
            {
                add => _lifecycle.PhaseChanged += value;
                remove => _lifecycle.PhaseChanged -= value;
            }

            public void Initialize(AppInitContext context)
            {
                _lifecycle.TransitionTo(AppLifecyclePhase.Configuring);
                _lifecycle.TransitionTo(AppLifecyclePhase.Initialized);
            }

            public void Run()
            {
                _lifecycle.TransitionTo(AppLifecyclePhase.Running);
                _lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
                _lifecycle.TransitionTo(AppLifecyclePhase.Terminated);
            }

            public void RequestShutdown(string reason)
            {
                if (_lifecycle.Phase == AppLifecyclePhase.Running)
                {
                    _lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
                }
            }
        }
    }
}
