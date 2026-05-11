# ============================================================
# Dockerfile — Container unificado: FastAPI + C# + Chrome
# Deploy no Render.com como "Docker" service
# ============================================================

# ── Estágio 1: Build do projeto C# ──────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-cs
WORKDIR /src

# Copia o projeto C#
COPY WebCrawler ./WebCrawler/
WORKDIR /src/WebCrawler
RUN dotnet publish -c Release -o /app/crawler --self-contained false

# ── Estágio 2: Runtime final (Usando SDK para garantir compatibilidade total) ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime

# ── Instala Python, Chrome e ChromeDriver ───────────────────
# ── Instala Python, Chrome e dependências do sistema ────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 python3-pip python3-venv python3-dev \
    build-essential libpq-dev \
    wget gnupg curl unzip ca-certificates \
    # Chrome e dependências para Headless (Super Conjunto)
    chromium chromium-driver \
    fonts-liberation libnss3 libatk-bridge2.0-0 libatk1.0-0 \
    libcups2 libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 \
    libxrandr2 libgbm1 libasound2 libpango-1.0-0 libpangocairo-1.0-0 \
    libxshmfence1 libnss3-dev libxrender1 libfontconfig1 \
    # Utilitários de diagnóstico
    procps \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# ── Variáveis para Chrome headless ──────────────────────────
ENV CHROMIUM_PATH=/usr/bin/chromium
ENV CHROMEDRIVER_PATH=/usr/bin/chromedriver
ENV CHROME_NO_SANDBOX=true
ENV DISPLAY=:99

# ── Copia binários C# do estágio anterior ───────────────────
COPY --from=build-cs /app/crawler /app/crawler

# ── Instala dependências Python (Direto no comando - Estilo Ultra-Safe) ──
WORKDIR /api
RUN python3 -m venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"
RUN pip install --no-cache-dir --upgrade pip setuptools wheel && \
    pip install --no-cache-dir fastapi uvicorn psycopg2-binary websockets python-multipart

# Copia o código FastAPI
COPY render_backend/main.py .

# ── Script de entrada ────────────────────────────────────────
COPY render_backend/start.sh /start.sh
RUN chmod +x /start.sh

EXPOSE 8000
CMD ["/start.sh"]
