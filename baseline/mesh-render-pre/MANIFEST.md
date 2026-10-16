# Baseline Visual — Pré-Malha "boneco de papel" (mesh-render Task 0 / FEAT-001)

Captura de referência do render rígido atual (ponte `DrawImage` com `Rectangle`,
incapaz de deformar) ANTES da introdução da malha deformável. Serve de ground
truth geométrico para comparar a fidelidade visual ao longo das FEAT-002..005.

## Como foi gerado

- Branch: `feature/mesh-render` (worktree `.worktrees/mesh-render`)
- `DebugMode = true` em `SlugcatDesktop.cs` (revertido para `false` após a captura)
- Build: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1`
  (Exit 0; apenas warnings CS0162/CS0414 esperados)
- Assets: `assets/player/*.png` copiados de
  `c:\dsn\Projetos\SlugCat-DesktopCompanion\assets\player` (gitignored; não versionados)
- Execução: `SlugcatCompanion-v20.exe --benchmark` (auto-fecha após 400 ticks)
- Captura a cada `DebugCaptureInterval = 10` frames

## Resultado

- Frames capturados: **40** (40 PNG + 40 JSON gerados em `debug_frames/`)
- Frames com `issues` não-vazio: **0** — todos os 40 têm `issues: []`
- Conclusão: a base refatorada já renderiza corretamente o estado rígido atual.

## O que está versionado aqui

Apenas os `frame_NNNNNN.json` (posições de chunks/cauda/braços + array `issues`).
Os PNGs correspondentes NÃO são versionados (mesmo critério de `debug_frames/`);
foram inspecionados visualmente e arquivados fora do git. Para regenerar os PNGs,
repita o loop visual com `DebugMode = true`.
