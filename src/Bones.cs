namespace SlugcatDesktop
{
    /// <summary>
    /// Osso generico com fisica de conexao elastica completa (baseado no slugcatpet).
    /// Usado para pernas e cabeca - partes que seguem o corpo com inercia real.
    /// </summary>
    internal sealed class GenericBone
    {
        public Vec2 Pos;
        public Vec2 LastPos;
        public Vec2 Vel;

        public GenericBone()
        {
            Pos = new Vec2(0f, 0f);
            LastPos = Pos;
            Vel = new Vec2(0f, 0f);
        }

        /// <summary>
        /// Atualiza fisica: inercia + atrito do ar.
        /// </summary>
        public void Update(float airFriction = 0.99f)
        {
            LastPos = Pos;
            Pos = Pos + Vel;
            Vel = Vel * airFriction;
        }

        /// <summary>
        /// Conecta este osso a um ponto-alvo com fisica elastica completa.
        /// Baseado no ConnectToPoint do slugcatpet com todos os parametros.
        /// </summary>
        /// <param name="target">Ponto alvo (ex: quadril)</param>
        /// <param name="hostVx">Velocidade X do host (para exagerar movimento)</param>
        /// <param name="hostVy">Velocidade Y do host (para exagerar movimento)</param>
        /// <param name="connectRad">Raio maximo de conexao</param>
        /// <param name="elastic">Forca elastica (quanto puxa de volta)</param>
        /// <param name="adaptRetain">Retencao da velocidade propria vs host</param>
        /// <param name="exaggerate">Quanto exagera o movimento do host</param>
        public void ConnectToPoint(Vec2 target, float hostVx, float hostVy, 
            float connectRad = 5f, float elastic = 0.25f, float adaptRetain = 0.5f, float exaggerate = 0.1f)
        {
            Vec2 diff = target - Pos;
            float dist = diff.Length;
            
            // 1. Forca elastica: puxa na direcao do alvo
            if (dist > 0.0001f)
            {
                Vel.X += diff.X * elastic;
                Vel.Y += diff.Y * elastic;
            }
            
            // 2. Exagera movimento do host (da inercia visual)
            Vel.X += hostVx * exaggerate;
            Vel.Y += hostVy * exaggerate;
            
            // 3. Limita distancia: se muito longe, reposiciona
            if (dist > connectRad && dist > 0.0001f)
            {
                Vec2 dir = diff / dist;
                float corr = dist - connectRad;
                Pos.X += dir.X * corr;
                Pos.Y += dir.Y * corr;
                Vel.X += dir.X * corr;
                Vel.Y += dir.Y * corr;
            }
            
            // 4. Adapta velocidade: mistura velocidade propria com a do host
            Vel.X = hostVx + (Vel.X - hostVx) * adaptRetain;
            Vel.Y = hostVy + (Vel.Y - hostVy) * adaptRetain;
        }

        /// <summary>
        /// Colisao com chao.
        /// </summary>
        public void PushOutOfFloor(float floorY = 0f, float margin = 1f)
        {
            if (Pos.Y < floorY + margin)
            {
                Pos.Y = floorY + margin;
                if (Vel.Y < 0f)
                    Vel.Y = -Vel.Y * 0.1f;
                Vel.X *= 0.9f;
            }
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
}
