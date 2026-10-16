using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SlugcatDesktop
{
    internal static class Log
    {
        private static int _count;

        public static void Write(string where, string message)
        {
            if (_count++ >= 50)
                return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
                File.AppendAllText(path, DateTime.Now.ToString("s") + " [" + where + "] " + message + "\r\n");
            }
            catch { }
        }

        public static void Error(string where, Exception ex)
        {
            Write(where, ex.ToString());
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("ThreadException", e.Exception);
            };
            try
            {
                Application.Run(new CompanionWindow());
            }
            catch (Exception ex)
            {
                Log.Error("Main", ex);
                MessageBox.Show("O Slugcat Companion não conseguiu iniciar.\r\n\r\n" + ex.Message,
                    "Slugcat Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
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

    /// <summary>
    /// A small standalone simulation of the vanilla Survivor's body chunks and tail.
    /// Terrain and room-dependent behaviors are replaced by a flat desktop floor.
    /// </summary>
    internal sealed class SlugcatSimulation
    {
        public const float Gravity = 0.9f;
        public const float AirFriction = 0.999f;
        public const float Bounce = 0.1f;
        public const float SurfaceFriction = 0.5f;
        public const float BodyConnectionDistance = 17f;

        // Com atrito de superfície 0,5 a velocidade de equilíbrio é igual à força aplicada por tick.
        public const float WalkTargetSpeed = 1.1f;

        // Metade da largura do mundo em unidades de simulação (definida pela janela a partir do monitor).
        public float WorldHalfWidth = 60f;

        public readonly BodyChunk[] Chunks;
        public readonly TailSegment[] Tail;
        private Vec2 _bellyPos;
        private Vec2 _bellyVel;
        public Vec2 HeadPos;
        public Vec2 HeadLastPos;
        public Vec2 HeadVel;
        public Vec2 LegsPos;
        public float Facing = 1f;
        public float LookX = 1f;
        public float LookY = 0f;
        public float WalkPhase;
        public bool Walking { get; private set; }
        public float WalkSpeed { get; private set; }

        private readonly Random _random = new Random();
        private int _stateTicks;
        private int _stateLength = 165;
        private int _direction = 1;
        private bool _jumpRequested;
        private float _walkAcceleration = 0f;

        // Expressões faciais e animações
        private int _blinkCounter;
        private int _faceFrame;
        private int _expressionTimer = 300;
        private int _expressionHoldTimer = 0;
        private bool _blinking;

        // Braços
        private float[] _armAngles = new float[2];
        private float _armSwing;

        // Comprimento visível (px) de cada PlayerArmN: 28 - spriteSourceSize.x do atlas.
        // O ombro fica na borda direita do sprite; a mão fica à esquerda.
        private static readonly float[] ArmLengths =
            { 5f, 5f, 6f, 8f, 10f, 12f, 14f, 17f, 19f, 21f, 24f, 28f, 28f };

        public SlugcatSimulation()
        {
            // Vanilla Player uses radii 9 and 8 with a 17-unit normal connection.
            Chunks = new BodyChunk[]
            {
                new BodyChunk(9f, new Vec2(0f, 25f)),
                new BodyChunk(8f, new Vec2(0f, 8f))
            };
            _bellyPos = Vec2.Lerp(Chunks[1].Pos, Chunks[0].Pos, 0.5f);

            // Vanilla adult Survivor tail radii and connection radii.
            float[] radii = new float[] { 6f, 4f, 2.5f, 1f };
            float[] connections = new float[] { 4f, 7f, 7f, 7f };
            const float tailLengthScale = 1.0f;
            Tail = new TailSegment[4];
            Vec2 anchor = TailAnchor;
            Vec2 back = new Vec2(-Facing, 0f);
            for (int i = 0; i < Tail.Length; i++)
            {
                float distance = connections[i] * tailLengthScale;
                anchor = anchor + back * distance + new Vec2(0f, -0.35f * (i + 1));
                Tail[i] = new TailSegment(radii[i], distance, anchor);
            }

            HeadPos = Chunks[0].Pos + new Vec2(0f, 5f);
            HeadLastPos = HeadPos;
            LegsPos = Chunks[1].Pos + new Vec2(0f, -2f);
            _blinkCounter = _random.Next(40, 180);
        }

        public Vec2 TailAnchor
        {
            get { return (Chunks[0].Pos + Chunks[1].Pos * 3f) / 4f; }
        }

        public BodyRig Rig
        {
            get
            {
                Vec2 waist = Chunks[1].Pos;
                Vec2 neck = Chunks[0].Pos;
                return new BodyRig(waist, _bellyPos, neck);
            }
        }

        public void RequestJump()
        {
            _jumpRequested = true;
        }

        private static bool Bad(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10000f;
        }

        /// <summary>Detecta NaN/infinito/valores absurdos que fariam o GDI+ lançar exceção ao desenhar.</summary>
        public bool IsStateValid()
        {
            for (int i = 0; i < Chunks.Length; i++)
            {
                BodyChunk c = Chunks[i];
                if (Bad(c.Pos.X) || Bad(c.Pos.Y) || Bad(c.Vel.X) || Bad(c.Vel.Y))
                    return false;
            }
            for (int i = 0; i < Tail.Length; i++)
            {
                TailSegment t = Tail[i];
                if (Bad(t.Pos.X) || Bad(t.Pos.Y) || Bad(t.Vel.X) || Bad(t.Vel.Y) || Bad(t.StretchedRadius))
                    return false;
            }
            return !(Bad(_bellyPos.X) || Bad(_bellyPos.Y) || Bad(HeadPos.X) || Bad(HeadPos.Y)
                || Bad(LookX) || Bad(LookY) || Bad(WalkPhase) || Bad(_armAngles[0]) || Bad(_armAngles[1]));
        }

        public void Tick()
        {
            UpdateBehavior();

            if (_jumpRequested)
            {
                _jumpRequested = false;
                // Só pula com os pés no chão (sem pulo duplo no ar).
                if (IsOnGround())
                {
                    Chunks[0].Vel.Y += 8.0f;
                    Chunks[1].Vel.Y += 5.5f;
                    Chunks[0].Vel.X += Facing * 1.5f;
                    Chunks[1].Vel.X += Facing * 0.8f;
                    Walking = false;
                    _stateTicks = 0;
                    _stateLength = 100;
                }
            }

            if (Walking && IsOnGround())
            {
                // Aceleração gradual para movimento mais natural
                _walkAcceleration = Clamp(_walkAcceleration + 0.015f, 0f, 1f);
                float force = _direction * WalkTargetSpeed * _walkAcceleration;
                Chunks[0].Vel.X += force;
                Chunks[1].Vel.X += force;
                WalkPhase += 1f;
                if (WalkPhase > 35000f) WalkPhase -= 35000f;
                WalkSpeed = Math.Abs((Chunks[0].Vel.X + Chunks[1].Vel.X) * 0.5f);
                Facing = _direction;
            }
            else
            {
                // Desaceleração quando para de andar
                _walkAcceleration *= 0.92f;
                WalkSpeed = Math.Abs((Chunks[0].Vel.X + Chunks[1].Vel.X) * 0.5f);
            }

            UpdateChunk(Chunks[0]);
            UpdateChunk(Chunks[1]);
            ResolveBodyConnection();
            MaintainCompanionPose();

            // Bordas do mundo = largura real do monitor (WorldHalfWidth vem da janela)
            float limit = WorldHalfWidth;
            float bodyX = (Chunks[0].Pos.X + Chunks[1].Pos.X) * 0.5f;
            if (bodyX < -limit || bodyX > limit)
            {
                _direction = bodyX < 0f ? 1 : -1;
                Chunks[0].Pos.X = Clamp(Chunks[0].Pos.X, -limit, limit);
                Chunks[1].Pos.X = Clamp(Chunks[1].Pos.X, -limit, limit);
                Chunks[0].Vel.X *= -0.25f;
                Chunks[1].Vel.X *= -0.25f;
            }

            UpdateBelly();
            UpdateHead();
            UpdateArms();
            UpdateFacialExpression();
            UpdateTail();

            // Sincronização de pernas com velocidade real
            float stepLift = 0f;
            if (Walking && WalkSpeed > 0.05f)
            {
                float stepFreq = 0.35f * Clamp(WalkSpeed / 0.8f, 0.5f, 1.5f);
                stepLift = (float)Math.Abs(Math.Sin(WalkPhase * stepFreq)) * 1.3f;
            }
            LegsPos = Chunks[1].Pos + new Vec2(0f, -2f + stepLift);
        }

        private void UpdateBehavior()
        {
            _stateTicks++;
            if (_stateTicks >= _stateLength)
            {
                _stateTicks = 0;
                Walking = !Walking;
                // Caminhadas mais longas (2 a 6 s) para realmente atravessar o monitor
                _stateLength = Walking ? _random.Next(80, 240) : _random.Next(125, 235);
                if (Walking)
                    _direction = _random.Next(0, 2) == 0 ? -1 : 1;
                else if (_random.NextDouble() < 0.42)
                    LookX = _random.Next(0, 2) == 0 ? -1f : 1f;
            }

            // Suaviza transição do olhar para a direção de movimento
            if (Walking)
            {
                float targetLook = _direction;
                LookX = LookX + (targetLook - LookX) * 0.08f;
            }
        }

        private bool IsOnGround()
        {
            BodyChunk lower = Chunks[1];
            return lower.Pos.Y <= lower.Radius + 0.35f && Math.Abs(lower.Vel.Y) < 2f;
        }

        private static void UpdateChunk(BodyChunk chunk)
        {
            chunk.Vel.Y -= Gravity;
            chunk.Vel = chunk.Vel * AirFriction;
            chunk.LastPos = chunk.Pos;
            chunk.Pos = chunk.Pos + chunk.Vel;

            // The desktop scene has a flat floor in place of Rain World's room tiles.
            if (chunk.Pos.Y < chunk.Radius)
            {
                chunk.Pos.Y = chunk.Radius;
                if (chunk.Vel.Y < 0f)
                {
                    chunk.Vel.Y = -chunk.Vel.Y * Bounce;
                    if (Math.Abs(chunk.Vel.Y) < 0.5f)
                        chunk.Vel.Y = 0f;
                }
                chunk.Vel.X *= SurfaceFriction;
            }
        }

        private void ResolveBodyConnection()
        {
            BodyChunk first = Chunks[0];
            BodyChunk second = Chunks[1];
            Vec2 between = second.Pos - first.Pos;
            float distance = between.Length;
            if (distance < 0.0001f)
                return;

            // BodyChunkConnection(Type.Normal, elasticity 1, weightSymmetry .5).
            Vec2 direction = between / distance;
            float correction = BodyConnectionDistance - distance;
            Vec2 halfCorrection = direction * (correction * 0.5f);
            first.Pos = first.Pos - halfCorrection;
            first.Vel = first.Vel - halfCorrection;
            second.Pos = second.Pos + halfCorrection;
            second.Vel = second.Vel + halfCorrection;
        }

        private void MaintainCompanionPose()
        {
            // The full game's Player.Update supplies leg forces and terrain-aware posture.
            // This overlay has no room tiles, so its idle stance needs a bounded posture support.
            BodyChunk upper = Chunks[0];
            BodyChunk lower = Chunks[1];
            bool grounded = IsOnGround();
            Vec2 axis = (upper.Pos - lower.Pos).Normalized;
            if (axis.Y < 0f)
                axis = axis * -1f;

            float maxLean = grounded ? (Walking ? 0.03f : 0.06f) : 0.18f;
            float horizontal = Clamp(axis.X, -maxLean, maxLean);
            float vertical = (float)Math.Sqrt(Math.Max(0f, 1f - horizontal * horizontal));
            upper.Pos = lower.Pos + new Vec2(horizontal, vertical) * BodyConnectionDistance;

            float relativeHorizontalSpeed = upper.Vel.X - lower.Vel.X;
            float damping = grounded ? 0.3f : 0.2f;
            upper.Vel.X -= relativeHorizontalSpeed * damping;
        }

        private void UpdateHead()
        {
            BodyRig rig = Rig;
            Vec2 bodyAxis = (rig.Neck - rig.Belly).Normalized;

            float lookXClamped = Clamp(LookX, -1f, 1f);
            float lookYClamped = Clamp(LookY, -0.5f, 0.5f);
            Vec2 target = rig.Neck + bodyAxis * 5f + new Vec2(lookXClamped * 0.5f, lookYClamped * 0.3f);

            HeadLastPos = HeadPos;

            // GenericBodyPart head: inércia, atrito do ar e ligação curta ao chunk 0
            HeadVel = HeadVel + (target - HeadPos) * 0.16f;
            HeadVel.Y -= Gravity * 0.08f;
            HeadVel = HeadVel * 0.82f;
            HeadPos = HeadPos + HeadVel;

            Vec2 fromTarget = HeadPos - target;
            if (fromTarget.Length > 1.5f)
                HeadPos = target + fromTarget.Normalized * 1.5f;

            // Direção vertical do olhar baseada no movimento
            float targetLookY = 0f;
            if (Chunks[0].Vel.Y > 2f)
                targetLookY = 0.4f;
            else if (Chunks[0].Vel.Y < -2f)
                targetLookY = -0.3f;
            LookY = Lerp(LookY, targetLookY, 0.1f);
        }

        private void UpdateArms()
        {
            if (Walking && WalkSpeed > 0.1f)
            {
                _armSwing += WalkSpeed * 0.15f;
                if (_armSwing > 628.318f) _armSwing -= 628.318f;
                float swing = (float)Math.Sin(_armSwing) * 0.3f;
                _armAngles[0] = Clamp(swing * Facing, -0.5f, 0.5f);
                _armAngles[1] = Clamp(-swing * Facing, -0.5f, 0.5f);
            }
            else
            {
                // Braços relaxados quando parado
                _armAngles[0] = Lerp(_armAngles[0], 0f, 0.15f);
                _armAngles[1] = Lerp(_armAngles[1], 0f, 0.15f);
            }
        }

        // Ombro e mão em coordenadas de simulação (y para cima).
        // No chão, o braço fica recolhido junto ao torso (como no jogo).
        public void GetArm(int index, bool front, out Vec2 shoulder, out Vec2 hand)
        {
            BodyRig rig = Rig;
            Vec2 up = (rig.Neck - rig.Waist).Normalized;
            if (up.Length < 0.001f)
                up = new Vec2(0f, 1f);

            // draw0 = Neck (chunk0), draw1 = Belly interpolado para ombro
            Vec2 draw0 = rig.Neck;
            Vec2 draw1 = rig.Belly;

            // Get spine angle from draw0 to draw1
            float spineAng = (float)Math.Atan2(draw0.Y - draw1.Y, draw0.X - draw1.X);
            // Rotate shoulder offset by spine angle (Y offset is 3.5 in Y-down, so -3.5 in our Y-up)
            float ox = Facing * 4.5f;
            float oy = -3.5f;
            float rotatedOx = ox * (float)Math.Cos(spineAng) - oy * (float)Math.Sin(spineAng);
            float rotatedOy = ox * (float)Math.Sin(spineAng) + oy * (float)Math.Cos(spineAng);
            shoulder = Vec2.Lerp(draw0, draw1, 0.35f) + new Vec2(rotatedOx, rotatedOy);

            float a = Clamp(_armAngles[index], -0.5f, 0.5f);
            float cos = (float)Math.Cos(a);
            float sin = (float)Math.Sin(a);
            Vec2 down = up * -1f;
            Vec2 swung = new Vec2(down.X * cos - down.Y * sin, down.X * sin + down.Y * cos);

            hand = shoulder + swung * 6f;
        }

        // Escolhe o frame cujo comprimento é o mais próximo da distância ombro->mão
        public static int ArmFrameForLength(float length)
        {
            int best = 0;
            float bestDiff = float.MaxValue;
            for (int i = 0; i < ArmLengths.Length; i++)
            {
                float diff = Math.Abs(ArmLengths[i] - length);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }
            return best;
        }

        private void UpdateFacialExpression()
        {
            if (_expressionHoldTimer > 0)
            {
                _expressionHoldTimer--;
                if (_expressionHoldTimer <= 0)
                    _faceFrame = 0;
                return;
            }

            // Sistema de piscar
            _blinkCounter--;
            if (_blinkCounter <= 0)
            {
                if (!_blinking)
                {
                    _blinking = true;
                    _faceFrame = _random.Next(1, 4);
                    _blinkCounter = _random.Next(3, 6);
                }
                else
                {
                    _blinking = false;
                    _faceFrame = 0;
                    _blinkCounter = _random.Next(80, 220);
                }
            }
            else if (!_blinking && --_expressionTimer <= 0)
            {
                _faceFrame = _random.Next(4, 9);
                _expressionHoldTimer = _random.Next(15, 45);
                _expressionTimer = _random.Next(200, 500);
            }
        }

        private void UpdateBelly()
        {
            Vec2 waist = Chunks[1].Pos;
            Vec2 neck = Chunks[0].Pos;
            Vec2 bodyAxis = (neck - waist).Normalized;
            Vec2 perpendicular = new Vec2(-bodyAxis.Y, bodyAxis.X);
            Vec2 midpoint = Vec2.Lerp(waist, neck, 0.5f);

            float sway;
            if (Walking)
                sway = (float)Math.Sin(WalkPhase * 0.35f) * 0.7f * _walkAcceleration;
            else
                sway = (float)Math.Sin(_stateTicks * 0.035f) * 0.2f; // respiração
            Vec2 target = midpoint + perpendicular * sway;

            _bellyVel = _bellyVel + (target - _bellyPos) * 0.18f;
            _bellyVel = _bellyVel * 0.72f;
            _bellyPos = _bellyPos + _bellyVel;

            // Keep the intermediate pivot linked to both ends while allowing a small bend.
            Vec2 oldPos = _bellyPos;
            _bellyPos = ClampFromAnchor(_bellyPos, waist, 9.25f);
            _bellyPos = ClampFromAnchor(_bellyPos, neck, 9.25f);
            _bellyVel = _bellyVel + (_bellyPos - oldPos) * 0.5f;
        }

        private static Vec2 ClampFromAnchor(Vec2 point, Vec2 anchor, float maxDistance)
        {
            Vec2 offset = point - anchor;
            if (offset.Length <= maxDistance || offset.Length < 0.0001f)
                return point;
            return anchor + offset.Normalized * maxDistance;
        }

        private void UpdateTail()
        {
            Vec2 anchor = TailAnchor;
            float speed = Clamp(WalkSpeed / 2.5f, 0f, 1f);
            Vec2 previous = anchor;
            for (int i = 0; i < Tail.Length; i++)
            {
                TailSegment segment = Tail[i];
                segment.LastPos = segment.Pos;

                // PlayerGraphics.Update applies speed-dependent damping and gravity to TailSegment velocity.
                segment.Vel.Y -= Lerp(0.1f, 0.5f, speed) * Gravity;
                segment.Vel.X += (Chunks[1].Vel.X - segment.Vel.X) * 0.025f;
                segment.Vel = segment.Vel * Lerp(0.75f, 0.95f, speed);
                segment.Pos = segment.Pos + segment.Vel;

                Vec2 offset = segment.Pos - previous;
                float distance = offset.Length;
                if (distance > segment.ConnectionRadius && distance > 0.0001f)
                {
                    Vec2 direction = offset / distance;
                    float excess = distance - segment.ConnectionRadius;
                    float share = i == 0 ? 0.6f : 0.5f;
                    segment.Pos = segment.Pos - direction * (excess * share);
                    if (i > 0)
                    {
                        float previousShare = 1f - share;
                        Tail[i - 1].Pos = Tail[i - 1].Pos + direction * (excess * previousShare);
                        previous = Tail[i - 1].Pos;
                    }
                    segment.Vel = segment.Vel - direction * (excess * share * 0.5f);
                }

                // Keep the vanilla reach bound; local connection lengths are scaled for a longer visible tail.
                Vec2 fromBody = segment.Pos - Chunks[1].Pos;
                float maxDistance = 9f * (i + 1);
                if (fromBody.Length > maxDistance)
                    segment.Pos = Chunks[1].Pos + fromBody.Normalized * maxDistance;

                if (segment.Pos.Y < segment.Radius * 0.45f)
                {
                    segment.Pos.Y = segment.Radius * 0.45f;
                    if (segment.Vel.Y < 0f)
                        segment.Vel.Y = -segment.Vel.Y * 0.1f;
                    segment.Vel.X *= 0.85f;
                }

                distance = Vec2.Distance(segment.Pos, previous);
                float ratio = segment.ConnectionRadius / Math.Max(0.01f, distance * 0.5f);
                segment.StretchedRadius = segment.Radius * Clamp((ratio + 2f) / 3f, 0.2f, 1f);
                previous = segment.Pos;
            }
        }

        public int LegsFrame
        {
            get { return Walking && WalkSpeed > 0.08f ? ((int)(WalkPhase / 5f) % 7) : 0; }
        }

        public int HeadFrame
        {
            get
            {
                // FIX 4: use physical angle, not LookX
                Vec2 headDir = Chunks[0].Pos - Chunks[1].Pos; // head chunk - body chunk
                float headAng = (float)(Math.Atan2(headDir.X, headDir.Y) * 180.0 / Math.PI); // degrees
                int frame = (int)Math.Round(Math.Abs(headAng / 360.0 * 34.0));
                frame = Math.Min(frame, 17); // clamp to valid range
                return frame;
            }
        }

        public int FaceFrame
        {
            get { return Clamp(_faceFrame, 0, 8); }
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }

    internal sealed class CompanionWindow : Form
    {
        private const int PixelScale = 3;
        private const int WindowWidth = 450;
        private const int WindowHeight = 214;
        private const float EdgeMargin = 28f; // unidades de simulação reservadas para rabo/cabeça nas bordas
        private static readonly Color KeyColor = Color.Fuchsia;

        private SlugcatSimulation _simulation = new SlugcatSimulation();
        private float _worldHalfWidth = 60f;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly Bitmap _body;
        private readonly Bitmap _hips;
        private readonly Bitmap[] _heads = new Bitmap[18];
        private readonly Bitmap[] _faces = new Bitmap[9];
        private readonly Bitmap[] _legs = new Bitmap[7];
        private readonly Bitmap[] _arms = new Bitmap[13];
        private float _accumulator;
        
        // Sistema de drag
        private bool _dragging;
        private Point _dragStart;

        // Buffers pre-alocados para DrawTail (sem GC a 60 FPS)
        private readonly Vec2[] _tailCL = new Vec2[5];
        private readonly float[] _tailR  = new float[5];
        private readonly Vec2[] _tailSmooth  = new Vec2[21];
        private readonly float[] _tailSmoothR = new float[21];
        private readonly Point[] _tailOutline  = new Point[42];
        private static readonly SolidBrush _whiteBrush = new SolidBrush(Color.White);

        public CompanionWindow()
        {
            string assetDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets\\player");
            _body = LoadSprite(assetDirectory, "BodyA");
            _hips = LoadSprite(assetDirectory, "HipsA");

            for (int i = 0; i < _heads.Length; i++)
                _heads[i] = LoadSprite(assetDirectory, "HeadA" + i);

            for (int i = 0; i < _faces.Length; i++)
                _faces[i] = Tint(LoadSprite(assetDirectory, "FaceA" + i), Color.Black);

            for (int i = 0; i < _legs.Length; i++)
                _legs[i] = LoadSprite(assetDirectory, "LegsA" + i);

            for (int i = 0; i < _arms.Length; i++)
                _arms[i] = LoadSprite(assetDirectory, "PlayerArm" + i);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            ShowIcon = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;

            BackColor = KeyColor;
            TransparencyKey = KeyColor;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Text = "Slugcat Companion";
            
            // Inicia centralizado na parte de baixo da tela
            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(workArea.Width, WindowHeight);
            Location = new Point(workArea.Left, workArea.Bottom - WindowHeight);

            // As bordas do mundo seguem a largura real da janela
            _worldHalfWidth = Math.Max(30f, ClientSize.Width * 0.5f / PixelScale - EdgeMargin);
            _simulation.WorldHalfWidth = _worldHalfWidth;

            _menu = new ContextMenuStrip();
            _menu.Items.Add("Dar um pulinho", null, delegate { _simulation.RequestJump(); });
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Sair", null, delegate { Close(); });
            ContextMenuStrip = _menu;

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = SystemIcons.Application;
            _trayIcon.Text = "Slugcat Companion — clique no Slugcat para ele pular!";
            _trayIcon.ContextMenuStrip = _menu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { Visible = !Visible; };

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 16;
            _timer.Tick += OnFrame;
            _clock.Start();
            _timer.Start();
        }

        private static Bitmap LoadSprite(string directory, string name)
        {
            string path = Path.Combine(directory, name + ".png");
            if (!File.Exists(path))
                throw new FileNotFoundException("Não encontrei uma peça do Slugcat extraída do Rain World.", path);
            using (Bitmap source = new Bitmap(path))
                return new Bitmap(source);
        }

        private static Bitmap Tint(Bitmap source, Color color)
        {
            Bitmap tinted = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    int alpha = source.GetPixel(x, y).A;
                    tinted.SetPixel(x, y, Color.FromArgb(alpha, color.R, color.G, color.B));
                }
            }
            source.Dispose();
            return tinted;
        }

        private void ResetSimulation()
        {
            _simulation = new SlugcatSimulation();
            _simulation.WorldHalfWidth = _worldHalfWidth;
            _accumulator = 0f;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            _clock.Restart();
            _accumulator += (float)Math.Min(elapsed, 0.08);
            const float tickSeconds = 1f / 40f;
            int updates = 0;
            while (_accumulator >= tickSeconds && updates < 4)
            {
                try
                {
                    _simulation.Tick();
                    if (!_simulation.IsStateValid())
                    {
                        Log.Write("Tick", "Estado inválido (NaN/infinito). Simulação reiniciada.");
                        ResetSimulation();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Tick", ex);
                    ResetSimulation();
                    break;
                }
                _accumulator -= tickSeconds;
                updates++;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                DrawScene(e.Graphics);
            }
            catch (Exception ex)
            {
                // Sem este catch o WinForms desenha um X vermelho e para de pintar o controle.
                Log.Error("Paint", ex);
            }
        }

        private void DrawScene(Graphics g)
        {
            g.Clear(KeyColor);
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            BodyRig rig = _simulation.Rig;
            Vec2 anchor = _simulation.TailAnchor;
            float centerX = ClientSize.Width * 0.5f;
            float floorY = ClientSize.Height - 19f;

            Vec2 upperAxis = (rig.Neck - rig.Belly).Normalized;
            Vec2 lowerAxis = (rig.Belly - rig.Waist).Normalized;
            float upperAngle = (float)(Math.Atan2(upperAxis.X, upperAxis.Y) * 180.0 / Math.PI);
            float lowerAngle = (float)(Math.Atan2(lowerAxis.X, lowerAxis.Y) * 180.0 / Math.PI);

            // Ordem (Rain World original): braco traseiro -> corpo -> rabo -> pernas -> quadril -> braco frontal -> cabeca -> rosto
            int backArmIndex = _simulation.Facing > 0 ? 1 : 0;
            int frontArmIndex = _simulation.Facing > 0 ? 0 : 1;

            DrawArm(g, backArmIndex, false, centerX, floorY);

            DrawSprite(g, _body, ToScreen(rig.Neck, centerX, floorY), upperAngle,
                false, 0.5f, 0.7894737f);

            DrawTail(g, anchor, centerX, floorY);

            DrawSprite(g, _legs[_simulation.LegsFrame], ToScreen(_simulation.LegsPos, centerX, floorY),
                0f, false, 0.5f, 0.25f);

            DrawSprite(g, _hips, ToScreen(rig.Belly, centerX, floorY), lowerAngle,
                false, 0.5f, 0.7894737f);

            DrawArm(g, frontArmIndex, true, centerX, floorY);

            Vec2 headPos = _simulation.HeadPos;
            DrawSprite(g, _heads[_simulation.HeadFrame], ToScreen(headPos, centerX, floorY),
                upperAngle * 0.35f, _simulation.Facing < 0f);

            Vec2 facePos = headPos + new Vec2(_simulation.LookX * 2.2f, -2f + _simulation.LookY * 1.5f);
            DrawSprite(g, _faces[_simulation.FaceFrame], ToScreen(facePos, centerX, floorY),
                upperAngle * 0.35f, _simulation.Facing < 0f);
        }

        private void DrawArm(Graphics g, int index, bool front, float centerX, float floorY)
        {
            Vec2 shoulder, hand;
            _simulation.GetArm(index, front, out shoulder, out hand);

            int frame = SlugcatSimulation.ArmFrameForLength(Vec2.Distance(shoulder, hand));
            PointF ps = ToScreen(shoulder, centerX, floorY);
            PointF ph = ToScreen(hand, centerX, floorY);

            // O sprite aponta do ombro (direita) para a mão (esquerda): o eixo -X do sprite
            // deve coincidir com o vetor ombro->mão na tela.
            // FIX 3: rotation formula needs +90 degrees (atan2(dx, dy) + PI/2)
            float dx = hand.X - shoulder.X;
            float dy = hand.Y - shoulder.Y;
            float angle = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI) + 90f;

            // Âncora = ombro (borda direita, meio da altura). Sem flip: a direção já espelha.
            // FIX 2: draw at HAND position (ph), not shoulder, with anchor (0.9, 0.5)
            DrawSprite(g, _arms[frame], ph, angle, false, 0.9f, 0.5f);
        }

        private void DrawTail(Graphics g, Vec2 anchor, float centerX, float floorY)
        {
            int count = _simulation.Tail.Length + 1;
            Vec2[] centerline = _tailCL;
            float[] radii = _tailR;
            centerline[0] = anchor;
            const float renderWidthScale = 0.72f;
            radii[0] = _simulation.Tail[0].StretchedRadius * renderWidthScale;
            for (int i = 0; i < _simulation.Tail.Length; i++)
            {
                centerline[i + 1] = _simulation.Tail[i].Pos;
                radii[i + 1] = i == _simulation.Tail.Length - 1
                    ? 0f
                    : _simulation.Tail[i].StretchedRadius * renderWidthScale;
            }

            // Interpolate between the four simulated tail segments before outlining them.
            const int curveSteps = 5;
            int smoothCount = (count - 1) * curveSteps + 1;
            Vec2[] smoothLine = _tailSmooth;
            float[] smoothRadii = _tailSmoothR;
            int sample = 0;
            for (int i = 0; i < count - 1; i++)
            {
                Vec2 p0 = centerline[Math.Max(0, i - 1)];
                Vec2 p1 = centerline[i];
                Vec2 p2 = centerline[i + 1];
                Vec2 p3 = centerline[Math.Min(count - 1, i + 2)];
                for (int step = 0; step < curveSteps; step++)
                {
                    float t = step / (float)curveSteps;
                    smoothLine[sample] = CatmullRom(p0, p1, p2, p3, t);
                    smoothRadii[sample] = radii[i] + (radii[i + 1] - radii[i]) * t;
                    sample++;
                }
            }
            smoothLine[sample] = centerline[count - 1];
            smoothRadii[sample] = radii[count - 1];

            Point[] outline = _tailOutline;
            for (int i = 0; i < smoothCount; i++)
            {
                Vec2 tangent = i == 0
                    ? smoothLine[1] - smoothLine[0]
                    : i == smoothCount - 1
                        ? smoothLine[i] - smoothLine[i - 1]
                        : smoothLine[i + 1] - smoothLine[i - 1];
                tangent = tangent.Normalized;
                if (tangent.Length < 0.001f)
                    tangent = new Vec2(-_simulation.Facing, 0f);
                Vec2 normal = new Vec2(-tangent.Y, tangent.X);
                outline[i] = RoundPoint(ToScreen(smoothLine[i] + normal * smoothRadii[i], centerX, floorY));
                outline[smoothCount * 2 - 1 - i] = RoundPoint(
                    ToScreen(smoothLine[i] - normal * smoothRadii[i], centerX, floorY));
            }

            g.FillPolygon(_whiteBrush, outline);
        }

        private static Vec2 CatmullRom(Vec2 p0, Vec2 p1, Vec2 p2, Vec2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            Vec2 value = p1 * 2f
                + (p2 - p0) * t
                + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2
                + (p1 * 3f - p0 - p2 * 3f + p3) * t3;
            return value * 0.5f;
        }

        private PointF ToScreen(Vec2 point, float centerX, float floorY)
        {
            return new PointF(centerX + point.X * PixelScale, floorY - point.Y * PixelScale);
        }

        private static Point RoundPoint(PointF point)
        {
            return new Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }

        private void DrawSprite(Graphics g, Bitmap sprite, PointF center, float angle, bool flipX)
        {
            DrawSprite(g, sprite, center, angle, flipX, 0.5f, 0.5f);
        }

        private void DrawSprite(Graphics g, Bitmap sprite, PointF anchor, float angle, bool flipX,
            float anchorX, float anchorY)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform((float)Math.Round(anchor.X), (float)Math.Round(anchor.Y));
            if (angle != 0f)
                g.RotateTransform(angle);
            if (flipX)
                g.ScaleTransform(-1f, 1f);

            int width = sprite.Width * PixelScale;
            int height = sprite.Height * PixelScale;
            int left = (int)Math.Round(-width * anchorX);
            int top = (int)Math.Round(-height * (1f - anchorY));
            g.DrawImage(sprite, new Rectangle(left, top, width, height),
                0, 0, sprite.Width, sprite.Height, GraphicsUnit.Pixel);
            g.Restore(state);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
            {
                Point delta = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y);
                Location = new Point(Location.X + delta.X, Location.Y + delta.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = false;
                
                // Se foi um click rápido (pouco movimento), faz o Slugcat pular
                Point delta = new Point(e.X - _dragStart.X, e.Y - _dragStart.Y);
                float distance = (float)Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
                if (distance < 10f) // Menos de 10 pixels = click, não drag
                {
                    // Verifica se clicou perto do corpo (área de 60x80px)
                    float centerX = ClientSize.Width * 0.5f;
                    float floorY = ClientSize.Height - 19f;
                    Vec2 bodyPos = (_simulation.Chunks[0].Pos + _simulation.Chunks[1].Pos) * 0.5f;
                    PointF screenPos = ToScreen(bodyPos, centerX, floorY);
                    
                    if (Math.Abs(e.X - screenPos.X) < 30 && Math.Abs(e.Y - screenPos.Y) < 40)
                        _simulation.RequestJump();
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _menu.Dispose();
            _body.Dispose();
            _hips.Dispose();
            for (int i = 0; i < _heads.Length; i++)
                _heads[i].Dispose();
            for (int i = 0; i < _faces.Length; i++)
                _faces[i].Dispose();
            for (int i = 0; i < _legs.Length; i++)
                _legs[i].Dispose();
            for (int i = 0; i < _arms.Length; i++)
                _arms[i].Dispose();
            base.OnFormClosed(e);
        }
    }
}
