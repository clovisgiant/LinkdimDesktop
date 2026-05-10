#pragma once
#include <QWidget>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QPainter>
#include <QPainterPath>
#include <QTimer>
#include <QPushButton>
#include <QGraphicsDropShadowEffect>
#include <QDateTime>
#include <QSqlDatabase>
#include <QSqlQuery>
#include <QSqlError>
#include <QPropertyAnimation>
#include <QEasingCurve>
#include <QFrame>
#include <cmath>

// ── Paleta LinkedIn Dashboard (clara/profissional) ──────────
static const QColor liBg        ("#F3F2EE");
static const QColor liCard      ("#FFFFFF");
static const QColor liHeader    ("#1A4A6B");   // azul petróleo
static const QColor liAccent    ("#E87722");   // laranja
static const QColor liGreen     ("#2E7D32");
static const QColor liRed       ("#C62828");
static const QColor liGold      ("#C07800");
static const QColor liText      ("#1B1B1B");
static const QColor liSubText   ("#6B6B6B");
static const QColor liBorder    ("#D8D8D8");

// ─────────────────────────────────────────────────────────────
// DonutChart — gráfico de rosca customizado
// ─────────────────────────────────────────────────────────────
class DonutChart : public QWidget {
    Q_OBJECT
    Q_PROPERTY(qreal anim READ anim WRITE setAnim)
public:
    struct Slice { QString label; int value; QColor color; };

    DonutChart(QWidget* p = nullptr) : QWidget(p), m_anim(0) {
        setMinimumSize(180, 180);
        auto* an = new QPropertyAnimation(this, "anim");
        an->setDuration(1200); an->setStartValue(0.0); an->setEndValue(1.0);
        an->setEasingCurve(QEasingCurve::OutCubic);
        an->start(QAbstractAnimation::DeleteWhenStopped);
    }
    void setSlices(const QList<Slice>& s) { m_slices = s; update(); }
    qreal anim() const { return m_anim; }
    void  setAnim(qreal v) { m_anim = v; update(); }

protected:
    void paintEvent(QPaintEvent*) override {
        if (m_slices.isEmpty()) return;
        QPainter p(this);
        p.setRenderHint(QPainter::Antialiasing);
        QRectF r = rect(); r.adjust(10,10,-10,-10);
        double side = qMin(r.width(), r.height());
        QRectF arc(r.left()+(r.width()-side)/2, r.top()+(r.height()-side)/2, side, side);
        QRectF inner = arc.adjusted(side*0.28,side*0.28,-side*0.28,-side*0.28);

        int total = 0; for (auto& s : m_slices) total += s.value;
        if (total == 0) return;

        double start = -90.0;
        for (auto& sl : m_slices) {
            double span = 360.0 * sl.value / total * m_anim;
            p.setBrush(sl.color); p.setPen(Qt::NoPen);
            QPainterPath path;
            path.moveTo(arc.center());
            path.arcTo(arc, start, span);
            path.closeSubpath();
            QPainterPath hole; hole.addEllipse(inner);
            p.drawPath(path.subtracted(hole));
            start += span;
        }

        // Percentual central
        if (total > 0) {
            int success = 0; for (auto& s : m_slices) if (s.color == liGreen) success = s.value;
            QString pct = QString("%1%").arg(int(100.0*success/total));
            p.setPen(liText);
            QFont f; f.setPixelSize(int(side*0.18)); f.setBold(true); p.setFont(f);
            p.drawText(arc, Qt::AlignCenter, pct);
        }
    }
private:
    QList<Slice> m_slices;
    qreal m_anim;
};

// ─────────────────────────────────────────────────────────────
// GaugeWidget — mostrador "Robô online/offline"
// ─────────────────────────────────────────────────────────────
class GaugeWidget : public QWidget {
    Q_OBJECT
    Q_PROPERTY(qreal angle READ angle WRITE setAngle)
public:
    GaugeWidget(QWidget* p = nullptr) : QWidget(p), m_angle(-100), m_online(false) {
        setMinimumSize(150,150);
    }
    void setOnline(bool on, int minsAgo = 0) {
        m_online = on; m_minsAgo = minsAgo;
        auto* an = new QPropertyAnimation(this, "angle");
        an->setDuration(900);
        an->setStartValue(m_angle);
        an->setEndValue(on ? 20.0 : -100.0);
        an->setEasingCurve(QEasingCurve::OutBounce);
        an->start(QAbstractAnimation::DeleteWhenStopped);
    }
    qreal angle() const { return m_angle; }
    void  setAngle(qreal a) { m_angle = a; update(); }

protected:
    void paintEvent(QPaintEvent*) override {
        QPainter p(this); p.setRenderHint(QPainter::Antialiasing);
        double side = qMin(width(), height()) - 20.0;
        QRectF arc(width()/2.0-side/2, height()/2.0-side/2, side, side);

        // Aro exterior
        p.setPen(QPen(liBorder, 6)); p.setBrush(Qt::NoBrush);
        p.drawArc(arc, 210*16, -240*16);

        // Arco colorido
        QColor arcCol = m_online ? liGreen : liRed;
        p.setPen(QPen(arcCol, 6));
        p.drawArc(arc, 210*16, int(-120*16*(m_angle+100)/120.0));

        // Agulha
        QPointF center(width()/2.0, height()/2.0+side*0.05);
        double rad = (m_angle) * M_PI / 180.0;
        double len = side * 0.38;
        QPointF tip(center.x() + len*cos(rad), center.y() + len*sin(rad));
        p.setPen(QPen(liText, 2.5)); p.drawLine(center, tip);
        p.setBrush(liText); p.setPen(Qt::NoPen);
        p.drawEllipse(center, 5.0, 5.0);

        // Label
        p.setPen(m_online ? liGreen : liRed);
        QFont f; f.setPixelSize(11); f.setBold(true); p.setFont(f);
        p.drawText(QRectF(0, height()-36, width(), 18), Qt::AlignCenter,
                   m_online ? "ONLINE" : "OFF");
        if (!m_online && m_minsAgo > 0) {
            p.setPen(liSubText);
            QFont f2; f2.setPixelSize(9); p.setFont(f2);
            p.drawText(QRectF(0, height()-20, width(), 16), Qt::AlignCenter,
                       QString("último: %1 min").arg(m_minsAgo));
        }
    }
private:
    qreal m_angle; bool m_online; int m_minsAgo = 0;
};

// ─────────────────────────────────────────────────────────────
// StatMetricCard — card individual de métrica
// ─────────────────────────────────────────────────────────────
class StatMetricCard : public QWidget {
public:
    QLabel* valueLabel;
    QLabel* titleLabel;
    QLabel* subLabel;

    StatMetricCard(const QString& title, const QString& sub,
                   const QColor& accent, QWidget* p = nullptr) : QWidget(p)
    {
        setAttribute(Qt::WA_StyledBackground);
        setStyleSheet("background:#FFFFFF; border-radius:10px; border:1px solid #D8D8D8;");
        auto* sh = new QGraphicsDropShadowEffect(this);
        sh->setBlurRadius(14); sh->setOffset(0,3); sh->setColor(QColor(0,0,0,25));
        setGraphicsEffect(sh);

        auto* lay = new QVBoxLayout(this);
        lay->setContentsMargins(20,16,20,16); lay->setSpacing(4);

        titleLabel = new QLabel(title);
        titleLabel->setStyleSheet(QString("color:%1;font-size:12px;font-weight:600;").arg(liSubText.name()));

        valueLabel = new QLabel("—");
        valueLabel->setStyleSheet(QString("color:%1;font-size:32px;font-weight:800;").arg(accent.name()));

        subLabel = new QLabel(sub);
        subLabel->setStyleSheet(QString("color:%1;font-size:11px;").arg(liSubText.name()));
        subLabel->setWordWrap(true);

        lay->addWidget(titleLabel);
        lay->addWidget(valueLabel);
        lay->addWidget(subLabel);
        setMinimumHeight(110);
        setCursor(Qt::PointingHandCursor);
    }

    void setValue(int v) {
        valueLabel->setText(QLocale(QLocale::Portuguese).toString(v));
    }
};

// ─────────────────────────────────────────────────────────────
// LinkedInDashboardPage — página principal de monitoramento
// ─────────────────────────────────────────────────────────────
class LinkedInDashboardPage : public QWidget {
    Q_OBJECT
public:
    explicit LinkedInDashboardPage(const QString& connStr, QWidget* parent = nullptr)
        : QWidget(parent), m_connStr(connStr)
    {
        setAttribute(Qt::WA_StyledBackground);
        setStyleSheet(QString("background:%1;").arg(liBg.name()));
        buildUI();

        // Atualiza a cada 30s
        auto* timer = new QTimer(this);
        connect(timer, &QTimer::timeout, this, &LinkedInDashboardPage::refreshData);
        timer->start(30000);
        QTimer::singleShot(300, this, &LinkedInDashboardPage::refreshData);
    }

private:
    QString      m_connStr;
    StatMetricCard* m_cardTotal   = nullptr;
    StatMetricCard* m_cardSuccess = nullptr;
    StatMetricCard* m_cardToday   = nullptr;
    StatMetricCard* m_cardUnavail = nullptr;
    StatMetricCard* m_cardPending = nullptr;
    DonutChart*     m_donut       = nullptr;
    GaugeWidget*    m_gauge       = nullptr;
    QLabel*         m_lastUpdate  = nullptr;
    QLabel*         m_robotStatus = nullptr;
    QLabel*         m_robotDetail = nullptr;
    QPushButton*    m_refreshBtn  = nullptr;

    void buildUI() {
        auto* root = new QVBoxLayout(this);
        root->setContentsMargins(0,0,0,0); root->setSpacing(0);

        // ── Header azul ──────────────────────────────────────
        auto* header = new QWidget(); header->setFixedHeight(80);
        header->setAttribute(Qt::WA_StyledBackground);
        header->setStyleSheet(QString("background:%1;").arg(liHeader.name()));
        auto* hlay = new QHBoxLayout(header);
        hlay->setContentsMargins(32,0,32,0);

        auto* htxt = new QVBoxLayout();
        auto* htag = new QLabel("MONITORAMENTO EM TEMPO REAL");
        htag->setStyleSheet("color:rgba(255,255,255,160);font-size:10px;letter-spacing:1px;");
        auto* htitle = new QLabel("AppLinkdin Dashboard");
        htitle->setStyleSheet("color:#FFFFFF;font-size:22px;font-weight:bold;");
        htxt->addWidget(htag); htxt->addWidget(htitle);

        m_robotStatus = new QLabel("● Automação offline");
        m_robotStatus->setStyleSheet("color:#FFAA44;font-size:12px;font-weight:bold;"
                                     "background:rgba(0,0,0,40);border-radius:10px;padding:4px 12px;");

        hlay->addLayout(htxt);
        hlay->addWidget(m_robotStatus);
        hlay->addStretch();

        m_refreshBtn = new QPushButton("Atualizar agora");
        m_refreshBtn->setCursor(Qt::PointingHandCursor);
        m_refreshBtn->setStyleSheet(QString(
            "QPushButton{background:%1;color:#FFF;border:none;border-radius:8px;"
            "padding:10px 20px;font-weight:bold;font-size:13px;}"
            "QPushButton:hover{background:%2;}")
            .arg(liAccent.name()).arg(liAccent.lighter(115).name()));
        connect(m_refreshBtn, &QPushButton::clicked, this, &LinkedInDashboardPage::refreshData);

        auto* infoV = new QVBoxLayout();
        m_lastUpdate = new QLabel("Última leitura: —");
        m_lastUpdate->setStyleSheet("color:rgba(255,255,255,170);font-size:11px;");
        infoV->addWidget(m_refreshBtn); infoV->addWidget(m_lastUpdate);
        infoV->setAlignment(m_lastUpdate, Qt::AlignRight);
        hlay->addLayout(infoV);
        root->addWidget(header);

        // Sub-header descritivo
        auto* subBar = new QLabel("  Fluxo end-to-end das candidaturas com dados diretos do PostgreSQL.");
        subBar->setFixedHeight(30);
        subBar->setStyleSheet(QString("background:%1;color:%2;font-size:12px;")
                              .arg(liHeader.darker(115).name()).arg("rgba(255,255,255,180)"));
        root->addWidget(subBar);

        // ── Área de conteúdo (scrollable) ───────────────────
        auto* content = new QWidget();
        auto* clay = new QVBoxLayout(content);
        clay->setContentsMargins(28,24,28,24); clay->setSpacing(20);

        // 5 cards de métricas
        auto* cardsRow = new QHBoxLayout(); cardsRow->setSpacing(14);
        m_cardTotal   = new StatMetricCard("Total de vagas",    "Volume atual no banco",      liText,    this);
        m_cardSuccess = new StatMetricCard("Enviadas com sucesso","Aplicações finalizadas",   liGreen,   this);
        m_cardToday   = new StatMetricCard("Sucesso hoje",       "Vagas enviadas com sucesso no dia", liGreen, this);
        m_cardUnavail = new StatMetricCard("Indisponíveis",      "Com bloqueio ou erro",      liRed,     this);
        m_cardPending = new StatMetricCard("Pendentes",          "Ainda não processadas",     liGold,    this);
        cardsRow->addWidget(m_cardTotal);
        cardsRow->addWidget(m_cardSuccess);
        cardsRow->addWidget(m_cardToday);
        cardsRow->addWidget(m_cardUnavail);
        cardsRow->addWidget(m_cardPending);
        clay->addLayout(cardsRow);

        // Linha inferior: taxa de sucesso + gauge + donut
        auto* bottomCard = new QWidget();
        bottomCard->setAttribute(Qt::WA_StyledBackground);
        bottomCard->setStyleSheet("background:#EAE8E0;border-radius:12px;");
        auto* blay = new QHBoxLayout(bottomCard);
        blay->setContentsMargins(24,20,24,20); blay->setSpacing(30);

        // Texto esquerdo
        auto* leftV = new QVBoxLayout();
        auto* taxaTitle = new QLabel("Taxa de sucesso");
        taxaTitle->setStyleSheet("color:#1B1B1B;font-size:18px;font-weight:bold;");
        auto* taxaSub = new QLabel("<b>Distribuição por status de vagas no ciclo.</b>");
        taxaSub->setStyleSheet("color:#444;font-size:12px;");
        auto* taxaDesc = new QLabel("Pie chart: Enviadas com sucesso, Indisponíveis e Pendentes.");
        taxaDesc->setStyleSheet("color:#777;font-size:11px;");
        auto* perfBadge = new QLabel("Performance média");
        perfBadge->setStyleSheet("background:#C07800;color:#FFF;border-radius:8px;"
                                 "padding:3px 10px;font-size:11px;font-weight:bold;");
        perfBadge->setFixedWidth(130);
        leftV->addWidget(taxaTitle); leftV->addWidget(taxaSub);
        leftV->addWidget(taxaDesc); leftV->addSpacing(12);
        leftV->addWidget(perfBadge); leftV->addStretch();
        blay->addLayout(leftV, 2);

        // Gauge central
        auto* gaugeV = new QVBoxLayout();
        gaugeV->setAlignment(Qt::AlignCenter);
        m_gauge = new GaugeWidget(this);
        m_gauge->setFixedSize(160,160);
        m_robotDetail = new QLabel("Sem heartbeat recente no PostgreSQL.");
        m_robotDetail->setStyleSheet("color:#777;font-size:10px;");
        m_robotDetail->setAlignment(Qt::AlignCenter);
        m_robotDetail->setWordWrap(true);
        gaugeV->addWidget(m_gauge, 0, Qt::AlignCenter);
        gaugeV->addWidget(m_robotDetail);
        auto* gaugeLabel = new QLabel("Robô offline");
        gaugeLabel->setAlignment(Qt::AlignCenter);
        gaugeLabel->setStyleSheet("color:#333;font-size:12px;font-weight:bold;");
        gaugeV->insertWidget(0, gaugeLabel);
        blay->addLayout(gaugeV, 2);

        // Donut + legenda
        auto* donutV = new QVBoxLayout();
        donutV->setAlignment(Qt::AlignVCenter);
        m_donut = new DonutChart(this);
        m_donut->setFixedSize(170,170);

        auto* legendV = new QVBoxLayout(); legendV->setSpacing(6);
        auto mkLeg = [&](const QString& lbl, const QColor& col) {
            auto* row = new QHBoxLayout(); row->setSpacing(8);
            auto* dot = new QLabel("●");
            dot->setStyleSheet(QString("color:%1;font-size:14px;").arg(col.name()));
            auto* txt = new QLabel(lbl);
            txt->setStyleSheet("color:#333;font-size:12px;");
            row->addWidget(dot); row->addWidget(txt); row->addStretch();
            legendV->addLayout(row);
        };
        mkLeg("Enviadas com sucesso", liGreen);
        mkLeg("Indisponíveis",        liRed);
        mkLeg("Pendentes",            liGold);

        auto* donutRow = new QHBoxLayout();
        donutRow->addWidget(m_donut, 0, Qt::AlignVCenter);
        donutRow->addLayout(legendV);
        donutV->addLayout(donutRow);
        blay->addLayout(donutV, 3);

        clay->addWidget(bottomCard);
        clay->addStretch();
        root->addWidget(content, 1);
    }

    // ── Conecta ao PostgreSQL (suporta 2 formatos) ───────────
    // Formato 1 (Render/URL): postgresql://user:pass@host:5432/db
    // Formato 2 (Npgsql/C#) : Host=...;Port=...;Database=...;Username=...;Password=...
    bool connectDB(QSqlDatabase& db) {
        static int cnt = 0;
        QString name = QString("li_dash_%1").arg(++cnt);
        db = QSqlDatabase::addDatabase("QPSQL", name);

        QString host, dbName, user, pass;
        int port = 5432;

        QUrl url(m_connStr);
        if (url.isValid() && url.scheme().startsWith("postgres")) {
            // ── Formato URL ──────────────────────────────────
            host   = url.host();
            port   = url.port(5432);
            dbName = url.path().mid(1); // remove '/'
            user   = url.userName();
            pass   = url.password();
        } else {
            // ── Formato key=value (Npgsql / C# style) ────────
            for (const QString& part : m_connStr.split(';', Qt::SkipEmptyParts)) {
                int eq = part.indexOf('=');
                if (eq <= 0) continue;
                QString k = part.left(eq).trimmed().toLower();
                QString v = part.mid(eq + 1).trimmed();
                if      (k == "host"   || k == "server")                    host   = v;
                else if (k == "port")                                        port   = v.toInt();
                else if (k == "database" || k == "db")                      dbName = v;
                else if (k == "username" || k == "user id" || k == "uid")   user   = v;
                else if (k == "password" || k == "pwd")                     pass   = v;
            }
        }

        if (host.isEmpty() || dbName.isEmpty()) {
            qWarning() << "[LINKDIM] Connection string inválida:" << m_connStr.left(60);
            return false;
        }

        db.setHostName(host);
        db.setPort(port);
        db.setDatabaseName(dbName);
        db.setUserName(user);
        db.setPassword(pass);

        qDebug() << "[LINKDIM] Conectando em" << host << port << dbName << "user=" << user;

        // Tenta sem SSL primeiro (banco local), depois com SSL (Render.com)
        db.setConnectOptions("");
        if (db.open()) return true;

        db.setConnectOptions("sslmode=require");
        if (db.open()) return true;

        qWarning() << "[LINKDIM] Falha ao conectar:" << db.lastError().text();
        return false;
    }

private slots:
    void refreshData() {
        m_refreshBtn->setEnabled(false);
        m_refreshBtn->setText("Atualizando...");

        QString dbConnName;
        {
            QSqlDatabase db;
            if (!connectDB(db)) {
                m_refreshBtn->setEnabled(true);
                m_refreshBtn->setText("Atualizar agora");
                return;
            }
            dbConnName = db.connectionName();

            auto q = [&](const QString& sql) {
                QSqlQuery qry(db); qry.exec(sql);
                if (qry.next()) return qry.value(0).toInt();
                return 0;
            };

            int total   = q("SELECT COUNT(*) FROM vagas");
            int success = q("SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_enviada_sucesso,FALSE)=TRUE");
            int today   = q("SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_enviada_sucesso,FALSE)=TRUE AND DATE(data_envio_sucesso)=CURRENT_DATE");
            int unavail = q("SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_indisponivel,FALSE)=TRUE");
            int pending = q("SELECT COUNT(*) FROM vagas WHERE candidatura_simplificada=TRUE AND COALESCE(candidatura_enviada,FALSE)=FALSE AND COALESCE(candidatura_enviada_sucesso,FALSE)=FALSE AND COALESCE(candidatura_indisponivel,FALSE)=FALSE");

            // Heartbeat do robô
            QSqlQuery hbQ(db);
            hbQ.exec("SELECT last_heartbeat FROM crawler_jobs ORDER BY last_heartbeat DESC LIMIT 1");
            bool robotOnline = false; int minsAgo = 9999;
            if (hbQ.next() && !hbQ.value(0).isNull()) {
                QDateTime hb = hbQ.value(0).toDateTime();
                minsAgo = int(hb.secsTo(QDateTime::currentDateTime()) / 60);
                robotOnline = (minsAgo < 5);
            }

            db.close();

            // Atualiza UI
            m_cardTotal->setValue(total);
            m_cardSuccess->setValue(success);
            m_cardToday->setValue(today);
            m_cardUnavail->setValue(unavail);
            m_cardPending->setValue(pending);

            // Donut
            m_donut->setSlices({
                {QLocale().toString(success), success, liGreen},
                {QLocale().toString(unavail), unavail, liRed},
                {QLocale().toString(pending), pending, liGold}
            });

            // Gauge
            m_gauge->setOnline(robotOnline, minsAgo);
            if (robotOnline) {
                m_robotStatus->setText("● Automação online");
                m_robotStatus->setStyleSheet("color:#44FF88;font-size:12px;font-weight:bold;"
                                             "background:rgba(0,0,0,40);border-radius:10px;padding:4px 12px;");
                m_robotDetail->setText("Robô ativo!");
            } else {
                m_robotStatus->setText("● Automação offline");
                m_robotStatus->setStyleSheet("color:#FFAA44;font-size:12px;font-weight:bold;"
                                             "background:rgba(0,0,0,40);border-radius:10px;padding:4px 12px;");
                QString detail = (minsAgo < 9999)
                    ? QString("Último heartbeat: %1 min.").arg(minsAgo)
                    : "Sem heartbeat recente no PostgreSQL.";
                m_robotDetail->setText(detail);
            }

            m_lastUpdate->setText("Última leitura: " +
                QDateTime::currentDateTime().toString("dd/MM/yyyy, HH:mm"));
        }

        QSqlDatabase::removeDatabase(dbConnName);
        m_refreshBtn->setEnabled(true);
        m_refreshBtn->setText("Atualizar agora");
    }
};
