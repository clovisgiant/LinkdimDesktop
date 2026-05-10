# Script para compilar e rodar a aplicacao Qt automaticamente

Write-Host "Configurando as ferramentas de build (Qt, CMake, MinGW)..." -ForegroundColor Cyan
$env:PATH = "C:\Qt\6.11.0\mingw_64\bin;C:\Qt\Tools\mingw1310_64\bin;C:\Qt\Tools\CMake_64\bin;C:\Qt\Tools\Ninja;$env:PATH"

Write-Host "Limpando diretorio de build anterior..." -ForegroundColor Yellow
if (Test-Path build) { Remove-Item -Recurse -Force build }

Write-Host "Gerando arquivos de build com CMake..." -ForegroundColor Yellow
cmake -S . -B build -G "Ninja"

Write-Host "Compilando o projeto..." -ForegroundColor Yellow
cmake --build build

if ($LASTEXITCODE -eq 0) {
    Write-Host "Compilacao concluida com sucesso! Abrindo o aplicativo..." -ForegroundColor Green
    
    # Executa o aplicativo
    .\build\DashboardProject.exe
} else {
    Write-Host "Ocorreu um erro na compilacao. Verifique o codigo." -ForegroundColor Red
}
