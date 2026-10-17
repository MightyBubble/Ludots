using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class TagRuleSetTests
    {
        private readonly TagOps _tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
        private World _world;
        private Entity _entity;
        
        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _entity = _world.Create();
            _world.Add(_entity, new GameplayTagContainer());
            _world.Add(_entity, new TagCountContainer());
            _tagOps.ClearRuleRegistry();
        }
        
        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }
        
        [Test]
        public void TestAddTag_Basic()
        {
            int tagId = 1;
            
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagId), Is.True);
            
            Console.WriteLine($"[TagRuleSetTests] TestAddTag_Basic: Tag {tagId} added successfully");
        }
        
        [Test]
        public void TestRemoveTag_Basic()
        {
            int tagId = 1;
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            bool result = _tagOps.RemoveTag(ref tagsRef, ref countsRef, tagId);
            
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagId), Is.False);
            
            Console.WriteLine($"[TagRuleSetTests] TestRemoveTag_Basic: Tag {tagId} removed successfully");
        }
        
        [Test]
        public void TestTagRuleSet_BlockedTags()
        {
            int tagA = 1;
            int tagB = 2;
            
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.BlockedTags[0] = tagA;
                ruleSet.BlockedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagB, ruleSet);
            
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagB);
            
            That(result, Is.False);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.False);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleSet_BlockedTags: Tag {tagB} blocked by {tagA}");
        }
        
        [Test]
        public void TestTagRuleSet_AttachedTags()
        {
            int tagA = 1;
            int tagB = 2;
            
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.AttachedTags[0] = tagB;
                ruleSet.AttachedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagA, ruleSet);
            
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.True);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleSet_AttachedTags: Tag {tagB} attached to {tagA}");
        }
        
        [Test]
        public void TestTagRuleSet_RemovedTags()
        {
            int tagA = 1;
            int tagB = 2;
            
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.RemovedTags[0] = tagB;
                ruleSet.RemovedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagA, ruleSet);
            
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagB);
            
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.False);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleSet_RemovedTags: Tag {tagB} removed by {tagA}");
        }
        
        [Test]
        public void TestTagCount_AddCount()
        {
            int tagId = 1;
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            ref var countContainer = ref _world.Get<TagCountContainer>(_entity);
            ushort count = countContainer.GetCount(tagId);
            That(count, Is.EqualTo(3));
            
            Console.WriteLine($"[TagRuleSetTests] TestTagCount_AddCount: Tag {tagId} count = {count}");
        }
        
        [Test]
        public void TestTagCount_RemoveCount()
        {
            int tagId = 1;
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            _tagOps.RemoveTag(ref tagsRef, ref countsRef, tagId);
            
            ref var countContainer = ref _world.Get<TagCountContainer>(_entity);
            ushort count = countContainer.GetCount(tagId);
            That(count, Is.EqualTo(2));
            
            Console.WriteLine($"[TagRuleSetTests] TestTagCount_RemoveCount: Tag {tagId} count = {count}");
        }
        
        [Test]
        public void TestTagRuleTransaction_CyclePrevention()
        {
            int tagA = 1;
            int tagB = 2;
            
            var ruleSetA = new TagRuleSet();
            unsafe
            {
                ruleSetA.AttachedTags[0] = tagB;
                ruleSetA.AttachedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagA, ruleSetA);
            
            var ruleSetB = new TagRuleSet();
            unsafe
            {
                ruleSetB.AttachedTags[0] = tagA;
                ruleSetB.AttachedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagB, ruleSetB);
            
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.True);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleTransaction_CyclePrevention: Cycle prevented, both tags added");
        }

        [Test]
        public void TestTagRuleSet_RequiredTags()
        {
            int tagA = 1;
            int tagB = 2;

            var ruleSetB = new TagRuleSet();
            unsafe
            {
                ruleSetB.RequiredTags[0] = tagA;
                ruleSetB.RequiredCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagB, ruleSetB);

            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);

            bool fail = _tagOps.AddTag(ref tagsRef, ref countsRef, tagB);
            That(fail, Is.False);

            bool okA = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            That(okA, Is.True);

            bool okB = _tagOps.AddTag(ref tagsRef, ref countsRef, tagB);
            That(okB, Is.True);
        }

        [Test]
        public void TestTagRuleSet_DisabledIfTags()
        {
            int tagA = 1;
            int tagB = 2;

            var ruleSetB = new TagRuleSet();
            unsafe
            {
                ruleSetB.DisabledIfTags[0] = tagA;
                ruleSetB.DisabledIfCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagB, ruleSetB);

            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            That(_tagOps.AddTag(ref tagsRef, ref countsRef, tagA), Is.True);
            That(_tagOps.AddTag(ref tagsRef, ref countsRef, tagB), Is.True);

            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(_tagOps.HasTag(ref tags, tagB, TagSense.Present), Is.True);
            That(_tagOps.HasTag(ref tags, tagB, TagSense.Effective), Is.False);
        }

        [Test]
        public void TestTagRuleSet_RemoveIfTags()
        {
            int tagA = 1;
            int tagB = 2;

            var ruleSetB = new TagRuleSet();
            unsafe
            {
                ruleSetB.RemoveIfTags[0] = tagA;
                ruleSetB.RemoveIfCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagB, ruleSetB);

            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            That(_tagOps.AddTag(ref tagsRef, ref countsRef, tagA), Is.True);
            That(_tagOps.AddTag(ref tagsRef, ref countsRef, tagB), Is.True);

            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagB), Is.False);
        }
    }
}
