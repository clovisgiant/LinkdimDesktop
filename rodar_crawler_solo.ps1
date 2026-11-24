# Script para rodar o WebCrawler de forma independente (Standalone)
# Isso ignora o Backend/FastAPI e roda o robô puro no terminal.

$CrawlerBin = "C:\Users\CLOVIS\Documents\LinkdimDesktop\WebCrawler_Bin\WebCrawler.exe"
$EnvFile = "C:\Users\CLOVIS\Documents\LinkdimDesktop\.env"

if (-not (Test-Path $CrawlerBin)) {
    Write-Host "❌ Erro: Executável não encontrado em $CrawlerBin. Compile o projeto primeiro." -ForegroundColor Red
    exit
}

Write-Host "----------------------------------------------------" -ForegroundColor Cyan
Write-Host "       LINKDIM - MODO CRAWLER SOLO (STANDALONE)     " -ForegroundColor Cyan
Write-Host "----------------------------------------------------" -ForegroundColor Cyan
Write-Host "INFO: Usando configurações de: $EnvFile"
Write-Host "INFO: Pressione CTRL+C para parar o robô."
Write-Host ""

# Executa o robô passando o diretório atual como base para o .env
& $CrawlerBin
