# =============================================================
# render_backend/main.py — Orquestrador FastAPI
# Deploy no Render.com como "Web Service"
# =============================================================
import asyncio, os, subprocess, signal, time, threading
from datetime import datetime
from typing import Optional
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import psycopg2

app = FastAPI(title="LinkDim Crawler API", version="1.0")
app.add_middleware(CORSMiddleware, allow_origins=["*"],
                   allow_methods=["*"], allow_headers=["*"])

# ── Estado global do crawler ──────────────────────────────────
class CrawlerState:
    process: Optional[subprocess.Popen] = None
    status: str = "stopped"
    last_heartbeat: Optional[str] = None
    log_buffer: list = [] # Guarda os últimos 100 logs
    config: dict = {
        "max_apply_per_cycle": 15,
        "cycle_wait_minutes":  45,
        "active_hours_start":  8,
        "active_hours_end":    22,
        "search_terms":        "C#, Python, Qt"
    }
    log_subscribers: list = []

state = CrawlerState()

# ── Modelos Pydantic ──────────────────────────────────────────
class StartRequest(BaseModel):
    max_apply_per_cycle: int = 15
    cycle_wait_minutes:  int = 45
    active_hours_start:  int = 8
    active_hours_end:    int = 22
    search_terms:        str = ""

class ConfigRequest(BaseModel):
    max_apply_per_cycle: int = 15
    cycle_wait_minutes:  int = 45
    active_hours_start:  int = 8
    active_hours_end:    int = 22
    search_terms:        str = ""

# ── Broadcast de logs para todos os WebSocket conectados ──────
async def broadcast_log(msg: str):
    # Salva no buffer
    ts_msg = f"[{datetime.now().strftime('%H:%M:%S')}] {msg}"
    state.log_buffer.append(ts_msg)
    if len(state.log_buffer) > 100:
        state.log_buffer.pop(0)

    dead = []
    for ws in state.log_subscribers:
        try:
            await ws.send_text(ts_msg)
        except Exception:
            dead.append(ws)
    for ws in dead:
        if ws in state.log_subscribers:
            state.log_subscribers.remove(ws)

def sync_broadcast(msg: str):
    """Chama broadcast de threads síncronas (ex: reader do subprocess)"""
    try:
        loop = asyncio.get_event_loop()
        if loop.is_running():
            asyncio.run_coroutine_threadsafe(broadcast_log(msg), loop)
    except Exception:
        pass

# ── Lê stdout/stderr do subprocess e envia via WebSocket ─────
def stream_process_output(proc: subprocess.Popen):
    sync_broadcast("⚙️ Iniciando leitura de fluxo do processo...")
    while True:
        line = proc.stdout.readline()
        if not line and proc.poll() is not None:
            break
        if line:
            decoded = line.decode("utf-8", errors="replace").strip()
            if decoded:
                sync_broadcast(decoded)
    
    return_code = proc.wait()
    state.status = "stopped"
    sync_broadcast(f"⏹ Processo encerrado (Exit Code: {return_code})")

# ── Endpoints REST ────────────────────────────────────────────
@app.get("/crawler/status")
def get_status():
    # Verifica último heartbeat no banco
    last_hb = None
    try:
        conn = psycopg2.connect(os.environ["WEBCRAWLER_DB_CONNECTION"])
        cur = conn.cursor()
        cur.execute("SELECT last_heartbeat FROM crawler_jobs ORDER BY last_heartbeat DESC LIMIT 1")
        row = cur.fetchone()
        if row and row[0]:
            last_hb = row[0].isoformat()
            # Se heartbeat < 5 min → considera online
            delta = (datetime.utcnow() - row[0]).total_seconds() / 60
            if state.status != "running" and delta < 5:
                state.status = "running"
        conn.close()
    except Exception as e:
        last_hb = f"DB error: {e}"

    return {
        "status":         state.status,
        "pid":            state.process.pid if state.process else None,
        "last_heartbeat": last_hb,
        "config":         state.config
    }

@app.post("/crawler/start")
async def start_crawler(req: StartRequest):
    if state.status == "running":
        return {"ok": False, "msg": "Já está rodando."}

    # Atualiza config
    state.config.update({
        "max_apply_per_cycle": req.max_apply_per_cycle,
        "cycle_wait_minutes":  req.cycle_wait_minutes,
        "active_hours_start":  req.active_hours_start,
        "active_hours_end":    req.active_hours_end,
        "search_terms":        req.search_terms or state.config["search_terms"]
    })

    # Monta variáveis de ambiente para o processo C#
    env = os.environ.copy()
    env["WEBCRAWLER_MAX_APPLY_PER_CYCLE"] = str(req.max_apply_per_cycle)
    env["WEBCRAWLER_CYCLE_WAIT_MINUTES"]  = str(req.cycle_wait_minutes)
    env["WEBCRAWLER_ACTIVE_HOURS_START"]  = str(req.active_hours_start)
    env["WEBCRAWLER_ACTIVE_HOURS_END"]    = str(req.active_hours_end)
    if req.search_terms:
        env["WEBCRAWLER_JOB_SEARCH_TERMS"] = req.search_terms

    try:
        # O caminho correto dentro do container Docker é /app/crawler/
        proc = subprocess.Popen(
            ["dotnet", "/app/crawler/WebCrawler.dll"], 
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            env=env,
            bufsize=1,
            universal_newlines=False # Garante leitura binária para não dar erro de encoding
        )
        state.process = proc
        state.status  = "running"

        # Thread que lê a saída e faz broadcast
        t = threading.Thread(target=stream_process_output, args=(proc,), daemon=True)
        t.start()

        await broadcast_log("▶ Crawler iniciado! PID: " + str(proc.pid))
        return {"ok": True, "pid": proc.pid}
    except FileNotFoundError:
        state.status = "error"
        return {"ok": False, "msg": "dotnet não encontrado no container."}
    except Exception as e:
        state.status = "error"
        return {"ok": False, "msg": str(e)}

@app.post("/crawler/stop")
async def stop_crawler():
    if not state.process or state.status != "running":
        return {"ok": False, "msg": "Não está rodando."}
    try:
        state.process.send_signal(signal.SIGTERM)
        state.process.wait(timeout=10)
    except Exception:
        state.process.kill()
    state.status = "stopped"
    await broadcast_log("⏹ Sinal STOP enviado ao crawler.")
    return {"ok": True}

@app.post("/crawler/config")
async def update_config(req: ConfigRequest):
    state.config.update(req.dict())
    await broadcast_log("⚙️  Configuração atualizada pelo painel Qt.")
    return {"ok": True, "config": state.config}

# ── WebSocket para streaming de logs ─────────────────────────
@app.websocket("/ws/logs")
async def ws_logs(ws: WebSocket):
    await ws.accept()
    state.log_subscribers.append(ws)
    
    # Envia histórico de logs
    for log_msg in state.log_buffer:
        await ws.send_text(log_msg)
        
    await ws.send_text("✅ Conectado ao servidor LinkDim. Aguardando novos logs...")
    try:
        while True:
            await asyncio.sleep(30)
            await ws.send_text("♥ ping")  # keepalive
    except WebSocketDisconnect:
        if ws in state.log_subscribers:
            state.log_subscribers.remove(ws)

# ── Healthcheck (Render chama isso para manter online) ───────
@app.get("/health")
def health():
    return {"ok": True, "time": datetime.utcnow().isoformat()}

@app.get("/")
def root():
    return {"service": "LinkDim Crawler API", "status": state.status}
