using System;
namespace MajdataPlay.Alpha
{
    // Signed radii are visual coordinates; the upstream sensor ring stays fixed.
    internal static class AlphaRingGeometry
    {
        public static float Direction(float spawn, float destroy) => destroy < spawn ? -1 : 1;
        public static float PathPosition(float radius, float spawn, float destroy) => Direction(spawn, destroy) * (radius - spawn);
        public static float Distance(float visualTime, float speed, float spawn, float destroy) => destroy + Direction(spawn, destroy) * visualTime * speed;
        public static float Scale(float radius, float spawn, float destroy, float rate) => Math.Max(0, Math.Min(1, 1 + PathPosition(radius, spawn, destroy) * rate));
        public static float GateRadius(float radius, float spawn, float destroy) => PathPosition(radius, spawn, destroy) < 0 ? spawn : radius;
    }
}
