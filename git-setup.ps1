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

Write-Host "`nFazendo commit inicial..." -ForegroundColor Yellow
git commit -m "feat: v16.1 - Slugcat Desktop Companion with arms, dynamic head, and facial expressions

- Corpo articulado com 3 pivôs (waist, belly, neck)
- Braços dinâmicos com 13 frames de animação
- Cabeça rotativa com frames 0-8
- Expressões faciais com 9 frames + sistema de piscadas
- Rabo fluido com interpolação Catmull-Rom
- Janela full-width do monitor
- Click preciso no Slugcat (hit-box 60x80px)
- Proteções anti-bug em cálculos proceduais"

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Commit realizado com sucesso!" -ForegroundColor Green
} else {
    Write-Host "! Nada para commitar ou erro" -ForegroundColor Yellow
}

# Volta para main e faz merge do develop
Write-Host "`nConfigurando branch main..." -ForegroundColor Yellow
git checkout main
git merge develop --no-ff -m "release: v16.1 - First stable release"

Write-Host "`n=== Setup concluído! ===" -ForegroundColor Cyan
Write-Host "Branches:" -ForegroundColor Yellow
git branch -v

Write-Host "`nPróximos passos:" -ForegroundColor Green
Write-Host "1. Para desenvolvimento: git checkout develop"
Write-Host "2. Para fazer commits: git add . && git commit -m 'mensagem'"
Write-Host "3. Para criar release: merge develop -> main"
Write-Host "4. Para push: git push origin develop (ou main)"
