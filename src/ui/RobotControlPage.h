#pragma once
#include <QWidget>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QPushButton>
#include <QTextEdit>
#include <QScrollBar>
#include <QTimer>
#include <QDateTime>
#include <QSlider>
#include <QSpinBox>
#include <QLineEdit>
#include <QFrame>
#include <QGraphicsDropShadowEffect>
#include <QNetworkAccessManager>
#include <QNetworkRequest>
#include <QNetworkReply>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QWebSocket>
#include <QUrl>
#include <QPainter>
#include <QPainterPath>
#include <QPropertyAnimation>
#include <QEasingCurve>
#include <QListWidget>
#include <QDesktopServices>

// ─────────────────────────────────────────────────────────────
// PulseIndicator — bolinha animada de status
// ─────────────────────────────────────────────────────────────
class PulseIndicator : public QWidget {
    Q_OBJECT
    Q_PROPERTY(qreal pulse READ pulse WRITE setPulse)
public:
    enum State { Idle, Running, Error };

    PulseIndicator(QWidget* p = nullptr) : QWidget(p), m_pulse(0), m_state(Idle) {
        setFixedSize(18, 18);
        m_anim = new QPropertyAnimation(this, "pulse");
        m_anim->setDuration(1200);
        m_anim->setStartValue(0.0); m_anim->setEndValue(1.0);
        m_anim->setLoopCount(-1);   // infinito
        m_anim->setEasingCurve(QEasingCurve::SineCurve);
    }

    void setState(State s) {
        m_state = s;
        if (s == Running) m_anim->start();
        else              m_anim->stop();
        update();
    }

    qreal pulse() const { return m_pulse; }
    void  setPulse(qreal v) { m_pulse = v; update(); }

protected:
    void paintEvent(QPaintEvent*) override {
        QPainter p(this); p.setRenderHint(QPainter::Antialiasing);
        QColor col = (m_state == Running) ? QColor("#22C55E")
                   : (m_state == Error)   ? QColor("#EF4444")
                                          : QColor("#6B7280");
        // Anel pulsante externo
        if (m_state == Running) {
            QColor ring = col; ring.setAlpha(int(60 * (1.0 - m_pulse)));
            p.setBrush(ring); p.setPen(Qt::NoPen);
            double r = 9.0 * (0.6 + 0.4 * m_pulse);
            p.drawEllipse(QPointF(9,9), r, r);
        }
        // Núcleo sólido
        p.setBrush(col); p.setPen(Qt::NoPen);
        p.drawEllipse(QPointF(9,9), 5.5, 5.5);
    }
private:
    qreal m_pulse; State m_state;
    QPropertyAnimation* m_anim;
};

// ─────────────────────────────────────────────────────────────
// ConfigRow — linha de configuração label + input
// ─────────────────────────────────────────────────────────────
static QHBoxLayout* makeConfigRow(const QString& label, QWidget* input) {
    auto* row = new QHBoxLayout(); row->setSpacing(12);
    auto* lbl = new QLabel(label);
    lbl->setFixedWidth(200);
    lbl->setStyleSheet("color:#A39C93;font-size:12px;font-weight:600;");
    row->addWidget(lbl);
    row->addWidget(input, 1);
    return row;
}

// ─────────────────────────────────────────────────────────────
// LinkedInButton — Botão estilo InfoJobs (como solicitado)
// ─────────────────────────────────────────────────────────────
class LinkedInButton : public QPushButton {
public:
    LinkedInButton(QWidget* parent = nullptr) : QPushButton(parent) {
        setCursor(Qt::PointingHandCursor);
        setFixedHeight(50);
        setMinimumWidth(300);
        
        // Estilo Premium White (estilo InfoJobs)
        setStyleSheet(
            "QPushButton {"
            "  background-color: white;"
            "  color: #1F2937;"
            "  border: 1px solid #D1D5DB;"
            "  border-radius: 25px;"
            "  font-size: 15px;"
            "  font-weight: 600;"
            "  padding: 0 20px;"
            "}"
            "QPushButton:hover {"
            "  background-color: #F9FAFB;"
            "  border: 1px solid #9CA3AF;"
            "}"
            "QPushButton:pressed {"
            "  background-color: #F3F4F6;"
            "}"
        );

        auto* lay = new QHBoxLayout(this);
        lay->setContentsMargins(20, 0, 20, 0);
        lay->setSpacing(12);

        // Ícone LinkedIn (Desenhado via Painter)
        m_iconPlaceholder = new QWidget();
        m_iconPlaceholder->setFixedSize(24, 24);
        
        auto* textLabel = new QLabel("LinkedIn");
        textLabel->setStyleSheet("color: #1F2937; font-weight: bold; font-size: 16px; background: transparent;");

        lay->addStretch();
        lay->addWidget(m_iconPlaceholder);
        lay->addWidget(textLabel);
        lay->addStretch();
    }

protected:
    void paintEvent(QPaintEvent* e) override {
        QPushButton::paintEvent(e);
        QPainter p(this);
        p.setRenderHint(QPainter::Antialiasing);
        
        // Geometria do ícone baseado no placeholder
        QRect r = m_iconPlaceholder->geometry();
        
        // Desenha o fundo azul do LinkedIn
        p.setBrush(QColor("#0A66C2"));
        p.setPen(Qt::NoPen);
        p.drawRoundedRect(r, 4, 4);
        
        // Desenha o "in" estilizado (simplificado com texto, mas fiel à marca)
        p.setPen(Qt::white);
        QFont f = p.font();
        f.setFamily("Arial");
        f.setBold(true);
        f.setPixelSize(16);
        p.setFont(f);
        p.drawText(r.adjusted(0, -1, 0, 0), Qt::AlignCenter, "in");
    }

private:
    QWidget* m_iconPlaceholder;
};

// ─────────────────────────────────────────────────────────────
// RobotControlPage — tela de controle do WebCrawler
// ─────────────────────────────────────────────────────────────
class RobotControlPage : public QWidget {
    Q_OBJECT
public:
    explicit RobotControlPage(QWidget* parent = nullptr) : QWidget(parent) {
        setAttribute(Qt::WA_StyledBackground);
        setStyleSheet("background:#111216;");
        m_nam = new QNetworkAccessManager(this);
        buildUI();
        connectWebSocket();

        // Polling de status a cada 10s
        auto* t = new QTimer(this);
        connect(t, &QTimer::timeout, this, &RobotControlPage::fetchStatus);
        t->start(10000);
        QTimer::singleShot(500, this, &RobotControlPage::fetchStatus);
        QTimer::singleShot(1000, this, &RobotControlPage::fetchDiagnostics);
    }

private:
    // ── Membros UI ───────────────────────────────────────────
    PulseIndicator* m_pulse       = nullptr;
    QLabel*         m_statusLabel = nullptr;
    QLabel*         m_cycleLabel  = nullptr;
    QLabel*         m_uptimeLabel = nullptr;
    QPushButton*    m_startBtn    = nullptr;
    QPushButton*    m_stopBtn     = nullptr;
    QPushButton*    m_resetBtn    = nullptr;
    QTextEdit*      m_logArea     = nullptr;
    QLineEdit*      m_apiUrlInput = nullptr;
    QSpinBox*       m_maxApply    = nullptr;
    QSpinBox*       m_cycleWait   = nullptr;
    QSpinBox*       m_activeStart = nullptr;
    QSpinBox*       m_activeEnd   = nullptr;
    QLineEdit*      m_termsInput  = nullptr;
    QPushButton*    m_testBtn     = nullptr;
    QListWidget*    m_diagList    = nullptr;
    QPushButton*    m_refreshDiag = nullptr;
    QPushButton*    m_openDiag    = nullptr;
    
    // Autenticação
    LinkedInButton* m_loginBtn     = nullptr;
    QLineEdit*      m_cookieInput  = nullptr;
    QPushButton*    m_saveCookieBtn = nullptr;

    // ── Membros de rede ──────────────────────────────────────
    QNetworkAccessManager* m_nam = nullptr;
    QWebSocket             m_ws;
    QString m_apiBase = "https://linkdimdesktop.onrender.com"; // ← Sua URL real agora é o padrão

    void buildUI() {
        auto* root = new QVBoxLayout(this);
        root->setContentsMargins(0, 0, 0, 0); // Remove margens do root (o scroll cuida do resto)
        root->setSpacing(0);

        // ── Cabeçalho (FORA DO SCROLL) ──────────────────────
        auto* hdrContainer = new QWidget();
        hdrContainer->setFixedHeight(100);
        hdrContainer->setStyleSheet("background:#1A1D24; border-bottom:1px solid #252830;");
        auto* hdr = new QHBoxLayout(hdrContainer);
        hdr->setContentsMargins(32, 10, 32, 10);
        
        auto* titleV = new QVBoxLayout();
        auto* title = new QLabel("Controle do Robô");
        title->setStyleSheet("color:#F0F4F8;font-size:24px;font-weight:bold;");
        auto* sub = new QLabel("Orquestrador Render.com via API.");
        sub->setStyleSheet("color:#A39C93;font-size:12px;");
        titleV->addWidget(title); titleV->addWidget(sub);
        hdr->addLayout(titleV);
        hdr->addStretch();

        // Campo URL da API
        m_apiUrlInput = new QLineEdit(m_apiBase);
        m_apiUrlInput->setFixedWidth(360);
        m_apiUrlInput->setStyleSheet(
            "QLineEdit{background:#111216;color:#F0F4F8;border:1px solid #252830;"
            "border-radius:8px;padding:8px 14px;font-size:12px;font-family:Consolas;}"
            "QLineEdit:focus{border:1px solid #E39B35;}");
        
        // MUDEI PARA textChanged para salvar na hora!
        connect(m_apiUrlInput, &QLineEdit::textChanged, this, [this](const QString& text){
            m_apiBase = text.trimmed();
            // Debounce simples para não reconectar a cada letra
            QTimer::singleShot(1000, this, &RobotControlPage::connectWebSocket);
        });
        
        auto* apiLbl = new QLabel("API URL:");
        apiLbl->setStyleSheet("color:#A39C93;font-size:11px;font-weight:bold;");
        auto* apiRow = new QVBoxLayout();
        apiRow->addWidget(apiLbl); apiRow->addWidget(m_apiUrlInput);
        hdr->addLayout(apiRow);

        // BOTÃO DE AUTODIAGNÓSTICO (O "RAIO-X")
        m_testBtn = new QPushButton("🔍 TESTAR INFRA");
        m_testBtn->setCursor(Qt::PointingHandCursor);
        m_testBtn->setFixedSize(140, 40);
        m_testBtn->setStyleSheet(
            "QPushButton{background:#3B82F6;color:white;border:none;border-radius:8px;"
            "font-weight:bold;font-size:11px;}"
            "QPushButton:hover{background:#60A5FA;}"
        );
        connect(m_testBtn, &QPushButton::clicked, this, &RobotControlPage::testInfra);
        hdr->addWidget(m_testBtn);
        root->addWidget(hdrContainer);

        // ── Status Card ──────────────────────────────────────
        auto* statusCard = makeCard();
        auto* sclay = new QHBoxLayout(statusCard);
        sclay->setContentsMargins(24, 20, 24, 20); sclay->setSpacing(32);

        // Indicador + status
        m_pulse = new PulseIndicator(this);
        m_statusLabel = new QLabel("Verificando...");
        m_statusLabel->setStyleSheet("color:#F0F4F8;font-size:20px;font-weight:bold;");
        auto* stV = new QVBoxLayout();
        auto* stRow = new QHBoxLayout();
        stRow->addWidget(m_pulse); stRow->addWidget(m_statusLabel); stRow->addStretch();
        m_cycleLabel = new QLabel("Ciclo: —");
        m_cycleLabel->setStyleSheet("color:#A39C93;font-size:12px;");
        m_uptimeLabel = new QLabel("Último heartbeat: —");
        m_uptimeLabel->setStyleSheet("color:#A39C93;font-size:12px;");
        stV->addLayout(stRow);
        stV->addWidget(m_cycleLabel);
        stV->addWidget(m_uptimeLabel);
        sclay->addLayout(stV, 1);

        // Botões START / STOP
        m_startBtn = makeActionBtn("▶  INICIAR CRAWLER", "#22C55E");
        m_stopBtn  = makeActionBtn("■  PARAR CRAWLER",   "#EF4444");
        m_stopBtn->setEnabled(false);
        m_resetBtn = makeActionBtn("🧹  LIMPAR SESSÃO", "#6B7280");
        m_resetBtn->setFixedHeight(36); m_resetBtn->setStyleSheet(m_resetBtn->styleSheet().replace("14px", "11px"));
        
        connect(m_startBtn, &QPushButton::clicked, this, &RobotControlPage::startCrawler);
        connect(m_stopBtn,  &QPushButton::clicked, this, &RobotControlPage::stopCrawler);
        connect(m_resetBtn, &QPushButton::clicked, this, &RobotControlPage::resetSession);
        
        auto* btnV = new QVBoxLayout();
        btnV->addWidget(m_startBtn); btnV->addWidget(m_stopBtn); btnV->addWidget(m_resetBtn);
        sclay->addLayout(btnV);
        root->addWidget(statusCard);

        // ── Layout horizontal: Config + Log ─────────────────
        auto* midRow = new QHBoxLayout(); midRow->setSpacing(20);

        // ── Painel de Configuração ───────────────────────────
        auto* cfgCard = makeCard();
        cfgCard->setFixedWidth(360);
        auto* cfglay = new QVBoxLayout(cfgCard);
        cfglay->setContentsMargins(24, 20, 24, 20); cfglay->setSpacing(16);

        auto* cfgTitle = new QLabel("⚙️  Configuração do Ciclo");
        cfgTitle->setStyleSheet("color:#E39B35;font-size:14px;font-weight:bold;");
        cfglay->addWidget(cfgTitle);
        addSeparator(cfglay);

        m_maxApply = makeSpinBox(1, 100, 15);
        m_cycleWait = makeSpinBox(1, 480, 45);
        m_activeStart = makeSpinBox(0, 23, 8);
        m_activeEnd   = makeSpinBox(0, 23, 22);
        m_termsInput  = new QLineEdit("C#, Python, Qt, PostgreSQL");
        styleInput(m_termsInput);

        cfglay->addLayout(makeConfigRow("Candidaturas / ciclo:", m_maxApply));
        cfglay->addLayout(makeConfigRow("Espera entre ciclos (min):", m_cycleWait));
        cfglay->addLayout(makeConfigRow("Horário início (h):", m_activeStart));
        cfglay->addLayout(makeConfigRow("Horário fim (h):", m_activeEnd));
        cfglay->addLayout(makeConfigRow("Termos de busca:", m_termsInput));

        addSeparator(cfglay);

        auto* applyBtn = new QPushButton("Salvar e Aplicar Configuração");
        applyBtn->setCursor(Qt::PointingHandCursor);
        applyBtn->setStyleSheet(
            "QPushButton{background:#E39B35;color:#111216;border:none;border-radius:8px;"
            "padding:10px;font-weight:bold;font-size:13px;}"
            "QPushButton:hover{background:#f0ab45;}");
        connect(applyBtn, &QPushButton::clicked, this, &RobotControlPage::applyConfig);
        cfglay->addWidget(applyBtn);
        cfglay->addStretch();

        // ── Painel de Autenticação & Sessão ──────────────────
        auto* authCard = makeCard();
        authCard->setFixedWidth(360);
        auto* authlay = new QVBoxLayout(authCard);
        authlay->setContentsMargins(24, 20, 24, 20); authlay->setSpacing(16);

        auto* authTitle = new QLabel("🔐  Autenticação & Sessão");
        authTitle->setStyleSheet("color:#3B82F6;font-size:14px;font-weight:bold;");
        authlay->addWidget(authTitle);
        addSeparator(authlay);

        auto* authInfo = new QLabel("Se o LinkedIn estiver bloqueando o robô, faça login no seu navegador e cole o cookie 'li_at' abaixo:");
        authInfo->setWordWrap(true);
        authInfo->setStyleSheet("color:#A39C93;font-size:11px;line-height:1.4;");
        authlay->addWidget(authInfo);

        m_loginBtn = new LinkedInButton(this);
        m_loginBtn->setToolTip("Abrir LinkedIn para login manual");
        connect(m_loginBtn, &QPushButton::clicked, this, [this]{
            QDesktopServices::openUrl(QUrl("https://www.linkedin.com/login"));
        });
        authlay->addWidget(m_loginBtn, 0, Qt::AlignCenter);

        m_cookieInput = new QLineEdit();
        m_cookieInput->setPlaceholderText("Cole o valor do cookie li_at...");
        styleInput(m_cookieInput);
        authlay->addLayout(makeConfigRow("Cookie li_at:", m_cookieInput));

        m_saveCookieBtn = new QPushButton("Atualizar Sessão no Robô");
        m_saveCookieBtn->setCursor(Qt::PointingHandCursor);
        m_saveCookieBtn->setStyleSheet(
            "QPushButton{background:#3B82F6;color:white;border:none;border-radius:8px;"
            "padding:10px;font-weight:bold;font-size:13px;}"
            "QPushButton:hover{background:#60A5FA;}");
        connect(m_saveCookieBtn, &QPushButton::clicked, this, &RobotControlPage::saveSessionCookie);
        authlay->addWidget(m_saveCookieBtn);

        authlay->addStretch();

        
        // ── Painel de Diagnósticos (Raio-X) ──────────────────
        auto* leftCol = new QVBoxLayout(); leftCol->setSpacing(20);
        leftCol->addWidget(cfgCard);
        leftCol->addWidget(authCard);

        auto* diagCard = makeCard();
        diagCard->setFixedWidth(360);
        auto* diaglay = new QVBoxLayout(diagCard);
        diaglay->setContentsMargins(24, 20, 24, 20); diaglay->setSpacing(12);

        auto* diagHdr = new QHBoxLayout();
        auto* diagTitle = new QLabel("📸 Galeria de Raio-X");
        diagTitle->setStyleSheet("color:#3B82F6;font-size:14px;font-weight:bold;");
        m_refreshDiag = new QPushButton("🔄");
        m_refreshDiag->setFixedSize(30, 30);
        m_refreshDiag->setCursor(Qt::PointingHandCursor);
        m_refreshDiag->setStyleSheet("QPushButton{background:#252830;border-radius:15px;color:white;}QPushButton:hover{background:#3B82F6;}");
        connect(m_refreshDiag, &QPushButton::clicked, this, &RobotControlPage::fetchDiagnostics);
        diagHdr->addWidget(diagTitle); diagHdr->addStretch(); diagHdr->addWidget(m_refreshDiag);
        diaglay->addLayout(diagHdr);
        
        addSeparator(diaglay);

        m_diagList = new QListWidget();
        m_diagList->setStyleSheet(
            "QListWidget{background:#111216;color:#F0F4F8;border:1px solid #252830;border-radius:8px;padding:5px;font-size:11px;}"
            "QListWidget::item{padding:8px;border-bottom:1px solid #1A1D24;}"
            "QListWidget::item:selected{background:#3B82F6;color:white;border-radius:4px;}"
        );
        diaglay->addWidget(m_diagList, 1);

        m_openDiag = new QPushButton("Abrir Snapshot no Navegador");
        m_openDiag->setCursor(Qt::PointingHandCursor);
        m_openDiag->setStyleSheet(
            "QPushButton{background:#3B82F6;color:white;border:none;border-radius:8px;"
            "padding:10px;font-weight:bold;font-size:12px;}"
            "QPushButton:hover{background:#60A5FA;}");
        connect(m_openDiag, &QPushButton::clicked, this, &RobotControlPage::openDiagnostic);
        diaglay->addWidget(m_openDiag);
        
        leftCol->addWidget(diagCard, 1);
        midRow->addLayout(leftCol);

        // ── Log em Tempo Real ────────────────────────────────
        auto* logCard = makeCard();
        auto* loglay = new QVBoxLayout(logCard);
        loglay->setContentsMargins(20, 16, 20, 16); loglay->setSpacing(10);

        auto* logHdr = new QHBoxLayout();
        auto* logTitle = new QLabel("📋  Log em Tempo Real (WebSocket)");
        logTitle->setStyleSheet("color:#F0F4F8;font-size:14px;font-weight:bold;");
        auto* wsStatus = new QLabel("● Conectando...");
        wsStatus->setObjectName("wsStatus");
        wsStatus->setStyleSheet("color:#E39B35;font-size:11px;");
        logHdr->addWidget(logTitle); logHdr->addStretch(); logHdr->addWidget(wsStatus);

        auto* clearBtn = new QPushButton("Limpar");
        clearBtn->setCursor(Qt::PointingHandCursor);
        clearBtn->setFixedHeight(28);
        clearBtn->setStyleSheet(
            "QPushButton{background:transparent;color:#A39C93;border:1px solid #252830;"
            "border-radius:5px;padding:0 10px;font-size:11px;}"
            "QPushButton:hover{color:#F0F4F8;}");
        connect(clearBtn, &QPushButton::clicked, this, [this]{ m_logArea->clear(); });
        logHdr->addWidget(clearBtn);
        loglay->addLayout(logHdr);

        m_logArea = new QTextEdit();
        m_logArea->setReadOnly(true);
        m_logArea->setStyleSheet(
            "QTextEdit{background:#0D0F13;color:#A3E635;border:1px solid #252830;"
            "border-radius:8px;padding:10px;font-family:'Courier New',monospace;font-size:11px;}"
            "QScrollBar:vertical{background:#1A1D24;width:8px;}"
            "QScrollBar::handle:vertical{background:#252830;border-radius:4px;}");
        loglay->addWidget(m_logArea, 1);

        // Status do WebSocket
        connect(&m_ws, &QWebSocket::connected, this, [wsStatus, this]{
            wsStatus->setText("● WebSocket conectado");
            wsStatus->setStyleSheet("color:#22C55E;font-size:11px;");
            appendLog("✅ WebSocket conectado ao servidor Render.");
        });
        connect(&m_ws, &QWebSocket::disconnected, this, [wsStatus, this]{
            wsStatus->setText("● Desconectado");
            wsStatus->setStyleSheet("color:#EF4444;font-size:11px;");
            appendLog("⚠️  WebSocket desconectado. Reconectando em 5s...");
            QTimer::singleShot(5000, this, &RobotControlPage::connectWebSocket);
        });
        connect(&m_ws, &QWebSocket::textMessageReceived, this, [this](const QString& msg){
            appendLog(msg);
        });

        midRow->addWidget(logCard, 1);
        root->addLayout(midRow, 1);
    }

    // ── Helpers de UI ────────────────────────────────────────
    QWidget* makeCard() {
        auto* w = new QWidget(this);
        w->setAttribute(Qt::WA_StyledBackground);
        w->setStyleSheet("background:#1A1D24;border-radius:12px;border:1px solid #252830;");
        auto* sh = new QGraphicsDropShadowEffect(w);
        sh->setBlurRadius(20); sh->setOffset(0,6); sh->setColor(QColor(0,0,0,80));
        w->setGraphicsEffect(sh);
        return w;
    }

    QPushButton* makeActionBtn(const QString& text, const QString& color) {
        auto* btn = new QPushButton(text);
        btn->setCursor(Qt::PointingHandCursor);
        btn->setFixedHeight(48);
        btn->setMinimumWidth(220);
        btn->setStyleSheet(QString(
            "QPushButton{background:%1;color:#FFF;border:none;border-radius:10px;"
            "font-size:14px;font-weight:bold;letter-spacing:0.5px;}"
            "QPushButton:hover{background:%2;}"
            "QPushButton:disabled{background:#2A2D35;color:#555;}")
            .arg(color).arg(QColor(color).lighter(115).name()));
        return btn;
    }

    QSpinBox* makeSpinBox(int min, int max, int val) {
        auto* sb = new QSpinBox();
        sb->setRange(min, max); sb->setValue(val);
        sb->setStyleSheet(
            "QSpinBox{background:#111216;color:#F0F4F8;border:1px solid #252830;"
            "border-radius:6px;padding:6px 10px;font-size:12px;}"
            "QSpinBox:focus{border:1px solid #E39B35;}"
            "QSpinBox::up-button,QSpinBox::down-button{background:#252830;border:none;width:18px;}");
        return sb;
    }

    void styleInput(QLineEdit* e) {
        e->setStyleSheet(
            "QLineEdit{background:#111216;color:#F0F4F8;border:1px solid #252830;"
            "border-radius:6px;padding:6px 10px;font-size:12px;}"
            "QLineEdit:focus{border:1px solid #E39B35;}");
    }

    void addSeparator(QVBoxLayout* lay) {
        auto* f = new QFrame(); f->setFrameShape(QFrame::HLine);
        f->setStyleSheet("background:#252830;max-height:1px;");
        lay->addWidget(f);
    }

    void appendLog(const QString& msg) {
        QString ts = QDateTime::currentDateTime().toString("[HH:mm:ss] ");
        m_logArea->append("<span style='color:#6B7280;'>" + ts + "</span>" + msg);
        m_logArea->verticalScrollBar()->setValue(m_logArea->verticalScrollBar()->maximum());
    }

    void setRunningState(bool running) {
        m_startBtn->setEnabled(!running);
        m_stopBtn->setEnabled(running);
        m_pulse->setState(running ? PulseIndicator::Running : PulseIndicator::Idle);
        m_statusLabel->setText(running ? "Robô RODANDO" : "Robô PARADO");
        m_statusLabel->setStyleSheet(running
            ? "color:#22C55E;font-size:20px;font-weight:bold;"
            : "color:#F0F4F8;font-size:20px;font-weight:bold;");
    }

    // ── API Calls ─────────────────────────────────────────────
    void connectWebSocket() {
        if (m_ws.state() == QAbstractSocket::ConnectedState) m_ws.close();
        
        // Limpa a URL (remove barras extras no final)
        QString base = m_apiBase.trimmed();
        while(base.endsWith('/')) base.chop(1);
        
        QString wsUrl = base;
        wsUrl.replace("https://", "wss://").replace("http://", "ws://");
        wsUrl += "/ws/logs";
        m_ws.open(QUrl(wsUrl));
    }

    QNetworkRequest makeRequest(const QString& endpoint) {
        // Limpa a URL base
        QString base = m_apiBase.trimmed();
        while(base.endsWith('/')) base.chop(1);
        
        // Garante que o endpoint comece com barra
        QString path = endpoint;
        if (!path.startsWith('/')) path = "/" + path;
        
        QUrl url(base + path);
        QNetworkRequest req(url);
        req.setHeader(QNetworkRequest::ContentTypeHeader, "application/json");
        return req;
    }

private slots:
    // GET /crawler/status
    void fetchStatus() {
        auto* reply = m_nam->get(makeRequest("/crawler/status"));
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            if (reply->error() != QNetworkReply::NoError) {
                m_statusLabel->setText("API inacessível");
                m_pulse->setState(PulseIndicator::Error);
                m_cycleLabel->setText("Erro: " + reply->errorString().left(50));
                return;
            }
            auto doc = QJsonDocument::fromJson(reply->readAll());
            auto obj = doc.object();
            QString status = obj["status"].toString();
            bool running = (status == "running");
            setRunningState(running);
            m_cycleLabel->setText("Status: " + status);
            if (obj.contains("last_heartbeat") && !obj["last_heartbeat"].isNull())
                m_uptimeLabel->setText("Último heartbeat: " + obj["last_heartbeat"].toString());
        });
    }

    // POST /crawler/start
    void startCrawler() {
        appendLog("▶ Enviando comando START ao Render...");
        QJsonObject body;
        body["max_apply_per_cycle"] = m_maxApply->value();
        body["cycle_wait_minutes"]  = m_cycleWait->value();
        body["active_hours_start"]  = m_activeStart->value();
        body["active_hours_end"]    = m_activeEnd->value();
        body["search_terms"] = m_termsInput->text().trimmed();

        auto* reply = m_nam->post(makeRequest("/crawler/start"),
                                  QJsonDocument(body).toJson());
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            if (reply->error() != QNetworkReply::NoError) {
                appendLog("❌ Erro ao iniciar: " + reply->errorString());
                m_pulse->setState(PulseIndicator::Error);
                return;
            }
            appendLog("✅ Crawler iniciado no Render!");
            setRunningState(true);
        });
    }

    // POST /crawler/stop
    void stopCrawler() {
        appendLog("■ Enviando comando STOP ao Render...");
        auto* reply = m_nam->post(makeRequest("/crawler/stop"), QByteArray("{}"));
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            if (reply->error() != QNetworkReply::NoError) {
                appendLog("❌ Erro ao parar: " + reply->errorString());
                return;
            }
            appendLog("⏹ Crawler parado.");
            setRunningState(false);
        });
    }

    void resetSession() {
        if (m_statusLabel->text().contains("RODANDO")) {
            appendLog("⚠️ Pare o robô antes de limpar a sessão.");
            return;
        }
        appendLog("🧹 Solicitando limpeza de sessão no Render...");
        auto* reply = m_nam->post(makeRequest("/crawler/reset_session"), QByteArray("{}"));
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            if (reply->error() != QNetworkReply::NoError) {
                appendLog("❌ Erro ao resetar: " + reply->errorString());
                return;
            }
            appendLog("✅ Sessão e cache limpos no servidor.");
        });
    }

    // POST /crawler/config
    void applyConfig() {
        appendLog("⚙️  Aplicando configuração...");
        QJsonObject cfg;
        cfg["max_apply_per_cycle"] = m_maxApply->value();
        cfg["cycle_wait_minutes"]  = m_cycleWait->value();
        cfg["active_hours_start"]  = m_activeStart->value();
        cfg["active_hours_end"]    = m_activeEnd->value();
        cfg["search_terms"]        = m_termsInput->text().trimmed();

        auto* reply = m_nam->post(makeRequest("/crawler/config"),
                                  QJsonDocument(cfg).toJson());
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            if (reply->error() != QNetworkReply::NoError)
                appendLog("❌ Erro ao salvar config: " + reply->errorString());
            else
                appendLog("✅ Configuração salva com sucesso!");
        });
    }

    // GET /crawler/test (O MODO RAIO-X)
    void testInfra() {
        appendLog("🔍 [DIAGNÓSTICO] Verificando integridade da infraestrutura...");
        m_testBtn->setEnabled(false);
        auto* reply = m_nam->get(makeRequest("/crawler/test"));
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            m_testBtn->setEnabled(true);
            if (reply->error() != QNetworkReply::NoError) {
                appendLog("❌ Erro ao conectar na API de Diagnóstico.");
                return;
            }
            appendLog("✅ Diagnóstico concluído! Verifique as mensagens acima.");
            fetchDiagnostics(); // Atualiza a lista após testar
        });
    }

    // GET /diag
    void fetchDiagnostics() {
        m_diagList->clear();
        m_diagList->addItem("Carregando...");
        
        auto* reply = m_nam->get(makeRequest("/diag"));
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            m_diagList->clear();
            if (reply->error() != QNetworkReply::NoError) {
                m_diagList->addItem("Erro ao buscar diagnósticos");
                return;
            }
            auto doc = QJsonDocument::fromJson(reply->readAll());
            auto obj = doc.object();
            if (obj["ok"].toBool()) {
                auto files = obj["files"].toArray();
                if (files.isEmpty()) {
                    m_diagList->addItem("Nenhum snapshot disponível.");
                } else {
                    for (const auto& f : files) {
                        m_diagList->addItem(f.toString());
                    }
                }
            }
        });
    }

    void openDiagnostic() {
        auto* item = m_diagList->currentItem();
        if (!item) return;
        QString filename = item->text();
        if (filename.isEmpty() || filename.contains(" ")) return;

        QString base = m_apiBase.trimmed();
        while(base.endsWith('/')) base.chop(1);
        QUrl url(base + "/diag/" + filename);
        QDesktopServices::openUrl(url);
    }

    void saveSessionCookie() {
        QString cookie = m_cookieInput->text().trimmed();
        if (cookie.isEmpty()) {
            appendLog("⚠️ Por favor, cole um cookie válido.");
            return;
        }

        appendLog("🍪 Enviando cookie de sessão ao servidor...");
        QJsonObject cfg;
        cfg["session_cookie"] = cookie;
        
        // Também envia as configs atuais para não sobrescrever com padrão
        cfg["max_apply_per_cycle"] = m_maxApply->value();
        cfg["cycle_wait_minutes"]  = m_cycleWait->value();
        cfg["search_terms"]        = m_termsInput->text().trimmed();

        auto* reply = m_nam->post(makeRequest("/crawler/config"),
                                  QJsonDocument(cfg).toJson());
        connect(reply, &QNetworkReply::finished, this, [this, reply]{
            reply->deleteLater();
            if (reply->error() != QNetworkReply::NoError)
                appendLog("❌ Erro ao salvar cookie: " + reply->errorString());
            else {
                appendLog("✅ Cookie de sessão atualizado! O robô usará sua conta no próximo ciclo.");
                m_cookieInput->clear();
            }
        });
    }
};
