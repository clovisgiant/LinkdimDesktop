# ======================================================================
# SCRIPT DE DEPLOY AUTOMATIZADO - HST SECURE DASHBOARD
# ======================================================================
# Este script compila o C++ em modo otimizado (Release),
# empacota as DLLs do Qt e MinGW, adiciona a imagem de fundo,
# e gera um arquivo .ZIP pronto para rodar em outras maquinas.
# ======================================================================

Write-Host ">>> Iniciando processo de Deploy Automatizado..." -ForegroundColor Cyan

# 1. Configurar Caminhos do Qt e MinGW
$env:PATH = "C:\Qt\6.11.0\mingw_64\bin;C:\Qt\Tools\mingw1310_64\bin;C:\Qt\Tools\CMake_64\bin;C:\Qt\Tools\Ninja;$env:PATH"

# 2. Criar pastas necessarias e compilar em Modo Release
Write-Host ">>> Compilando em Modo Release (Alta Performance)..." -ForegroundColor Yellow
cmake -S . -B build_release -G "Ninja" -DCMAKE_BUILD_TYPE=Release
cmake --build build_release --target DashboardProject

if (-Not (Test-Path "build_release\DashboardProject.exe")) {
    Write-Host ">>> ERRO FATAL: Falha ao compilar o executavel." -ForegroundColor Red
    exit
}

# 3. Montar a pasta de Deploy e copiar os arquivos vitais
Write-Host ">>> Montando pasta de Distribuicao (Deploy)..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path "Deploy" | Out-Null
Copy-Item "build_release\DashboardProject.exe" "Deploy\"
if (Test-Path "fundo.jpg") {
    Copy-Item "fundo.jpg" "Deploy\"
}

# 4. Injetar dependencias automaticas (DLLs do Qt e Runtime C++)
Write-Host ">>> Coletando e copiando DLLs do Qt..." -ForegroundColor Yellow
windeployqt --compiler-runtime "Deploy\DashboardProject.exe"

# 5. Compactar tudo num ZIP portatil
Write-Host ">>> Gerando Arquivo ZIP Final..." -ForegroundColor Yellow
$ZipName = "Dashboard_Standalone.zip"
if (Test-Path $ZipName) { Remove-Item -Force $ZipName }
Compress-Archive -Path "Deploy\*" -DestinationPath $ZipName -Force

Write-Host "=====================================================" -ForegroundColor Green
Write-Host " SUCESSO! O pacote foi gerado:" -ForegroundColor Green
Write-Host " Arquivo: $ZipName" -ForegroundColor Green
Write-Host " Este arquivo pode ser levado em um Pen Drive!" -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
