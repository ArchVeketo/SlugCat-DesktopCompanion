# Changelog - Slugcat Desktop Companion

## [v16.2] - 2026-10-15

### 🐛 Correções de Bugs (Bugfix Patch)
- **Largura real de movimentação (Monitor Bound)**:
  - O Slugcat estava preso em um raio central de 360px (`screenSimWidth = 60f`). Agora `ScreenSimHalfWidth` é calculado com base na largura real do monitor primário menos margem segura, permitindo que ele caminhe pela tela inteira.
- **Inversão da ordem e posição dos braços (Arm Inversion & Facing)**:
  - O braço de trás e da frente estavam invertidos em relação ao observador quando virado para a direita.
  - Posições dos ombros agora consideram `Facing`, garantindo que troquem de lado ao mudar de direção sem descolar do corpo.
- **Rotação facial síncrona com a cabeça**:
  - A rotação do rosto (`FaceA`) estava fixada em `0f`, fazendo os olhos ficarem horizontais mesmo com o corpo ou a cabeça inclinados. Agora herda a rotação angular do segmento superior.
- **Pivô de flip horizontal de sprites assimétricos**:
  - `DrawSprite` ajustado para espelhar o `anchorX` quando `flipX = true`, evitando que peças assimétricas como ombros/braços sofram deslocamento lateral involuntário no giro.
- **Proteção contra float overflow em sessões longas**:
  - `WalkPhase` agora faz wrap preventivo para preservar a precisão decimal de ponto flutuante em execuções prolongadas sem alterar o ciclo de passos.

---

## [v16.1] - 2026-10-15

### 🐛 Correções de Bugs
- **Procedural generation bugs corrigidos**
  - Proteções contra NaN em UpdateHead()
  - Proteções contra NaN em UpdateArms()
  - Verificações de array null antes de renderizar
  - Clamp em todos os valores calculados

- **Animações estabilizadas**
  - Reduzida amplitude dos braços (0.6 → 0.3)
  - Reduzida velocidade do balanço (0.25 → 0.15)
  - HeadFrame limitado a 0-8 (frames mais estáveis)
  - FaceFrame com Clamp adicional
  - ArmFrame com Clamp em ângulos

- **Renderização segura**
  - Verificação de null em _heads, _faces, _arms
  - Verificação de bounds antes de acessar arrays
  - Fallback para não renderizar se sprite inválido

### ✨ Recursos Mantidos
- ✅ Corpo articulado (3 pivôs)
- ✅ Braços dinâmicos (13 frames)
- ✅ Cabeça rotativa (frames 0-8)
- ✅ Expressões faciais (9 frames + piscadas)
- ✅ Rabo fluido (Catmull-Rom)
- ✅ Física melhorada
- ✅ Janela full-width do monitor
- ✅ Click preciso no Slugcat

### 📝 Notas Técnicas
- Todos os cálculos proceduais agora têm proteções contra valores inválidos
- Arrays verificados antes de acesso
- Amplitude e velocidade reduzidas para maior estabilidade
- Versão deve ser **estável** sem travamentos intermitentes

---

## [v16.0] - 2026-10-15

### 🐛 Correções Principais
- Janela agora ocupa largura completa do monitor
- Slugcat não é mais cortado nas bordas
- Click funciona apenas no Slugcat (hit-box 60x80px)
- Área de movimento expandida (±60 unidades)
- Janela fixa (não pode mais arrastar)

### ⚠️ Problemas Conhecidos (corrigidos em v16.1)
- Animações travavam intermitentemente
- Bugs visuais na cabeça e braços
- Instabilidade procedural

---

## [v15.0] - 2026-10-15

### ✨ MEGA UPDATE ESTÉTICA
- Braços dinâmicos completos (13 frames)
- Cabeça rotativa (18 orientações)
- Expressões faciais animadas (9 frames)
- Sistema automático de piscadas
- Olhar vertical dinâmico

### ⚠️ Problemas (corrigidos em v16.0)
- Muitos bugs de renderização
- Instabilidade geral

---

## [v14.0] - 2026-10-15

### 🔧 Correções de Física
- Rabo mais fluido
- Cabeça sincronizada com direção
- Pernas sem deslizamento
- Aceleração/desaceleração gradual
- Área de movimento expandida

---

## [v13.0] - 2026-10-15

### ✨ Primeira versão estável
- Torso aprimorado (3 pivôs)
- Cabeça acompanha pescoço
- Rabo alongado e suavizado
- Física básica funcional

---

## Versionamento

- **Major** (vX.0): Features novas significativas
- **Minor** (vX.Y): Correções de bugs, melhorias menores
- **Patch** (vX.Y.Z): Hotfixes críticos
