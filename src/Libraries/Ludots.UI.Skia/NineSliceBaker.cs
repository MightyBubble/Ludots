using System;
using SkiaSharp;

namespace Ludots.UI.Skia
{
    /// <summary>
    /// 九宫格/三宫格切片的纯几何推导与补丁绘制,UI 与 HUD 两条 Skia 车道共用。
    /// 切片非法(负值或对边切片和≥源尺寸)时 fail-closed:TryBuildPatches 返回 false,
    /// 调用方不回退整图拉伸(与 UiNineSlicePanelTests 钉死的合同一致)。
    /// </summary>
    internal static class NineSliceBaker
    {
        internal readonly struct Patch
        {
            internal Patch(SKRect source, SKRect destination)
            {
                Source = source;
                Destination = destination;
            }

            internal SKRect Source { get; }

            internal SKRect Destination { get; }
        }

        internal readonly struct PatchSet
        {
            private readonly Patch _p0;
            private readonly Patch _p1;
            private readonly Patch _p2;
            private readonly Patch _p3;
            private readonly Patch _p4;
            private readonly Patch _p5;
            private readonly Patch _p6;
            private readonly Patch _p7;
            private readonly Patch _p8;

            internal PatchSet(Patch p0, Patch p1, Patch p2, Patch p3, Patch p4, Patch p5, Patch p6, Patch p7, Patch p8)
            {
                _p0 = p0;
                _p1 = p1;
                _p2 = p2;
                _p3 = p3;
                _p4 = p4;
                _p5 = p5;
                _p6 = p6;
                _p7 = p7;
                _p8 = p8;
            }

            internal int Count => 9;

            internal Patch this[int index] => index switch
            {
                0 => _p0,
                1 => _p1,
                2 => _p2,
                3 => _p3,
                4 => _p4,
                5 => _p5,
                6 => _p6,
                7 => _p7,
                _ => _p8,
            };
        }

        /// <summary>切片值语义:top/right/bottom/left,px。</summary>
        internal static bool TryBuildPatches(
            float sourceWidth,
            float sourceHeight,
            float sourceLeft,
            float sourceTop,
            SKRect destination,
            float sliceTop,
            float sliceRight,
            float sliceBottom,
            float sliceLeft,
            out PatchSet patches)
        {
            patches = default;
            if (sourceWidth <= 0.01f || sourceHeight <= 0.01f ||
                sliceLeft < 0f || sliceTop < 0f || sliceRight < 0f || sliceBottom < 0f ||
                sliceLeft + sliceRight >= sourceWidth || sliceTop + sliceBottom >= sourceHeight)
            {
                return false;
            }

            float left = sliceLeft;
            float top = sliceTop;
            float right = sliceRight;
            float bottom = sliceBottom;
            float destinationLeft = Math.Min(left, destination.Width);
            float destinationTop = Math.Min(top, destination.Height);
            float destinationRight = Math.Min(right, Math.Max(0f, destination.Width - destinationLeft));
            float destinationBottom = Math.Min(bottom, Math.Max(0f, destination.Height - destinationTop));
            float sourceCenterWidth = Math.Max(0f, sourceWidth - left - right);
            float sourceCenterHeight = Math.Max(0f, sourceHeight - top - bottom);
            float destinationCenterWidth = Math.Max(0f, destination.Width - destinationLeft - destinationRight);
            float destinationCenterHeight = Math.Max(0f, destination.Height - destinationTop - destinationBottom);
            patches = new PatchSet(
                new Patch(new SKRect(sourceLeft, sourceTop, sourceLeft + left, sourceTop + top), new SKRect(destination.Left, destination.Top, destination.Left + destinationLeft, destination.Top + destinationTop)),
                new Patch(new SKRect(sourceLeft + left, sourceTop, sourceLeft + left + sourceCenterWidth, sourceTop + top), new SKRect(destination.Left + destinationLeft, destination.Top, destination.Right - destinationRight, destination.Top + destinationTop)),
                new Patch(new SKRect(sourceLeft + sourceWidth - right, sourceTop, sourceLeft + sourceWidth, sourceTop + top), new SKRect(destination.Right - destinationRight, destination.Top, destination.Right, destination.Top + destinationTop)),
                new Patch(new SKRect(sourceLeft, sourceTop + top, sourceLeft + left, sourceTop + top + sourceCenterHeight), new SKRect(destination.Left, destination.Top + destinationTop, destination.Left + destinationLeft, destination.Bottom - destinationBottom)),
                new Patch(new SKRect(sourceLeft + left, sourceTop + top, sourceLeft + left + sourceCenterWidth, sourceTop + top + sourceCenterHeight), new SKRect(destination.Left + destinationLeft, destination.Top + destinationTop, destination.Left + destinationLeft + destinationCenterWidth, destination.Top + destinationTop + destinationCenterHeight)),
                new Patch(new SKRect(sourceLeft + sourceWidth - right, sourceTop + top, sourceLeft + sourceWidth, sourceTop + top + sourceCenterHeight), new SKRect(destination.Right - destinationRight, destination.Top + destinationTop, destination.Right, destination.Bottom - destinationBottom)),
                new Patch(new SKRect(sourceLeft, sourceTop + sourceHeight - bottom, sourceLeft + left, sourceTop + sourceHeight), new SKRect(destination.Left, destination.Bottom - destinationBottom, destination.Left + destinationLeft, destination.Bottom)),
                new Patch(new SKRect(sourceLeft + left, sourceTop + sourceHeight - bottom, sourceLeft + left + sourceCenterWidth, sourceTop + sourceHeight), new SKRect(destination.Left + destinationLeft, destination.Bottom - destinationBottom, destination.Right - destinationRight, destination.Bottom)),
                new Patch(new SKRect(sourceLeft + sourceWidth - right, sourceTop + sourceHeight - bottom, sourceLeft + sourceWidth, sourceTop + sourceHeight), new SKRect(destination.Right - destinationRight, destination.Bottom - destinationBottom, destination.Right, destination.Bottom)));
            return true;
        }

        internal static void DrawImage(SKCanvas canvas, SKImage image, in PatchSet patches)
        {
            using SKPaint paint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };
            for (int i = 0; i < patches.Count; i++)
            {
                Patch patch = patches[i];
                if (!(patch.Source.Width <= 0.01f) && !(patch.Source.Height <= 0.01f) &&
                    !(patch.Destination.Width <= 0.01f) && !(patch.Destination.Height <= 0.01f))
                {
                    canvas.DrawImage(image, patch.Source, patch.Destination, paint);
                }
            }
        }

        internal static void DrawPicture(SKCanvas canvas, SKPicture picture, in PatchSet patches)
        {
            for (int i = 0; i < patches.Count; i++)
            {
                Patch patch = patches[i];
                if (patch.Source.Width <= 0.01f || patch.Source.Height <= 0.01f ||
                    patch.Destination.Width <= 0.01f || patch.Destination.Height <= 0.01f)
                {
                    continue;
                }

                int count = canvas.Save();
                canvas.ClipRect(patch.Destination, SKClipOperation.Intersect, antialias: true);
                float scaleX = patch.Destination.Width / patch.Source.Width;
                float scaleY = patch.Destination.Height / patch.Source.Height;
                canvas.Translate(patch.Destination.Left - patch.Source.Left * scaleX, patch.Destination.Top - patch.Source.Top * scaleY);
                canvas.Scale(scaleX, scaleY);
                canvas.DrawPicture(picture);
                canvas.RestoreToCount(count);
            }
        }
    }
}
