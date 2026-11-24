# ============================================================
# gerar_backend_exe.ps1 -- Gera executavel do Backend FastAPI
# ============================================================

$ErrorActionPreference = "Stop"

$ROOT_DIR = $PSScriptRoot
$BACKEND_DIR = "$ROOT_DIR\render_backend"
$VENV_DIR = "$ROOT_DIR\.venv"
$VENV_PYTHON = "$VENV_DIR\Scripts\python.exe"

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Gerando Backend FastAPI (EXE)      " -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Passo 1: Ativar venv
Write-Host "[1/5] Ativando ambiente virtual..." -ForegroundColor Yellow
if (!(Test-Path $VENV_PYTHON)) {
    Write-Host "ERRO: Virtual environment nao encontrado em $VENV_DIR" -ForegroundColor Red
    Write-Host "Crie primeiro: python -m venv .venv" -ForegroundColor Yellow
    exit 1
}

# Passo 2: Instalar PyInstaller
Write-Host "[2/5] Instalando PyInstaller..." -ForegroundColor Yellow
& $VENV_PYTHON -m pip install pyinstaller -q

# Passo 3: Instalar dependencias do backend
Write-Host "[3/5] Instalando dependencias do backend..." -ForegroundColor Yellow
& $VENV_PYTHON -m pip install -r "$BACKEND_DIR\requirements.txt" -q

# Passo 4: Gerar EXE
Write-Host "[4/5] Gerando EXE com PyInstaller..." -ForegroundColor Yellow
Push-Location $BACKEND_DIR
try {
    & $VENV_PYTHON -m PyInstaller `
        --onefile `
        --name "LinkdimBackend" `
        --console `
        --add-data "start.sh:." `
        main.py
    
    if ($LASTEXITCODE -ne 0) {
        throw "PyInstaller falhou"
    }
} finally {
    Pop-Location
}

# Passo 5: Copiar EXE para raiz
Write-Host "[5/5] Copiando EXE para a raiz do projeto..." -ForegroundColor Yellow
$EXE_SOURCE = "$BACKEND_DIR\dist\LinkdimBackend.exe"
$EXE_DEST = "$ROOT_DIR\LinkdimBackend.exe"

if (Test-Path $EXE_SOURCE) {
    Copy-Item $EXE_SOURCE $EXE_DEST -Force
    Write-Host "Criado com sucesso!" -ForegroundColor Green
    Write-Host "  Localizacao: $EXE_DEST" -ForegroundColor Green
} else {
    Write-Host "ERRO: EXE nao foi gerado em $EXE_SOURCE" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  Para rodar o backend:" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  $EXE_DEST" -ForegroundColor Green
Write-Host ""
Write-Host "Ou execute o script:" -ForegroundColor Yellow
Write-Host "  .\rodar_backend.ps1" -ForegroundColor Green
Write-Host ""
