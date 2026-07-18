using System;

namespace TaskOverlay.Core;

public static class PointerDragGesture
{
    public const double DefaultThreshold = 5;

    public static bool HasExceededThreshold(
        double startX,
        double startY,
        double currentX,
        double currentY,
        double threshold = DefaultThreshold)
    {
        return Math.Abs(currentX - startX) >= threshold ||
               Math.Abs(currentY - startY) >= threshold;
    }
}
