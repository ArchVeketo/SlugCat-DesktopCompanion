using System;

namespace SlugcatDesktop
{
    /// <summary>
    /// Render de malha deformavel do Slugcat. Por enquanto contem apenas helpers
    /// compativeis com .NET Framework 4.0 (sem Math.Clamp, sem MathF). As rotinas
    /// de malha (DrawTexturedTriangle, PadTexture, BuildTailVertices/Triangles,
    /// BuildBodyGrid) entram nas FEATs seguintes.
    /// </summary>
    internal static class MeshRenderer
    {
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
