using System;

namespace Aquashot.History;

// Index math for stepping through a list (the detail carousel). Returns -1 for an empty list.
public static class CarouselIndex
{
    public static int Step(int current, int count, int delta, bool wrap)
    {
        if (count <= 0) return -1;
        int next = current + delta;
        if (wrap)
        {
            next %= count;
            if (next < 0) next += count;
            return next;
        }
        return Math.Clamp(next, 0, count - 1);
    }
}
