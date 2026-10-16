using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SlugcatDesktop
{
    /// <summary>
    /// Render de malha deformavel do Slugcat via warp afim por triangulo em GDI+.
    /// Contem os helpers de FW 4.0 (sem Math.Clamp, sem MathF) e o nucleo da malha:
    /// DrawTexturedTriangle (+ guard IsDegenerate) e PadTexture (borda replicada).
    /// Montagem da malha da cauda: BuildTailVertices/BuildTailTriangles (FEAT-003).
    /// A grade do tronco (BuildBodyGrid) entra em FEAT seguinte.
    /// </summary>
    internal static class MeshRenderer
    {
        /// <summary>
        /// Epsilon da area (dobro do triangulo via produto vetorial) abaixo do qual
        /// o triangulo e tratado como degenerado e nao e desenhado.
        /// </summary>
        private const float EpsilonArea = 1e-3f;

        /// <summary>
        /// Desenha a textura mapeando o triangulo UV (coordenadas em pixels da textura)
        /// ao triangulo de tela via warp afim. GDI+ resolve a transformacao afim a partir
        /// dos 3 pontos de destino, que correspondem respectivamente aos cantos
        /// superior-esquerdo, superior-direito e inferior-esquerdo do retangulo-fonte.
        /// Como o triangulo raramente e "metade de um retangulo UV", recortamos a saida ao
        /// poligono de destino com SetClip; o excedente da textura warpada fica fora do clip.
        /// Pixel art: SmoothingMode.None, InterpolationMode.NearestNeighbor, PixelOffsetMode.Half.
        /// </summary>
        /// <param name="g">Superficie de desenho.</param>
        /// <param name="textura">Bitmap de origem (idealmente ja passado por PadTexture).</param>
        /// <param name="uv0">Canto UV correspondente a d0 (px da textura).</param>
        /// <param name="uv1">Canto UV correspondente a d1 (px da textura).</param>
        /// <param name="uv2">Canto UV correspondente a d2 (px da textura).</param>
        /// <param name="d0">Vertice de tela correspondente a uv0 (px).</param>
        /// <param name="d1">Vertice de tela correspondente a uv1 (px).</param>
        /// <param name="d2">Vertice de tela correspondente a uv2 (px).</param>
        public static void DrawTexturedTriangle(
            Graphics g, Bitmap textura,
            PointF uv0, PointF uv1, PointF uv2,
            PointF d0, PointF d1, PointF d2)
        {
            // Guard R1.4: triangulo degenerado/invertido/nao-finito nao e desenhado.
            if (IsDegenerate(d0, d1, d2))
                return;

            using (GraphicsPath recorte = new GraphicsPath())
            {
                recorte.AddPolygon(new[] { d0, d1, d2 });

                GraphicsState estado = g.Save();
                try
                {
                    // Pixel art: sem antialiasing que borre a borda (R1.3).
                    g.SmoothingMode = SmoothingMode.None;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.SetClip(recorte);

                    // destino = cantos (0,0),(largura,0),(0,altura) do srcRect, nessa ordem.
                    PointF[] destino = { d0, d1, d2 };
                    RectangleF origem = BoundingRect(uv0, uv1, uv2);
                    g.DrawImage(textura, destino, origem, GraphicsUnit.Pixel);
                }
                finally
                {
                    g.Restore(estado);
                }
            }
        }

        /// <summary>
        /// Verdadeiro quando o triangulo nao deve ser desenhado: algum vertice nao-finito
        /// OU area praticamente nula (vertices colineares/coincidentes). O sinal do produto
        /// vetorial tambem detecta inversao quando combinado com o lado esperado da malha.
        /// </summary>
        public static bool IsDegenerate(PointF a, PointF b, PointF c)
        {
            if (!IsFinite(a.X) || !IsFinite(a.Y) ||
                !IsFinite(b.X) || !IsFinite(b.Y) ||
                !IsFinite(c.X) || !IsFinite(c.Y))
            {
                return true;
            }

            float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return Math.Abs(cross) < EpsilonArea;
        }

        /// <summary>
        /// Retorna uma copia da textura com 1px de borda replicada em todos os lados
        /// (porta de _padded do tailtex.py). Evita que o preenchimento de um triangulo
        /// amostre pixels do lado oposto na costura entre triangulos vizinhos (R1.2).
        /// A copia tem dimensoes (largura+2) x (altura+2); o conteudo original fica em (1,1).
        /// Chamador e dono do Bitmap retornado (deve descartar com Dispose).
        /// </summary>
        public static Bitmap PadTexture(Bitmap origem)
        {
            if (origem == null)
                return null;

            int largura = origem.Width;
            int altura = origem.Height;
            Bitmap destino = new Bitmap(largura + 2, altura + 2, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(destino))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;

                // Centro: textura original deslocada em (1,1).
                g.DrawImage(origem, new Rectangle(1, 1, largura, altura),
                    new Rectangle(0, 0, largura, altura), GraphicsUnit.Pixel);

                // Bordas verticais (replica coluna 0 e coluna largura-1).
                g.DrawImage(origem, new Rectangle(0, 1, 1, altura),
                    new Rectangle(0, 0, 1, altura), GraphicsUnit.Pixel);
                g.DrawImage(origem, new Rectangle(largura + 1, 1, 1, altura),
                    new Rectangle(largura - 1, 0, 1, altura), GraphicsUnit.Pixel);

                // Bordas horizontais (replica linha 0 e linha altura-1).
                g.DrawImage(origem, new Rectangle(1, 0, largura, 1),
                    new Rectangle(0, 0, largura, 1), GraphicsUnit.Pixel);
                g.DrawImage(origem, new Rectangle(1, altura + 1, largura, 1),
                    new Rectangle(0, altura - 1, largura, 1), GraphicsUnit.Pixel);

                // Cantos (replica o pixel diagonal mais proximo).
                g.DrawImage(origem, new Rectangle(0, 0, 1, 1),
                    new Rectangle(0, 0, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(origem, new Rectangle(largura + 1, 0, 1, 1),
                    new Rectangle(largura - 1, 0, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(origem, new Rectangle(0, altura + 1, 1, 1),
                    new Rectangle(0, altura - 1, 1, 1), GraphicsUnit.Pixel);
                g.DrawImage(origem, new Rectangle(largura + 1, altura + 1, 1, 1),
                    new Rectangle(largura - 1, altura - 1, 1, 1), GraphicsUnit.Pixel);
            }

            return destino;
        }

        /// <summary>
        /// Epsilon para divisoes por comprimento (tangente/perpendicular) na montagem
        /// da malha da cauda. Abaixo disso o segmento e tratado como de comprimento nulo.
        /// </summary>
        private const float EpsilonComprimento = 1e-6f;

        /// <summary>
        /// Gera os vertices da malha da cauda em coordenadas de simulacao (Y-para-cima).
        /// Porta de game_tail_vertices (tailtex.py do slugcatpet, report §1.3).
        /// Layout: N segmentos -> 4N-1 vertices. Para cada segmento emite um par
        /// esquerda/direita na base (deslocado pela perpendicular * largura) e um par
        /// no topo recuado k = dist/5 para o miolo; o ultimo segmento colapsa num unico
        /// vertice de ponta. A conversao Y-para-baixo (perpendicular invertida) fica a
        /// cargo do chamador via ToScreen.
        /// </summary>
        /// <param name="root">Raiz da cauda = TailAnchor = (quadril*3 + torax)/4, em simulacao.</param>
        /// <param name="rootRadius">Largura inicial da faixa na raiz (StretchedRadius do 1o segmento).</param>
        /// <param name="tail">Segmentos da cauda (Pos + StretchedRadius).</param>
        /// <param name="destino">Buffer de saida pre-alocado com pelo menos 4N-1 posicoes.</param>
        /// <returns>Quantidade de vertices escritos em <paramref name="destino"/> (4N-1).</returns>
        public static int BuildTailVertices(Vec2 root, float rootRadius, TailSegment[] tail, Vec2[] destino)
        {
            Vec2 v = root;
            float w = rootRadius;
            int n = 0;

            for (int i = 0; i < tail.Length; i++)
            {
                Vec2 s = tail[i].Pos;
                float r = tail[i].StretchedRadius;

                Vec2 d = s - v;
                float dist = d.Length;
                Vec2 tangente = dist > EpsilonComprimento ? d / dist : new Vec2(0f, 1f);
                Vec2 perpendicular = new Vec2(tangente.Y, -tangente.X);
                float k = (i == 0) ? 0f : dist / 5f;

                // Par da base (recuado +k ao longo da tangente, no inicio do segmento).
                destino[n++] = v - perpendicular * w + tangente * k;
                destino[n++] = v + perpendicular * w + tangente * k;

                if (i < tail.Length - 1)
                {
                    // Par do topo (recuado -k, no fim do segmento).
                    destino[n++] = s - perpendicular * r - tangente * k;
                    destino[n++] = s + perpendicular * r - tangente * k;
                }
                else
                {
                    // Ultimo segmento: ponta unica.
                    destino[n++] = s;
                }

                w = r;
                v = s;
            }

            return n;
        }

        /// <summary>
        /// Gera os indices de triangulos da malha da cauda. Porta de _tris (tailtex.py).
        /// Padrao por segmento: 2 triangulos "own" (base->topo do proprio segmento) e,
        /// entre segmentos, 2 triangulos "link"; o ultimo segmento fecha com 1 triangulo
        /// de ponta. Total 4N-3 triangulos (12N-9 indices) para N segmentos.
        /// Supoe o layout 4N-1 de BuildTailVertices: cada segmento ocupa 4 vertices
        /// (base esq, base dir, topo esq, topo dir), e o ultimo tem 3 (base esq, base dir, ponta).
        /// </summary>
        /// <param name="vertCount">Quantidade de vertices (4N-1).</param>
        /// <param name="destino">Buffer de saida pre-alocado com pelo menos 12N-9 posicoes.</param>
        /// <returns>Quantidade de indices escritos (12N-9).</returns>
        public static int BuildTailTriangles(int vertCount, int[] destino)
        {
            // 4N-1 = vertCount  ->  N = (vertCount + 1) / 4
            int n = (vertCount + 1) / 4;
            int idx = 0;

            for (int i = 0; i < n; i++)
            {
                int baseEsq = i * 4;
                int baseDir = baseEsq + 1;

                if (i < n - 1)
                {
                    int topoEsq = baseEsq + 2;
                    int topoDir = baseEsq + 3;

                    // "own": o quad do proprio segmento (base -> topo).
                    destino[idx++] = baseEsq; destino[idx++] = baseDir; destino[idx++] = topoEsq;
                    destino[idx++] = baseDir; destino[idx++] = topoDir; destino[idx++] = topoEsq;

                    // "link": liga o topo deste segmento a base do proximo.
                    int proxBaseEsq = (i + 1) * 4;
                    int proxBaseDir = proxBaseEsq + 1;
                    destino[idx++] = topoEsq; destino[idx++] = topoDir; destino[idx++] = proxBaseEsq;
                    destino[idx++] = topoDir; destino[idx++] = proxBaseDir; destino[idx++] = proxBaseEsq;
                }
                else
                {
                    // Ultimo segmento: fecha na ponta (vertice baseEsq+2).
                    int ponta = baseEsq + 2;
                    destino[idx++] = baseEsq; destino[idx++] = baseDir; destino[idx++] = ponta;
                }
            }

            return idx;
        }

        /// <summary>Retangulo-fonte (bounding) que cobre os tres cantos UV.</summary>
        private static RectangleF BoundingRect(PointF uv0, PointF uv1, PointF uv2)
        {
            float minX = Math.Min(uv0.X, Math.Min(uv1.X, uv2.X));
            float minY = Math.Min(uv0.Y, Math.Min(uv1.Y, uv2.Y));
            float maxX = Math.Max(uv0.X, Math.Max(uv1.X, uv2.X));
            float maxY = Math.Max(uv0.Y, Math.Max(uv1.Y, uv2.Y));
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>Limita um valor ao intervalo [min, max]. Substitui Math.Clamp (ausente no FW 4.0).</summary>
        public static float Clamp(float valor, float minimo, float maximo)
        {
            return Math.Max(minimo, Math.Min(maximo, valor));
        }

        /// <summary>Verdadeiro quando o valor e um numero finito (nao NaN nem Infinity).</summary>
        public static bool IsFinite(float valor)
        {
            return !(float.IsNaN(valor) || float.IsInfinity(valor));
        }

        /// <summary>Verdadeiro quando ambas as componentes do vetor sao finitas.</summary>
        public static bool IsFinite(Vec2 vetor)
        {
            return IsFinite(vetor.X) && IsFinite(vetor.Y);
        }
    }
}
