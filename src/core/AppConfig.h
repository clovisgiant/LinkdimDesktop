#pragma once
#include <QString>
#include <QStringList>
#include <QFile>
#include <QTextStream>
#include <QRegularExpression>
#include <QProcessEnvironment>

// ============================================================
// AppConfig — equivalente a Program.Configuration.cs (C#)
// Lê variáveis de ambiente e arquivo .env
// ============================================================
class AppConfig {
public:
    // Singleton
    static AppConfig& instance() {
        static AppConfig inst;
        return inst;
    }

    // Carrega arquivo .env (igual ao LoadEnvFileIfExists do C#)
    void loadEnvFile(const QString& path = ".env") {
        QStringList candidates = {
            path,
            "../.env",
            "WebCrawler/.env"
        };

        for (const auto& candidate : candidates) {
            QFile file(candidate);
            if (!file.exists()) continue;
            if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) continue;

            QTextStream in(&file);
            while (!in.atEnd()) {
                QString line = in.readLine().trimmed();
                if (line.isEmpty() || line.startsWith('#')) continue;

                int eqIdx = line.indexOf('=');
                if (eqIdx <= 0) continue;

                QString key   = line.left(eqIdx).trimmed();
                QString value = line.mid(eqIdx + 1).trimmed();
                // Remove aspas
                if (value.startsWith('"') && value.endsWith('"'))
                    value = value.mid(1, value.length() - 2);

                // Só define se não estiver já definida (mesmo comportamento do C#)
                if (key.isEmpty()) continue;
                if (qgetenv(key.toUtf8()).isEmpty())
                    qputenv(key.toUtf8(), value.toUtf8());
            }
            file.close();
            break;
        }
    }

    // ── Variáveis obrigatórias ─────────────────────────────
    QString connectionString() const {
        return getRequired("WEBCRAWLER_DB_CONNECTION");
    }
    QString linkedinUsername() const {
        return getRequired("LINKEDIN_USERNAME");
    }
    QString linkedinPassword() const {
        return getRequired("LINKEDIN_PASSWORD");
    }

    // ── Variáveis opcionais ────────────────────────────────
    bool databaseEnabled() const {
        return !getEnvBool("WEBCRAWLER_DISABLE_DATABASE", true);
    }
    bool testMode() const {
        return getEnvBool("WEBCRAWLER_TEST_MODE", false);
    }
    int maxPagesPerCycle() const {
        return getEnvInt("WEBCRAWLER_MAX_PAGES_PER_CYCLE", testMode() ? 1 : 0);
    }
    int maxApplyPerCycle() const {
        return getEnvInt("WEBCRAWLER_MAX_APPLY_PER_CYCLE", testMode() ? 2 : 15);
    }
    int cycleWaitMinutes() const {
        return getEnvInt("WEBCRAWLER_CYCLE_WAIT_MINUTES", testMode() ? 1 : 45);
    }
    bool usePersistentProfile() const {
        return getEnvBool("WEBCRAWLER_USE_PERSISTENT_PROFILE", true);
    }
    bool persistIgnoredLinks() const {
        return getEnvBool("WEBCRAWLER_PERSIST_IGNORED_LINKS", true);
    }
    bool persistSuccessfulLinks() const {
        return getEnvBool("WEBCRAWLER_PERSIST_SUCCESSFUL_LINKS", true);
    }
    bool autoFillMandatoryFields() const {
        return getEnvBool("WEBCRAWLER_AUTO_FILL_MANDATORY_FIELDS", true);
    }
    int interactionDelayMinMs() const {
        return getEnvInt("WEBCRAWLER_INTERACTION_DELAY_MIN_MS", testMode() ? 200 : 800);
    }
    int interactionDelayMaxMs() const {
        return getEnvInt("WEBCRAWLER_INTERACTION_DELAY_MAX_MS", testMode() ? 600 : 2500);
    }
    int applyDelayMinMs() const {
        return getEnvInt("WEBCRAWLER_APPLY_DELAY_MIN_MS", testMode() ? 800 : 5000);
    }
    int applyDelayMaxMs() const {
        return getEnvInt("WEBCRAWLER_APPLY_DELAY_MAX_MS", testMode() ? 1500 : 15000);
    }
    int paginationDelayMinMs() const {
        return getEnvInt("WEBCRAWLER_PAGINATION_DELAY_MIN_MS", testMode() ? 500 : 1200);
    }
    int paginationDelayMaxMs() const {
        return getEnvInt("WEBCRAWLER_PAGINATION_DELAY_MAX_MS", testMode() ? 1200 : 2800);
    }
    bool useJobsSearchEntry() const {
        return getEnvBool("WEBCRAWLER_USE_JOBS_SEARCH_ENTRY", true);
    }

    // AutoFill defaults
    QString autoFillFirstName()    const { return getEnvStr("WEBCRAWLER_DEFAULT_FIRST_NAME", "Clovis"); }
    QString autoFillLastName()     const { return getEnvStr("WEBCRAWLER_DEFAULT_LAST_NAME", "Silva"); }
    QString autoFillPhone()        const { return getEnvStr("WEBCRAWLER_DEFAULT_PHONE", "11999999999"); }
    QString autoFillLocation()     const { return getEnvStr("WEBCRAWLER_DEFAULT_LOCATION", "Sao Paulo"); }
    QString autoFillEmail()        const { return getEnvStr("WEBCRAWLER_DEFAULT_EMAIL", linkedinUsername()); }
    QString autoFillWebsite()      const { return getEnvStr("WEBCRAWLER_DEFAULT_WEBSITE", ""); }
    QString autoFillLinkedIn()     const { return getEnvStr("WEBCRAWLER_DEFAULT_LINKEDIN_URL", ""); }
    QString autoFillGithub()       const { return getEnvStr("WEBCRAWLER_DEFAULT_GITHUB_URL", ""); }
    QString autoFillSalary()       const { return getEnvStr("WEBCRAWLER_DEFAULT_SALARY", "7000"); }
    QString autoFillGenericText()  const {
        return getEnvStr("WEBCRAWLER_DEFAULT_GENERIC_TEXT",
            "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.");
    }
    int autoFillYearsExperience()  const {
        int v = getEnvInt("WEBCRAWLER_DEFAULT_YEARS_EXPERIENCE", 3);
        return v <= 0 ? 1 : v;
    }
    bool autoFillCheckboxTrue()         const { return getEnvBool("WEBCRAWLER_DEFAULT_CHECKBOX_TRUE", true); }
    bool autoFillWorkAuthorization()    const { return getEnvBool("WEBCRAWLER_DEFAULT_WORK_AUTHORIZATION", true); }
    bool autoFillNeedVisaSponsorship()  const { return getEnvBool("WEBCRAWLER_DEFAULT_NEED_VISA_SPONSORSHIP", false); }

    // Termos de busca
    QStringList jobSearchTerms() const {
        QString raw = getEnvStr("WEBCRAWLER_JOB_SEARCH_TERMS", "");
        if (!raw.isEmpty()) {
            QStringList result;
            for (auto& t : raw.split(QRegularExpression("[,;|]")))
                if (!t.trimmed().isEmpty()) result << t.trimmed();
            if (!result.isEmpty()) return result;
        }
        return defaultSearchTerms();
    }

private:
    AppConfig() = default;
    AppConfig(const AppConfig&) = delete;
    AppConfig& operator=(const AppConfig&) = delete;

    QString getRequired(const QString& name) const {
        QByteArray val = qgetenv(name.toUtf8());
        if (val.isEmpty())
            qFatal("Variável de ambiente obrigatória não definida: %s", name.toUtf8().constData());
        return QString::fromUtf8(val).trimmed();
    }

    QString getEnvStr(const QString& name, const QString& def) const {
        QByteArray val = qgetenv(name.toUtf8());
        return val.isEmpty() ? def : QString::fromUtf8(val).trimmed();
    }

    bool getEnvBool(const QString& name, bool def) const {
        QByteArray val = qgetenv(name.toUtf8());
        if (val.isEmpty()) return def;
        QString s = QString::fromUtf8(val).trimmed().toLower();
        return (s == "true" || s == "1" || s == "yes");
    }

    int getEnvInt(const QString& name, int def) const {
        QByteArray val = qgetenv(name.toUtf8());
        if (val.isEmpty()) return def;
        bool ok = false;
        int v = QString::fromUtf8(val).trimmed().toInt(&ok);
        return ok ? v : def;
    }

    static QStringList defaultSearchTerms() {
        return {
            "Desenvolvedor Backend .NET - Pleno/Senior",
            "Engenharia de Software",
            "Desenvolvedor C#",
            "Analista I de Desenvolvimento de Software",
            "Engenheiro de Software",
            "Analista de Desenvolvimento de Software FullStack",
            "Senior Full Stack Developer | .NET",
            "C#", "JavaScript", "Python", "VB.NET", "PHP", "VBA",
            "Angular", "React", "HTML5", "CSS3", ".NET", "APIs REST",
            "Automacao de processos", "AWS", "EC2", "S3", "Lambda",
            "Athena", "Glue", "SQL", "PostgreSQL", "MySQL",
            "Git", "Visual Studio", "VS Code", "Linux"
        };
    }
};

