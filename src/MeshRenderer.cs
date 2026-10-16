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
    /// As rotinas de montagem de malha (BuildTailVertices/Triangles, BuildBodyGrid)
    /// entram nas FEATs seguintes.
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
