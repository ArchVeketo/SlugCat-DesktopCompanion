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
        private static void Main(string[] args)
        {
            // Modo benchmark: roda automaticamente e fecha
            bool benchmarkMode = args.Length > 0 && args[0] == "--benchmark";
            
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Error("ThreadException", e.Exception);
            };
            try
            {
                Application.Run(new CompanionWindow(benchmarkMode));
            }
            catch (Exception ex)
            {
                Log.Error("Main", ex);
                if (!benchmarkMode)
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
    /// Parte generica do corpo com fisica propria (inercia, velocidade, conexao elastica).
    /// Baseado no GenericBodyPart do Rain World original.
    /// </summary>
    internal sealed class GenericBodyPart
    {
        public Vec2 Pos;
        public Vec2 LastPos;
        public Vec2 Vel;
        public float Rad;

        public GenericBodyPart(float rad, Vec2 pos)
        {
            Rad = rad;
            Pos = pos;
            LastPos = pos;
            Vel = new Vec2(0f, 0f);
        }

        /// <summary>
        /// Atualiza fisica: inercia + atrito do ar.
        /// </summary>
        public void Update(float airFriction)
        {
            LastPos = Pos;
            Pos = Pos + Vel;
            Vel = Vel * airFriction;
        }

        /// <summary>
        /// Conecta esta parte a um ponto-alvo com distancia maxima.
        /// Se a distancia exceder connectionRad, reposiciona e aplica push na velocidade.
        /// </summary>
        public void ConnectToPoint(Vec2 target, float connectionRad, float push)
        {
            Vec2 diff = Pos - target;
            float dist = diff.Length;
            if (dist > connectionRad && dist > 0.0001f)
            {
                Vec2 dir = diff / dist;
                Pos = target + dir * connectionRad;
                if (push > 0f)
                    Vel = Vel + dir * (dist - connectionRad) * push;
            }
        }

        /// <summary>
        /// Colisao com chao (y=0).
        /// </summary>
        public void PushOutOfFloor(float floorY = 0f)
        {
            if (Pos.Y < floorY + Rad * 0.5f)
            {
                Pos.Y = floorY + Rad * 0.5f;
                if (Vel.Y < 0f)
                    Vel.Y = -Vel.Y * 0.1f;
                Vel.X *= 0.9f;
            }
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

        // Constantes para fisica das pernas (escaladas para sprite maior)
        private const float LegsAirFriction = 0.99f;
        private const float LegsConnectRad = 7f;

        public readonly BodyChunk[] Chunks;
        public readonly TailSegment[] Tail;
        private Vec2 _bellyPos;
        private Vec2 _bellyVel;
        public Vec2 HeadPos;
        public Vec2 HeadLastPos;
        public Vec2 HeadVel;
        public Vec2 LegsPos;
        public GenericBodyPart Legs;
        public Vec2 LegsDirection;
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
            
            // Pernas como GenericBodyPart real (fisica igual ao jogo)
            Vec2 hipPos = Chunks[1].Pos;
            Legs = new GenericBodyPart(2f, hipPos + new Vec2(0f, -2f));
            LegsDirection = new Vec2(0f, -1f);
            LegsPos = Legs.Pos;
            
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
            // Verifica GenericBodyPart Legs
            if (Legs != null)
            {
                if (Bad(Legs.Pos.X) || Bad(Legs.Pos.Y) || Bad(Legs.Vel.X) || Bad(Legs.Vel.Y))
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
            UpdateLegs();
        }

        /// <summary>
        /// Atualiza fisica das pernas com inercia real (igual ao GenericBodyPart do jogo).
        /// </summary>
        private void UpdateLegs()
        {
            // 1. Atualiza fisica (inercia + atrito)
            Legs.Update(LegsAirFriction);
            
            // 2. Gravidade leve nas pernas
            Legs.Vel.Y -= Gravity * 0.3f;
            
            // 3. Conecta ao quadril (Chunk[1])
            Vec2 hipPos = Chunks[1].Pos;
            Legs.ConnectToPoint(hipPos, LegsConnectRad, 0.5f);
            
            // 4. Colisao com chao
            Legs.PushOutOfFloor();
            
            // 5. Atualiza legsDirection baseado no bodyMode/movimento
            UpdateLegsDirection();
            
            // 6. Forca as pernas a seguirem a direcao desejada (suavemente)
            Vec2 targetPos = hipPos + LegsDirection * 5f;
            Legs.Vel = Legs.Vel + (targetPos - Legs.Pos) * 0.08f;
            
            // 7. Sincroniza LegsPos para compatibilidade com codigo existente
            LegsPos = Legs.Pos;
        }

        /// <summary>
        /// Atualiza a direcao das pernas conforme o movimento e estado.
        /// </summary>
        private void UpdateLegsDirection()
        {
            // No chao andando: pernas apontam ligeiramente para tras (oposto a direcao)
            if (Walking && IsOnGround())
            {
                float targetX = -Facing * 0.3f;
                LegsDirection.X = LegsDirection.X + (targetX - LegsDirection.X) * 0.1f;
                LegsDirection.Y = -0.9f;
            }
            // No ar (pulando/caindo): pernas apontam para baixo e ligeiramente para tras
            else if (!IsOnGround())
            {
                float fallSpeed = -Chunks[1].Vel.Y;
                if (fallSpeed > 2f)
                {
                    LegsDirection.X = LegsDirection.X * 0.95f;
                    LegsDirection.Y = -1f;
                }
                else if (Chunks[1].Vel.Y > 2f)
                {
                    LegsDirection.X = Facing * 0.2f;
                    LegsDirection.Y = -0.8f;
                }
            }
            // Parado: pernas retas para baixo
            else
            {
                LegsDirection.X = LegsDirection.X * 0.92f;
                LegsDirection.Y = -1f;
            }
            
            // Normaliza
            LegsDirection = LegsDirection.Normalized;
            if (LegsDirection.Length < 0.001f)
                LegsDirection = new Vec2(0f, -1f);
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

            hand = shoulder + swung * 10f;
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
            get
            {
                if (!Walking || WalkSpeed < 0.08f)
                    return 0;
                
                // Usa a posicao horizontal relativa das pernas para determinar o frame
                float legOffset = Legs.Pos.X - Chunks[1].Pos.X;
                float normalizedOffset = legOffset / LegsConnectRad;
                
                // Combina com WalkPhase para animacao suave
                float phase = (float)Math.Sin(WalkPhase * 0.35f);
                int frame = (int)Math.Round((phase + 1f) * 3f);
                return Clamp(frame, 0, 6);
            }
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

        // === DEBUG MODE ===
        private const bool DebugMode = true; // Debug mode
        private int _debugFrameCounter;
        private const int DebugCaptureInterval = 10; // Captura mais frequente no benchmark
        private string _debugFolder;
        private Font _debugFont;
        private Font _debugFontSmall;
        private bool _debugCapturePaused;
        private bool _debugShowBounds = true;
        private bool _debugShowOrder = true;
        private bool _debugShowVectors = true;
        
        // === BENCHMARK MODE ===
        private bool _benchmarkMode;
        private int _benchmarkFrameTarget = 400; // Total de frames a simular
        private int _benchmarkPhase = 0; // 0=idle, 1=walk, 2=jump, 3=walk outro lado

        public CompanionWindow() : this(false) { }
        
        public CompanionWindow(bool benchmarkMode)
        {
            _benchmarkMode = benchmarkMode;
            
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

            // === DEBUG SETUP ===
            if (DebugMode)
            {
                _debugFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_frames");
                if (Directory.Exists(_debugFolder))
                    Directory.Delete(_debugFolder, true);
                Directory.CreateDirectory(_debugFolder);
                _debugFont = new Font("Consolas", 9f, FontStyle.Bold);
                _debugFontSmall = new Font("Consolas", 7f);
                KeyPreview = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (DebugMode)
            {
                if (e.KeyCode == Keys.Space) _debugCapturePaused = !_debugCapturePaused;
                if (e.KeyCode == Keys.B) _debugShowBounds = !_debugShowBounds;
                if (e.KeyCode == Keys.O) _debugShowOrder = !_debugShowOrder;
                if (e.KeyCode == Keys.V) _debugShowVectors = !_debugShowVectors;
                if (e.KeyCode == Keys.Escape) Close();
            }
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
            // Benchmark: controla fases e fecha quando terminar
            if (_benchmarkMode)
            {
                RunBenchmarkPhase();
                if (_debugFrameCounter >= _benchmarkFrameTarget)
                {
                    Close();
                    return;
                }
            }
            
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

        private void RunBenchmarkPhase()
        {
            // Fases do benchmark para testar diferentes estados
            // 0-100: idle (parado)
            // 100-200: walking direita
            // 200-250: jump
            // 250-350: walking esquerda  
            // 350-400: idle final
            
            int frame = _debugFrameCounter;
            
            if (frame == 0)
            {
                // Reset para estado inicial
                _simulation.Facing = 1f;
                _simulation.LookX = 1f;
            }
            else if (frame == 100)
            {
                // Força walking para direita via reflexão do estado interno
                _benchmarkPhase = 1;
            }
            else if (frame == 200)
            {
                // Jump
                _simulation.RequestJump();
                _benchmarkPhase = 2;
            }
            else if (frame == 250)
            {
                // Walking esquerda
                _benchmarkPhase = 3;
            }
            else if (frame == 350)
            {
                // Idle final
                _benchmarkPhase = 4;
            }
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
            
            // Debug: fundo preto
            if (DebugMode)
                g.FillRectangle(Brushes.Black, 0, 0, ClientSize.Width, ClientSize.Height);
            
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

            // === DESENHO COM DEBUG ===
            // Ordem correta Rain World: Pernas -> BracoTras -> Quadril -> Corpo -> Rabo -> BracoFrente -> Cabeca -> Rosto
            int drawOrder = 0;

            // 1. Pernas (mais atrás de tudo)
            // Âncora: centro horizontal (0.5), topo do sprite (1.0) para conectar ao corpo
            drawOrder++;
            DrawSpriteDebug(g, _legs[_simulation.LegsFrame], ToScreen(_simulation.LegsPos, centerX, floorY),
                0f, false, 0.5f, 0.75f, drawOrder, "Legs", Color.Lime);

            // 2. Braço traseiro
            drawOrder++;
            DrawArmDebug(g, backArmIndex, false, centerX, floorY, drawOrder, "BackArm");

            // 3. Quadril
            drawOrder++;
            DrawSpriteDebug(g, _hips, ToScreen(rig.Belly, centerX, floorY), lowerAngle,
                false, 0.5f, 0.5f, drawOrder, "Hips", Color.Orange);

            // 4. Corpo
            drawOrder++;
            DrawSpriteDebug(g, _body, ToScreen(rig.Neck, centerX, floorY), upperAngle,
                false, 0.5f, 0.2105263f, drawOrder, "Body", Color.Cyan);

            // 5. Rabo
            drawOrder++;
            DrawTail(g, anchor, centerX, floorY);
            if (DebugMode && _debugShowOrder)
                DrawOrderLabel(g, ToScreen(anchor, centerX, floorY), drawOrder, "Tail", Color.Yellow);

            // 6. Braço frontal
            drawOrder++;
            DrawArmDebug(g, frontArmIndex, true, centerX, floorY, drawOrder, "FrontArm");

            // 7. Cabeça
            drawOrder++;
            Vec2 headPos = _simulation.HeadPos;
            DrawSpriteDebug(g, _heads[_simulation.HeadFrame], ToScreen(headPos, centerX, floorY),
                upperAngle * 0.35f, _simulation.Facing < 0f, 0.5f, 0.5f, drawOrder, "Head", Color.Red);

            // 8. Rosto
            drawOrder++;
            Vec2 facePos = headPos + new Vec2(_simulation.LookX * 2.2f, -2f + _simulation.LookY * 1.5f);
            DrawSpriteDebug(g, _faces[_simulation.FaceFrame], ToScreen(facePos, centerX, floorY),
                upperAngle * 0.35f, _simulation.Facing < 0f, 0.5f, 0.5f, drawOrder, "Face", Color.Magenta);

            // === DEBUG OVERLAY ===
            if (DebugMode)
            {
                DrawDebugVectors(g, rig, centerX, floorY);
                DrawDebugPanel(g, rig, upperAngle, lowerAngle);
                
                _debugFrameCounter++;
                if (!_debugCapturePaused && _debugFrameCounter % DebugCaptureInterval == 0)
                    CaptureDebugFrame();
            }
        }

        private void DrawSpriteDebug(Graphics g, Bitmap sprite, PointF anchor, float angle, bool flipX,
            float anchorX, float anchorY, int order, string name, Color debugColor)
        {
            // Desenha o sprite normalmente
            DrawSprite(g, sprite, anchor, angle, flipX, anchorX, anchorY);
            
            if (!DebugMode) return;
            
            // Debug: ponto de âncora
            using (SolidBrush b = new SolidBrush(debugColor))
                g.FillEllipse(b, anchor.X - 3, anchor.Y - 3, 6, 6);
            
            // Debug: label de ordem
            if (_debugShowOrder)
                DrawOrderLabel(g, anchor, order, name, debugColor);
            
            // Debug: bounding box aproximada
            if (_debugShowBounds)
            {
                int w = sprite.Width * PixelScale;
                int h = sprite.Height * PixelScale;
                using (Pen p = new Pen(debugColor, 1f))
                {
                    p.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                    g.DrawRectangle(p, anchor.X - w/2, anchor.Y - h/2, w, h);
                }
            }
        }

        private void DrawArmDebug(Graphics g, int index, bool front, float centerX, float floorY, int order, string name)
        {
            Vec2 shoulder, hand;
            _simulation.GetArm(index, front, out shoulder, out hand);

            int frame = SlugcatSimulation.ArmFrameForLength(Vec2.Distance(shoulder, hand));
            PointF ps = ToScreen(shoulder, centerX, floorY);
            PointF ph = ToScreen(hand, centerX, floorY);

            float dx = hand.X - shoulder.X;
            float dy = hand.Y - shoulder.Y;
            float angle = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI) + 90f;

            DrawSprite(g, _arms[frame], ph, angle, false, 0.9f, 0.5f);
            
            if (!DebugMode) return;
            
            Color col = front ? Color.Magenta : Color.Cyan;
            
            // Ombro
            using (SolidBrush b = new SolidBrush(Color.Cyan))
                g.FillEllipse(b, ps.X - 4, ps.Y - 4, 8, 8);
            
            // Mão
            using (SolidBrush b = new SolidBrush(Color.Magenta))
                g.FillEllipse(b, ph.X - 4, ph.Y - 4, 8, 8);
            
            // Linha ombro->mão
            if (_debugShowVectors)
            {
                using (Pen p = new Pen(Color.White, 1f))
                    g.DrawLine(p, ps, ph);
            }
            
            // Label
            if (_debugShowOrder)
            {
                string label = string.Format("{0}.{1} f{2} d{3:F0}", order, name, frame, Vec2.Distance(shoulder, hand));
                DrawOrderLabel(g, ps, order, label, col);
            }
        }

        private void DrawOrderLabel(Graphics g, PointF pos, int order, string name, Color color)
        {
            string text = string.Format("{0}.{1}", order, name);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (SolidBrush fg = new SolidBrush(color))
            {
                SizeF size = g.MeasureString(text, _debugFontSmall);
                g.FillRectangle(bg, pos.X + 5, pos.Y - 10, size.Width + 2, size.Height);
                g.DrawString(text, _debugFontSmall, fg, pos.X + 6, pos.Y - 10);
            }
        }

        private void DrawDebugVectors(Graphics g, BodyRig rig, float centerX, float floorY)
        {
            if (!_debugShowVectors) return;
            
            // Chunks
            PointF c0 = ToScreen(_simulation.Chunks[0].Pos, centerX, floorY);
            PointF c1 = ToScreen(_simulation.Chunks[1].Pos, centerX, floorY);
            g.FillEllipse(Brushes.Red, c0.X - 5, c0.Y - 5, 10, 10);
            g.FillEllipse(Brushes.Blue, c1.X - 5, c1.Y - 5, 10, 10);
            g.DrawLine(Pens.White, c0, c1);
            
            // Rig points
            PointF waist = ToScreen(rig.Waist, centerX, floorY);
            PointF belly = ToScreen(rig.Belly, centerX, floorY);
            PointF neck = ToScreen(rig.Neck, centerX, floorY);
            g.FillEllipse(Brushes.Green, belly.X - 3, belly.Y - 3, 6, 6);
            
            // Tail segments
            PointF tailAnchor = ToScreen(_simulation.TailAnchor, centerX, floorY);
            g.FillEllipse(Brushes.Orange, tailAnchor.X - 4, tailAnchor.Y - 4, 8, 8);
            PointF prev = tailAnchor;
            for (int i = 0; i < _simulation.Tail.Length; i++)
            {
                PointF tp = ToScreen(_simulation.Tail[i].Pos, centerX, floorY);
                g.FillEllipse(Brushes.Yellow, tp.X - 3, tp.Y - 3, 6, 6);
                g.DrawLine(Pens.Yellow, prev, tp);
                prev = tp;
            }
        }

        private void DrawDebugPanel(Graphics g, BodyRig rig, float upperAngle, float lowerAngle)
        {
            Vec2 shoulder0, hand0, shoulder1, hand1;
            _simulation.GetArm(0, true, out shoulder0, out hand0);
            _simulation.GetArm(1, false, out shoulder1, out hand1);
            
            string[] lines = new string[]
            {
                "=== SLUGCAT DEBUG v25 ===",
                string.Format("Frame:{0} Capture:{1}", _debugFrameCounter, _debugCapturePaused ? "PAUSED" : "ON"),
                string.Format("Facing:{0:F1} Walk:{1} Spd:{2:F2}", _simulation.Facing, _simulation.Walking, _simulation.WalkSpeed),
                "",
                "--- Z-ORDER (back to front) ---",
                "1.Legs 2.BackArm 3.Hips 4.Body",
                "5.Tail 6.FrontArm 7.Head 8.Face",
                "",
                "--- POSITIONS ---",
                string.Format("Chunk0(red): {0:F1},{1:F1}", _simulation.Chunks[0].Pos.X, _simulation.Chunks[0].Pos.Y),
                string.Format("Chunk1(blue): {0:F1},{1:F1}", _simulation.Chunks[1].Pos.X, _simulation.Chunks[1].Pos.Y),
                string.Format("LegsPos: {0:F1},{1:F1}", _simulation.LegsPos.X, _simulation.LegsPos.Y),
                "",
                "--- ARMS ---",
                string.Format("Arm0: sh({0:F0},{1:F0}) hd({2:F0},{3:F0}) d={4:F0}", 
                    shoulder0.X, shoulder0.Y, hand0.X, hand0.Y, Vec2.Distance(shoulder0, hand0)),
                string.Format("Arm1: sh({0:F0},{1:F0}) hd({2:F0},{3:F0}) d={4:F0}",
                    shoulder1.X, shoulder1.Y, hand1.X, hand1.Y, Vec2.Distance(shoulder1, hand1)),
                "",
                "--- KEYS ---",
                "[B] Bounds  [O] Order  [V] Vectors",
                "[SPACE] Pause capture  [ESC] Exit"
            };
            
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (SolidBrush fg = new SolidBrush(Color.Lime))
            {
                float y = 8;
                float maxW = 0;
                foreach (string line in lines)
                {
                    SizeF sz = g.MeasureString(line, _debugFont);
                    if (sz.Width > maxW) maxW = sz.Width;
                }
                g.FillRectangle(bg, 5, 5, maxW + 15, lines.Length * 14 + 10);
                foreach (string line in lines)
                {
                    g.DrawString(line, _debugFont, fg, 10, y);
                    y += 14;
                }
            }
        }

        private void CaptureDebugFrame()
        {
            try
            {
                // Salva imagem PNG
                using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
                    string filename = Path.Combine(_debugFolder, string.Format("frame_{0:D6}.png", _debugFrameCounter));
                    bmp.Save(filename, ImageFormat.Png);
                }
                
                // Salva dados estruturados em JSON para análise automatizada
                SaveFrameData();
            }
            catch (Exception ex)
            {
                Log.Error("CaptureDebugFrame", ex);
            }
        }

        private void SaveFrameData()
        {
            try
            {
                Vec2 shoulder0, hand0, shoulder1, hand1;
                _simulation.GetArm(0, true, out shoulder0, out hand0);
                _simulation.GetArm(1, false, out shoulder1, out hand1);
                
                // Formato JSON manual (sem dependência de biblioteca)
                string json = string.Format(@"{{
  ""frame"": {0},
  ""state"": {{
    ""facing"": {1:F2},
    ""walking"": {2},
    ""walkSpeed"": {3:F2},
    ""lookX"": {4:F2},
    ""lookY"": {5:F2}
  }},
  ""chunks"": {{
    ""head"": {{ ""x"": {6:F1}, ""y"": {7:F1}, ""vx"": {8:F2}, ""vy"": {9:F2} }},
    ""body"": {{ ""x"": {10:F1}, ""y"": {11:F1}, ""vx"": {12:F2}, ""vy"": {13:F2} }}
  }},
  ""arms"": {{
    ""arm0"": {{ ""shoulder"": {{ ""x"": {14:F1}, ""y"": {15:F1} }}, ""hand"": {{ ""x"": {16:F1}, ""y"": {17:F1} }}, ""distance"": {18:F1} }},
    ""arm1"": {{ ""shoulder"": {{ ""x"": {19:F1}, ""y"": {20:F1} }}, ""hand"": {{ ""x"": {21:F1}, ""y"": {22:F1} }}, ""distance"": {23:F1} }}
  }},
  ""legs"": {{ ""x"": {24:F1}, ""y"": {25:F1}, ""frame"": {26} }},
  ""head"": {{ ""x"": {27:F1}, ""y"": {28:F1}, ""frame"": {29} }},
  ""tail"": [
    {{ ""x"": {30:F1}, ""y"": {31:F1}, ""radius"": {32:F2} }},
    {{ ""x"": {33:F1}, ""y"": {34:F1}, ""radius"": {35:F2} }},
    {{ ""x"": {36:F1}, ""y"": {37:F1}, ""radius"": {38:F2} }},
    {{ ""x"": {39:F1}, ""y"": {40:F1}, ""radius"": {41:F2} }}
  ],
  ""issues"": []
}}",
                    _debugFrameCounter,
                    _simulation.Facing,
                    _simulation.Walking.ToString().ToLower(),
                    _simulation.WalkSpeed,
                    _simulation.LookX,
                    _simulation.LookY,
                    _simulation.Chunks[0].Pos.X, _simulation.Chunks[0].Pos.Y,
                    _simulation.Chunks[0].Vel.X, _simulation.Chunks[0].Vel.Y,
                    _simulation.Chunks[1].Pos.X, _simulation.Chunks[1].Pos.Y,
                    _simulation.Chunks[1].Vel.X, _simulation.Chunks[1].Vel.Y,
                    shoulder0.X, shoulder0.Y, hand0.X, hand0.Y, Vec2.Distance(shoulder0, hand0),
                    shoulder1.X, shoulder1.Y, hand1.X, hand1.Y, Vec2.Distance(shoulder1, hand1),
                    _simulation.LegsPos.X, _simulation.LegsPos.Y, _simulation.LegsFrame,
                    _simulation.HeadPos.X, _simulation.HeadPos.Y, _simulation.HeadFrame,
                    _simulation.Tail[0].Pos.X, _simulation.Tail[0].Pos.Y, _simulation.Tail[0].StretchedRadius,
                    _simulation.Tail[1].Pos.X, _simulation.Tail[1].Pos.Y, _simulation.Tail[1].StretchedRadius,
                    _simulation.Tail[2].Pos.X, _simulation.Tail[2].Pos.Y, _simulation.Tail[2].StretchedRadius,
                    _simulation.Tail[3].Pos.X, _simulation.Tail[3].Pos.Y, _simulation.Tail[3].StretchedRadius
                );
                
                string jsonFile = Path.Combine(_debugFolder, string.Format("frame_{0:D6}.json", _debugFrameCounter));
                File.WriteAllText(jsonFile, json);
            }
            catch (Exception ex)
            {
                Log.Error("SaveFrameData", ex);
            }
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
            // Anchor semantics (matching slugcatpet): anchorY=0.75 means 75% of sprite height ABOVE the draw point
            int left = (int)Math.Round(-width * anchorX);
            int top = (int)Math.Round(-height * anchorY);
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
