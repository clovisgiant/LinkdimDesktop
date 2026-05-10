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

# ── Estágio 2: Runtime final ─────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# ── Instala Python, Chrome e ChromeDriver ───────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 python3-pip python3-venv python3-dev \
    gcc libc6-dev libpq-dev \
    wget gnupg curl unzip ca-certificates \
    # Chrome headless
    chromium chromium-driver \
    # Fontes para não crashar na renderização
    fonts-liberation libappindicator3-1 libasound2 \
    libatk-bridge2.0-0 libatk1.0-0 libcups2 libdbus-1-3 \
    libgdk-pixbuf2.0-0 libnspr4 libnss3 libx11-xcb1 \
    libxcomposite1 libxdamage1 libxrandr2 xdg-utils \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# ── Variáveis para Chrome headless ──────────────────────────
ENV CHROMIUM_PATH=/usr/bin/chromium
ENV CHROMEDRIVER_PATH=/usr/bin/chromedriver
ENV CHROME_NO_SANDBOX=true
ENV DISPLAY=:99

# ── Copia binários C# do estágio anterior ───────────────────
COPY --from=build-cs /app/crawler /app/crawler

# ── Instala dependências Python via VENV (Mais robusto) ─────
WORKDIR /api
COPY render_backend/requirements.txt .
RUN python3 -m venv /opt/venv
ENV PATH="/opt/venv/bin:$PATH"
RUN pip install --no-cache-dir --upgrade pip && \
    pip install --no-cache-dir -r requirements.txt

# Copia o código FastAPI
COPY render_backend/main.py .

# ── Script de entrada ────────────────────────────────────────
COPY render_backend/start.sh /start.sh
RUN chmod +x /start.sh

EXPOSE 8000
CMD ["/start.sh"]
