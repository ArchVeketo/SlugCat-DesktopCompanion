# Slugcat Desktop Companion

Companion de desktop do Rain World para Windows. O Slugcat vive na sua área de trabalho, caminha, pula quando você clica nele e segue a mesma física do jogo original.

Feito com .NET Framework 4.0, sem dependências externas. O executável é o único arquivo necessário além da pasta de sprites.

> **Aviso de copyright:** os sprites são assets oficiais do Rain World (Videocult). Este é um fan project não oficial para uso pessoal. Não redistribua os arquivos de imagem separadamente do jogo.

---

## Executando

1. Baixe um executável das versões abaixo
2. Mantenha a pasta `assets\player` ao lado do `.exe`
3. Execute `SlugcatCompanion-v21.exe`

---

## Controles

| Ação | Como |
|------|------|
| Pular | Clique rápido no corpo |
| Mover a janela | Clique e arraste |
| Menu | Botão direito |
| Esconder/mostrar | Duplo clique no ícone da bandeja |
| Sair | Bandeja → Sair |

---

## Compilando

```powershell
.\build.ps1
```

Ou diretamente com o compilador nativo do Windows:

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /win32manifest:"app.manifest" /out:"SlugcatCompanion.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll SlugcatDesktop.cs
```

Requer .NET Framework 4.0, que já vem instalado no Windows 7 e posterior.

---

## Física

O sistema recria a simulação de corpo do Rain World original, extraída do código do jogo via descompilação pela comunidade de modding.

**Body chunks**

Dois pontos de massa conectados por uma constraint de mola:

| Parâmetro | Valor |
|-----------|-------|
| Raio do tórax | 9 px |
| Raio do quadril | 8 px |
| Distância de conexão | 17 px |
| Gravidade | 0.9 px/tick² |
| Atrito do ar | 0.999/tick |
| Bounce | 0.1 |
| Atrito de superfície | 0.5 |

**Rig articulado**

Três pivôs conectam os chunks ao visual: cintura (ancorada no quadril), barriga (pivô intermediário com física própria) e pescoço (ancorado no tórax). A barriga oscila sutilmente durante a caminhada e respira quando parado.

**Cauda**

Quatro segmentos físicos independentes com raios 6, 4, 2.5 e 1 px. Comprimentos de conexão: 4+7+7+7 px (25 px no total). Renderizada com interpolação Catmull-Rom.

**Ordem de renderização (fundo para frente)**

```
braço traseiro → corpo → cauda → pernas → quadril → braço frontal → cabeça → rosto
```

**Loop de simulação:** física a 40 Hz com acumulador de passo fixo, renderização a ~60 FPS.

---

## Animação

Todo o sistema de animação é procedural: nenhum frame de animação pré-computado, tudo calculado a partir do estado físico atual.

- **Cabeça:** 18 orientações (HeadA0–17), frame determinado pelo ângulo da cabeça em relação ao pescoço
- **Rosto:** 9 frames de expressão (FaceA0–8), piscadas automáticas e expressões ocasionais
- **Braços:** 13 frames (PlayerArm0–12), frame escolhido pela distância ombro → mão em tempo real
- **Pernas:** 7 frames de caminhada (LegsA0–6), frequência sincronizada com a velocidade

---

## Estrutura do projeto

```
SlugCat-DesktopCompanion/
├── SlugcatDesktop.cs        # código-fonte completo
├── app.manifest             # configurações de DPI e permissões UAC
├── build.ps1                # script de compilação
├── assets/
│   └── player/
│       ├── atlas-frames.json
│       ├── BodyA.png, HipsA.png
│       ├── HeadA0-17.png
│       ├── FaceA0-8.png
│       ├── LegsA0-6.png
│       └── PlayerArm0-12.png
└── SlugcatCompanion-*.exe
```

---

## Problemas comuns

**Não abre:** verifique se a pasta `assets\player` está na mesma pasta do `.exe`.

**Performance ruim:** o app é leve (~200KB, GDI+), mas máquinas muito antigas com CPUs lentas podem ter dificuldade com o loop de 60 FPS.

---

## Créditos

Rain World é desenvolvido pela [Videocult](https://www.rainworldgame.com/). Os sprites são dos assets oficiais do jogo.

Referência de física: [slugcatpet](https://github.com/lingxiaojun/slugcatpet) de lingxiaojun (MIT), implementação Python que descompilou o código original do Rain World.
