#pragma once
#include <QWidget>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QLineEdit>
#include <QPushButton>
#include <QTableWidget>
#include <QHeaderView>
#include <QSqlDatabase>
#include <QSqlQuery>
#include <QSqlError>
#include <QDateTime>
#include <QDesktopServices>
#include <QUrl>
#include <QClipboard>
#include <QApplication>
#include <QGraphicsDropShadowEffect>

class LinkedInJobsPage : public QWidget {
    Q_OBJECT
public:
    explicit LinkedInJobsPage(const QString& connStr, QWidget* parent = nullptr) 
        : QWidget(parent), m_connStr(connStr), m_currentPage(0), m_pageSize(10) 
    {
        buildUI();
        refreshData();
    }

private slots:
    void refreshData() {
        m_table->setRowCount(0);
        
        QSqlDatabase db = QSqlDatabase::addDatabase("QPSQL", "JobsView_" + QString::number(m_currentPage));
        parseAndConnect(db);

        if (!db.open()) {
            qWarning() << "Falha ao conectar no JobsPage:" << db.lastError().text();
            return;
        }

        QString filter = m_searchEdit->text().trimmed();
        
        // Count total for pagination
        QString countSql = "SELECT COUNT(*) FROM vagas WHERE COALESCE(candidatura_enviada_sucesso, FALSE) = TRUE ";
        if (!filter.isEmpty()) countSql += QString("AND empresa ILIKE '%%1%' ").arg(filter);
        
        QSqlQuery qc(countSql, db);
        int totalRows = 0;
        if (qc.next()) totalRows = qc.value(0).toInt();

        // Main Query with Pagination
        QString sql = "SELECT titulo, empresa, localizacao, data_envio_sucesso, link "
                      "FROM vagas WHERE COALESCE(candidatura_enviada_sucesso, FALSE) = TRUE ";
        
        if (!filter.isEmpty()) {
            sql += QString("AND empresa ILIKE '%%1%' ").arg(filter);
        }
        
        sql += QString("ORDER BY data_envio_sucesso DESC LIMIT %1 OFFSET %2")
                .arg(m_pageSize)
                .arg(m_currentPage * m_pageSize);

        QSqlQuery q(sql, db);
        int row = 0;
        while (q.next()) {
            m_table->insertRow(row);
            
            auto createItem = [](const QString& text) {
                auto* item = new QTableWidgetItem(text);
                item->setTextAlignment(Qt::AlignCenter);
                return item;
            };

            m_table->setItem(row, 0, createItem(q.value(0).toString()));
            m_table->setItem(row, 1, createItem(q.value(1).toString()));
            m_table->setItem(row, 2, createItem(q.value(2).toString()));
            
            QDateTime dt = q.value(3).toDateTime();
            m_table->setItem(row, 3, createItem(dt.isValid() ? dt.toString("dd/MM HH:mm") : "---"));

            // Actions
            auto* actionWidget = new QWidget();
            auto* alay = new QHBoxLayout(actionWidget);
            alay->setContentsMargins(5, 5, 5, 5); alay->setSpacing(8);
            
            QString link = q.value(4).toString();
            
            auto* btnGo = new QPushButton("Candidatar");
            btnGo->setCursor(Qt::PointingHandCursor);
            btnGo->setStyleSheet(
                "QPushButton { background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 #00F260, stop:1 #0575E6); "
                "color: white; border: none; border-radius: 6px; padding: 6px 12px; font-weight: bold; font-size: 11px; }"
                "QPushButton:hover { background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 #0575E6, stop:1 #00F260); }"
            );
            connect(btnGo, &QPushButton::clicked, [link](){ QDesktopServices::openUrl(QUrl(link)); });

            auto* btnCopy = new QPushButton();
            btnCopy->setIcon(style()->standardIcon(QStyle::SP_DialogSaveButton)); // Placeholder for copy icon
            btnCopy->setFixedSize(28, 28);
            btnCopy->setCursor(Qt::PointingHandCursor);
            btnCopy->setToolTip("Copiar Link");
            btnCopy->setStyleSheet("QPushButton { background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.2); border-radius: 14px; color: white; }"
                                   "QPushButton:hover { background: rgba(255,255,255,0.3); }");
            connect(btnCopy, &QPushButton::clicked, [link](){ QApplication::clipboard()->setText(link); });

            alay->addWidget(btnGo);
            alay->addWidget(btnCopy);
            m_table->setCellWidget(row, 4, actionWidget);
            m_table->setRowHeight(row, 60);

            row++;
        }
        
        updatePaginationUI(totalRows);
        db.close();
        QSqlDatabase::removeDatabase(db.connectionName());
    }

    void updatePaginationUI(int total) {
        int totalPages = (total + m_pageSize - 1) / m_pageSize;
        if (totalPages == 0) totalPages = 1;
        
        m_pageInfo->setText(QString("Página %1 de %2").arg(m_currentPage + 1).arg(totalPages));
        m_prevBtn->setEnabled(m_currentPage > 0);
        m_nextBtn->setEnabled(m_currentPage < totalPages - 1);
        m_countLbl->setText(QString("%1 no total").arg(total));
    }

    void nextPage() { m_currentPage++; refreshData(); }
    void prevPage() { m_currentPage--; refreshData(); }

private:
    void buildUI() {
        auto* root = new QVBoxLayout(this);
        root->setContentsMargins(20, 20, 20, 20);
        root->setSpacing(15);

        // Header Moderno
        auto* hdr = new QHBoxLayout();
        auto* title = new QLabel("Histórico de Candidaturas");
        title->setStyleSheet("color: white; font-size: 24px; font-weight: 800; letter-spacing: 1px;");
        
        m_countLbl = new QLabel("0 no total");
        m_countLbl->setStyleSheet("color: #AAA; font-size: 13px; background: rgba(255,255,255,0.05); "
                                  "padding: 5px 15px; border-radius: 12px; border: 1px solid rgba(255,255,255,0.1);");
        
        hdr->addWidget(title);
        hdr->addStretch();
        hdr->addWidget(m_countLbl);
        root->addLayout(hdr);

        // Barra de Busca Glass
        auto* sbox = new QHBoxLayout();
        m_searchEdit = new QLineEdit();
        m_searchEdit->setPlaceholderText("🔍 Filtrar por empresa...");
        m_searchEdit->setStyleSheet(
            "QLineEdit { background: rgba(255,255,255,0.07); border: 1px solid rgba(255,255,255,0.1); "
            "border-radius: 10px; padding: 12px; color: white; font-size: 14px; }"
            "QLineEdit:focus { border: 1px solid #00F260; background: rgba(255,255,255,0.12); }"
        );
        connect(m_searchEdit, &QLineEdit::textChanged, [this](){ m_currentPage = 0; refreshData(); });
        
        auto* btnRefresh = new QPushButton("Atualizar");
        btnRefresh->setCursor(Qt::PointingHandCursor);
        btnRefresh->setStyleSheet(
            "QPushButton { background: #23272E; color: white; padding: 12px 25px; border-radius: 10px; "
            "font-weight: bold; border: 1px solid rgba(255,255,255,0.1); }"
            "QPushButton:hover { background: #2C313C; border: 1px solid #00F260; }"
        );
        connect(btnRefresh, &QPushButton::clicked, this, &LinkedInJobsPage::refreshData);

        sbox->addWidget(m_searchEdit, 1);
        sbox->addWidget(btnRefresh);
        root->addLayout(sbox);

        // Tabela Ultra Moderna
        m_table = new QTableWidget();
        m_table->setColumnCount(5);
        m_table->setHorizontalHeaderLabels({"Vaga", "Empresa", "Local", "Data", "Ações"});
        m_table->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
        m_table->horizontalHeader()->setSectionResizeMode(4, QHeaderView::Fixed);
        m_table->setColumnWidth(4, 160);
        m_table->verticalHeader()->setVisible(false);
        m_table->setShowGrid(false);
        m_table->setSelectionBehavior(QAbstractItemView::SelectRows);
        m_table->setEditTriggers(QAbstractItemView::NoEditTriggers);
        
        m_table->setStyleSheet(
            "QTableWidget { background: rgba(30, 34, 42, 0.6); border-radius: 15px; gridline-color: transparent; "
            "color: #DDD; font-size: 13px; outline: none; border: 1px solid rgba(255,255,255,0.05); }"
            "QTableWidget::item { padding: 15px; border-bottom: 1px solid rgba(255,255,255,0.03); }"
            "QTableWidget::item:selected { background: rgba(0, 242, 96, 0.1); color: #00F260; }"
            "QHeaderView::section { background: rgba(45, 52, 64, 0.8); color: #AAA; padding: 15px; "
            "border: none; font-weight: bold; text-transform: uppercase; font-size: 11px; letter-spacing: 1px; }"
        );
        
        root->addWidget(m_table);

        // Paginação
        auto* footer = new QHBoxLayout();
        m_prevBtn = new QPushButton("◀");
        m_nextBtn = new QPushButton("▶");
        m_pageInfo = new QLabel("Página 1 de 1");
        m_pageInfo->setStyleSheet("color: #777; font-weight: bold; margin: 0 20px;");

        QString navStyle = "QPushButton { background: rgba(255,255,255,0.05); color: white; border-radius: 15px; "
                           "width: 30px; height: 30px; border: 1px solid rgba(255,255,255,0.1); }"
                           "QPushButton:hover { background: #00F260; color: black; }"
                           "QPushButton:disabled { color: #444; background: transparent; border: 1px solid #333; }";
        m_prevBtn->setStyleSheet(navStyle);
        m_nextBtn->setStyleSheet(navStyle);

        connect(m_prevBtn, &QPushButton::clicked, this, &LinkedInJobsPage::prevPage);
        connect(m_nextBtn, &QPushButton::clicked, this, &LinkedInJobsPage::nextPage);

        footer->addStretch();
        footer->addWidget(m_prevBtn);
        footer->addWidget(m_pageInfo);
        footer->addWidget(m_nextBtn);
        footer->addStretch();
        root->addLayout(footer);
    }

    void parseAndConnect(QSqlDatabase& db) {
        QString host, dbName, user, pass; int port = 5432;
        QString uri = m_connStr.trimmed().remove('\"').remove('\'');

        if (uri.startsWith("postgresql://")) {
             QString tmp = uri.mid(13);
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
             QStringList parts = uri.split(';');
             for (auto& p : parts) {
                 int eq = p.indexOf('='); if (eq <= 0) continue;
                 QString k = p.left(eq).trimmed().toLower(); QString v = p.mid(eq+1).trimmed();
                 if (k=="host") host=v; else if (k=="port") port=v.toInt(); else if (k=="database") dbName=v;
                 else if (k=="username") user=v; else if (k=="password") pass=v;
             }
        }
        db.setHostName(host); db.setPort(port); db.setDatabaseName(dbName);
        db.setUserName(user); db.setPassword(pass);
        db.setConnectOptions(host == "localhost" || host == "127.0.0.1" ? "" : "sslmode=require");
    }

    QString m_connStr;
    QTableWidget* m_table;
    QLineEdit* m_searchEdit;
    QLabel* m_countLbl;
    QLabel* m_pageInfo;
    QPushButton* m_prevBtn;
    QPushButton* m_nextBtn;
    int m_currentPage;
    int m_pageSize;
};
