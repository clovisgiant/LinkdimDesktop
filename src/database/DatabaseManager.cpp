#include "DatabaseManager.h"
#include <QSqlError>
#include <QSqlRecord>
#include <QRegularExpression>
#include <QUrl>
#include <QUrlQuery>
#include <QDebug>

// ============================================================
// DatabaseManager — Implementação completa
// Tradução fiel do Program.Database.cs (C#/Npgsql)
// ============================================================

DatabaseManager::DatabaseManager(const QString& connectionString)
    : m_connStr(connectionString)
{
    // Cada instância tem seu próprio nome de conexão único
    static int s_connCounter = 0;
    QString connName = QStringLiteral("linkdim_db_%1").arg(++s_connCounter);
    m_db = QSqlDatabase::addDatabase("QPSQL", connName);
}

DatabaseManager::~DatabaseManager()
{
    if (m_db.isOpen())
        m_db.close();
    QString connName = m_db.connectionName();
    m_db = QSqlDatabase(); // desassocia antes de remover
    QSqlDatabase::removeDatabase(connName);
}

// ── Parseia "postgresql://user:pass@host:5432/dbname" ──────
bool DatabaseManager::parseConnectionString(
    const QString& connStr,
    QString& host, int& port,
    QString& dbName, QString& user, QString& password)
{
    // Suporte ao formato padrão: postgresql://user:pass@host:port/db
    QUrl url(connStr);
    if (url.isValid() && (url.scheme() == "postgresql" || url.scheme() == "postgres")) {
        host     = url.host();
        port     = url.port(5432);
        dbName   = url.path().mid(1); // remove a barra inicial
        user     = url.userName();
        password = url.password();
        return true;
    }

    // Suporte ao formato key=value: "Host=...;Port=...;Database=...;Username=...;Password=..."
    QStringList parts = connStr.split(QRegularExpression("[;]"));
    for (const QString& part : parts) {
        int eq = part.indexOf('=');
        if (eq <= 0) continue;
        QString k = part.left(eq).trimmed().toLower();
        QString v = part.mid(eq + 1).trimmed();
        if (k == "host" || k == "server")          host     = v;
        else if (k == "port")                       port     = v.toInt();
        else if (k == "database" || k == "db")      dbName   = v;
        else if (k == "username" || k == "user id" || k == "uid") user = v;
        else if (k == "password" || k == "pwd")     password = v;
    }
    return !host.isEmpty() && !dbName.isEmpty();
}

// ── Conecta e valida (ValidateDatabaseConnection) ──────────
bool DatabaseManager::connectAndValidate()
{
    QString host, dbName, user, password;
    int port = 5432;

    if (!parseConnectionString(m_connStr, host, port, dbName, user, password)) {
        m_lastError = "Connection string inválida: " + m_connStr;
        qWarning() << "[BANCO]" << m_lastError;
        return false;
    }

    m_db.setHostName(host);
    m_db.setPort(port);
    m_db.setDatabaseName(dbName);
    m_db.setUserName(user);
    m_db.setPassword(password);
    // Habilita SSL se Render.com (sslmode=require)
    m_db.setConnectOptions("sslmode=require");

    if (!m_db.open()) {
        // Tenta sem SSL como fallback (local)
        m_db.setConnectOptions("");
        if (!m_db.open()) {
            m_lastError = m_db.lastError().text();
            qWarning() << "[BANCO] Falha ao conectar PostgreSQL:" << m_lastError;
            return false;
        }
    }

    // Validação simples: SELECT 1
    QSqlQuery q(m_db);
    if (!q.exec("SELECT 1")) {
        m_lastError = q.lastError().text();
        qWarning() << "[BANCO] Falha na validação SELECT 1:" << m_lastError;
        m_db.close();
        return false;
    }

    m_connected = true;
    qDebug() << "[BANCO] Conexão com PostgreSQL validada com sucesso.";
    return true;
}

bool DatabaseManager::isConnected() const { return m_connected && m_db.isOpen(); }
QString DatabaseManager::lastError()  const { return m_lastError; }

// ── Executa SQL sem retorno ─────────────────────────────────
bool DatabaseManager::execQuery(const QString& sql)
{
    QSqlQuery q(m_db);
    if (!q.exec(sql)) {
        m_lastError = q.lastError().text();
        qWarning() << "[BANCO] Erro SQL:" << m_lastError;
        return false;
    }
    return true;
}

// ── EnsureJobsSchema ────────────────────────────────────────
bool DatabaseManager::ensureJobsSchema()
{
    return execQuery(R"(
        CREATE TABLE IF NOT EXISTS vagas (
            id                       BIGSERIAL PRIMARY KEY,
            titulo                   TEXT NOT NULL,
            empresa                  TEXT NOT NULL,
            localizacao              TEXT NOT NULL,
            link                     TEXT NOT NULL UNIQUE,
            data_insercao            TIMESTAMP NOT NULL DEFAULT NOW(),
            candidatura_simplificada BOOLEAN DEFAULT FALSE,
            candidatura_enviada      BOOLEAN DEFAULT FALSE,
            data_candidatura         TIMESTAMP NULL,
            candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE,
            data_envio_sucesso       TIMESTAMP NULL,
            candidatura_indisponivel BOOLEAN DEFAULT FALSE,
            motivo_indisponibilidade TEXT NULL,
            data_indisponibilidade   TIMESTAMP NULL
        )
    )");
}

// ── EnsureSuccessTrackingColumns ────────────────────────────
bool DatabaseManager::ensureSuccessTrackingColumns()
{
    return execQuery(R"(
        ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE;
        ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_envio_sucesso TIMESTAMP NULL;
    )");
}

// ── EnsureUnavailableTrackingColumns ───────────────────────
bool DatabaseManager::ensureUnavailableTrackingColumns()
{
    return execQuery(R"(
        ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_indisponivel BOOLEAN DEFAULT FALSE;
        ALTER TABLE vagas ADD COLUMN IF NOT EXISTS motivo_indisponibilidade TEXT NULL;
        ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_indisponibilidade TIMESTAMP NULL;
    )");
}

// ── EnsureApplicationTrackingSchema ────────────────────────
bool DatabaseManager::ensureApplicationTrackingSchema()
{
    bool ok = execQuery(R"(
        CREATE TABLE IF NOT EXISTS candidatura_etapas (
            id              BIGSERIAL PRIMARY KEY,
            link            TEXT NOT NULL,
            etapa           TEXT NOT NULL,
            sucesso         BOOLEAN NOT NULL,
            detalhe         TEXT NULL,
            html_path       TEXT NULL,
            screenshot_path TEXT NULL,
            criado_em       TIMESTAMP NOT NULL DEFAULT NOW()
        )
    )");
    if (ok) {
        ok = execQuery(R"(
            CREATE INDEX IF NOT EXISTS idx_candidatura_etapas_link_criado_em
            ON candidatura_etapas (link, criado_em DESC)
        )");
    }
    return ok;
}

// ── ensureFullSchema — chama todos os ensures ───────────────
bool DatabaseManager::ensureFullSchema()
{
    bool ok = true;
    ok &= ensureJobsSchema();
    ok &= execQuery("ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_simplificada BOOLEAN");
    ok &= execQuery("ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE");
    ok &= execQuery("ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL");
    ok &= ensureSuccessTrackingColumns();
    ok &= ensureUnavailableTrackingColumns();
    ok &= ensureApplicationTrackingSchema();
    return ok;
}

// ── normalizeLink — equivalente a NormalizeLinkedInJobLink ──
QString DatabaseManager::normalizeLink(const QString& rawLink)
{
    if (rawLink.trimmed().isEmpty()) return {};

    // Remove tracking params, força https, remove trailing slash
    QUrl url(rawLink.trimmed());
    if (!url.isValid()) return rawLink.trimmed();

    // Mantém apenas path limpo sem query
    QString path = url.path();
    while (path.endsWith('/')) path.chop(1);

    QUrl clean;
    clean.setScheme("https");
    clean.setHost("www.linkedin.com");
    clean.setPath(path);
    return clean.toString();
}

// ── extractJobId — equivalente a ExtractLinkedInJobId ───────
QString DatabaseManager::extractJobId(const QString& rawLink)
{
    QUrl url(rawLink.trimmed());
    if (!url.isValid()) return {};

    QStringList segments = url.path().split('/', Qt::SkipEmptyParts);
    for (int i = 0; i < segments.size() - 2; ++i) {
        if (segments[i].compare("jobs",  Qt::CaseInsensitive) == 0 &&
            segments[i+1].compare("view", Qt::CaseInsensitive) == 0) {
            return segments[i+2].trimmed();
        }
    }
    return {};
}

// ── SaveCollectedJobsToDatabase ─────────────────────────────
bool DatabaseManager::saveJobs(const QList<JobData>& jobs)
{
    if (!isConnected()) return false;

    ensureFullSchema();

    int saved = 0;
    for (const JobData& job : jobs) {
        QString normLink = normalizeLink(job.link);
        if (normLink.isEmpty()) continue;

        QSqlQuery q(m_db);
        q.prepare(R"(
            INSERT INTO vagas
                (titulo, empresa, localizacao, link, data_insercao,
                 candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso)
            VALUES
                (:titulo, :empresa, :localizacao, :link, NOW(),
                 TRUE, FALSE, FALSE)
            ON CONFLICT (link) DO NOTHING
        )");
        q.bindValue(":titulo",    job.titulo.isEmpty()    ? "(sem titulo)"    : job.titulo);
        q.bindValue(":empresa",   job.empresa.isEmpty()   ? "(sem empresa)"   : job.empresa);
        q.bindValue(":localizacao", job.localizacao.isEmpty() ? "(sem localizacao)" : job.localizacao);
        q.bindValue(":link",      normLink);

        if (q.exec()) ++saved;
        else qWarning() << "[BANCO] Erro ao inserir vaga:" << q.lastError().text();
    }

    qDebug() << "[BANCO]" << saved << "vagas salvas no PostgreSQL (sem duplicidade).";
    return true;
}

// ── GetSimplifiedJobLinksFromDatabase ───────────────────────
QStringList DatabaseManager::getPendingJobLinks()
{
    QStringList links;
    if (!isConnected()) return links;

    ensureFullSchema();
    markExhaustedJobsAsUnavailable();

    // Carrega links já candidatados (para filtrar)
    QSet<QString> appliedLinks;
    QSet<QString> appliedJobIds;
    {
        QSqlQuery q(m_db);
        q.exec(R"(
            SELECT DISTINCT link FROM vagas
            WHERE (COALESCE(candidatura_enviada, FALSE) = TRUE
                OR COALESCE(candidatura_enviada_sucesso, FALSE) = TRUE)
              AND link IS NOT NULL AND link <> ''
        )");
        while (q.next()) {
            QString raw = q.value(0).toString();
            QString norm = normalizeLink(raw);
            if (!norm.isEmpty()) appliedLinks.insert(norm);
            QString jid = extractJobId(raw);
            if (!jid.isEmpty()) appliedJobIds.insert(jid);
        }
    }

    // Busca vagas pendentes (mesma query do C#)
    QSqlQuery q(m_db);
    q.exec(R"(
        SELECT link FROM vagas
        WHERE candidatura_simplificada = TRUE
          AND COALESCE(candidatura_enviada, FALSE) = FALSE
          AND COALESCE(candidatura_enviada_sucesso, FALSE) = FALSE
          AND COALESCE(candidatura_indisponivel, FALSE) = FALSE
          AND link IS NOT NULL AND link <> ''
        ORDER BY data_insercao DESC
    )");

    QSet<QString> seen;
    while (q.next()) {
        QString raw  = q.value(0).toString();
        if (raw.trimmed().isEmpty()) continue;

        QString norm = normalizeLink(raw);
        if (norm.isEmpty()) continue;
        if (appliedLinks.contains(norm)) continue;

        QString jid = extractJobId(norm);
        if (!jid.isEmpty() && appliedJobIds.contains(jid)) continue;

        if (!seen.contains(norm)) {
            seen.insert(norm);
            links << norm;
        }
    }

    return links;
}

// ── MarkJobAsApplied ────────────────────────────────────────
bool DatabaseManager::markJobAsApplied(const QString& link)
{
    if (!isConnected() || link.trimmed().isEmpty()) return false;

    QString norm   = normalizeLink(link);
    if (norm.isEmpty()) norm = link.trimmed();
    QString jobId  = extractJobId(norm);
    QString now    = "NOW()";

    ensureFullSchema();

    // UPDATE com todas as variações de link (igual ao C#)
    QSqlQuery q(m_db);
    q.prepare(R"(
        UPDATE vagas
        SET candidatura_enviada         = TRUE,
            data_candidatura            = NOW(),
            candidatura_enviada_sucesso = TRUE,
            data_envio_sucesso          = NOW(),
            candidatura_indisponivel    = FALSE,
            motivo_indisponibilidade    = NULL,
            data_indisponibilidade      = NULL
        WHERE link = :link
           OR link = :norm
           OR split_part(link, '?', 1) = split_part(:link2, '?', 1)
           OR split_part(link, '?', 1) = split_part(:norm2, '?', 1)
           OR (:job_id <> '' AND split_part(link, '?', 1) LIKE ('%/jobs/view/' || :job_id3 || '%'))
    )");
    q.bindValue(":link",   link.trimmed());
    q.bindValue(":norm",   norm);
    q.bindValue(":link2",  link.trimmed());
    q.bindValue(":norm2",  norm);
    q.bindValue(":job_id", jobId);
    q.bindValue(":job_id3",jobId);

    if (!q.exec()) {
        m_lastError = q.lastError().text();
        qWarning() << "[BANCO] Falha ao marcar candidatura:" << m_lastError;
        return false;
    }

    int affected = q.numRowsAffected();
    if (affected <= 0) {
        // UPSERT se não encontrou (mesmo comportamento do C#)
        QSqlQuery uq(m_db);
        uq.prepare(R"(
            INSERT INTO vagas
                (titulo, empresa, localizacao, link, data_insercao,
                 candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso,
                 data_candidatura, data_envio_sucesso, candidatura_indisponivel,
                 motivo_indisponibilidade, data_indisponibilidade)
            VALUES
                ('(titulo indisponivel)', '(empresa indisponivel)', '(localizacao indisponivel)',
                 :link, NOW(), TRUE, TRUE, TRUE, NOW(), NOW(), FALSE, NULL, NULL)
            ON CONFLICT (link) DO UPDATE
            SET candidatura_enviada         = TRUE,
                candidatura_enviada_sucesso = TRUE,
                data_candidatura            = NOW(),
                data_envio_sucesso          = NOW(),
                candidatura_indisponivel    = FALSE,
                motivo_indisponibilidade    = NULL,
                data_indisponibilidade      = NULL
        )");
        uq.bindValue(":link", norm);
        if (!uq.exec()) {
            qWarning() << "[BANCO] Upsert falhou:" << uq.lastError().text();
            return false;
        }
        affected = uq.numRowsAffected();
    }

    qDebug() << "[BANCO] candidatura_enviada atualizada para" << affected << "registro(s). link=" << norm;
    return affected > 0;
}

// ── MarkJobAsUnavailable ────────────────────────────────────
bool DatabaseManager::markJobAsUnavailable(const QString& link, const QString& reason)
{
    if (!isConnected() || link.trimmed().isEmpty()) return false;

    QString norm = normalizeLink(link);
    if (norm.isEmpty()) norm = link.trimmed();

    ensureJobsSchema();
    ensureUnavailableTrackingColumns();

    QSqlQuery q(m_db);
    q.prepare(R"(
        UPDATE vagas
        SET candidatura_indisponivel = TRUE,
            motivo_indisponibilidade = :motivo,
            data_indisponibilidade   = NOW()
        WHERE link = :link
           OR link = :norm
           OR split_part(link, '?', 1) = split_part(:link2, '?', 1)
           OR split_part(link, '?', 1) = split_part(:norm2, '?', 1)
    )");
    q.bindValue(":motivo", reason.isEmpty() ? "indisponivel" : reason);
    q.bindValue(":link",   link.trimmed());
    q.bindValue(":norm",   norm);
    q.bindValue(":link2",  link.trimmed());
    q.bindValue(":norm2",  norm);

    if (!q.exec()) {
        m_lastError = q.lastError().text();
        qWarning() << "[BANCO] Falha ao marcar vaga como indisponível:" << m_lastError;
        return false;
    }
    return true;
}

// ── LogApplicationStep ──────────────────────────────────────
bool DatabaseManager::logApplicationStep(
    const QString& link,
    const QString& etapa,
    bool sucesso,
    const QString& detalhe,
    const QString& htmlPath,
    const QString& screenshotPath)
{
    if (!isConnected()) return false;
    if (link.trimmed().isEmpty() || etapa.trimmed().isEmpty()) return false;

    ensureApplicationTrackingSchema();

    QSqlQuery q(m_db);
    q.prepare(R"(
        INSERT INTO candidatura_etapas
            (link, etapa, sucesso, detalhe, html_path, screenshot_path)
        VALUES
            (:link, :etapa, :sucesso, :detalhe, :html_path, :screenshot_path)
    )");
    q.bindValue(":link",            link.trimmed());
    q.bindValue(":etapa",           etapa.trimmed());
    q.bindValue(":sucesso",         sucesso);
    q.bindValue(":detalhe",         detalhe.isEmpty()       ? QVariant(QMetaType::fromType<QString>()) : QVariant(detalhe));
    q.bindValue(":html_path",       htmlPath.isEmpty()      ? QVariant(QMetaType::fromType<QString>()) : QVariant(htmlPath));
    q.bindValue(":screenshot_path", screenshotPath.isEmpty()? QVariant(QMetaType::fromType<QString>()) : QVariant(screenshotPath));

    if (!q.exec()) {
        m_lastError = q.lastError().text();
        qWarning() << "[BANCO] Falha ao registrar etapa" << etapa << ":" << m_lastError;
        return false;
    }
    return true;
}

// ── hasSuccessfulApplicationRecorded ───────────────────────
bool DatabaseManager::hasSuccessfulApplicationRecorded(const QString& link)
{
    if (!isConnected()) return false;

    QSqlQuery q(m_db);
    q.prepare(R"(
        SELECT COUNT(*) FROM vagas
        WHERE (link = :link OR link = :norm)
          AND COALESCE(candidatura_enviada_sucesso, FALSE) = TRUE
    )");
    q.bindValue(":link", link.trimmed());
    q.bindValue(":norm", normalizeLink(link));

    if (q.exec() && q.next())
        return q.value(0).toInt() > 0;
    return false;
}

// ── MarkExhaustedJobsAsUnavailable ─────────────────────────
// Marca vagas com >= 12 falhas em candidatura_etapas como indisponíveis
// (limiar igual ao C# — aumentado de 5 para 12)
int DatabaseManager::markExhaustedJobsAsUnavailable()
{
    if (!isConnected()) return 0;

    QSqlQuery q(m_db);
    bool ok = q.exec(R"(
        UPDATE vagas
        SET candidatura_indisponivel = TRUE,
            motivo_indisponibilidade = 'MAX_RETRY_EXCEEDED',
            data_indisponibilidade   = NOW()
        WHERE candidatura_simplificada = TRUE
          AND COALESCE(candidatura_enviada, FALSE) = FALSE
          AND COALESCE(candidatura_enviada_sucesso, FALSE) = FALSE
          AND COALESCE(candidatura_indisponivel, FALSE) = FALSE
          AND link IS NOT NULL AND link <> ''
          AND (
              SELECT COUNT(*) FROM candidatura_etapas ce
              WHERE ce.link = vagas.link AND ce.sucesso = FALSE
          ) >= 12
    )");

    int affected = ok ? q.numRowsAffected() : 0;
    if (affected > 0)
        qDebug() << "[BANCO]" << affected << "vaga(s) marcada(s) como MAX_RETRY_EXCEEDED (>=12 falhas).";

    return affected;
}
