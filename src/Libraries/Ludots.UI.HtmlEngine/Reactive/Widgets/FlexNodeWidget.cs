using System;
using System.Collections.Generic;
using SkiaSharp;
using FlexLayoutSharp;
using Ludots.UI.Widgets;
using Ludots.UI.Input;

namespace Ludots.UI.Reactive.Widgets
{
    public class FlexNodeWidget : Widget
    {
        public Node FlexNode { get; private set; }
        private List<FlexNodeWidget> _children = new List<FlexNodeWidget>();
        public IReadOnlyList<FlexNodeWidget> Children => _children;

        // Style Properties
        public SKColor BackgroundColor { get; set; } = SKColors.Transparent;
        public SKColor BorderColor { get; set; } = SKColors.Transparent;
        public float BorderWidth { get; set; } = 0;
        public float BorderRadius { get; set; } = 0;
        
        // Text Properties (if this node is a text leaf)
        private string _text;
        public string Text 
        { 
            get => _text; 
            set 
            { 
                if (_text != value)
                {
                    _text = value;
                    UpdateMeasureFunc();
                    MarkDirty();
                }
            }
        }
        public float FontSize { get; set; } = 16;
        public SKColor TextColor { get; set; } = SKColors.Black;
        public SKTextAlign TextAlign { get; set; } = SKTextAlign.Left;

        // Flex Properties Wrappers
        public Direction Direction { get => FlexNode.StyleGetDirection(); set => FlexNode.StyleSetDirection(value); }
        public FlexDirection FlexDirection { get => FlexNode.StyleGetFlexDirection(); set => FlexNode.StyleSetFlexDirection(value); }
        public Justify JustifyContent { get => FlexNode.StyleGetJustifyContent(); set => FlexNode.StyleSetJustifyContent(value); }
        public Align AlignItems { get => FlexNode.StyleGetAlignItems(); set => FlexNode.StyleSetAlignItems(value); }
        public Align AlignSelf { get => FlexNode.StyleGetAlignSelf(); set => FlexNode.StyleSetAlignSelf(value); }
        public Align AlignContent { get => FlexNode.StyleGetAlignContent(); set => FlexNode.StyleSetAlignContent(value); }
        public PositionType PositionType { get => FlexNode.StyleGetPositionType(); set => FlexNode.StyleSetPositionType(value); }
        public Wrap FlexWrap { get => FlexNode.StyleGetFlexWrap(); set => FlexNode.StyleSetFlexWrap(value); }
        public float FlexGrow { get => FlexNode.StyleGetFlexGrow(); set => FlexNode.StyleSetFlexGrow(value); }
        public float FlexShrink { get => FlexNode.StyleGetFlexShrink(); set => FlexNode.StyleSetFlexShrink(value); }
        
        // Dimensions
        public void SetWidth(float val) => FlexNode.StyleSetWidth(val);
        public void SetHeight(float val) => FlexNode.StyleSetHeight(val);
        public void SetWidthPercent(float val) => FlexNode.StyleSetWidthPercent(val);
        public void SetHeightPercent(float val) => FlexNode.StyleSetHeightPercent(val);
        
        // Padding/Margin Helpers
        public float Padding { set => FlexNode.StyleSetPadding(Edge.All, value); }
        public float PaddingLeft { set => FlexNode.StyleSetPadding(Edge.Left, value); }
        public float PaddingTop { set => FlexNode.StyleSetPadding(Edge.Top, value); }
        public float PaddingRight { set => FlexNode.StyleSetPadding(Edge.Right, value); }
        public float PaddingBottom { set => FlexNode.StyleSetPadding(Edge.Bottom, value); }

        public float Margin { set => FlexNode.StyleSetMargin(Edge.All, value); }
        public float MarginLeft { set => FlexNode.StyleSetMargin(Edge.Left, value); }
        public float MarginTop { set => FlexNode.StyleSetMargin(Edge.Top, value); }
        public float MarginRight { set => FlexNode.StyleSetMargin(Edge.Right, value); }
        public float MarginBottom { set => FlexNode.StyleSetMargin(Edge.Bottom, value); }
        
        // Events
        public Action OnClick { get; set; }

        public FlexNodeWidget()
        {
            FlexNode = Flex.CreateDefaultNode();
            FlexNode.Context = this;
        }

        private void UpdateMeasureFunc()
        {
            if (!string.IsNullOrEmpty(Text) && _children.Count == 0)
            {
                FlexNode.SetMeasureFunc(MeasureFunc);
            }
            else
            {
                FlexNode.SetMeasureFunc(null);
            }
        }

        private FlexLayoutSharp.Size MeasureFunc(Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return new FlexLayoutSharp.Size(0, 0);
            }

            using var paint = new SKPaint
            {
                TextSize = FontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Arial")
            };

            var bounds = new SKRect();
            paint.MeasureText(Text, ref bounds);
            
            // Simple measurement
            return new FlexLayoutSharp.Size(bounds.Width, bounds.Height + 5); // +5 for baseline padding
        }

        public void AddChild(FlexNodeWidget child)
        {
            _children.Add(child);
            FlexNode.SetMeasureFunc(null);
            FlexNode.AddChild(child.FlexNode);
            MarkDirty();
        }

        public void InsertChild(int index, FlexNodeWidget child)
        {
            _children.Insert(index, child);
            FlexNode.SetMeasureFunc(null);
            FlexNode.InsertChild(child.FlexNode, index);
            MarkDirty();
        }

        public void RemoveChild(FlexNodeWidget child)
        {
            _children.Remove(child);
            FlexNode.RemoveChild(child.FlexNode);
            UpdateMeasureFunc();
            MarkDirty();
        }

        public void RemoveChildAt(int index)
        {
            var child = _children[index];
            _children.RemoveAt(index);
            FlexNode.RemoveChild(child.FlexNode);
            UpdateMeasureFunc();
            MarkDirty();
        }

        public void ClearChildren()
        {
            while(_children.Count > 0)
            {
                RemoveChildAt(0);
            }
        }

        private void MarkDirty()
        {
            FlexNode.MarkAsDirty();
            // Propagate up? The root needs to know to recalculate layout
            // For now, we rely on the Root calling CalculateLayout
        }

        // Layout Calculation
        public void CalculateLayout(float width, float height)
        {
            FlexNode.CalculateLayout(width, height, Direction.LTR);
        }

        // Rendering
        protected override void OnRender(SKCanvas canvas)
        {
            // Auto-layout if we are the root of the Flex tree
            if (FlexNode.Parent == null)
            {
                CalculateLayout(Width, Height);
            }

            // Update our Widget properties from FlexNode Layout
            float left = FlexNode.LayoutGetLeft();
            float top = FlexNode.LayoutGetTop();
            float w = FlexNode.LayoutGetWidth();
            float h = FlexNode.LayoutGetHeight();

            // Note: In Ludots.UI, Widget.Render() does canvas.Translate(X, Y).
            // But FlexLayout calculates absolute positions relative to parent.
            // So we can assume X/Y are set correctly by the parent render loop or we use local coordinates here?
            
            // Actually, `Widget.Render` calls `canvas.Translate(X, Y)`.
            // So we should set X/Y properties of this Widget instance to match FlexNode layout 
            // BEFORE Render is called? 
            // OR we ignore Widget.X/Y and just draw using Flex coordinates?
            
            // Better approach: Since `Widget.Render` is recursive, we should let the parent 
            // pass the position or we update X/Y before render.
            // Let's assume the parent (or Reconciler) triggers a layout pass that updates X/Y of all widgets.
            
            // But wait, `FlexNodeWidget` IS A `Widget`.
            // `Widget.Render` uses `this.X` and `this.Y`.
            // So we must sync FlexNode layout to Widget properties.
            
            // Sync Layout
            // Ideally this happens in a separate pass, but we can do it lazily or assume it's done.
            // For this implementation, let's assume CalculateLayout was called on Root.
            // But we need to update OUR X/Y from FlexNode.
            
            // However, FlexNode layout is relative to parent.
            // Widget.X/Y is also relative to parent.
            // So it matches!
            
            // We just need to make sure `X` and `Y` are updated.
            // Since we don't have a "Layout Pass" hook in Widget, we might need to do it explicitly.
            // Or we just override Render and ignore base logic? 
            // No, base Render does useful things (Translate).
            
            // Let's create a helper to Sync.
            // Or just do it here? But Render is called AFTER Translate. 
            // If X/Y were wrong, Translate was wrong.
            
            // SOLUTION: The Root Widget (FlexRoot) should call `CalculateLayout` and then `SyncLayout` 
            // which recursively updates X/Y/Width/Height of all widgets.
            
            // For drawing INSIDE this widget (OnRender):
            // Coordinate system is already (0,0) at top-left of this widget.
            // We draw background and children.
            
            var rect = new SKRect(0, 0, Width, Height);
            
            // Background
            if (BackgroundColor != SKColors.Transparent)
            {
                using var paint = new SKPaint { Color = BackgroundColor, IsAntialias = true, Style = SKPaintStyle.Fill };
                if (BorderRadius > 0)
                    canvas.DrawRoundRect(new SKRoundRect(rect, BorderRadius, BorderRadius), paint);
                else
                    canvas.DrawRect(rect, paint);
            }
            
            // Border
            if (BorderWidth > 0 && BorderColor != SKColors.Transparent)
            {
                using var paint = new SKPaint { Color = BorderColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = BorderWidth };
                if (BorderRadius > 0)
                    canvas.DrawRoundRect(new SKRoundRect(rect, BorderRadius, BorderRadius), paint);
                else
                    canvas.DrawRect(rect, paint);
            }
            
            // Text
            if (!string.IsNullOrEmpty(Text))
            {
                using var paint = new SKPaint
                {
                    Color = TextColor,
                    TextSize = FontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Arial")
                };
                
                var bounds = new SKRect();
                paint.MeasureText(Text, ref bounds);
                
                // Simple centering or alignment
                // For true Flex text layout, we'd need more complex logic.
                // Here we just draw it.
                // Assuming the node size was measured to fit text.
                
                float tx = 0;
                float ty = FontSize; // Baseline
                
                if (TextAlign == SKTextAlign.Center)
                    tx = (Width - bounds.Width) / 2;
                else if (TextAlign == SKTextAlign.Right)
                    tx = Width - bounds.Width;
                    
                canvas.DrawText(Text, tx, ty, paint);
            }
            
            // Children
            foreach (var child in _children)
            {
                // Sync child layout before rendering it
                child.X = child.FlexNode.LayoutGetLeft();
                child.Y = child.FlexNode.LayoutGetTop();
                child.Width = child.FlexNode.LayoutGetWidth();
                child.Height = child.FlexNode.LayoutGetHeight();
                
                child.Render(canvas);
            }
        }

        public override bool HandleInput(InputEvent e, float parentX, float parentY)
        {
            var globalX = parentX + X;
            var globalY = parentY + Y;

            for (int i = _children.Count - 1; i >= 0; i--)
            {
                var child = _children[i];
                child.X = child.FlexNode.LayoutGetLeft();
                child.Y = child.FlexNode.LayoutGetTop();
                child.Width = child.FlexNode.LayoutGetWidth();
                child.Height = child.FlexNode.LayoutGetHeight();

                if (child.HandleInput(e, globalX, globalY))
                {
                    return true;
                }
            }

            return base.HandleInput(e, parentX, parentY);
        }

        protected override bool OnPointerEvent(PointerEvent e, float localX, float localY)
        {
            if (e.Action == PointerAction.Up) // Click on Up
            {
                if (OnClick != null)
                {
                    OnClick.Invoke();
                    return true;
                }
            }
            return false;
        }
    }
}
