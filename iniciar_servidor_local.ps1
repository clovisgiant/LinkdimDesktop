# ============================================================
# iniciar_servidor_local.ps1 -- Inicia o Backend para Teste Local
# ============================================================

$ROOT_DIR = $PSScriptRoot

# Carrega variaveis do arquivo .env se existir
$EnvFile = Join-Path $ROOT_DIR ".env"
if (Test-Path $EnvFile) {
    Get-Content $EnvFile | Where-Object { $_ -and -not $_.Trim().StartsWith("#") -and $_.Contains("=") } | ForEach-Object {
        $parts = $_ -split "=", 2
        [System.Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim().Trim('"'), "Process")
    }
}

# 3. Caminhos para Binarios
$env:WEBCRAWLER_DIR = "${ROOT_DIR}\WebCrawler_Bin"
$env:WEBCRAWLER_BINARY = "WebCrawler.exe"

# 4. IP da Rede (Servidor)
$IP = "192.168.1.70" 

Write-Host ""
Write-Host "----------------------------------------------------" -ForegroundColor Cyan
Write-Host "       LINKDIM - MODO SERVIDOR LOCAL                " -ForegroundColor Cyan
Write-Host "----------------------------------------------------" -ForegroundColor Cyan
Write-Host ""
Write-Host "INFO: SEU IP LOCAL: http://${IP}:8000" -ForegroundColor Yellow
Write-Host "INFO: Use este endereco no campo API URL do Dashboard." -ForegroundColor White
Write-Host "INFO: Pasta do Crawler: $env:WEBCRAWLER_DIR" -ForegroundColor Gray
Write-Host ""
Write-Host "INFO: Iniciando Backend FastAPI..." -ForegroundColor Green

# 5. Iniciar FastAPI
Set-Location "${ROOT_DIR}\render_backend"
python -m uvicorn main:app --host 0.0.0.0 --port 8000
