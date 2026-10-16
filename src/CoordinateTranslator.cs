using System;

namespace SlugcatDesktop
{
    /// <summary>
    /// Documentacao-apenas da convencao de coordenadas do projeto. NAO esta ligada ao
    /// caminho quente (hot path) e NAO altera nenhum valor computado pela simulacao.
    ///
    /// CONVENCAO REAL (Y-up): a simulacao usa Y para cima. A gravidade e aplicada como
    /// Vel.Y -= 0.9f (ver SlugcatSimulation.UpdateChunk / PhysicsConstants.Gravity), o chao
    /// e Pos.Y &lt; Radius, e o chunk 0 (torax/cabeca) fica ACIMA do chunk 1 (quadril).
    /// A conversao para a tela (Y-down do GDI+) e feita isoladamente em CompanionWindow.ToScreen.
    ///
    /// Os metodos abaixo existem apenas como referencia/utilitario de documentacao; nao devem
    /// ser inseridos no loop de fisica, pois a fisica ja esta inteiramente em Y-up.
    /// </summary>
    internal static class CoordinateTranslator
    {
        /// <summary>
        /// Converte um sinal/valor de gravidade entre as convencoes Y-up e Y-down.
        /// No projeto (Y-up) a gravidade e aplicada como Vel.Y -= value; em Y-down seria +=.
        /// Utilitario de documentacao: inverte o sinal do valor fornecido.
        /// </summary>
        public static float FlipGravity(float gravity)
        {
            return -gravity;
        }

        /// <summary>
        /// Espelha uma coordenada Y em torno de uma altura de referencia (ex.: converter
        /// entre Y-up e Y-down dado o topo da area). Utilitario de documentacao.
        /// </summary>
        public static float FlipY(float y, float referenceHeight)
        {
            return referenceHeight - y;
        }
    }
}
