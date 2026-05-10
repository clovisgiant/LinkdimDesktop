// ============================================================
// crawler_main.cpp — Entry point do WebCrawler C++
// Equivalente ao Program.cs (C#) — Fase 1: Database + Config
// ============================================================
#include <QCoreApplication>
#include <QDebug>
#include <QTimer>
#include "src/core/AppConfig.h"
#include "src/core/HumanBehavior.h"
#include "src/database/DatabaseManager.h"

int main(int argc, char* argv[])
{
    QCoreApplication app(argc, argv);

    qDebug() << "=== LinkDim WebCrawler C++ ===";
    qDebug() << "Fase 1: Validando Database...";

    // 1. Carrega .env (igual ao LoadEnvFileIfExists do C#)
    AppConfig& cfg = AppConfig::instance();
    cfg.loadEnvFile(".env");

    // 2. Configura humanização
    HumanBehavior::configure(
        cfg.interactionDelayMinMs(), cfg.interactionDelayMaxMs(),
        cfg.applyDelayMinMs(),       cfg.applyDelayMaxMs(),
        cfg.paginationDelayMinMs(),  cfg.paginationDelayMaxMs()
    );

    qDebug() << "Configuração:";
    qDebug() << "  DATABASE_ENABLED   =" << cfg.databaseEnabled();
    qDebug() << "  TEST_MODE          =" << cfg.testMode();
    qDebug() << "  MAX_APPLY_PER_CYCLE=" << cfg.maxApplyPerCycle();
    qDebug() << "  CYCLE_WAIT_MINUTES =" << cfg.cycleWaitMinutes();
    qDebug() << "  TERMOS DE BUSCA    =" << cfg.jobSearchTerms().join(" | ");
    qDebug() << "  JANELA ATIVA       =" << HumanBehavior::describeActiveHoursWindow();

    // 3. Valida conexão com PostgreSQL (ValidateDatabaseConnection)
    if (cfg.databaseEnabled()) {
        DatabaseManager db(cfg.connectionString());

        if (!db.connectAndValidate()) {
            qCritical() << "[BANCO] Falha na conexão PostgreSQL:" << db.lastError();
            return 1;
        }

        // 4. Garante schema completo (EnsureFullSchema)
        if (!db.ensureFullSchema()) {
            qCritical() << "[BANCO] Falha ao garantir schema:" << db.lastError();
            return 1;
        }

        qDebug() << "[BANCO] Schema validado com sucesso!";

        // 5. Teste: salvar uma vaga de teste e ler de volta
        if (cfg.testMode()) {
            qDebug() << "[TESTE] Inserindo vaga de teste...";
            QList<JobData> testJobs = {{
                .titulo     = "Desenvolvedor C++ Qt - Teste",
                .empresa    = "LinkDim Test",
                .localizacao= "São Paulo",
                .link       = "https://www.linkedin.com/jobs/view/999999999/"
            }};

            if (db.saveJobs(testJobs))
                qDebug() << "[TESTE] Vaga de teste inserida com sucesso!";

            QStringList pending = db.getPendingJobLinks();
            qDebug() << "[TESTE] Vagas pendentes encontradas:" << pending.size();
            for (const auto& link : pending)
                qDebug() << "  -" << link;

            // Registra etapa de teste
            db.logApplicationStep(
                "https://www.linkedin.com/jobs/view/999999999/",
                "teste_cpp_fase1",
                true,
                "Teste de integração C++/Qt → PostgreSQL OK"
            );
            qDebug() << "[TESTE] Etapa de candidatura registrada.";
        }

        qDebug() << "=== Fase 1 concluída com SUCESSO ===";
    } else {
        qDebug() << "[BANCO] Database desabilitado. Pulando validação.";
    }

    // Em modo não-teste: encerra após validação (Fase 1)
    QTimer::singleShot(0, &app, &QCoreApplication::quit);
    return app.exec();
}
