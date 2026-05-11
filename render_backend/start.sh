#!/bin/bash
# ============================================================
# start.sh — Entrypoint do container no Render
# Inicia o FastAPI (orquestrador); o crawler C# é iniciado
# sob demanda via POST /crawler/start pelo painel Qt.
# ============================================================
set -e

echo "╔══════════════════════════════════════════════════╗"
echo "║     LinkDim WebCrawler API — Iniciando...        ║"
echo "╚══════════════════════════════════════════════════╝"

# Verifica Chrome
if [ -f "$CHROMIUM_PATH" ]; then
    echo "✅ Chromium: $($CHROMIUM_PATH --version)"
else
    echo "⚠️  Chromium não encontrado em $CHROMIUM_PATH"
fi

# Verifica ChromeDriver
if [ -f "$CHROMEDRIVER_PATH" ]; then
    echo "✅ ChromeDriver: $($CHROMEDRIVER_PATH --version)"
fi

# Verifica .NET (Runtime)
dotnet --list-runtimes && echo "✅ .NET Runtime OK" || echo "⚠️  .NET: Runtime detectado, mas --version não suportado (OK)"

echo "🚀 Iniciando FastAPI na porta 8000..."
cd /api
exec uvicorn main:app --host 0.0.0.0 --port 8000 --workers 1
