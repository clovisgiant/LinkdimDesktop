# ============================================================
# rodar_backend.ps1 — Executa o Backend FastAPI (EXE)
# ============================================================

$ROOT_DIR = $PSScriptRoot
$EXE = "$ROOT_DIR\LinkdimBackend.exe"

# Carrega variaveis do arquivo .env se existir
$EnvFile = Join-Path $ROOT_DIR ".env"
if (Test-Path $EnvFile) {
    Get-Content $EnvFile | Where-Object { $_ -and -not $_.Trim().StartsWith("#") -and $_.Contains("=") } | ForEach-Object {
        $parts = $_ -split "=", 2
        [System.Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim().Trim('"'), "Process")
    }
}

$env:WEBCRAWLER_DIR = "$ROOT_DIR\WebCrawler_Bin"
$env:WEBCRAWLER_BINARY = "WebCrawler.exe"

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "       LINKDIM BACKEND - FastAPI (EXE)              " -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "INFO: URL do Backend: http://localhost:8000" -ForegroundColor Yellow
Write-Host ""

# Verifica se o EXE existe
if (!(Test-Path $EXE)) {
    Write-Host "ERRO: LinkdimBackend.exe não encontrado!" -ForegroundColor Red
    Write-Host "Execute primeiro: .\gerar_backend_exe.ps1" -ForegroundColor Yellow
    exit 1
}

# Executa o EXE
Write-Host "Iniciando Backend..." -ForegroundColor Green
Write-Host ""
& $EXE

