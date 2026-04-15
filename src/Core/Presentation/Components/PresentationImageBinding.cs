using System;

namespace Ludots.Core.Presentation.Components
{
    public enum PresentationImageRole : byte
    {
        Portrait = 0,
        Avatar = 1,
        CardArt = 2,
        Illustration = 3,
        Thumbnail = 4,
    }

    public enum PresentationImageState : byte
    {
        Default = 0,
        Selected = 1,
        Damaged = 2,
        Disabled = 3,
    }

    public struct PresentationImageBindingEntry
    {
        public PresentationImageRole Role;
        public PresentationImageState State;
        public int ImageAssetId;
    }

    public struct PresentationImageBinding
    {
        public const int Capacity = 16;

        private PresentationImageBindingEntry _entry0;
        private PresentationImageBindingEntry _entry1;
        private PresentationImageBindingEntry _entry2;
        private PresentationImageBindingEntry _entry3;
        private PresentationImageBindingEntry _entry4;
        private PresentationImageBindingEntry _entry5;
        private PresentationImageBindingEntry _entry6;
        private PresentationImageBindingEntry _entry7;
        private PresentationImageBindingEntry _entry8;
        private PresentationImageBindingEntry _entry9;
        private PresentationImageBindingEntry _entry10;
        private PresentationImageBindingEntry _entry11;
        private PresentationImageBindingEntry _entry12;
        private PresentationImageBindingEntry _entry13;
        private PresentationImageBindingEntry _entry14;
        private PresentationImageBindingEntry _entry15;

        public int Count;

        public void Set(PresentationImageRole role, PresentationImageState state, int imageAssetId)
        {
            if (imageAssetId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(imageAssetId), "Presentation image binding requires a positive image asset id.");
            }

            for (int i = 0; i < Count; i++)
            {
                PresentationImageBindingEntry existing = Get(i);
                if (existing.Role == role && existing.State == state)
                {
                    SetEntry(i, new PresentationImageBindingEntry
                    {
                        Role = role,
                        State = state,
                        ImageAssetId = imageAssetId,
                    });
                    return;
                }
            }

            if (Count >= Capacity)
            {
                throw new InvalidOperationException($"PresentationImageBinding supports at most {Capacity} entries.");
            }

            SetEntry(Count++, new PresentationImageBindingEntry
            {
                Role = role,
                State = state,
                ImageAssetId = imageAssetId,
            });
        }

        public bool TryGet(PresentationImageRole role, PresentationImageState state, out int imageAssetId)
        {
            for (int i = 0; i < Count; i++)
            {
                PresentationImageBindingEntry entry = Get(i);
                if (entry.Role == role && entry.State == state)
                {
                    imageAssetId = entry.ImageAssetId;
                    return true;
                }
            }

            imageAssetId = 0;
            return false;
        }

        public PresentationImageBindingEntry Get(int index)
        {
            return index switch
            {
                0 => _entry0,
                1 => _entry1,
                2 => _entry2,
                3 => _entry3,
                4 => _entry4,
                5 => _entry5,
                6 => _entry6,
                7 => _entry7,
                8 => _entry8,
                9 => _entry9,
                10 => _entry10,
                11 => _entry11,
                12 => _entry12,
                13 => _entry13,
                14 => _entry14,
                15 => _entry15,
                _ => throw new ArgumentOutOfRangeException(nameof(index), $"Presentation image binding index '{index}' is out of range."),
            };
        }

        private void SetEntry(int index, PresentationImageBindingEntry entry)
        {
            switch (index)
            {
                case 0: _entry0 = entry; break;
                case 1: _entry1 = entry; break;
                case 2: _entry2 = entry; break;
                case 3: _entry3 = entry; break;
                case 4: _entry4 = entry; break;
                case 5: _entry5 = entry; break;
                case 6: _entry6 = entry; break;
                case 7: _entry7 = entry; break;
                case 8: _entry8 = entry; break;
                case 9: _entry9 = entry; break;
                case 10: _entry10 = entry; break;
                case 11: _entry11 = entry; break;
                case 12: _entry12 = entry; break;
                case 13: _entry13 = entry; break;
                case 14: _entry14 = entry; break;
                case 15: _entry15 = entry; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), $"Presentation image binding index '{index}' is out of range.");
            }
        }
    }
}
