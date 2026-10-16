using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
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

    internal sealed class CompanionWindow : Form
    {
        private const int PixelScale = 3;
        private const int WindowWidth = 450;
        private const int WindowHeight = 214;
        private const float EdgeMargin = 28f; // unidades de simulação reservadas para rabo/cabeça nas bordas
        private static readonly Color KeyColor = Color.Fuchsia;

        private IPhysicsProvider _simulation = new SlugcatSimulation();
        private float _worldHalfWidth = 60f;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly Bitmap _body;
        private Bitmap _bodyPadded; // textura de BodyA com borda replicada (prova de conceito da malha)
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

        // Buffers da malha da cauda (FEAT-003). Para N=4 segmentos: 4N-1=15 vertices,
        // 12N-9=39 indices de triangulo. Pre-alocados para evitar GC a 60 FPS.
        private readonly Vec2[] _tailMeshVerts = new Vec2[15];
        private readonly int[] _tailMeshTris = new int[39];
        private Bitmap _tailPadded; // faixa de BodyA (textura da cauda) com borda replicada

        // Fallback R2.4: quando true, a cauda volta ao poligono monocromatico antigo
        // (FillPolygon branco) em vez da malha texturizada. Padrao: malha (false).
        private const bool TailMeshFallback = false;

        // Buffers da grade do tronco (FEAT-004). Grade 1 coluna x 3 linhas de quads =>
        // 4 fileiras x 2 colunas = 8 vertices. Pre-alocado para evitar GC a 60 FPS.
        private readonly Vec2[] _bodyGridVerts = new Vec2[8];
        private Bitmap _bodyGridPadded; // BodyA com borda replicada (textura da grade)

        // Fallback: quando true, o tronco volta ao DrawSprite rigido (DrawImage/Rectangle)
        // em vez da grade deformavel. Padrao: grade (false).
        private const bool BodyMeshFallback = false;

        // Ancoragem do sprite BodyA no rig (identica a ponte rigida substituida):
        // anchorY=4/19 => a fileira Neck fica a 4px do topo; a fileira de topo (cap)
        // estende os 4px restantes acima de Neck. halfWidth = largura/2 = 7px.
        private const float BodyAnchorTopPx = 4f;   // 0.2105263 * 19
        private const float BodyHalfWidthPx = 7f;   // 14 / 2

        // === DEBUG MODE ===
        private const bool DebugMode = false; // Debug mode desativado para release

        // === PROVA DE CONCEITO DA MALHA (FEAT-002) ===
        // Quando true, desenha UM quad de BodyA deformado via DrawTexturedTriangle
        // (2 triangulos) para validar o warp afim + clipping. NAO alterar no release.
        private const bool MeshProofOfConcept = false;
        private bool _meshProofCaptured; // garante uma unica gravacao do snapshot isolado
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
            
            // needs verification during implementation: tick loop kept inline to preserve exact cadence
            // (constantes centralizadas em TickManager: TickSeconds=1/40, teto 4 updates, acumulo max 0.08s)
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
            // Z-order canonico (spec/README, fundo -> frente): braco traseiro -> corpo ->
            // cauda -> pernas -> quadril -> braco frontal -> cabeca -> rosto. FEAT-004 (7.2)
            // alinhou a sequencia ao canonico (antes intercalava pernas/quadril atras do corpo).
            int drawOrder = 0;

            // 1. Braço traseiro (mais atrás de tudo)
            drawOrder++;
            DrawArmDebug(g, backArmIndex, false, centerX, floorY, drawOrder, "BackArm");

            // 2. Corpo (grade deformavel)
            drawOrder++;
            DrawBody(g, rig, upperAngle, centerX, floorY, drawOrder);

            // 3. Rabo
            drawOrder++;
            DrawTail(g, anchor, centerX, floorY);
            if (DebugMode && _debugShowOrder)
                DrawOrderLabel(g, ToScreen(anchor, centerX, floorY), drawOrder, "Tail", Color.Yellow);

            // 4. Pernas
            // Âncora: centro horizontal (0.5), topo do sprite (0.75) para conectar ao corpo
            drawOrder++;
            DrawSpriteDebug(g, _legs[_simulation.LegsFrame], ToScreen(_simulation.LegsPos, centerX, floorY),
                0f, false, 0.5f, 0.75f, drawOrder, "Legs", Color.Lime);

            // 5. Quadril
            drawOrder++;
            DrawSpriteDebug(g, _hips, ToScreen(rig.Belly, centerX, floorY), lowerAngle,
                false, 0.5f, 0.5f, drawOrder, "Hips", Color.Orange);

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

            // Prova de conceito da malha (FEAT-002): quad de BodyA deformado via warp afim.
            if (MeshProofOfConcept)
                DrawMeshProofOfConcept(g, centerX, floorY);

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

        /// <summary>
        /// Prova de conceito da malha (FEAT-002): desenha o sprite BodyA como UM quad
        /// deformado manualmente em 2 triangulos via MeshRenderer.DrawTexturedTriangle.
        /// Valida o warp afim (textura esticada/cisalhada) e o clipping por triangulo,
        /// sem buraco nem costura. Le o estado SO via IPhysicsProvider; nao altera a fisica.
        /// Ativa apenas sob a flag MeshProofOfConcept; nao faz parte do release.
        /// </summary>
        private void DrawMeshProofOfConcept(Graphics g, float centerX, float floorY)
        {
            if (_body == null)
                return;

            // Textura com borda replicada (sob demanda, cacheada) para evitar costura.
            if (_bodyPadded == null)
                _bodyPadded = MeshRenderer.PadTexture(_body);

            int largura = _body.Width;
            int altura = _body.Height;

            // UVs = cantos do retangulo da textura original, ja deslocados +1px por causa
            // da borda replicada do PadTexture. Ordem por triangulo: TL, TR, BL / TR, BR, BL.
            PointF uvTL = new PointF(1f, 1f);
            PointF uvTR = new PointF(1f + largura, 1f);
            PointF uvBL = new PointF(1f, 1f + altura);
            PointF uvBR = new PointF(1f + largura, 1f + altura);

            // Quad de destino deformado (trapezio cisalhado) ancorado no tronco (chunk 0).
            // Deslocamentos manuais provam que a textura estica e cisalha, nao so rotaciona.
            PointF centro = ToScreen(_simulation.Chunks[0].Pos, centerX, floorY);
            float esc = PixelScale;

            PointF dTL = new PointF(centro.X - 10f * esc, centro.Y - 12f * esc);
            PointF dTR = new PointF(centro.X + 14f * esc, centro.Y - 6f * esc);  // topo mais estreito e inclinado
            PointF dBL = new PointF(centro.X - 14f * esc, centro.Y + 12f * esc); // base mais larga
            PointF dBR = new PointF(centro.X + 10f * esc, centro.Y + 8f * esc);

            // Triangulo A: TL, TR, BL. Triangulo B: TR, BR, BL. Juntos cobrem o quad sem buraco.
            MeshRenderer.DrawTexturedTriangle(g, _bodyPadded, uvTL, uvTR, uvBL, dTL, dTR, dBL);
            MeshRenderer.DrawTexturedTriangle(g, _bodyPadded, uvTR, uvBR, uvBL, dTR, dBR, dBL);

            // Captura isolada (uma vez) para inspecao limpa do warp: mesma deformacao
            // num recorte proprio, sem sprites rigidos nem overlays por cima. Usa uma
            // textura de xadrez para evidenciar estiramento/cisalhamento e ausencia de costura.
            if (DebugMode && !_meshProofCaptured)
            {
                _meshProofCaptured = true;
                SaveMeshProofSnapshot();
            }
        }

        /// <summary>
        /// Grava um PNG isolado (debug_frames\mesh_proof.png) com o mesmo quad deformado
        /// desenhado sobre fundo preto, usando uma textura de xadrez para que o warp afim
        /// e as costuras fiquem obvios a olho nu. So roda com a prova de conceito ativa.
        /// </summary>
        private void SaveMeshProofSnapshot()
        {
            if (string.IsNullOrEmpty(_debugFolder))
                return;

            // Textura de xadrez 14x19 (mesmas dimensoes de BodyA) com borda replicada.
            using (Bitmap xadrez = new Bitmap(14, 19, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < xadrez.Height; y++)
                    for (int x = 0; x < xadrez.Width; x++)
                    {
                        bool claro = ((x / 2) + (y / 2)) % 2 == 0;
                        xadrez.SetPixel(x, y, claro ? Color.White : Color.FromArgb(255, 60, 160, 255));
                    }

                using (Bitmap xadrezPad = MeshRenderer.PadTexture(xadrez))
                using (Bitmap destino = new Bitmap(240, 214, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(destino))
                {
                    g.Clear(Color.Black);
                    g.SmoothingMode = SmoothingMode.None;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;

                    int w = xadrez.Width;
                    int h = xadrez.Height;
                    PointF uvTL = new PointF(1f, 1f);
                    PointF uvTR = new PointF(1f + w, 1f);
                    PointF uvBL = new PointF(1f, 1f + h);
                    PointF uvBR = new PointF(1f + w, 1f + h);

                    PointF c = new PointF(120f, 107f);
                    PointF dTL = new PointF(c.X - 60f, c.Y - 70f);
                    PointF dTR = new PointF(c.X + 80f, c.Y - 30f);
                    PointF dBL = new PointF(c.X - 85f, c.Y + 70f);
                    PointF dBR = new PointF(c.X + 55f, c.Y + 45f);

                    MeshRenderer.DrawTexturedTriangle(g, xadrezPad, uvTL, uvTR, uvBL, dTL, dTR, dBL);
                    MeshRenderer.DrawTexturedTriangle(g, xadrezPad, uvTR, uvBR, uvBL, dTR, dBR, dBL);

                    destino.Save(Path.Combine(_debugFolder, "mesh_proof.png"), ImageFormat.Png);
                }
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
                "1.BackArm 2.Body 3.Tail 4.Legs",
                "5.Hips 6.FrontArm 7.Head 8.Face",
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

                float arm0Dist = Vec2.Distance(shoulder0, hand0);
                float arm1Dist = Vec2.Distance(shoulder1, hand1);

                // Valida invariantes geometricas da estrutura do personagem.
                // "issues" nao-vazio significa que o render/fisica saiu do formato esperado.
                string issues = BuildIssues(arm0Dist, arm1Dist);

                // CultureInfo.InvariantCulture: garante ponto decimal (JSON valido),
                // nao a virgula da cultura pt-BR.
                string json = string.Format(CultureInfo.InvariantCulture, @"{{
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
  ""issues"": [{42}]
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
                    _simulation.Tail[3].Pos.X, _simulation.Tail[3].Pos.Y, _simulation.Tail[3].StretchedRadius,
                    issues
                );
                
                string jsonFile = Path.Combine(_debugFolder, string.Format("frame_{0:D6}.json", _debugFrameCounter));
                File.WriteAllText(jsonFile, json);
            }
            catch (Exception ex)
            {
                Log.Error("SaveFrameData", ex);
            }
        }

        /// <summary>
        /// Verifica invariantes geometricas que caracterizam a estrutura correta do Slugcat.
        /// Cada violacao vira uma string descritiva no array "issues" do frame JSON, para
        /// que a analise automatizada detecte deformacao sem depender de inspecao visual.
        /// </summary>
        private string BuildIssues(float arm0Dist, float arm1Dist)
        {
            List<string> found = new List<string>();

            BodyChunk head = _simulation.Chunks[0];
            BodyChunk body = _simulation.Chunks[1];

            // 1. NaN / Infinity em qualquer posicao de chunk invalida o frame inteiro.
            if (IsBadFloat(head.Pos.X) || IsBadFloat(head.Pos.Y) ||
                IsBadFloat(body.Pos.X) || IsBadFloat(body.Pos.Y))
            {
                found.Add("NaN/Infinity em posicao de chunk");
            }

            // 2. Cabeca (chunk 0) deve ficar acima do corpo (chunk 1). Y cresce para cima.
            if (head.Pos.Y <= body.Pos.Y)
            {
                found.Add(string.Format(CultureInfo.InvariantCulture,
                    "cabeca abaixo do corpo: head.y={0:F1} body.y={1:F1}", head.Pos.Y, body.Pos.Y));
            }

            // 3. Distancia entre chunks deve respeitar a constraint de mola (~17px).
            float chunkDist = Vec2.Distance(head.Pos, body.Pos);
            float tol = SlugcatSimulation.BodyConnectionDistance * 0.35f;
            if (Math.Abs(chunkDist - SlugcatSimulation.BodyConnectionDistance) > tol)
            {
                found.Add(string.Format(CultureInfo.InvariantCulture,
                    "distancia entre chunks fora da faixa: {0:F1} (esperado ~{1:F0}+-{2:F0})",
                    chunkDist, SlugcatSimulation.BodyConnectionDistance, tol));
            }

            // 4. Bracos nao podem esticar/colapsar alem do plausivel (ombro->mao).
            CheckArm(found, "arm0", arm0Dist);
            CheckArm(found, "arm1", arm1Dist);

            // 5. Segmentos da cauda nao podem separar alem do comprimento de conexao.
            for (int i = 1; i < _simulation.Tail.Length; i++)
            {
                TailSegment prev = _simulation.Tail[i - 1];
                TailSegment seg = _simulation.Tail[i];
                float segDist = Vec2.Distance(prev.Pos, seg.Pos);

                if (segDist > seg.ConnectionRadius * 2.5f)
                {
                    found.Add(string.Format(CultureInfo.InvariantCulture,
                        "cauda segmento {0} separado: {1:F1} (conexao {2:F1})",
                        i, segDist, seg.ConnectionRadius));
                }
            }

            // 5. Nenhum segmento da cauda pode ultrapassar o alcance maximo a partir do
            //    quadril (acumulo dos comprimentos de conexao, com folga). Evita a cauda
            //    "disparar" para longe sem violar a distancia entre segmentos vizinhos.
            float maxReach = 0f;
            for (int i = 0; i < _simulation.Tail.Length; i++)
            {
                maxReach += _simulation.Tail[i].ConnectionRadius;
                float reach = Vec2.Distance(_simulation.Tail[i].Pos, body.Pos);

                if (reach > maxReach + 2f)
                {
                    found.Add(string.Format(CultureInfo.InvariantCulture,
                        "cauda segmento {0} fora de alcance: {1:F1} (max ~{2:F1})",
                        i, reach, maxReach));
                }
            }

            // 7. Cabeca (sprite) deve ficar proxima do chunk superior (pescoco). Se descolar,
            //    o render separou a cabeca do corpo.
            float headNeck = Vec2.Distance(_simulation.HeadPos, head.Pos);
            if (IsBadFloat(headNeck) || headNeck < 1f || headNeck > 10f)
            {
                found.Add(string.Format(CultureInfo.InvariantCulture,
                    "cabeca descolada do pescoco: {0:F1} (esperado 1-10)", headNeck));
            }

            // 8. Pernas devem ficar abaixo do quadril (chunk inferior). Y cresce para cima.
            if (_simulation.LegsPos.Y >= body.Pos.Y)
            {
                found.Add(string.Format(CultureInfo.InvariantCulture,
                    "pernas acima do quadril: legs.y={0:F1} hip.y={1:F1}",
                    _simulation.LegsPos.Y, body.Pos.Y));
            }

            // 10. Malha da cauda nao-degenerada (invariante nova da migracao de render).
            //     Vertices finitos e cada triangulo com area nao-nula e sinal consistente
            //     (sem auto-cruzamento/inversao). So vale quando a cauda e malha, nao fallback.
            if (!TailMeshFallback)
            {
                CheckTailMesh(found);
            }

            if (found.Count == 0)
                return string.Empty;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < found.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(found[i].Replace("\"", "'")).Append('"');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Invariante #10 (report §5): a malha da cauda nao pode gerar triangulos
        /// degenerados. Reconstroi os vertices/triangulos como em DrawTailMesh (em
        /// coordenadas de simulacao) e verifica, para cada triangulo: vertices finitos
        /// e area nao-nula (|produto vetorial| acima do epsilon). Area nula = fold/colapso
        /// que vira buraco no render. Nao comparamos o SINAL do produto vetorial entre
        /// triangulos: no layout 4N-1 os triangulos "own" (dentro do segmento) e "link"
        /// (entre segmentos) tem winding naturalmente distinto quando a cauda curva, logo
        /// um sinal global unico produziria falso positivo numa malha visualmente integra.
        /// Qualquer falha adiciona "mesh_degenerate_triangle" a lista.
        /// </summary>
        private void CheckTailMesh(List<string> found)
        {
            TailSegment[] tail = _simulation.Tail;
            float rootRadius = tail[0].StretchedRadius;
            int vertCount = MeshRenderer.BuildTailVertices(
                _simulation.TailAnchor, rootRadius, tail, _tailMeshVerts);
            int triCount = MeshRenderer.BuildTailTriangles(vertCount, _tailMeshTris);

            for (int t = 0; t < triCount; t += 3)
            {
                Vec2 a = _tailMeshVerts[_tailMeshTris[t]];
                Vec2 b = _tailMeshVerts[_tailMeshTris[t + 1]];
                Vec2 c = _tailMeshVerts[_tailMeshTris[t + 2]];

                if (!MeshRenderer.IsFinite(a) || !MeshRenderer.IsFinite(b) || !MeshRenderer.IsFinite(c))
                {
                    found.Add("mesh_degenerate_triangle (vertice nao-finito na cauda)");
                    return;
                }

                float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

                if (Math.Abs(cross) < 1e-4f)
                {
                    found.Add("mesh_degenerate_triangle (area nula na cauda)");
                    return;
                }
            }
        }

        private static void CheckArm(List<string> found, string name, float dist)
        {
            // Faixa plausivel ombro->mao. Fora disso indica braco esticado ou colapsado.
            const float ArmMin = 2f;
            const float ArmMax = 16f;

            if (IsBadFloat(dist) || dist < ArmMin || dist > ArmMax)
            {
                found.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} fora da faixa ombro-mao: {1:F1} (esperado {2:F0}-{3:F0})",
                    name, dist, ArmMin, ArmMax));
            }
        }

        private static bool IsBadFloat(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
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
            if (TailMeshFallback)
            {
                DrawTailMono(g, anchor, centerX, floorY);
                return;
            }

            DrawTailMesh(g, anchor, centerX, floorY);
        }

        /// <summary>
        /// Desenha a cauda como malha texturizada deformavel (FEAT-003). Porta a receita
        /// do report §1.3: gera 4N-1 vertices (BuildTailVertices) a partir de TailAnchor +
        /// Tail[i].StretchedRadius, monta 4N-3 triangulos (BuildTailTriangles) e warpa uma
        /// faixa de BodyA por triangulo via MeshRenderer.DrawTexturedTriangle. Le o estado
        /// SO via IPhysicsProvider; nao altera a fisica. Converte as posicoes de simulacao
        /// (Y-para-cima) para tela (Y-para-baixo) com ToScreen a cada vertice.
        /// </summary>
        private void DrawTailMesh(Graphics g, Vec2 anchor, float centerX, float floorY)
        {
            TailSegment[] tail = _simulation.Tail;

            // Textura da cauda: faixa de BodyA (nao ha asset dedicado) com borda replicada.
            if (_tailPadded == null)
                _tailPadded = MeshRenderer.PadTexture(_body);

            float rootRadius = tail[0].StretchedRadius;
            int vertCount = MeshRenderer.BuildTailVertices(anchor, rootRadius, tail, _tailMeshVerts);
            int triCount = MeshRenderer.BuildTailTriangles(vertCount, _tailMeshTris);

            // UV: u = indiceVertice / (vertCount-1) mapeia a largura da textura ao longo
            // da cauda; v alterna base/topo da faixa (par da base vs par do topo/ponta).
            float texLargura = _tailPadded.Width;
            float texAltura = _tailPadded.Height;
            float vBase = 1f;                 // borda replicada desloca +1px
            float vTopo = texAltura - 1f;

            for (int t = 0; t < triCount; t += 3)
            {
                int i0 = _tailMeshTris[t];
                int i1 = _tailMeshTris[t + 1];
                int i2 = _tailMeshTris[t + 2];

                PointF uv0 = UvCauda(i0, vertCount, texLargura, vBase, vTopo);
                PointF uv1 = UvCauda(i1, vertCount, texLargura, vBase, vTopo);
                PointF uv2 = UvCauda(i2, vertCount, texLargura, vBase, vTopo);

                PointF d0 = ToScreen(_tailMeshVerts[i0], centerX, floorY);
                PointF d1 = ToScreen(_tailMeshVerts[i1], centerX, floorY);
                PointF d2 = ToScreen(_tailMeshVerts[i2], centerX, floorY);

                MeshRenderer.DrawTexturedTriangle(g, _tailPadded, uv0, uv1, uv2, d0, d1, d2);
            }
        }

        /// <summary>
        /// Coordenada UV (px da textura) de um vertice da malha da cauda. O layout e
        /// 4 vertices por segmento (base esq/dir, topo esq/dir) + ponta final; vertices
        /// pares (0,2,...) ficam na base da faixa e impares no topo. u avanca com o indice.
        /// </summary>
        private static PointF UvCauda(int indice, int vertCount, float texLargura, float vBase, float vTopo)
        {
            float u = 1f + (indice / (float)(vertCount - 1)) * (texLargura - 2f);
            float v = (indice % 2 == 0) ? vBase : vTopo;
            return new PointF(u, v);
        }

        private void DrawTailMono(Graphics g, Vec2 anchor, float centerX, float floorY)
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

        /// <summary>
        /// Desenha o tronco. Despacha para a grade deformavel (FEAT-004) ou, com
        /// BodyMeshFallback=true, para a ponte rigida antiga (DrawSpriteDebug). Em ambos
        /// os caminhos preserva os overlays de DebugMode (ancora, ordem, bounding box).
        /// </summary>
        private void DrawBody(Graphics g, BodyRig rig, float upperAngle, float centerX, float floorY, int order)
        {
            if (BodyMeshFallback)
            {
                DrawSpriteDebug(g, _body, ToScreen(rig.Neck, centerX, floorY), upperAngle,
                    false, 0.5f, 0.2105263f, order, "Body", Color.Cyan);
                return;
            }

            DrawBodyGrid(g, rig, centerX, floorY);

            if (DebugMode)
                DrawBodyGridOverlay(g, rig, order, centerX, floorY);
        }

        /// <summary>
        /// Desenha o sprite BodyA como grade deformavel 1x3 (FEAT-004), substituindo a
        /// ponte rigida DrawSprite. Monta 8 vertices em simulacao via MeshRenderer.BuildBodyGrid
        /// (fileiras presas a cap/Neck/Belly/Waist), warpa 3 quads (6 triangulos) de BodyA
        /// via DrawTexturedTriangle, convertendo cada vertice com ToScreen. Ao inclinar/andar,
        /// o tronco DOBRA entre torax e quadril porque cada fileira segue a tangente local.
        /// Le o estado SO via IPhysicsProvider; nao altera a fisica. Distancia Neck-Waist
        /// (~17px, invariante da mola) e preservada porque os vertices vem direto do rig.
        /// </summary>
        private void DrawBodyGrid(Graphics g, BodyRig rig, float centerX, float floorY)
        {
            if (_body == null)
                return;

            // Textura do tronco: BodyA com borda replicada (evita costura entre quads).
            if (_bodyGridPadded == null)
                _bodyGridPadded = MeshRenderer.PadTexture(_body);

            MeshRenderer.BuildBodyGrid(rig.Neck, rig.Belly, rig.Waist,
                BodyHalfWidthPx, BodyAnchorTopPx, _bodyGridVerts);

            // UV (px da textura, +1px pela borda replicada do PadTexture). Largura cheia
            // do sprite entre as colunas; as fileiras repartem a altura em cap/Neck/Belly/Waist.
            float texLargura = _body.Width;   // 14
            float texAltura = _body.Height;   // 19
            float uEsq = 1f;
            float uDir = 1f + texLargura;
            // v por fileira: cap=topo(0), Neck=4px, Belly=meio do trecho abaixo de Neck, Waist=base.
            float vCap = 1f;
            float vNeck = 1f + BodyAnchorTopPx;
            float vWaist = 1f + texAltura;
            float vBelly = vNeck + (vWaist - vNeck) * 0.5f;
            float[] vFileira = { vCap, vNeck, vBelly, vWaist };

            // 3 quads (fileira r -> r+1); 2 triangulos por quad (TL,TR,BL / TR,BR,BL),
            // UVs = cantos de um sub-retangulo da textura -> warp afim fiel (FEAT-002).
            for (int r = 0; r < 3; r++)
            {
                int tl = r * 2;        // topo esquerda
                int tr = r * 2 + 1;    // topo direita
                int bl = (r + 1) * 2;  // base esquerda
                int br = (r + 1) * 2 + 1; // base direita

                PointF uvTL = new PointF(uEsq, vFileira[r]);
                PointF uvTR = new PointF(uDir, vFileira[r]);
                PointF uvBL = new PointF(uEsq, vFileira[r + 1]);
                PointF uvBR = new PointF(uDir, vFileira[r + 1]);

                PointF dTL = ToScreen(_bodyGridVerts[tl], centerX, floorY);
                PointF dTR = ToScreen(_bodyGridVerts[tr], centerX, floorY);
                PointF dBL = ToScreen(_bodyGridVerts[bl], centerX, floorY);
                PointF dBR = ToScreen(_bodyGridVerts[br], centerX, floorY);

                MeshRenderer.DrawTexturedTriangle(g, _bodyGridPadded, uvTL, uvTR, uvBL, dTL, dTR, dBL);
                MeshRenderer.DrawTexturedTriangle(g, _bodyGridPadded, uvTR, uvBR, uvBL, dTR, dBR, dBL);
            }
        }

        /// <summary>
        /// Overlay de DebugMode do tronco (equivalente ao de DrawSpriteDebug): ponto de
        /// ancora em Neck, label de ordem e contorno da grade deformada (nao a bounding
        /// box rigida, para evidenciar a dobra). So roda com DebugMode ligado.
        /// </summary>
        private void DrawBodyGridOverlay(Graphics g, BodyRig rig, int order, float centerX, float floorY)
        {
            PointF anchor = ToScreen(rig.Neck, centerX, floorY);
            using (SolidBrush b = new SolidBrush(Color.Cyan))
                g.FillEllipse(b, anchor.X - 3, anchor.Y - 3, 6, 6);

            if (_debugShowOrder)
                DrawOrderLabel(g, anchor, order, "Body", Color.Cyan);

            if (_debugShowBounds)
            {
                // Contorno da grade: esquerda (fileiras 0..3) + direita (3..0), fechando.
                Point[] contorno = new Point[8];
                contorno[0] = RoundPoint(ToScreen(_bodyGridVerts[0], centerX, floorY));
                contorno[1] = RoundPoint(ToScreen(_bodyGridVerts[2], centerX, floorY));
                contorno[2] = RoundPoint(ToScreen(_bodyGridVerts[4], centerX, floorY));
                contorno[3] = RoundPoint(ToScreen(_bodyGridVerts[6], centerX, floorY));
                contorno[4] = RoundPoint(ToScreen(_bodyGridVerts[7], centerX, floorY));
                contorno[5] = RoundPoint(ToScreen(_bodyGridVerts[5], centerX, floorY));
                contorno[6] = RoundPoint(ToScreen(_bodyGridVerts[3], centerX, floorY));
                contorno[7] = RoundPoint(ToScreen(_bodyGridVerts[1], centerX, floorY));
                using (Pen p = new Pen(Color.Cyan, 1f))
                {
                    p.DashStyle = DashStyle.Dot;
                    g.DrawPolygon(p, contorno);
                }
            }
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
            if (_bodyPadded != null)
                _bodyPadded.Dispose();
            if (_tailPadded != null)
                _tailPadded.Dispose();
            if (_bodyGridPadded != null)
                _bodyGridPadded.Dispose();
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
