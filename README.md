# 🐱 Slugcat Desktop Companion

<div align="center">

**Um companheiro de desktop animado do Rain World que vive na sua área de trabalho!**

*Baseado na física e animações originais do Survivor do Rain World*

[🎮 Baixar](#executar) • [🔨 Compilar](#recompilar) • [📖 Documentação](#física-e-renderização)

</div>

---

## ✨ Características

- **🎭 Animações autênticas** - 60+ frames de animação do Rain World original
- **🏃 Comportamento autônomo** - Anda, para, olha ao redor e descansa sozinho
- **🖱️ Interativo** - Clique para fazer pular, arraste para mover pela tela
- **⚙️ Física realista** - Sistema completo de body chunks, rabo físico e rig articulado
- **👁️ Expressões vivas** - Pisca automaticamente, olha na direção do movimento
- **🎨 Totalmente transparente** - Janela sem bordas integrada ao desktop
- **💾 Leve e eficiente** - ~200KB, usa apenas .NET Framework nativo do Windows

## 🚀 Executar

1. Baixe ou compile o executável (veja versões abaixo)
2. Mantenha a pasta `assets\player` ao lado do `.exe`
3. Execute `SlugcatCompanion-v18-DRAG.exe`

**Versões disponíveis:**
- `v20.exe` - **🌟 RECOMENDADA 🌟** → Versão definitiva com todas as melhorias
  - ✅ Sistema de Log robusto (debug em log.txt)
  - ✅ Proteção anti-NaN/Infinito com auto-reset
  - ✅ Largura dinâmica adaptada ao monitor
  - ✅ Física de braços realista com ombro+mão
  - ✅ Seleção inteligente de frames por comprimento
  - ✅ Velocidade otimizada (1.1x)
  - ✅ Caminhadas longas (2-6s)
  - ✅ Pulo apenas no chão (sem duplo)
  - ✅ Janela 450px arrastável
  - ✅ Click rápido = pulo / Arrastar = mover
- `v18-DRAG.exe` - Braços corrigidos + janela arrastável
- `v15.exe` - Primeira versão completa com animações
- Anteriores - Versões de desenvolvimento

> **Nota:** As imagens são partes oficiais extraídas do atlas do Rain World. Mantenha-as na pasta `assets\player\`.

## 🎮 Controles

| Ação | Como fazer |
|------|------------|
| **Mover janela** | Clique e arraste em qualquer lugar |
| **Fazer pular** | Clique rapidamente (sem arrastar) |
| **Menu** | Clique com botão direito |
| **Esconder/Mostrar** | Duplo clique no ícone da bandeja |
| **Sair** | Menu da bandeja → Sair |

## 🔨 Recompilar

### Método 1: PowerShell (se scripts estiverem habilitados)
```powershell
.\build.ps1
```

### Método 2: Compilação direta
```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ /win32manifest:"app.manifest" /out:"SlugcatCompanion.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll SlugcatDesktop.cs
```

O código usa o **compilador C# do .NET Framework 4.0** incluído nativamente no Windows. Não requer Visual Studio ou ferramentas externas.

**Requisitos:**
- Windows 7 ou superior
- .NET Framework 4.0+ (já vem no Windows)

## 🎨 Física e renderização

### Sistema de física (baseado no Rain World original)

O companion recria fielmente a física do **Survivor** de Rain World:

#### Body Chunks (núcleos físicos)
- **Chunk superior:** Raio 9 pixels (tórax)
- **Chunk inferior:** Raio 8 pixels (quadril)
- **Conexão:** Distância normal de 17 pixels
- **Gravidade:** 0.9 unidades/frame
- **Atrito do ar:** 0.999 (99,9%)
- **Quique:** 0.1 (10% ao bater no chão)
- **Atrito de superfície:** 0.5 (50% ao deslizar)

#### Rig articulado (3 pivôs)
1. **Cintura** - Ancorado no chunk inferior
2. **Barriga** - Pivô intermediário articulado com física própria
3. **Pescoço** - Ancorado no chunk superior

A barriga balança sutilmente durante caminhada e "respira" quando parado.

#### Sistema de cauda
- **4 segmentos físicos** independentes
- Raios decrescentes: 6 → 4 → 2.5 → 1 pixel
- Conexões alongadas em **30%** para visual mais fluido
- Física completa: gravidade, inércia e arrasto por velocidade
- Interpolação **Catmull-Rom** para contorno suave
- Colide com o chão e segue movimento do corpo

### 🎭 Sistema de animação

#### Cabeça dinâmica
- **18 orientações** (HeadA0-17) seguem direção do olhar
- **Olhar vertical:** Olha pra cima ao pular, pra baixo ao cair
- **Física independente:** Inércia e conexão elástica ao pescoço
- **Suavização:** Transições graduais entre frames

#### Expressões faciais
- **9 frames** de expressões (FaceA0-8)
- **Sistema automático de piscadas:**
  - Intervalo aleatório: 2-5.5 segundos
  - Duração: 0.075-0.15 segundos
  - Frames de olhos fechados durante piscada

#### Braços dinâmicos
- **13 frames** de posição (PlayerArm0-12)
- **Balanço alternado** durante caminhada (sincronizado com velocidade)
- **Posição relaxada** quando parado
- **Renderização em camadas:** Braço traseiro atrás do corpo, frontal na frente

#### Pernas animadas
- **7 frames** de caminhada (LegsA0-6)
- **Sincronização:** Frequência ajusta com velocidade real
- **Elevação:** Animação de levantamento durante passos
- **Frame parado:** Pernas estáticas quando não está andando

### 🖼️ Ordem de renderização (de trás pra frente)

```
1. Rabo (background)
2. Quadris (HipsA) - segmento inferior do rig
3. Corpo (BodyA) - segmento superior do rig
4. Pernas (LegsA) - ancoradas no chunk inferior
5. Braço traseiro (PlayerArm) - atrás do corpo
6. Braço frontal (PlayerArm) - na frente do corpo
7. Cabeça (HeadA) - com rotação sutil
8. Rosto (FaceA) - overlay com expressões
```

### 🤖 Comportamento autônomo

#### Estados comportamentais
- **Andando:** Duração 1.2-2.15 segundos
  - Direção aleatória (esquerda/direita)
  - Aceleração gradual (0-100% em ~0.6s)
  - Velocidade máxima: ~1.5 pixels/frame
  - Detecta bordas da tela e inverte direção

- **Parado:** Duração 3.125-5.875 segundos
  - Respiração sutil da barriga
  - Olhar ocasional para lados
  - Piscadas aleatórias

#### Sistema de pulo (quando clicado)
- **Impulso vertical:** +7.5 pixels/frame
- **Impulso horizontal:** +1.25 pixels/frame na direção atual
- **Física completa:** Parábola realista com gravidade

### ⚡ Performance

- **Taxa de simulação:** 40 atualizações/segundo (25ms por frame)
- **Taxa de renderização:** ~60 FPS (16ms por frame)
- **Acumulador de física:** Evita slowdowns em sistemas lentos
- **Limite de updates:** Máximo 4 updates por frame (proteção anti-lag)

### 🔧 Diferenças do Rain World original

Como este é um **companion de desktop standalone**, algumas adaptações foram necessárias:

❌ **Não incluído:**
- Terrenos complexos e tiles de sala
- Sistema completo de estados do Player
- Colisões com objetos e criaturas
- Escalada e agarrar em poles
- Runtime da Unity

✅ **Adaptações:**
- **Piso plano:** Substitui tiles complexos
- **Postura limitada:** Mantém Slugcat em pé sem terreno
- **Bordas invisíveis:** Limites da janela = limites do mundo
- **Física simplificada:** Apenas gravidade + chão

## 📦 Estrutura do projeto

```
SlugCat-DesktopCompanion/
├── SlugcatDesktop.cs          # Código-fonte completo (~1000 linhas)
├── app.manifest               # Configurações de DPI e permissões
├── build.ps1                  # Script de compilação
├── README.md                  # Esta documentação
├── assets/
│   └── player/
│       ├── atlas-frames.json  # Metadados dos frames
│       ├── BodyA.png         # Corpo
│       ├── HipsA.png         # Quadril
│       ├── HeadA0-17.png     # 18 orientações de cabeça
│       ├── FaceA0-8.png      # 9 expressões faciais
│       ├── LegsA0-6.png      # 7 frames de pernas
│       └── PlayerArm0-12.png # 13 frames de braços
└── SlugcatCompanion-*.exe    # Executáveis compilados
```

## 🛠️ Tecnologias

- **Linguagem:** C# (.NET Framework 4.0)
- **UI:** Windows Forms (System.Windows.Forms)
- **Gráficos:** GDI+ (System.Drawing)
- **Física:** Implementação custom baseada em Rain World
- **Transparência:** Chroma key (Color.Fuchsia)


## 🐛 Resolução de problemas

### O executável não abre
- Certifique-se de que a pasta `assets\player` está ao lado do `.exe`
- Verifique se o .NET Framework 4.0+ está instalado (já vem no Windows 7+)

### Braços parecem grudados/sobrepostos
- Use a versão **v18-DRAG.exe** ou superior
- Versões antigas (v13 e anteriores) tinham `armSpread` muito pequeno

### Não consigo arrastar a janela
- Certifique-se de estar usando **v18-DRAG.exe**
- Versões v16-v17 tinham janela fixa (fullscreen)

### Performance ruim
- O app é otimizado, mas sistemas muito antigos podem ter lentidão
- Fecha outros apps que usam muita GPU/CPU

## 📜 Histórico de versões

### v20 (atual) - **🌟 DEFINITIVA 🌟**
**Data:** 16/10/2026

Versão consolidada com TODAS as melhorias de estabilidade, física e UX:

#### 🛡️ Estabilidade e Debug
- ✅ Sistema de Log robusto (salva em `log.txt`)
- ✅ Proteção anti-NaN/Infinito com validação de estado
- ✅ Auto-reset da simulação em caso de valores inválidos
- ✅ Try-catch em rendering para evitar crashes no GDI+

#### 🎯 Física Melhorada
- ✅ Braços com física realista (ombro + mão + rotação)
- ✅ Seleção inteligente de frame por comprimento real
- ✅ Velocidade de caminhada otimizada (0.10 → 1.1)
- ✅ Caminhadas mais longas (2-6 segundos vs 1.2-2.15s)
- ✅ Pulo apenas no chão (sem pulo duplo no ar)

#### 📐 Adaptabilidade
- ✅ Largura do mundo dinâmica baseada no monitor
- ✅ EdgeMargin de 28px para cabeça/rabo nas bordas
- ✅ Janela portátil de 450x214px (não fullscreen)

#### 🖱️ Interação
- ✅ Sistema de drag restaurado e aperfeiçoado
- ✅ Click rápido (<10px movimento) = pulo
- ✅ Arrastar = move janela
- ✅ Área de click precisa (60x80px no corpo)

### v18-DRAG
- ✅ Braços corrigidos (armSpread aumentado de 3→7)
- ✅ Sistema de drag restaurado
- ✅ Janela menor (450x214) e portátil
- ✅ Click rápido = pulo, arrastar = move janela

### v15 - MEGA UPDATE ESTÉTICA
- 🎨 Sistema completo de braços (13 frames)
- 🎨 Cabeça com 18 orientações
- 🎨 9 expressões faciais com piscadas automáticas
- 🎨 Ordem correta de renderização

### v13 e anteriores
- 🏗️ Física básica funcional
- 🏗️ Desenvolvimento incremental

## 🤝 Créditos

- **Rain World** - [Videocult](https://www.rainworldgame.com/)
- **Arte original** - Extraída dos assets oficiais do jogo
- **Física e comportamento** - Baseados na implementação original
- **Código desktop** - Adaptação standalone em C#

## 📄 Licença

Este projeto é um **fan project educacional** e não oficial. Rain World e todos os seus assets pertencem à Videocult. Use apenas para fins pessoais e educacionais.

---

<div align="center">

**Feito com ❤️ para os fãs de Rain World**

*Survivor forever!* 🐱✨

</div>
