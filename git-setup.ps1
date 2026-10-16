# Script de setup do Git para o projeto
# Execute este script DEPOIS de fechar e reabrir o PowerShell

Write-Host "=== Slugcat Desktop Companion - Git Setup ===" -ForegroundColor Cyan

# Configura user se ainda não configurado
Write-Host "`nConfigurando Git user..." -ForegroundColor Yellow
git config user.name
if ($LASTEXITCODE -ne 0) {
    Write-Host "Configure seu nome:"
    $name = Read-Host "Nome"
    git config --global user.name "$name"
}

git config user.email
if ($LASTEXITCODE -ne 0) {
    Write-Host "Configure seu email:"
    $email = Read-Host "Email"
    git config --global user.email "$email"
}

# Cria branch develop
Write-Host "`nConfigurando branches..." -ForegroundColor Yellow
git checkout -b develop 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Branch 'develop' criada" -ForegroundColor Green
} else {
    git checkout develop
    Write-Host "✓ Branch 'develop' já existe" -ForegroundColor Green
}

# Add e commit arquivos
Write-Host "`nAdicionando arquivos..." -ForegroundColor Yellow
git add -A
git status --short

Write-Host "`nFazendo commit de atualização..." -ForegroundColor Yellow
git commit -m "fix(v16.2): monitor full-width traversal, arm order inversion and face rotation

- Movimentação cobrindo a largura real do monitor
- Correção de inversão de ordem dos braços (frente/trás) e suporte a Facing
- Rosto rotacionando em sincronia com o segmento da cabeça
- Espelhamento de anchorX em sprites assimétricos com flip horizontal
- Prevenção de perda de precisão float no ciclo de passos"

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Commit realizado com sucesso!" -ForegroundColor Green
} else {
    Write-Host "! Nada para commitar ou erro" -ForegroundColor Yellow
}

# Volta para main e faz merge do develop
Write-Host "`nConfigurando branch main..." -ForegroundColor Yellow
git checkout main
git merge develop --no-ff -m "release: v16.2 - Bugfix patch for companion simulation and rendering"

Write-Host "`n=== Setup concluído! ===" -ForegroundColor Cyan
Write-Host "Branches:" -ForegroundColor Yellow
git branch -v

Write-Host "`nPróximos passos:" -ForegroundColor Green
Write-Host "1. Para desenvolvimento: git checkout develop"
Write-Host "2. Para fazer commits: git add . && git commit -m 'mensagem'"
Write-Host "3. Para criar release: merge develop -> main"
Write-Host "4. Para push: git push origin develop (ou main)"
