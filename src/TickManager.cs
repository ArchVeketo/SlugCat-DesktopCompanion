using System;

namespace SlugcatDesktop
{
    /// <summary>
    /// Acumulador de passo fixo da simulacao (40 Hz), extraido de CompanionWindow.OnFrame.
    /// Constantes e condicoes IDENTICAS ao loop original: tickSeconds = 1/40, teto de 4
    /// updates por frame, acumulo limitado a 0.08s por frame.
    ///
    /// NOTA: o loop de tick permanece inline em OnFrame (ver comentario la) para garantir
    /// cadencia e ordenacao exatas identicas ao baseline. Esta classe documenta/centraliza
    /// as constantes de cadencia e oferece a mesma logica caso o loop seja migrado no futuro,
    /// sem alterar nenhum valor computado.
    /// </summary>
    internal sealed class TickManager
    {
        public const float TickSeconds = 1f / 40f;
        public const int MaxUpdatesPerFrame = 4;
        public const double MaxAccumulatePerFrame = 0.08;

        private float _accumulator;

        public float Accumulator { get { return _accumulator; } }

        public void Reset()
        {
            _accumulator = 0f;
        }

        /// <summary>Soma o tempo decorrido (limitado a MaxAccumulatePerFrame), igual ao OnFrame original.</summary>
        public void Accumulate(double elapsedSeconds)
        {
            _accumulator += (float)Math.Min(elapsedSeconds, MaxAccumulatePerFrame);
        }

        /// <summary>Retorna true enquanto houver passo acumulado e o teto de updates nao for atingido.</summary>
        public bool ShouldStep(int updatesSoFar)
        {
            return _accumulator >= TickSeconds && updatesSoFar < MaxUpdatesPerFrame;
        }

        /// <summary>Consome um passo fixo do acumulador.</summary>
        public void ConsumeStep()
        {
            _accumulator -= TickSeconds;
        }
    }
}
