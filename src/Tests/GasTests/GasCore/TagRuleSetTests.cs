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
        private readonly TagOps _tagOps = new TagOps();
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
            // Arrange
            int tagId = 1;
            
            // Act
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            // Assert
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagId), Is.True);
            
            Console.WriteLine($"[TagRuleSetTests] TestAddTag_Basic: Tag {tagId} added successfully");
        }
        
        [Test]
        public void TestRemoveTag_Basic()
        {
            // Arrange
            int tagId = 1;
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            // Act
            bool result = _tagOps.RemoveTag(ref tagsRef, ref countsRef, tagId);
            
            // Assert
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagId), Is.False);
            
            Console.WriteLine($"[TagRuleSetTests] TestRemoveTag_Basic: Tag {tagId} removed successfully");
        }
        
        [Test]
        public void TestTagRuleSet_BlockedTags()
        {
            // Arrange
            int tagA = 1;
            int tagB = 2;
            
            // 注册TagRuleSet：tagB blocked tagA
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.BlockedTags[0] = tagA;
                ruleSet.BlockedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagB, ruleSet);
            
            // 先添加tagA
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            // Act: 尝试添加tagB（应该失败，因为tagA存在）
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagB);
            
            // Assert
            That(result, Is.False);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.False);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleSet_BlockedTags: Tag {tagB} blocked by {tagA}");
        }
        
        [Test]
        public void TestTagRuleSet_AttachedTags()
        {
            // Arrange
            int tagA = 1;
            int tagB = 2;
            
            // 注册TagRuleSet：tagA attached tagB
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.AttachedTags[0] = tagB;
                ruleSet.AttachedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagA, ruleSet);
            
            // Act: 添加tagA，应该自动添加tagB
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            // Assert
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.True);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleSet_AttachedTags: Tag {tagB} attached to {tagA}");
        }
        
        [Test]
        public void TestTagRuleSet_RemovedTags()
        {
            // Arrange
            int tagA = 1;
            int tagB = 2;
            
            // 注册TagRuleSet：tagA removed tagB
            var ruleSet = new TagRuleSet();
            unsafe
            {
                ruleSet.RemovedTags[0] = tagB;
                ruleSet.RemovedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagA, ruleSet);
            
            // 先添加tagB
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagB);
            
            // Act: 添加tagA，应该自动移除tagB
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            // Assert
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(_entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.False);
            
            Console.WriteLine($"[TagRuleSetTests] TestTagRuleSet_RemovedTags: Tag {tagB} removed by {tagA}");
        }
        
        [Test]
        public void TestTagCount_AddCount()
        {
            // Arrange
            int tagId = 1;
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            // Act: 多次添加同一Tag
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            // Assert
            ref var countContainer = ref _world.Get<TagCountContainer>(_entity);
            ushort count = countContainer.GetCount(tagId);
            That(count, Is.EqualTo(3));
            
            Console.WriteLine($"[TagRuleSetTests] TestTagCount_AddCount: Tag {tagId} count = {count}");
        }
        
        [Test]
        public void TestTagCount_RemoveCount()
        {
            // Arrange
            int tagId = 1;
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagId);
            
            // Act: 移除Tag
            _tagOps.RemoveTag(ref tagsRef, ref countsRef, tagId);
            
            // Assert
            ref var countContainer = ref _world.Get<TagCountContainer>(_entity);
            ushort count = countContainer.GetCount(tagId);
            That(count, Is.EqualTo(2));
            
            Console.WriteLine($"[TagRuleSetTests] TestTagCount_RemoveCount: Tag {tagId} count = {count}");
        }
        
        [Test]
        public void TestTagRuleTransaction_CyclePrevention()
        {
            // Arrange
            int tagA = 1;
            int tagB = 2;
            
            // 注册循环规则：tagA attached tagB, tagB attached tagA
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
            
            // Act: 添加tagA，应该触发循环，但ProcessedSet应该阻止
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(_entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(_entity);
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            // Assert: 应该成功添加tagA和tagB，但不会无限循环
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
