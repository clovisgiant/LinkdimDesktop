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
    
    # Thread para ler logs em tempo real
    def stream_logs():
        try:
            # Lê stdout e stderr (combinados via subprocess.STDOUT)
            for line in iter(proc.stdout.readline, ""):
                if line:
                    clean_line = line.strip()
                    sync_broadcast(clean_line)
            
            # Se o loop acabar, o processo terminou
            rc = proc.wait()
            sync_broadcast(f"⚠️ [C#] Processo encerrado. Código de saída: {rc}")
        except Exception as e:
            sync_broadcast(f"❌ [Orquestrador] Erro ao ler logs: {e}")
        finally:
            state.process = None
            state.status = "stopped"
            sync_broadcast("ℹ️ Crawler parado.")

    thread = threading.Thread(target=stream_logs, daemon=True)
    thread.start()

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
        
        # Limpeza profunda (remove aspas, espaços e caracteres de escape)
        uri = uri.strip().strip("'").strip('"').replace("\\n", "").replace("\\r", "")
        
        # Log de diagnóstico (seguro: oculta senha)
        masked_uri = uri
        if "@" in uri:
            parts = uri.split("@")
            prefix = parts[0].split("://")
            if len(prefix) > 1:
                masked_uri = f"{prefix[0]}://***:***@{parts[1]}"
        
        errs = []
        sync_broadcast(f"🔍 [DB] Tentando conectar ao banco: {masked_uri[:60]}...")

        # Garantir prefixo correto para psycopg2
        if uri.startswith("postgres://"):
            uri = uri.replace("postgres://", "postgresql://", 1)
        
        # Tentativa 1: Conexão Direta (LibPQ)
        try:
            return psycopg2.connect(uri, connect_timeout=10), None
        except Exception as e:
            errs.append(f"Erro URI: {str(e)}")

        # Tentativa 2: Parsing Manual (Blindagem contra erros de DSN do LibPQ)
        if "://" in uri:
            try:
                import urllib.parse
                res = urllib.parse.urlparse(uri)
                # Extrai componentes manualmente para evitar o erro "missing =" do libpq
                conn = psycopg2.connect(
                    host=res.hostname,
                    port=res.port or 5432,
                    database=res.path[1:],
                    user=res.username,
                    password=res.password,
                    connect_timeout=10,
                    sslmode="require"
                )
                return conn, None
            except Exception as e:
                errs.append(f"Erro Manual: {str(e)}")

        return None, " | ".join(errs)

    conn, db_err = connect_to_db(conn_str)
    try:
        if conn:
            cur = conn.cursor()
            # Garante que a tabela de status existe (caso o C# ainda não tenha rodado)
            cur.execute("""
                CREATE TABLE IF NOT EXISTS crawler_runtime_status (
                    instance_name TEXT PRIMARY KEY,
                    state TEXT NOT NULL,
                    detail TEXT NULL,
                    is_running BOOLEAN NOT NULL DEFAULT TRUE,
                    process_id INTEGER NULL,
                    host_name TEXT NULL,
                    started_at TIMESTAMP NOT NULL DEFAULT NOW(),
                    last_heartbeat TIMESTAMP NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
                );
            """)
            conn.commit()

            # Consulta o status
            cur.execute("SELECT state, last_heartbeat, is_running FROM crawler_runtime_status WHERE instance_name = 'default' LIMIT 1")
            row = cur.fetchone()
            if row:
                st, lb, is_run = row
                last_hb = lb.isoformat() if lb else "N/A"
                if is_run:
                    state.status = "running"
                else:
                    state.status = "stopped"
            else:
                last_hb = "Aguardando primeira execução do robô..."
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
        crawler_dir = "/app/crawler"
        dll_path = os.path.join(crawler_dir, "WebCrawler.dll")
        
        # Passa a conexão limpa para o C#
        raw_conn = os.environ.get("WEBCRAWLER_DB_CONNECTION", "")
        clean_conn = raw_conn.strip().replace('"', '').replace("'", "")
        
        # O robô C# (Npgsql) pode precisar da URL convertida ou limpa
        env["WEBCRAWLER_DB_CONNECTION"] = clean_conn
        
        # 3. Verifica se o executável nativo existe (mais estável)
        binary_path = os.path.join(crawler_dir, "WebCrawler")
        cmd = ["dotnet", dll_path]
        
        if os.path.exists(binary_path):
            await broadcast_log(f"✅ Executável nativo encontrado em {binary_path}. Usando execução direta.")
            os.chmod(binary_path, 0o755) # Garante permissão de execução
            cmd = [binary_path]
        elif not os.path.exists(dll_path):
            await broadcast_log(f"❌ ERRO CRÍTICO: {dll_path} não encontrado!")
            state.status = "error"
            return {"ok": False, "msg": "Binário do robô não encontrado."}

        await broadcast_log(f"🚀 Lançando processo: {' '.join(cmd)}")
        
        # Variáveis extras para estabilidade do .NET no Docker/Linux
        env["DOTNET_RUNNING_IN_CONTAINER"] = "true"
        env["COMPlus_EnableDiagnostics"] = "0"
        
        proc = subprocess.Popen(
            cmd, 
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            env=env,
            cwd=crawler_dir,
            bufsize=1,
            universal_newlines=True,
            shell=False
        )
        state.process = proc
        state.status  = "running"

        # Thread que lê a saída e faz broadcast
        t = threading.Thread(target=stream_process_output, args=(proc,), daemon=True)
        t.start()

        await broadcast_log(f"▶ Robô em execução (PID: {proc.pid})")
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

@app.get("/crawler/test")
async def test_env():
    results = {}
    # Teste .NET
    try:
        dn = subprocess.check_output(["dotnet", "--version"], text=True).strip()
        results["dotnet"] = f"OK ({dn})"
    except: results["dotnet"] = "ERRO"
    
    # Teste Chrome
    try:
        cv = subprocess.check_output(["chromium", "--version"], text=True).strip()
        results["chrome"] = f"OK ({cv})"
    except: results["chrome"] = "ERRO (Chromium não encontrado)"

    # Teste DB
    conn_str = os.environ.get("WEBCRAWLER_DB_CONNECTION")
    conn, err = connect_to_db(conn_str)
    if conn:
        results["database"] = "OK (Conectado)"
        conn.close()
    else:
        results["database"] = f"ERRO ({err})"

    await broadcast_log(f"📋 [DIAGNÓSTICO COMPLETO] .NET: {results['dotnet']} | Chrome: {results['chrome']} | DB: {results['database']}")
    return {"ok": True, "results": results}

@app.get("/health")
def health():
    return {"ok": True, "time": datetime.utcnow().isoformat()}

@app.get("/")
def root():
    return {"service": "LinkDim Crawler API", "status": state.status}
