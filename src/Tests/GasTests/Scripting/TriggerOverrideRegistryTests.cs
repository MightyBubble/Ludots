using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Commands;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.Gas.Scripting
{
    /// <summary>
    /// C# trigger override contracts: Replace/Wrap validation, fail-closed arbitration by
    /// mod dependency order, and WrappedTrigger execution order (Pre → base → Post, with
    /// Cancel suppressing the base).
    /// </summary>
    [TestFixture]
    public sealed class TriggerOverrideRegistryTests
    {
        [Test]
        public void Register_ReplaceWithoutReplacement_FailsClosed()
        {
            var registry = new TriggerOverrideRegistry();
            var spec = new TriggerOverrideSpec
            {
                Id = "bad",
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Replace,
            };

            var ex = Throws<InvalidOperationException>(() => registry.Register(spec, "ModA"));
            That(ex!.Message, Does.Contain("Replace"));
            That(ex.Message, Does.Contain("Replacement"));
        }

        [Test]
        public void Register_WrapWithNoPrePostOrCancel_FailsClosed()
        {
            var registry = new TriggerOverrideRegistry();
            var spec = new TriggerOverrideSpec
            {
                Id = "noop",
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Wrap,
            };

            var ex = Throws<InvalidOperationException>(() => registry.Register(spec, "ModA"));
            That(ex!.Message, Does.Contain("no-op"));
        }

        [Test]
        public void Register_NonTriggerTarget_FailsClosed()
        {
            var registry = new TriggerOverrideRegistry();
            var spec = new TriggerOverrideSpec
            {
                Id = "nonTrigger",
                TargetType = typeof(string),
                Mode = TriggerOverrideMode.Replace,
                Replacement = _ => new BaseRecordingTrigger(),
            };

            var ex = Throws<InvalidOperationException>(() => registry.Register(spec, "ModA"));
            That(ex!.Message, Does.Contain("not a trigger type"));
        }

        [Test]
        public void Register_MissingId_FailsClosed()
        {
            var registry = new TriggerOverrideRegistry();
            var spec = new TriggerOverrideSpec
            {
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Replace,
                Replacement = _ => new BaseRecordingTrigger(),
            };

            Throws<ArgumentException>(() => registry.Register(spec, "ModA"));
        }

        [Test]
        public void Register_ValidReplace_ThenTryGetReturnsIt()
        {
            var registry = new TriggerOverrideRegistry();
            registry.Register(new TriggerOverrideSpec
            {
                Id = "swap",
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Replace,
                Replacement = _ => new ReplacementTrigger(),
            }, "ModA");

            That(registry.TryGetApplicable(typeof(BaseRecordingTrigger), out var found), Is.True);
            That(found.Spec.Id, Is.EqualTo("swap"));
            That(found.ModId, Is.EqualTo("ModA"));
        }

        [Test]
        public void Register_TwoIndependentModsSameTarget_FailsClosedAsDiamond()
        {
            var registry = new TriggerOverrideRegistry();
            registry.Register(Replace("r1", "ModA"), "ModA");

            var ex = Throws<InvalidOperationException>(
                () => registry.Register(Replace("r2", "ModB"), "ModB"));
            That(ex!.Message, Does.Contain("no dependency order"));
        }

        [Test]
        public void Register_SameModTwiceSameTarget_FailsClosed()
        {
            var registry = new TriggerOverrideRegistry();
            registry.Register(Replace("r1", "ModA"), "ModA");

            var ex = Throws<InvalidOperationException>(
                () => registry.Register(Replace("r2", "ModA"), "ModA"));
            That(ex!.Message, Does.Contain("same mod"));
        }

        [Test]
        public void Register_DownstreamModOverridesUpstream_Wins()
        {
            var registry = new TriggerOverrideRegistry();
            registry.SetDependencyClosure(Closure("ModB", "ModA")); // ModB depends on ModA.
            registry.Register(Replace("up", "ModA"), "ModA");
            registry.Register(Replace("down", "ModB"), "ModB");

            That(registry.TryGetApplicable(typeof(BaseRecordingTrigger), out var found), Is.True);
            That(found.Spec.Id, Is.EqualTo("down"), "Downstream mod must win the override.");
        }

        [Test]
        public void Register_UpstreamModAfterDownstream_FailsClosed()
        {
            var registry = new TriggerOverrideRegistry();
            registry.SetDependencyClosure(Closure("ModB", "ModA"));
            registry.Register(Replace("down", "ModB"), "ModB");

            var ex = Throws<InvalidOperationException>(
                () => registry.Register(Replace("up", "ModA"), "ModA"));
            That(ex!.Message, Does.Contain("cannot override"));
        }

        [Test]
        public void WrappedTrigger_ExecutesPreThenBaseThenPost()
        {
            var order = new List<string>();
            var baseTrigger = new BaseRecordingTrigger(order);
            var spec = new TriggerOverrideSpec
            {
                Id = "wrap",
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Wrap,
                Pre = new GameCommand[] { Record("pre", order) },
                Post = new GameCommand[] { Record("post", order) },
            };
            var wrapped = new WrappedTrigger(baseTrigger, spec);

            var context = new ScriptContext();
            wrapped.ExecuteAsync(context).GetAwaiter().GetResult();

            That(order, Is.EqualTo(new[] { "pre", "base", "post" }));
        }

        [Test]
        public void WrappedTrigger_CancelSuppressesBase()
        {
            var order = new List<string>();
            var baseTrigger = new BaseRecordingTrigger(order);
            var spec = new TriggerOverrideSpec
            {
                Id = "cancel",
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Wrap,
                Pre = new GameCommand[] { Record("pre", order) },
                Post = new GameCommand[] { Record("post", order) },
                Cancel = true,
            };
            var wrapped = new WrappedTrigger(baseTrigger, spec);

            var context = new ScriptContext();
            wrapped.ExecuteAsync(context).GetAwaiter().GetResult();

            That(order, Is.EqualTo(new[] { "pre", "post" }), "Cancel must suppress the base trigger.");
            That(wrapped.Name, Does.Contain("cancel"));
        }

        private static TriggerOverrideSpec Replace(string id, string modId)
            => new()
            {
                Id = id,
                TargetType = typeof(BaseRecordingTrigger),
                Mode = TriggerOverrideMode.Replace,
                Replacement = _ => new ReplacementTrigger(),
            };

        private static DelegateCommand Record(string label, List<string> order)
            => new(_ => order.Add(label));

        private static IReadOnlyDictionary<string, IReadOnlySet<string>> Closure(string modId, params string[] upstream)
            => new Dictionary<string, IReadOnlySet<string>>
            {
                [modId] = new HashSet<string>(upstream),
            };

        private sealed class BaseRecordingTrigger : Trigger
        {
            private readonly List<string> _order;

            public BaseRecordingTrigger()
                : this(new List<string>())
            {
            }

            public BaseRecordingTrigger(List<string> order)
            {
                _order = order;
            }

            public override Task ExecuteAsync(ScriptContext context)
            {
                _order.Add("base");
                return Task.CompletedTask;
            }
        }

        private sealed class ReplacementTrigger : Trigger
        {
        }
    }
}
