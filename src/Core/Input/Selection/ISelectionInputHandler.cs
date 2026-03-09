using System;
using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Input.Selection
{
    public enum SelectionCommandKind : byte
    {
        None = 0,
        SelectAtPoint = 1,
        SelectInRectangle = 2,
        SelectInPolygon = 3,
        Clear = 4,
        SaveGroup = 5,
        RecallGroup = 6,
    }

    public enum SelectionApplyMode : byte
    {
        Replace = 0,
        Add = 1,
        Toggle = 2,
    }

    public struct SelectionInputCommand
    {
        public SelectionCommandKind Kind;
        public SelectionApplyMode ApplyMode;
        public Vector2 PointScreen;
        public Vector2 RectangleMinScreen;
        public Vector2 RectangleMaxScreen;
        public Vector2[]? PolygonScreen;
        public int GroupIndex;
        public float PickRadiusPx;
        public bool ExpandSameClassFromResolvedCandidate;
        public Entity ExplicitTarget;

        public static SelectionInputCommand CreateClear()
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.Clear,
                ApplyMode = SelectionApplyMode.Replace,
            };
        }

        public static SelectionInputCommand CreateSaveGroup(int groupIndex)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SaveGroup,
                ApplyMode = SelectionApplyMode.Replace,
                GroupIndex = groupIndex,
            };
        }

        public static SelectionInputCommand CreateRecallGroup(int groupIndex)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.RecallGroup,
                ApplyMode = SelectionApplyMode.Replace,
                GroupIndex = groupIndex,
            };
        }

        public static SelectionInputCommand CreatePoint(
            Vector2 pointScreen,
            float pickRadiusPx,
            SelectionApplyMode applyMode,
            bool expandSameClass)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SelectAtPoint,
                ApplyMode = applyMode,
                PointScreen = pointScreen,
                PickRadiusPx = pickRadiusPx,
                ExpandSameClassFromResolvedCandidate = expandSameClass,
            };
        }

        public static SelectionInputCommand CreateRectangle(
            Vector2 rectangleMinScreen,
            Vector2 rectangleMaxScreen,
            SelectionApplyMode applyMode)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SelectInRectangle,
                ApplyMode = applyMode,
                RectangleMinScreen = rectangleMinScreen,
                RectangleMaxScreen = rectangleMaxScreen,
            };
        }

        public static SelectionInputCommand CreatePolygon(
            Vector2[] polygonScreen,
            SelectionApplyMode applyMode)
        {
            return new SelectionInputCommand
            {
                Kind = SelectionCommandKind.SelectInPolygon,
                ApplyMode = applyMode,
                PolygonScreen = polygonScreen,
            };
        }
    }

    public interface ISelectionInputHandler
    {
        void Update(float dt);
        bool Poll(out SelectionInputCommand command);
    }
}
