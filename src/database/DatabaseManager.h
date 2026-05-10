#pragma once
#include <QString>
#include <QStringList>
#include <QList>
#include <QDateTime>
#include <QSqlDatabase>
#include <QSqlQuery>
#include <QSqlError>
#include <QDebug>

// ============================================================
// Estrutura equivalente à tupla (Titulo, Empresa, Localizacao, Link)
// usada em SaveCollectedJobsToDatabase no C#
// ============================================================
struct JobData {
    QString titulo;
    QString empresa;
    QString localizacao;
    QString link;
};

// ============================================================
// DatabaseManager — equivalente a Program.Database.cs (C#)
// Gerencia toda interação com o PostgreSQL.
// Usa QSqlDatabase com driver QPSQL.
// ============================================================
class DatabaseManager {
public:
    explicit DatabaseManager(const QString& connectionString);
    ~DatabaseManager();

    // Conecta e valida (equivalente a ValidateDatabaseConnection)
    bool connectAndValidate();

    // Garante que o schema completo existe
    // (equivalente a EnsureJobsSchema + EnsureSuccessTrackingColumns etc.)
    bool ensureFullSchema();

    // ── Equivalentes diretos dos métodos C# ───────────────

    // SaveCollectedJobsToDatabase
    bool saveJobs(const QList<JobData>& jobs);

    // GetSimplifiedJobLinksFromDatabase
    QStringList getPendingJobLinks();

    // MarkJobAsApplied
    bool markJobAsApplied(const QString& link);

    // MarkJobAsUnavailable
    bool markJobAsUnavailable(const QString& link, const QString& reason);

    // LogApplicationStep
    bool logApplicationStep(
        const QString& link,
        const QString& etapa,
        bool sucesso,
        const QString& detalhe       = {},
        const QString& htmlPath       = {},
        const QString& screenshotPath = {}
    );

    // Verifica se há candidatura com sucesso registrada
    bool hasSuccessfulApplicationRecorded(const QString& link);

    // Marca vagas exauridas (>= 12 falhas) como indisponíveis
    int  markExhaustedJobsAsUnavailable();

    bool isConnected() const;
    QString lastError() const;

private:
    QString        m_connStr;
    QSqlDatabase   m_db;
    QString        m_lastError;
    bool           m_connected = false;

    // Helpers internos
    bool ensureJobsSchema();
    bool ensureSuccessTrackingColumns();
    bool ensureUnavailableTrackingColumns();
    bool ensureApplicationTrackingSchema();

    // Normaliza link do LinkedIn (equivalente a NormalizeLinkedInJobLink)
    static QString normalizeLink(const QString& link);

    // Extrai job ID do link (equivalente a ExtractLinkedInJobId)
    static QString extractJobId(const QString& link);

    // Executa query simples sem retorno de dados
    bool execQuery(const QString& sql);

    // Parseia connection string PostgreSQL para QSqlDatabase
    static bool parseConnectionString(
        const QString& connStr,
        QString& host, int& port,
        QString& dbName, QString& user, QString& password
    );
};
