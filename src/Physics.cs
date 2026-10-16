using System;

namespace SlugcatDesktop
{
    /// <summary>
    /// Constantes numericas da simulacao fisica, extraidas verbatim de SlugcatSimulation.
    /// Projeto e Y-up: a gravidade e aplicada como Vel.Y -= Gravity.
    /// </summary>
    internal static class PhysicsConstants
    {
        public const float Gravity = 0.9f;
        public const float AirFriction = 0.999f;
        public const float Bounce = 0.1f;
        public const float SurfaceFriction = 0.5f;
        public const float BodyConnectionDistance = 17f;

        // Com atrito de superfície 0,5 a velocidade de equilíbrio é igual à força aplicada por tick.
        public const float WalkTargetSpeed = 1.1f;
    }

    internal struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }
        public Vec2 Normalized
        {
            get
            {
                float length = Length;
                return length > 0.0001f ? new Vec2(X / length, Y / length) : new Vec2(0f, 0f);
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, float b) { return new Vec2(a.X * b, a.Y * b); }
        public static Vec2 operator /(Vec2 a, float b) { return new Vec2(a.X / b, a.Y / b); }

        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length; }
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) { return a + (b - a) * t; }
    }

    internal struct BodyRig
    {
        public Vec2 Waist;
        public Vec2 Belly;
        public Vec2 Neck;

        public BodyRig(Vec2 waist, Vec2 belly, Vec2 neck)
        {
            Waist = waist;
            Belly = belly;
            Neck = neck;
        }
    }

    internal sealed class BodyChunk
    {
        public Vec2 Pos;
        public Vec2 LastPos;
        public Vec2 Vel;
        public readonly float Radius;

        public BodyChunk(float radius, Vec2 pos)
        {
            Radius = radius;
            Pos = pos;
            LastPos = pos;
            Vel = new Vec2(0f, 0f);
        }
    }
}
