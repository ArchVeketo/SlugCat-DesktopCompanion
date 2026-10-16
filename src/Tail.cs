namespace SlugcatDesktop
{
    internal sealed class TailSegment
    {
        public Vec2 Pos;
        public Vec2 LastPos;
        public Vec2 Vel;
        public readonly float Radius;
        public readonly float ConnectionRadius;
        public float StretchedRadius = 1f;

        public TailSegment(float radius, float connectionRadius, Vec2 pos)
        {
            Radius = radius;
            ConnectionRadius = connectionRadius;
            Pos = pos;
            LastPos = pos;
        }
    }
}
