#pragma once
#include <QWidget>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QPushButton>
#include <QScrollArea>
#include <QTimer>
#include <QDateTime>
#include <QPainter>
#include <QPainterPath>
#include <QPropertyAnimation>
#include <QEasingCurve>
#include <QGraphicsDropShadowEffect>
#include <QSqlDatabase>
#include <QSqlQuery>
#include <QSqlError>
#include <QRandomGenerator>
#include <QUrl>
#include <cmath>

// Cores LinkedIn
static const QColor liHeader(0x1B1B1B);
static const QColor liAccent(0x0A66C2);
static const QColor liGreen(0x057642);
static const QColor liRed(0xCC1016);
static const QColor liGold(0xC37D16);
static const QColor liText(0x1B1B1B);
static const QColor liSubText(0x777777);
static const QColor liBorder(0xEEEEEE);

// ── Gráfico de Tendências (Linhas) ────────────────────────
class TrendsChart : public QWidget {
    Q_OBJECT
public:
    struct DayData { QString label; int total; int success; int unavail; int pending; };
    explicit TrendsChart(QWidget* p = nullptr) : QWidget(p) { setMinimumHeight(240); }
    void setData(const QList<DayData>& d) { data = d; update(); }

protected:
    QList<DayData> data;
    void paintEvent(QPaintEvent*) override {
        QPainter p(this); p.setRenderHint(QPainter::Antialiasing);
        if (data.isEmpty()) { p.drawText(rect(), Qt::AlignCenter, "Sem dados históricos."); return; }

        int margin = 40;
        int w = width() - margin * 2;
        int h = height() - margin * 2;
        int maxVal = 1;
        for (auto& d : data) maxVal = std::max({maxVal, d.total, d.success, d.unavail, d.pending});
        maxVal = int(maxVal * 1.2);

        // Grid
        p.setPen(QPen(QColor(240,240,240), 1));
        for (int i = 0; i <= 4; ++i) {
            int y = margin + h - (i * h / 4);
            p.drawLine(margin, y, margin + w, y);
            p.drawText(5, y + 5, QString::number(maxVal * i / 4));
        }

        drawDataLine(p, margin, w, h, maxVal, [](const DayData& d){return d.total;},   liText);
        drawDataLine(p, margin, w, h, maxVal, [](const DayData& d){return d.success;}, liGreen);
        drawDataLine(p, margin, w, h, maxVal, [](const DayData& d){return d.unavail;}, liRed);
        drawDataLine(p, margin, w, h, maxVal, [](const DayData& d){return d.pending;}, liGold);
    }

    void drawDataLine(QPainter& p, int margin, int w, int h, int maxVal, std::function<int(const DayData&)> getter, QColor col) {
        QPainterPath path;
        for (int i = 0; i < data.size(); ++i) {
            int x = margin + (i * w / (data.size() - 1));
            int y = margin + h - (getter(data[i]) * h / maxVal);
            if (i == 0) path.moveTo(x, y); else path.lineTo(x, y);
        }
        p.setPen(QPen(col, 2.5)); p.drawPath(path);
        p.setBrush(col);
        for (int i = 0; i < data.size(); ++i) {
            int x = margin + (i * w / (data.size() - 1));
            int y = margin + h - (getter(data[i]) * h / maxVal);
            p.drawEllipse(QPoint(x, y), 3, 3);
        }
    }
};

// ── Widget de Gauge (Relógio) ──────────────────────────────
class GaugeWidget : public QWidget {
    Q_OBJECT
    Q_PROPERTY(qreal angle READ angle WRITE setAngle)
public:
    GaugeWidget(QWidget* p = nullptr) : QWidget(p), m_angle(-120), m_online(false), m_minsAgo(0) {
        setMinimumSize(180, 180);
    }
    qreal angle() const { return m_angle; }
    void  setAngle(qreal a) { m_angle = a; update(); }
    void setStatus(bool online, float val, int minsAgo) {
        m_online = online; m_minsAgo = minsAgo;
        QPropertyAnimation* anim = new QPropertyAnimation(this, "angle");
        anim->setDuration(1200); anim->setStartValue(m_angle);
        anim->setEndValue(-120.0f + (val * 150.0f / 100.0f));
        anim->setEasingCurve(QEasingCurve::OutCubic);
        anim->start(QAbstractAnimation::DeleteWhenStopped);
    }
protected:
    void paintEvent(QPaintEvent*) override {
        QPainter p(this); p.setRenderHint(QPainter::Antialiasing);
        double side = qMin(width(), height()) - 20.0;
        QRectF arcRect(width()/2.0 - side/2, height()/2.0 - side/2, side, side);
        p.setPen(QPen(QColor(230,230,230), 10, Qt::SolidLine, Qt::RoundCap));
        p.drawArc(arcRect, 210 * 16, -240 * 16);
        QColor arcCol = m_online ? liGreen : liRed;
        p.setPen(QPen(arcCol, 10, Qt::SolidLine, Qt::RoundCap));
        double span = (m_angle + 120) * (240.0 / 150.0);
        p.drawArc(arcRect, 210 * 16, -span * 16);
        p.save(); p.translate(width()/2.0, height()/2.0); p.rotate(m_angle);
        p.setPen(QPen(QColor(40,40,40), 4, Qt::SolidLine, Qt::RoundCap));
        p.drawLine(0, 0, 0, -side/2 + 15); p.setBrush(QColor(40,40,40));
        p.drawEllipse(QPointF(0,0), 6, 6); p.restore();
        p.setPen(arcCol); p.setFont(QFont("Segoe UI", 12, QFont::Bold));
        p.drawText(arcRect.adjusted(0,0,0,-20), Qt::AlignBottom | Qt::AlignHCenter, m_online ? "ON" : "OFF");
    }
private:
    qreal m_angle; bool m_online; int m_minsAgo;
};

// ── Donut Chart ───────────────────────────────────────────
class DonutChart : public QWidget {
    Q_OBJECT
public:
    struct Slice { QString label; int val; QColor col; };
    explicit DonutChart(QWidget* p = nullptr) : QWidget(p) {}
    void setSlices(const QList<Slice>& s) { slices = s; update(); }
protected:
    QList<Slice> slices;
    void paintEvent(QPaintEvent*) override {
        QPainter p(this); p.setRenderHint(QPainter::Antialiasing);
        double total = 0; for (auto& s : slices) total += s.val;
        if (total == 0) return;
        QRectF r = rect().adjusted(10,10,-10,-10);
        double start = 90;
        for (auto& s : slices) {
            double span = (s.val / total) * 360.0;
            p.setPen(Qt::NoPen); p.setBrush(s.col);
            p.drawPie(r, start * 16, -span * 16);
            start -= span;
        }
        p.setBrush(QColor("#EAE8E0")); p.drawEllipse(r.center(), r.width()*0.3, r.height()*0.3);
        p.setPen(liText); p.setFont(QFont("Segoe UI", 14, QFont::Black));
        int perc = total > 0 ? (slices[0].val * 100 / total) : 0;
        p.drawText(r, Qt::AlignCenter, QString("%1%").arg(perc));
    }
};

// ── StatMetricCard ────────────────────────────────────────
class StatMetricCard : public QWidget {
    Q_OBJECT
public:
    StatMetricCard(const QString& title, const QString& sub, const QColor& acc, QWidget* p = nullptr) : QWidget(p) {
        setMinimumWidth(160); setAttribute(Qt::WA_StyledBackground);
        setStyleSheet("background:white; border-radius:10px; border:1px solid #D8D8D8;");
        auto* lay = new QVBoxLayout(this); lay->setContentsMargins(15,12,15,12); lay->setSpacing(2);
        auto* t = new QLabel(title); t->setStyleSheet("color:#777; font-size:11px; font-weight:bold;");
        m_val = new QLabel("—"); m_val->setStyleSheet(QString("color:%1; font-size:26px; font-weight:900;").arg(acc.name()));
        auto* s = new QLabel(sub); s->setStyleSheet("color:#888; font-size:9px;"); s->setWordWrap(true);
        lay->addWidget(t); lay->addWidget(m_val); lay->addWidget(s);
    }
    void setValue(int v) { m_val->setText(QLocale().toString(v)); }
private:
    QLabel* m_val;
};

// ── LinkedInDashboardPage ──────────────────────────────────
class LinkedInDashboardPage : public QWidget {
    Q_OBJECT
public:
    explicit LinkedInDashboardPage(const QString& conn, QWidget* p = nullptr) : QWidget(p), m_connStr(conn) {
        auto* mainLay = new QVBoxLayout(this); mainLay->setContentsMargins(0,0,0,0); mainLay->setSpacing(0);
        m_statusIndicator = new QLabel("Aguardando banco de dados...");
        m_statusIndicator->setStyleSheet("background:#333; color:#EEE; padding:6px 20px; font-size:11px; font-family:Consolas;");
        mainLay->addWidget(m_statusIndicator);

        auto* scroll = new QScrollArea(this); scroll->setWidgetResizable(true); scroll->setFrameShape(QFrame::NoFrame);
        auto* contentWidget = new QWidget(); auto* root = new QVBoxLayout(contentWidget);
        root->setContentsMargins(25,25,25,25); root->setSpacing(20);
        scroll->setWidget(contentWidget); mainLay->addWidget(scroll);

        // Header
        auto* header = new QWidget(); header->setFixedHeight(70); header->setStyleSheet(QString("background:%1; border-radius:8px;").arg(liHeader.name()));
        auto* hlay = new QHBoxLayout(header);
        auto* titleV = new QVBoxLayout();
        auto* htitle = new QLabel("AppLinkdin Real-Time Dashboard"); htitle->setStyleSheet("color:white; font-size:18px; font-weight:bold;");
        m_lastUpdate = new QLabel("Última leitura: ---"); m_lastUpdate->setStyleSheet("color:rgba(255,255,255,150); font-size:10px;");
        titleV->addWidget(htitle); titleV->addWidget(m_lastUpdate);
        hlay->addLayout(titleV); hlay->addStretch();
        m_refreshBtn = new QPushButton("Atualizar Agora"); m_refreshBtn->setStyleSheet("background:#0A66C2; color:white; padding:8px 15px; border-radius:5px; font-weight:bold;");
        connect(m_refreshBtn, &QPushButton::clicked, this, &LinkedInDashboardPage::refreshData);
        hlay->addWidget(m_refreshBtn);
        root->addWidget(header);

        // Cards
        auto* crow = new QHBoxLayout(); crow->setSpacing(10);
        m_cardTotal = new StatMetricCard("Total", "Volume no banco", liText, this);
        m_cardSuccess = new StatMetricCard("Sucesso", "Aplicações ok", liGreen, this);
        m_cardToday = new StatMetricCard("Hoje", "Enviadas hoje", liGreen, this);
        m_cardUnavail = new StatMetricCard("Erro", "Indisponíveis", liRed, this);
        m_cardPending = new StatMetricCard("Pendente", "A processar", liGold, this);
        crow->addWidget(m_cardTotal); crow->addWidget(m_cardSuccess); crow->addWidget(m_cardToday); crow->addWidget(m_cardUnavail); crow->addWidget(m_cardPending);
        root->addLayout(crow);

        // Middle
        auto* midCard = new QWidget(); midCard->setStyleSheet("background:#F8F9FA; border-radius:12px; border:1px solid #DDD;");
        auto* mlay = new QHBoxLayout(midCard); mlay->setContentsMargins(20,20,20,20);
        auto* infoV = new QVBoxLayout();
        auto* t1 = new QLabel("Taxa de Sucesso"); t1->setStyleSheet("font-size:16px; font-weight:bold;");
        auto* t2 = new QLabel("Status por categoria"); t2->setStyleSheet("color:#666; font-size:11px;");
        infoV->addWidget(t1); infoV->addWidget(t2); infoV->addStretch();
        mlay->addLayout(infoV, 1);
        
        m_gauge = new GaugeWidget(this);
        m_robotDetail = new QLabel("Heartbeat..."); m_robotDetail->setStyleSheet("color:#555; font-size:10px;"); m_robotDetail->setAlignment(Qt::AlignCenter);
        auto* gv = new QVBoxLayout(); gv->addWidget(m_gauge); gv->addWidget(m_robotDetail);
        mlay->addLayout(gv, 2);

        m_donut = new DonutChart(this); m_donut->setFixedSize(140,140);
        mlay->addWidget(m_donut, 2);
        root->addWidget(midCard);

        // History
        m_trends = new TrendsChart(this);
        root->addWidget(m_trends);

        QTimer::singleShot(500, this, &LinkedInDashboardPage::refreshData);
    }

private slots:
    void refreshData() {
        m_refreshBtn->setEnabled(false);
        QSqlDatabase db = QSqlDatabase::addDatabase("QPSQL", "DashConn_" + QString::number(QRandomGenerator::global()->generate()));
        
        // Parser Manual (Fiel à String Original)
        QString host, dbName, user, pass; int port = 5432;
        if (m_connStr.startsWith("postgresql://")) {
            QString tmp = m_connStr.mid(13); // pula "postgresql://"
            int atIdx = tmp.lastIndexOf('@');
            int slashIdx = tmp.lastIndexOf('/');
            if (atIdx != -1 && slashIdx != -1) {
                QString auth = tmp.left(atIdx);
                host = tmp.mid(atIdx + 1, slashIdx - atIdx - 1);
                dbName = tmp.mid(slashIdx + 1);
                int colonIdx = auth.indexOf(':');
                if (colonIdx != -1) {
                    user = auth.left(colonIdx);
                    pass = auth.mid(colonIdx + 1);
                }
            }
        } else {
            for (const QString& part : m_connStr.split(';', Qt::SkipEmptyParts)) {
                int eq = part.indexOf('='); if (eq <= 0) continue;
                QString k = part.left(eq).trimmed().toLower(); QString v = part.mid(eq + 1).trimmed();
                if (k=="host") host=v; else if (k=="port") port=v.toInt(); else if (k=="database") dbName=v;
                else if (k=="username") user=v; else if (k=="password") pass=v;
            }
        }
        db.setHostName(host); db.setPort(port); db.setDatabaseName(dbName); db.setUserName(user); db.setPassword(pass);
        db.setConnectOptions("sslmode=require");

        if (!db.open()) {
            m_statusIndicator->setText("❌ Erro DB: " + db.lastError().text());
            m_statusIndicator->setStyleSheet("background:#991B1B; color:white; padding:6px 20px;");
            m_refreshBtn->setEnabled(true); return;
        }

        m_statusIndicator->setText("✅ Conectado ao Render: " + host);
        m_statusIndicator->setStyleSheet("background:#065F46; color:white; padding:6px 20px;");

        auto q = [&](const QString& sql) { QSqlQuery qry(db); qry.exec(sql); return qry.next() ? qry.value(0).toInt() : 0; };
        int total = q("SELECT COUNT(*) FROM vagas");
        int success = q("SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_enviada_sucesso,FALSE)=TRUE");
        int today = q("SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_enviada_sucesso,FALSE)=TRUE AND DATE(data_envio_sucesso)=CURRENT_DATE");
        int unavail = q("SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_indisponivel,FALSE)=TRUE");
        int pending = q("SELECT COUNT(*) FROM vagas WHERE candidatura_simplificada=TRUE AND COALESCE(candidatura_enviada,FALSE)=FALSE AND COALESCE(candidatura_enviada_sucesso,FALSE)=FALSE AND COALESCE(candidatura_indisponivel,FALSE)=FALSE");

        m_cardTotal->setValue(total); m_cardSuccess->setValue(success); m_cardToday->setValue(today); m_cardUnavail->setValue(unavail); m_cardPending->setValue(pending);
        m_donut->setSlices({{ "OK", success, liGreen }, { "Error", unavail, liRed }, { "Wait", pending, liGold }});

        QSqlQuery hbQ("SELECT last_heartbeat FROM crawler_jobs ORDER BY last_heartbeat DESC LIMIT 1", db);
        if (hbQ.next()) {
            int mins = int(hbQ.value(0).toDateTime().secsTo(QDateTime::currentDateTime()) / 60);
            m_gauge->setStatus(mins < 5, mins < 5 ? 80 : 10, mins);
            m_robotDetail->setText(mins < 5 ? "Robô Ativo" : QString("Offline há %1 min").arg(mins));
        }

        QList<TrendsChart::DayData> trendData;
        for (int i = 6; i >= 0; --i) {
            QString ds = QDate::currentDate().addDays(-i).toString("yyyy-MM-dd");
            auto qd = [&](const QString& f) { QSqlQuery qry(db); qry.exec(QString("SELECT COUNT(*) FROM vagas WHERE data_cadastro::date <= '%1'::date %2").arg(ds, f)); return qry.next() ? qry.value(0).toInt() : 0; };
            trendData.append({ ds.mid(5), qd(""), qd("AND COALESCE(candidatura_enviada_sucesso,FALSE)=TRUE"), qd("AND COALESCE(candidatura_indisponivel,FALSE)=TRUE"), 0 });
        }
        m_trends->setData(trendData);

        m_lastUpdate->setText("Leitura: " + QDateTime::currentDateTime().toString("HH:mm:ss"));
        db.close(); QSqlDatabase::removeDatabase(db.connectionName());
        m_refreshBtn->setEnabled(true);
    }

private:
    QString m_connStr;
    StatMetricCard *m_cardTotal, *m_cardSuccess, *m_cardToday, *m_cardUnavail, *m_cardPending;
    DonutChart* m_donut; GaugeWidget* m_gauge; TrendsChart* m_trends;
    QLabel *m_statusIndicator, *m_lastUpdate, *m_robotDetail;
    QPushButton* m_refreshBtn;
};
