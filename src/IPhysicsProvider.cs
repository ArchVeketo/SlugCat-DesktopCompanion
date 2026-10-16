namespace SlugcatDesktop
{
    /// <summary>
    /// Superficie estavel que o render/consumidor (CompanionWindow) usa da simulacao.
    /// Expoe EXATAMENTE os membros que SlugcatDesktop.cs le/escreve hoje em _simulation.
    /// Os membros estaticos ArmFrameForLength e BodyConnectionDistance NAO entram aqui:
    /// continuam sendo chamados pelo tipo concreto SlugcatSimulation.
    /// </summary>
    internal interface IPhysicsProvider
    {
        float Facing { get; set; }
        float LookX { get; set; }
        float LookY { get; set; }
        bool Walking { get; }
        float WalkSpeed { get; }
        float WorldHalfWidth { get; set; }

        BodyChunk[] Chunks { get; }
        TailSegment[] Tail { get; }

        Vec2 HeadPos { get; set; }
        Vec2 LegsPos { get; set; }
        BodyRig Rig { get; }
        Vec2 TailAnchor { get; }

        int LegsFrame { get; }
        int HeadFrame { get; }
        int FaceFrame { get; }

        void Tick();
        bool IsStateValid();
        void RequestJump();
        void GetArm(int index, bool front, out Vec2 shoulder, out Vec2 hand);
    }
}
