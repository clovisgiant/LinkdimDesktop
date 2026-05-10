# ============================================================
# rodar_crawler.ps1 — Build e execução do LinkdimCrawler
# Fase 1: Valida conexão PostgreSQL e schema
# ============================================================
param(
    [switch]$TestMode,
    [switch]$Rebuild
)

$ErrorActionPreference = "Stop"

# Ajuste para o caminho do seu Qt
$QtDir = "C:\Qt\6.11.0\mingw_64"
$MinGW  = "C:\Qt\Tools\mingw1310_64\bin"
$Build  = "$PSScriptRoot\build_crawler"

# Adiciona Qt e MinGW ao PATH da sessão
$env:PATH = "$QtDir\bin;$MinGW;$env:PATH"

Write-Host "=== LinkDim WebCrawler — Build ===" -ForegroundColor Cyan

# Cria diretório de build se necessário
if ($Rebuild -and (Test-Path $Build)) {
    Write-Host "Limpando build anterior..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $Build
}

if (!(Test-Path $Build)) {
    New-Item -ItemType Directory -Path $Build | Out-Null
}

Push-Location $Build

try {
    # Configura CMake
    Write-Host "Configurando CMake..." -ForegroundColor Yellow
    cmake .. `
        -G "MinGW Makefiles" `
        -DCMAKE_PREFIX_PATH="$QtDir" `
        -DCMAKE_BUILD_TYPE=Debug
    if ($LASTEXITCODE -ne 0) { throw "CMake configure falhou" }

    # Compila apenas o LinkdimCrawler
    Write-Host "Compilando LinkdimCrawler..." -ForegroundColor Yellow
    cmake --build . --target LinkdimCrawler --parallel
    if ($LASTEXITCODE -ne 0) { throw "Build falhou" }

    Write-Host "Build concluído!" -ForegroundColor Green

    # Copia DLLs do Qt necessárias
    Write-Host "Copiando DLLs Qt..." -ForegroundColor Yellow
    & "$QtDir\bin\windeployqt.exe" "$Build\LinkdimCrawler.exe" `
        --no-translations --compiler-runtime 2>$null

    # Copia o .env se existir
    $envFile = "$PSScriptRoot\.env"
    if (Test-Path $envFile) {
        Copy-Item $envFile "$Build\.env" -Force
        Write-Host ".env copiado para pasta de build." -ForegroundColor Green
    } else {
        Write-Host "AVISO: .env não encontrado. Crie-o a partir de .env.example" -ForegroundColor Yellow
    }

} finally {
    Pop-Location
}

# Executa o crawler
Write-Host ""
Write-Host "=== Iniciando LinkdimCrawler ===" -ForegroundColor Cyan

$exeArgs = @()
if ($TestMode) {
    Write-Host "Modo TESTE ativado" -ForegroundColor Yellow
    $env:WEBCRAWLER_TEST_MODE = "true"
}

Push-Location $Build
try {
    & ".\LinkdimCrawler.exe" @exeArgs
} finally {
    Pop-Location
}
