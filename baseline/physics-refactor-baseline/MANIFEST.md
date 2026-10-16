# Baseline Visual — Pré-Refatoração (Task 0)

Captura de referência do comportamento visual ANTES da refatoração modular da física.
Serve de ground truth que o refactor deve **preservar** (não melhorar).

## Como foi gerado

- Branch: `refactor/physics-modular` (worktree `.worktrees/physics-refactor`)
- `DebugMode = true` em `SlugcatDesktop.cs` (revertido para `false` após a captura)
- Build: `.\build.ps1` (Exit 0; warnings CS0162/CS0414 esperados)
- Execução: `SlugcatCompanion-v20.exe --benchmark` (auto-fecha em 400 ticks)
- Captura a cada `DebugCaptureInterval = 10` frames

> Observação: os PNGs de `assets/player/` não são versionados; foram copiados do
> repositório principal para o worktree apenas para permitir a execução headless.

## Resultado

- Frames capturados: **40** (40 PNG + 40 JSON)
- Frames com `issues` não-vazio: **0**
- Conclusão: **todos os 40 frames têm `issues: []`** — este é o estado correto
  que a refatoração deve manter na validação final.

Cada `frame_NNNNNN.json` contém posições dos chunks/cauda/braços e o array `issues`
(violações de invariantes geométricas). `frame_NNNNNN.png` é o render correspondente.
