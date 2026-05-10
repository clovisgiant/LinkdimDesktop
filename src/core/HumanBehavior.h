#pragma once
#include <QThread>
#include <QRandomGenerator>
#include <QTime>
#include <QDebug>

// ============================================================
// HumanBehavior — equivalente a Program.Humanization.cs (C#)
// Simula comportamento humano com delays aleatórios,
// para evitar detecção de automação pelo LinkedIn.
// ============================================================
class HumanBehavior {
public:
    // Configura os ranges de delay (chamado na inicialização)
    static void configure(
        int interactionMinMs, int interactionMaxMs,
        int applyMinMs,       int applyMaxMs,
        int paginationMinMs,  int paginationMaxMs,
        int activeHoursStart = -1,
        int activeHoursEnd   = -1)
    {
        s_interactionMin = interactionMinMs;
        s_interactionMax = interactionMaxMs;
        s_applyMin       = applyMinMs;
        s_applyMax       = applyMaxMs;
        s_paginationMin  = paginationMinMs;
        s_paginationMax  = paginationMaxMs;
        s_activeStart    = activeHoursStart;
        s_activeEnd      = activeHoursEnd;
    }

    // Delay antes de clicar em elemento (PauseBeforeClick)
    static void pauseBeforeClick() {
        sleepRandom(s_interactionMin, s_interactionMax);
    }

    // Delay entre candidaturas (BetweenApplicationsDelay)
    static void pauseBetweenApplications() {
        sleepRandom(s_applyMin, s_applyMax);
    }

    // Delay entre páginas (PaginationDelay)
    static void pauseBetweenPages() {
        sleepRandom(s_paginationMin, s_paginationMax);
    }

    // Delay genérico com range explícito (SleepRandomDelay)
    static void sleepRandom(int minMs, int maxMs) {
        if (minMs <= 0 && maxMs <= 0) return;
        if (minMs > maxMs) qSwap(minMs, maxMs);
        int delay = (minMs == maxMs)
            ? minMs
            : minMs + static_cast<int>(
                QRandomGenerator::global()->bounded(quint32(maxMs - minMs)));
        QThread::msleep(static_cast<unsigned long>(delay));
    }

    // Verifica se estamos dentro da janela de horário ativo
    static bool isWithinActiveHours() {
        if (s_activeStart < 0 || s_activeEnd < 0) return true; // sem restrição
        int hour = QTime::currentTime().hour();
        if (s_activeStart <= s_activeEnd)
            return hour >= s_activeStart && hour < s_activeEnd;
        // Caso vire a meia-noite: ex. 22h → 6h
        return hour >= s_activeStart || hour < s_activeEnd;
    }

    // Aguarda até entrar na janela ativa (WaitUntilWithinActiveHoursIfNeeded)
    static void waitUntilActiveHours() {
        if (isWithinActiveHours()) return;
        qDebug() << "[HUMAN] Fora da janela ativa. Aguardando...";
        while (!isWithinActiveHours()) {
            QThread::msleep(60000); // verifica a cada minuto
        }
        qDebug() << "[HUMAN] Janela ativa iniciada.";
    }

    // Descrição da janela ativa para log
    static QString describeActiveHoursWindow() {
        if (s_activeStart < 0 || s_activeEnd < 0)
            return "sem restricao";
        return QStringLiteral("%1h-%2h").arg(s_activeStart).arg(s_activeEnd);
    }

private:
    static int s_interactionMin;
    static int s_interactionMax;
    static int s_applyMin;
    static int s_applyMax;
    static int s_paginationMin;
    static int s_paginationMax;
    static int s_activeStart;
    static int s_activeEnd;
};

// Definições dos membros estáticos (em HumanBehavior.cpp)
// Valores padrão — modo normal (não-teste)
inline int HumanBehavior::s_interactionMin = 800;
inline int HumanBehavior::s_interactionMax = 2500;
inline int HumanBehavior::s_applyMin       = 5000;
inline int HumanBehavior::s_applyMax       = 15000;
inline int HumanBehavior::s_paginationMin  = 1200;
inline int HumanBehavior::s_paginationMax  = 2800;
inline int HumanBehavior::s_activeStart    = -1;
inline int HumanBehavior::s_activeEnd      = -1;
