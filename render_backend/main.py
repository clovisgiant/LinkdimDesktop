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
    sync_broadcast("🚀 [SISTEMA] Conectado ao fluxo de saída do robô. Aguardando mensagens...")
    
    # Lendo linha por linha em tempo real
    for line in iter(proc.stdout.readline, ""):
        if line:
            sync_broadcast(line.strip())
        if proc.poll() is not None:
            break
            
    return_code = proc.wait()
    state.status = "stopped"
    sync_broadcast(f"🏁 [SISTEMA] Robô finalizou a execução. (Código: {return_code})")

# ── Endpoints REST ────────────────────────────────────────────
@app.get("/crawler/status")
def get_status():
    # Verifica último heartbeat no banco
    last_hb = None
    conn_str = os.environ.get("WEBCRAWLER_DB_CONNECTION")
    
    if not conn_str:
        last_hb = "Erro: Variável WEBCRAWLER_DB_CONNECTION não configurada no Render."
        return {"status": state.status, "pid": state.process.pid if state.process else None, "last_heartbeat": last_hb, "config": state.config}

    def connect_to_db(uri):
        if not uri: return None, "URI vazia"
        uri = uri.strip().strip('"').strip("'")
        errs = []
        
        # Tentativa 1: Direta
        try:
            return psycopg2.connect(uri), None
        except Exception as e:
            errs.append(str(e))
            
        # Tentativa 2: Manual DSN
        if "://" in uri:
            try:
                import urllib.parse
                clean_url = uri.replace("postgres://", "postgresql://")
                res = urllib.parse.urlparse(clean_url)
                dsn = f"host={res.hostname} port={res.port or 5432} dbname={res.path[1:]} user={res.username} password={res.password} sslmode=require"
                return psycopg2.connect(dsn), None
            except Exception as e:
                errs.append(f"DSN Fallback Error: {e}")
                
        return None, " | ".join(errs)

    conn, db_err = connect_to_db(conn_str)
    try:
        if conn:
            cur = conn.cursor()
            cur.execute("SELECT last_heartbeat FROM crawler_jobs ORDER BY last_heartbeat DESC LIMIT 1")
            row = cur.fetchone()
            if row and row[0]:
                last_hb = row[0].isoformat()
                delta = (datetime.utcnow() - row[0]).total_seconds() / 60
                if delta < 5:
                    state.status = "running"
            conn.close()
        else:
            last_hb = f"Erro de Conexão: {db_err}"
    except Exception as e:
        last_hb = f"Erro de Operação: {e}"

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
        # Passa a conexão limpa para o C#
        raw_conn = os.environ.get("WEBCRAWLER_DB_CONNECTION", "")
        clean_conn = raw_conn.strip().strip('"').strip("'")
        env["WEBCRAWLER_DB_CONNECTION"] = clean_conn
        
        await broadcast_log("🔍 Preparando inicialização do robô...")
        
        proc = subprocess.Popen(
            ["dotnet", "/app/crawler/WebCrawler.dll"], 
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            env=env,
            bufsize=1,
            universal_newlines=True
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
            await asyncio.sleep(60)
            # Ping silencioso (sem texto no log)
            try: await ws.send_bytes(b"")
            except: break
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
