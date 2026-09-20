using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Dialogue
{
    /// <summary>
    /// Stable int ids for authored dialogue choice identities.
    /// Key is <c>{dialogueId}/{choiceId}</c> so choice ids stay unique across dialogues.
    /// </summary>
    public static class DialogueChoiceIdRegistry
    {
        private static StringIntRegistry _ids = CreateRegistry();

        public const int InvalidId = 0;
        public const int MaxChoices = 4095;

        public static bool IsFrozen => _ids.IsFrozen;

        public static void ResetForReload()
        {
            _ids = CreateRegistry();
        }

        public static void Freeze() => _ids.Freeze();

        public static int Register(string dialogueId, string choiceId)
        {
            if (_ids.IsFrozen)
            {
                throw new InvalidOperationException("DialogueChoiceIdRegistry is frozen.");
            }

            string key = ComposeKey(dialogueId, choiceId);
            int existing = _ids.GetId(key);
            if (existing != InvalidId)
            {
                return existing;
            }

            if (_ids.Count >= MaxChoices)
            {
                throw new InvalidOperationException(
                    $"DialogueChoiceIdRegistry supports up to {MaxChoices} choices.");
            }

            return _ids.Register(key);
        }

        public static int GetId(string dialogueId, string choiceId)
            => _ids.GetId(ComposeKey(dialogueId, choiceId));

        public static string GetName(int id) => _ids.GetName(id);

        public static bool TrySplit(int id, out string dialogueId, out string choiceId)
        {
            dialogueId = string.Empty;
            choiceId = string.Empty;
            string name = _ids.GetName(id);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            int slash = name.IndexOf('/');
            if (slash <= 0 || slash >= name.Length - 1)
            {
                return false;
            }

            dialogueId = name.Substring(0, slash);
            choiceId = name.Substring(slash + 1);
            return true;
        }

        public static string ComposeKey(string dialogueId, string choiceId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId))
            {
                throw new ArgumentException("Dialogue id is required.", nameof(dialogueId));
            }

            if (string.IsNullOrWhiteSpace(choiceId))
            {
                throw new ArgumentException("Choice id is required.", nameof(choiceId));
            }

            if (dialogueId.Contains('/'))
            {
                throw new ArgumentException(
                    $"Dialogue id '{dialogueId}' must not contain '/'.", nameof(dialogueId));
            }

            if (choiceId.Contains('/'))
            {
                throw new ArgumentException(
                    $"Choice id '{choiceId}' must not contain '/'.", nameof(choiceId));
            }

            return dialogueId + "/" + choiceId;
        }

        private static StringIntRegistry CreateRegistry()
            => new(
                capacity: MaxChoices + 1,
                startId: 1,
                invalidId: InvalidId,
                comparer: StringComparer.Ordinal);
    }
}

