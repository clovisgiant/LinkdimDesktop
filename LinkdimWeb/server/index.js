import cors from "cors";
import dotenv from "dotenv";
import express from "express";
import fs from "node:fs";
import { execSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import mysql from "mysql2/promise";
import pg from "pg";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

dotenv.config({ path: path.resolve(__dirname, "..", ".env") });
dotenv.config({ path: path.resolve(__dirname, "..", "..", ".env"), override: false });

const apiPort = Number(process.env.APPLINKDIN_API_PORT || process.env.PORT || 4000);
const clientDistPath = path.resolve(__dirname, "..", "dist");

// Detecção do Banco de Dados: MySQL ou PostgreSQL
const dbType = (process.env.DB_TYPE || (process.env.WEBCRAWLER_MYSQL_CONNECTION ? "mysql" : "postgres")).toLowerCase();

console.log(`[BANCO] Provedor de Banco de Dados selecionado: ${dbType.toUpperCase()}`);

// -------------------------------------------------------------
// Configuração do MySQL (via WEBCRAWLER_MYSQL_CONNECTION ou DB_*)
// -------------------------------------------------------------
function parseMySqlConnection(raw) {
  const defaultConn = {
    host: process.env.DB_HOST || "localhost",
    port: Number(process.env.DB_PORT || 3306),
    database: process.env.DB_NAME || "",
    user: process.env.DB_USER || "",
    password: process.env.DB_PASSWORD || "",
    waitForConnections: true,
    connectionLimit: 10,
    queueLimit: 0,
    connectTimeout: 15000
  };

  const str = (raw || "").trim();
  if (!str) return defaultConn;

  if (str.startsWith("mysql://")) {
    try {
      const url = new URL(str);
      return {
        host: url.hostname,
        port: Number(url.port || 3306),
        database: url.pathname.replace(/^\//, ""),
        user: decodeURIComponent(url.username),
        password: decodeURIComponent(url.password),
        waitForConnections: true,
        connectionLimit: 10,
        queueLimit: 0,
        connectTimeout: 15000
      };
    } catch {
      return defaultConn;
    }
  }

  const map = {};
  for (const part of str.split(";")) {
    if (!part || !part.includes("=")) continue;
    const [k, ...v] = part.split("=");
    map[k.trim().toLowerCase()] = v.join("=").trim();
  }

  return {
    host: map.server || map.host || map["data source"] || defaultConn.host,
    port: Number(map.port || defaultConn.port),
    database: map.database || map["initial catalog"] || defaultConn.database,
    user: map["user id"] || map.user || map.username || map.uid || defaultConn.user,
    password: map.password || map.pwd || defaultConn.password,
    waitForConnections: true,
    connectionLimit: 10,
    queueLimit: 0,
    connectTimeout: 15000
  };
}

// -------------------------------------------------------------
// Configuração do PostgreSQL
// -------------------------------------------------------------
function parsePostgresConnection(raw) {
  const defaultConn = {
    host: process.env.PGHOST || "localhost",
    port: Number(process.env.PGPORT || 5432),
    database: process.env.PGDATABASE || "ProjetoDb",
    user: process.env.PGUSER || "postgres",
    password: process.env.PGPASSWORD || ""
  };

  const str = (raw || "").trim();
  if (!str) return defaultConn;

  if (str.startsWith("postgres://") || str.startsWith("postgresql://")) {
    return { connectionString: str };
  }

  const map = {};
  for (const part of str.split(";")) {
    if (!part || !part.includes("=")) continue;
    const [k, ...v] = part.split("=");
    map[k.trim().toLowerCase()] = v.join("=").trim();
  }

  return {
    host: map.host || map.server || defaultConn.host,
    port: Number(map.port || defaultConn.port),
    database: map.database || defaultConn.database,
    user: map.username || map.user || defaultConn.user,
    password: map.password || defaultConn.password,
    ssl: map.sslmode && map.sslmode.toLowerCase() !== "disable"
  };
}

let mysqlPool = null;
let pgPool = null;

if (dbType === "mysql") {
  const mysqlConfig = parseMySqlConnection(process.env.WEBCRAWLER_MYSQL_CONNECTION);
  mysqlPool = mysql.createPool(mysqlConfig);
  console.log(`[BANCO] MySQL Pool criado para ${mysqlConfig.user}@${mysqlConfig.host}:${mysqlConfig.port}/${mysqlConfig.database}`);
} else {
  const pgConfig = parsePostgresConnection(process.env.WEBCRAWLER_DB_CONNECTION);
  pgPool = new pg.Pool(pgConfig);
  console.log(`[BANCO] PostgreSQL Pool criado.`);
}

async function queryRows(sqlText, values = []) {
  if (dbType === "mysql") {
    const [rows] = await mysqlPool.query(sqlText, values);
    return rows;
  } else {
    const { rows } = await pgPool.query(sqlText, values);
    return rows;
  }
}

const app = express();
app.use(cors());
app.use(express.json());
app.use("/api", (_req, res, next) => {
  res.set({
    "Cache-Control": "no-store, no-cache, must-revalidate, proxy-revalidate",
    Pragma: "no-cache",
    Expires: "0",
    "Surrogate-Control": "no-store"
  });
  next();
});

function normalizeSummary(row) {
  if (!row) {
    return {
      total: 0,
      sucesso: 0,
      enviada_sem_confirmacao: 0,
      indisponiveis: 0,
      pendentes: 0
    };
  }

  return {
    total: Number(row.total || 0),
    sucesso: Number(row.sucesso || 0),
    enviada_sem_confirmacao: Number(row.enviada_sem_confirmacao || 0),
    indisponiveis: Number(row.indisponiveis || 0),
    pendentes: Number(row.pendentes || 0)
  };
}

function normalizeTimeline(rows) {
  if (!Array.isArray(rows)) {
    return [];
  }

  return rows
    .map((row) => {
      const date = new Date(row?.day ?? "");
      if (Number.isNaN(date.getTime())) {
        return null;
      }

      return {
        day: date.toISOString(),
        total: Number(row?.total || 0),
        sucesso: Number(row?.sucesso || 0),
        indisponiveis: Number(row?.indisponiveis || 0),
        pendentes: Number(row?.pendentes || 0)
      };
    })
    .filter(Boolean)
    .sort((a, b) => new Date(a.day).getTime() - new Date(b.day).getTime());
}

function isCrawlerRunning() {
  if (process.platform !== "win32") {
    return false;
  }

  try {
    const out = execSync('tasklist /FI "IMAGENAME eq WebCrawler.exe" /NH /FO CSV', { timeout: 5000, windowsHide: true }).toString();
    return out.toLowerCase().includes("webcrawler.exe");
  } catch {
    return false;
  }
}

function normalizeCrawlerState(value) {
  const normalized = (value || "").trim().toLowerCase();
  if (normalized === "waiting") return "waiting";
  if (normalized === "running") return "running";
  return null;
}

async function readCrawlerHeartbeat() {
  try {
    const rows = await queryRows(`
      SELECT state, detail, is_running, process_id, host_name, started_at, last_heartbeat, updated_at
      FROM crawler_runtime_status
      WHERE instance_name = 'default'
      LIMIT 1;
    `);

    return rows[0] ?? null;
  } catch {
    return null;
  }
}

app.get("/api/crawler-status", async (_req, res) => {
  try {
    const tasklistRunning = isCrawlerRunning();

    let lastActivity = null;
    try {
      const rows = await queryRows(`
        SELECT MAX(happened_at) AS last_activity FROM (
          SELECT MAX(criado_em) AS happened_at FROM candidatura_etapas
          UNION ALL
          SELECT MAX(data_insercao) AS happened_at FROM vagas
          UNION ALL
          SELECT MAX(last_heartbeat) AS happened_at FROM crawler_runtime_status WHERE instance_name = 'default'
        ) AS activity_events;
      `);
      lastActivity = rows[0]?.last_activity ?? null;
    } catch { /* banco pode estar inacessivel */ }

    const heartbeat = await readCrawlerHeartbeat();

    const minutesSince = lastActivity
      ? (Date.now() - new Date(lastActivity).getTime()) / 60000
      : null;

    const heartbeatState = normalizeCrawlerState(heartbeat?.state);
    const heartbeatMinutesSince = heartbeat?.last_heartbeat
      ? (Date.now() - new Date(heartbeat.last_heartbeat).getTime()) / 60000
      : null;

    const heartbeatFresh = heartbeatMinutesSince !== null && heartbeatMinutesSince <= 3.5;

    let state;
    if (heartbeatFresh && heartbeatState) {
      state = heartbeatState;
    } else if (tasklistRunning) {
      state = minutesSince !== null && minutesSince <= 3 ? "running" : "waiting";
    } else if (minutesSince !== null && minutesSince <= 3) {
      state = "running";
    } else if (minutesSince !== null && minutesSince <= 60) {
      state = "waiting";
    } else {
      state = "offline";
    }

    res.json({
      state,
      running: tasklistRunning || heartbeatFresh,
      dbProvider: dbType,
      lastActivity,
      minutesSinceActivity: minutesSince !== null ? Math.round(minutesSince) : null,
      heartbeatState,
      heartbeatDetail: heartbeat?.detail ?? null,
      heartbeatAt: heartbeat?.last_heartbeat ?? null,
      minutesSinceHeartbeat: heartbeatMinutesSince !== null ? Math.round(heartbeatMinutesSince) : null
    });
  } catch (error) {
    res.json({ state: "offline", running: false, error: error.message });
  }
});

app.get("/api/health", async (_req, res) => {
  try {
    await queryRows("SELECT 1;");
    res.json({ ok: true, database: "connected", provider: dbType });
  } catch (error) {
    res.status(500).json({ ok: false, error: error.message });
  }
});

app.get("/api/dashboard", async (_req, res) => {
  try {
    let summaryQuery = "";
    let timelineQuery = "";

    if (dbType === "mysql") {
      summaryQuery = `
        SELECT
          COUNT(*) AS total,
          COALESCE(SUM(CASE WHEN candidatura_enviada_sucesso = 1 THEN 1 ELSE 0 END), 0) AS sucesso,
          COALESCE(SUM(
            CASE
              WHEN candidatura_enviada = 1
                AND (candidatura_enviada_sucesso IS NULL OR candidatura_enviada_sucesso = 0)
                AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
              THEN 1
              ELSE 0
            END
          ), 0) AS enviada_sem_confirmacao,
          COALESCE(SUM(CASE WHEN candidatura_indisponivel = 1 THEN 1 ELSE 0 END), 0) AS indisponiveis,
          COALESCE(SUM(
            CASE
              WHEN (candidatura_enviada IS NULL OR candidatura_enviada = 0)
                AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
              THEN 1
              ELSE 0
            END
          ), 0) AS pendentes
        FROM vagas;
      `;

      timelineQuery = `
        SELECT
          DATE(data_insercao) AS day,
          COUNT(*) AS total,
          COALESCE(SUM(CASE WHEN candidatura_enviada_sucesso = 1 THEN 1 ELSE 0 END), 0) AS sucesso,
          COALESCE(SUM(CASE WHEN candidatura_indisponivel = 1 THEN 1 ELSE 0 END), 0) AS indisponiveis,
          COALESCE(SUM(
            CASE
              WHEN (candidatura_enviada IS NULL OR candidatura_enviada = 0)
                AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
              THEN 1
              ELSE 0
            END
          ), 0) AS pendentes
        FROM vagas
        WHERE data_insercao >= DATE_SUB(NOW(), INTERVAL 60 DAY)
        GROUP BY DATE(data_insercao)
        ORDER BY day ASC;
      `;
    } else {
      summaryQuery = `
        SELECT
          COUNT(*) AS total,
          SUM(CASE WHEN candidatura_enviada_sucesso THEN 1 ELSE 0 END) AS sucesso,
          SUM(
            CASE
              WHEN candidatura_enviada
                AND NOT candidatura_enviada_sucesso
                AND NOT candidatura_indisponivel
              THEN 1
              ELSE 0
            END
          ) AS enviada_sem_confirmacao,
          SUM(CASE WHEN candidatura_indisponivel THEN 1 ELSE 0 END) AS indisponiveis,
          SUM(
            CASE
              WHEN NOT candidatura_enviada
                AND NOT candidatura_indisponivel
              THEN 1
              ELSE 0
            END
          ) AS pendentes
        FROM vagas;
      `;

      timelineQuery = `
        WITH bounds AS (
          SELECT
            COALESCE(DATE_TRUNC('day', MIN(data_insercao)), DATE_TRUNC('day', NOW()))::date AS first_day,
            DATE_TRUNC('day', NOW())::date AS last_day
          FROM vagas
        ),
        series AS (
          SELECT generate_series(
            GREATEST((SELECT first_day FROM bounds), ((SELECT last_day FROM bounds) - INTERVAL '59 day')::date),
            (SELECT last_day FROM bounds),
            INTERVAL '1 day'
          )::date AS day
        )
        SELECT
          series.day,
          (SELECT COUNT(*) FROM vagas WHERE DATE(data_insercao) <= series.day) AS total,
          (SELECT COUNT(*) FROM vagas WHERE candidatura_enviada_sucesso = TRUE AND DATE(COALESCE(data_envio_sucesso, data_candidatura, data_insercao)) <= series.day) AS sucesso,
          (SELECT COUNT(*) FROM vagas WHERE candidatura_indisponivel = TRUE AND DATE(COALESCE(data_indisponibilidade, data_candidatura, data_insercao)) <= series.day) AS indisponiveis,
          (SELECT COUNT(*) FROM vagas WHERE DATE(data_insercao) <= series.day AND NOT candidatura_indisponivel AND (NOT candidatura_enviada_sucesso OR DATE(COALESCE(data_envio_sucesso, data_candidatura, data_insercao)) > series.day)) AS pendentes
        FROM series
        ORDER BY series.day;
      `;
    }

    const [summaryRows, successfulJobs, unavailableJobs, pendingConfirmation, recentSteps, timelineRows] = await Promise.all([
      queryRows(summaryQuery),
      queryRows(`
        SELECT titulo, empresa, localizacao, link, data_envio_sucesso
        FROM vagas
        WHERE candidatura_enviada_sucesso = 1 OR candidatura_enviada_sucesso = TRUE
        ORDER BY data_envio_sucesso DESC
        LIMIT 50;
      `),
      queryRows(`
        SELECT titulo, empresa, localizacao, link, motivo_indisponibilidade, data_indisponibilidade
        FROM vagas
        WHERE candidatura_indisponivel = 1 OR candidatura_indisponivel = TRUE
        ORDER BY data_indisponibilidade DESC
        LIMIT 50;
      `),
      queryRows(`
        SELECT titulo, empresa, localizacao, link, data_candidatura
        FROM vagas
        WHERE (candidatura_enviada = 1 OR candidatura_enviada = TRUE)
          AND (candidatura_enviada_sucesso IS NULL OR candidatura_enviada_sucesso = 0 OR candidatura_enviada_sucesso = FALSE)
          AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0 OR candidatura_indisponivel = FALSE)
        ORDER BY data_candidatura DESC
        LIMIT 50;
      `),
      queryRows(`
        SELECT link, etapa, sucesso, detalhe, criado_em
        FROM candidatura_etapas
        ORDER BY criado_em DESC
        LIMIT 80;
      `),
      queryRows(timelineQuery)
    ]);

    res.json({
      generatedAt: new Date().toISOString(),
      provider: dbType,
      summary: normalizeSummary(summaryRows[0]),
      successfulJobs,
      unavailableJobs,
      pendingConfirmation,
      recentSteps,
      timeline: normalizeTimeline(timelineRows)
    });
  } catch (error) {
    console.error("[API] dashboard error", error);
    res.status(500).json({
      message: "Nao foi possivel carregar o dashboard no momento.",
      error: error.message
    });
  }
});

// Arquivos estáticos do frontend React compilado
if (fs.existsSync(clientDistPath)) {
  app.use(express.static(clientDistPath));

  app.get("*", (req, res, next) => {
    if (req.path.startsWith("/api/")) {
      next();
      return;
    }

    res.sendFile(path.join(clientDistPath, "index.html"));
  });
}

const server = app.listen(apiPort, () => {
  console.log(`[API] Dashboard Web LinkDim online em http://localhost:${apiPort}`);
  console.log(`[API] Conectado ao banco: ${dbType.toUpperCase()}`);
});

async function closeGracefully(signal) {
  console.log(`[API] Encerrando por ${signal}...`);
  server.close(async () => {
    if (mysqlPool) await mysqlPool.end();
    if (pgPool) await pgPool.end();
    process.exit(0);
  });
}

process.on("SIGINT", () => closeGracefully("SIGINT"));
process.on("SIGTERM", () => closeGracefully("SIGTERM"));
