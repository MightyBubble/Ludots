namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct AbilityOnActivateEffects
    {
        public const int CAPACITY = 16;
        public fixed int TemplateIds[CAPACITY];
        public int Count;

        public bool Add(int templateId)
        {
            if (Count >= CAPACITY) return false;
            TemplateIds[Count++] = templateId;
            return true;
        }
    }
}

