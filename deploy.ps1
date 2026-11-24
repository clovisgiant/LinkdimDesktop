# Script para gerar a Release e empacotar a aplicação com as DLLs (Deploy)

Write-Host "Configurando as ferramentas de build para o Deploy..." -ForegroundColor Cyan
$env:PATH = "C:\Qt\6.11.0\mingw_64\bin;C:\Qt\Tools\mingw1310_64\bin;C:\Qt\Tools\CMake_64\bin;C:\Qt\Tools\Ninja;$env:PATH"

Write-Host "Gerando arquivos de build (Modo Release)..." -ForegroundColor Yellow
cmake -S . -B build_release -G "Ninja" -DCMAKE_BUILD_TYPE=Release

Write-Host "Compilando o projeto em Release..." -ForegroundColor Yellow
cmake --build build_release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Falha na compilação!" -ForegroundColor Red
    exit 1
}

Write-Host "Preparando a pasta 'dist' (Distribuição)..." -ForegroundColor Yellow
if (Test-Path dist) { Remove-Item -Recurse -Force dist }
New-Item -ItemType Directory -Path dist | Out-Null

Write-Host "Copiando o executável e dependências de assets..." -ForegroundColor Yellow
Copy-Item "build_release\DashboardProject.exe" "dist\"
if (Test-Path "world_map.png") {
    Copy-Item "world_map.png" "dist\"
}
if (Test-Path "fundo.jpg") {
    Copy-Item "fundo.jpg" "dist\"
}

Write-Host "Empacotando as DLLs do Qt com windeployqt..." -ForegroundColor Yellow
# O windeployqt escaneia o executável e joga todas as DLLs do Qt necessárias na pasta
windeployqt --no-translations "dist\DashboardProject.exe"

Write-Host "Deploy finalizado com sucesso!" -ForegroundColor Green
Write-Host "Basta copiar a pasta 'dist' inteira para um pen drive e colar no outro computador." -ForegroundColor Cyan
