using System;

namespace SlugcatDesktop
{
    /// <summary>
    /// A small standalone simulation of the vanilla Survivor's body chunks and tail.
    /// Terrain and room-dependent behaviors are replaced by a flat desktop floor.
    /// </summary>
    internal sealed class SlugcatSimulation : IPhysicsProvider
    {
        public const float Gravity = 0.9f;
        public const float AirFriction = 0.999f;
        public const float Bounce = 0.1f;
        public const float SurfaceFriction = 0.5f;
        public const float BodyConnectionDistance = 17f;

        // Com atrito de superfície 0,5 a velocidade de equilíbrio é igual à força aplicada por tick.
        public const float WalkTargetSpeed = 1.1f;

        // Metade da largura do mundo em unidades de simulação (definida pela janela a partir do monitor).
        private float _worldHalfWidth = 60f;
        public float WorldHalfWidth { get { return _worldHalfWidth; } set { _worldHalfWidth = value; } }

        
        // Constantes para fisica da cabeca
        private const float HEAD_AIR = 0.99f;
        private const float HEAD_CONNECT_RAD = 3f;
        private const float HEAD_ELASTIC = 0.2f;
        private const float HEAD_ADAPT_RETAIN = 0.3f;
        private const float HEAD_EXAGGERATE = 0.1f;

        // Constantes para fisica das pernas (baseado no slugcatpet)
        private const float LEGS_CONNECT_RAD = 5f;
        private const float LEGS_HOST_VY = -10f;
        private const float LEGS_ELASTIC = 0.25f;
        private const float LEGS_ADAPT_RETAIN = 0.5f;
        private const float LEGS_EXAGGERATE = 0.1f;

        private readonly BodyChunk[] _chunks;
        private readonly TailSegment[] _tail;
        public BodyChunk[] Chunks { get { return _chunks; } }
        public TailSegment[] Tail { get { return _tail; } }
        private Vec2 _bellyPos;
        private Vec2 _bellyVel;
        private Vec2 _headPos;
        public Vec2 HeadPos { get { return _headPos; } set { _headPos = value; } }
        public Vec2 HeadLastPos;
        public Vec2 HeadVel;
        private Vec2 _legsPos;
        public Vec2 LegsPos { get { return _legsPos; } set { _legsPos = value; } }
        public GenericBone Legs;
        public Vec2 LegsDirection;
        private float _facing = 1f;
        public float Facing { get { return _facing; } set { _facing = value; } }
        private float _lookX = 1f;
        public float LookX { get { return _lookX; } set { _lookX = value; } }
        private float _lookY = 0f;
        public float LookY { get { return _lookY; } set { _lookY = value; } }
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
            _chunks = new BodyChunk[]
            {
                new BodyChunk(9f, new Vec2(0f, 25f)),
                new BodyChunk(8f, new Vec2(0f, 8f))
            };
            _bellyPos = Vec2.Lerp(Chunks[1].Pos, Chunks[0].Pos, 0.5f);

            // Vanilla adult Survivor tail radii and connection radii.
            float[] radii = new float[] { 6f, 4f, 2.5f, 1f };
            float[] connections = new float[] { 4f, 7f, 7f, 7f };
            const float tailLengthScale = 1.0f;
            _tail = new TailSegment[4];
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
            
            // Pernas como GenericBone (fisica elastica completa do slugcatpet)
            Vec2 hipPos = Chunks[1].Pos;
            Legs = new GenericBone();
            Legs.Pos = hipPos + new Vec2(0f, -8f);
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
        /// Atualiza fisica das pernas com fisica elastica completa (baseado no slugcatpet).
        /// Usa ConnectToPoint para inercia real.
        /// </summary>
        private void UpdateLegs()
        {
            Vec2 hipPos = Chunks[1].Pos;
            
            // Target: pernas ficam abaixo do quadril, com offset horizontal baseado no movimento
            float legOffsetX = 0f;
            float legOffsetY = -8f;  // Pernas ficam 8 unidades abaixo do quadril
            
            if (Walking && IsOnGround())
            {
                // Quando andando, as pernas oscilam com o ciclo de caminhada
                float swing = (float)Math.Sin(WalkPhase * 0.35f) * 2f * _walkAcceleration;
                legOffsetX = Facing * swing;
            }
            
            Vec2 target = hipPos + new Vec2(legOffsetX, legOffsetY);
            
            // Velocidades do corpo (host) para exagerar movimento
            float hostVx = Chunks[1].Vel.X;
            float hostVy = Chunks[1].Vel.Y + LEGS_HOST_VY;  // Bias para baixo
            
            // Atualiza fisica: inercia + atrito do ar
            Legs.Update(0.95f);
            
            // Conecta ao target com fisica elastica completa (como slugcatpet)
            Legs.ConnectToPoint(target, hostVx, hostVy, 
                LEGS_CONNECT_RAD, LEGS_ELASTIC, LEGS_ADAPT_RETAIN, LEGS_EXAGGERATE);
            
            // Colisao com chao
            Legs.PushOutOfFloor(0f, 1f);
            
            // Sincroniza LegsPos para compatibilidade com renderizacao
            LegsPos = Legs.Pos;
            
            // Atualiza direcao das pernas
            UpdateLegsDirection();
        }

        /// <summary>
        /// Atualiza a direcao das pernas conforme o movimento e estado.
        /// Simplificado: no chao andando (-Facing*0.3, -0.9), senao (decai, -1).
        /// </summary>
        private void UpdateLegsDirection()
        {
            if (IsOnGround() && Walking)
            {
                LegsDirection.X = -Facing * 0.3f;
                LegsDirection.Y = -0.9f;
            }
            else
            {
                LegsDirection.X *= 0.95f;
                LegsDirection.Y = -1f;
            }
            
            // Normaliza
            float len = LegsDirection.Length;
            if (len > 0.0001f)
            {
                LegsDirection = LegsDirection / len;
            }
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
                
                // Frame baseado em WalkPhase para animacao suave
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
}
