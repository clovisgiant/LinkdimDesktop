# ======================================================================
# SCRIPT DE BUILD E DEPLOY - LINKDIM WEB DASHBOARD (LOCAWEB & NUVEM)
# ======================================================================
# Compila a aplicacao React (Vite) com caminhos relativos
# e gera dois pacotes prontos:
# 1) LinkdimWeb_Locaweb.zip -> Para Apache/PHP na Locaweb (/public_html/linkdin)
# 2) LinkdimWeb_Nuvem.zip   -> Para Docker/VPS com Node.js
# ======================================================================

$ErrorActionPreference = "Stop"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  LINKDIM WEB - BUILDER PARA LOCAWEB & NUVEM" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ScriptDir) {
    $ScriptDir = Get-Location
}
Set-Location $ScriptDir

$WebDir = Join-Path $ScriptDir "LinkdimWeb"
$DeployLocawebDir = Join-Path $ScriptDir "Deploy_Locaweb"
$ZipLocawebName = "LinkdimWeb_Locaweb.zip"
$ZipLocawebPath = Join-Path $ScriptDir $ZipLocawebName

$DeployNuvemDir = Join-Path $ScriptDir "Deploy_Nuvem_Web"
$ZipNuvemName = "LinkdimWeb_Nuvem.zip"
$ZipNuvemPath = Join-Path $ScriptDir $ZipNuvemName

if (-not (Test-Path $WebDir)) {
    Write-Host "[ERRO] Pasta LinkdimWeb nao encontrada em $WebDir!" -ForegroundColor Red
    exit 1
}

# 1. Verificar Node.js
Write-Host "`n>>> [1/5] Verificando ambiente de compilacao..." -ForegroundColor Yellow
$nodeVersion = node -v
$npmVersion = npm -v
Write-Host "Node.js: $nodeVersion | npm: $npmVersion" -ForegroundColor Green

# 2. Compilar o Frontend React (Vite)
Write-Host "`n>>> [2/5] Compilando React em modo producao (Vite build)..." -ForegroundColor Yellow
Push-Location $WebDir
try {
    if (-not (Test-Path "node_modules")) {
        Write-Host "Instalando dependencias do LinkdimWeb..." -ForegroundColor Gray
        npm install
    }

    npm run build
    if (-not (Test-Path "dist\index.html")) {
        throw "O arquivo dist\index.html nao foi gerado."
    }
    Write-Host "Frontend React compilado com sucesso na pasta 'dist/'!" -ForegroundColor Green
}
finally {
    Pop-Location
}

# 3. Montar o Pacote Especial da LOCAWEB (Apache + PHP + MySQL)
Write-Host "`n>>> [3/5] Montando pacote para LOCAWEB em $DeployLocawebDir..." -ForegroundColor Yellow
if (Test-Path $DeployLocawebDir) {
    Remove-Item -Recurse -Force $DeployLocawebDir
}
New-Item -ItemType Directory -Force -Path $DeployLocawebDir | Out-Null

# Copiar arquivos do build para a pasta da Locaweb
Copy-Item "$WebDir\dist\*" $DeployLocawebDir -Recurse -Force

# Garantir que a pasta api e o .htaccess estejam presentes
if (-not (Test-Path "$DeployLocawebDir\api")) {
    New-Item -ItemType Directory -Force -Path "$DeployLocawebDir\api" | Out-Null
}
Copy-Item "$WebDir\public\api\index.php" "$DeployLocawebDir\api\index.php" -Force
if (Test-Path "$WebDir\public\api\config.local.php") {
    Copy-Item "$WebDir\public\api\config.local.php" "$DeployLocawebDir\api\config.local.php" -Force
}
Copy-Item "$WebDir\public\.htaccess" "$DeployLocawebDir\.htaccess" -Force

# Criar instrucoes de upload para Locaweb
$ReadmeLocaweb = @"
========================================================================
 INSTRUCOES DE PUBLICACAO NA HOSPEDAGEM (APACHE + PHP + MYSQL)
========================================================================

Este pacote foi gerado especificamente para rodar em hospedagem compartilhada
utilizando Apache + PHP (SEM necessitar de Node.js instalado no servidor).

COMO INSTALAR NO SERVIDOR:
------------------------------------------------------------------------
1. Acesse o Gerenciador de Arquivos do Painel da hospedagem (ou FTP).
2. Va ate a pasta: /public_html/linkdin/
3. Faca o upload do arquivo 'LinkdimWeb_Locaweb.zip' e descompacte la dentro.
4. Certifique-se de que api/config.local.php esteja preenchido com os dados do MySQL.
========================================================================
"@
Set-Content -Path (Join-Path $DeployLocawebDir "INSTRUCOES_LOCAWEB.txt") -Value $ReadmeLocaweb -Encoding UTF8

# Compactar pacote da Locaweb
if (Test-Path $ZipLocawebPath) {
    Remove-Item -Force $ZipLocawebPath
}
Compress-Archive -Path "$DeployLocawebDir\*" -DestinationPath $ZipLocawebPath -Force
$zipLocawebSize = (Get-Item $ZipLocawebPath).Length / 1MB
Write-Host ("Pacote LOCAWEB gerado com sucesso: {0} ({1:N2} MB)" -f $ZipLocawebName, $zipLocawebSize) -ForegroundColor Green

# 4. Montar o Pacote Node.js / Docker (Opcional)
Write-Host "`n>>> [4/5] Montando pacote Node.js/Docker em $DeployNuvemDir..." -ForegroundColor Yellow
if (Test-Path $DeployNuvemDir) {
    Remove-Item -Recurse -Force $DeployNuvemDir
}
New-Item -ItemType Directory -Force -Path $DeployNuvemDir | Out-Null
Copy-Item (Join-Path $WebDir "dist") (Join-Path $DeployNuvemDir "dist") -Recurse -Force
Copy-Item (Join-Path $WebDir "server") (Join-Path $DeployNuvemDir "server") -Recurse -Force
Copy-Item (Join-Path $WebDir "package.json") (Join-Path $DeployNuvemDir "package.json") -Force
Copy-Item (Join-Path $WebDir "package-lock.json") (Join-Path $DeployNuvemDir "package-lock.json") -Force
Copy-Item (Join-Path $WebDir ".env") (Join-Path $DeployNuvemDir ".env") -Force
Copy-Item (Join-Path $WebDir "Dockerfile") (Join-Path $DeployNuvemDir "Dockerfile") -Force
Copy-Item (Join-Path $WebDir "docker-compose.yml") (Join-Path $DeployNuvemDir "docker-compose.yml") -Force

if (Test-Path $ZipNuvemPath) {
    Remove-Item -Force $ZipNuvemPath
}
Compress-Archive -Path "$DeployNuvemDir\*" -DestinationPath $ZipNuvemPath -Force

# 5. Conclusao
Write-Host "`n======================================================" -ForegroundColor Green
Write-Host " BUILD CONCLUIDO COM SUCESSO!" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
Write-Host " [PACOTE PRINCIPAL LOCAWEB - SEM NODE.JS]" -ForegroundColor Cyan
Write-Host " Pasta:  $DeployLocawebDir" -ForegroundColor White
Write-Host " ZIP:    $ZipLocawebPath" -ForegroundColor Yellow
Write-Host " Destino no Servidor: /public_html/linkdin/" -ForegroundColor White
Write-Host "======================================================" -ForegroundColor Green
