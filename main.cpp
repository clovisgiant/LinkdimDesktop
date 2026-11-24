// ============================================================================
// HST SECURE DASHBOARD - C++ Qt Premium Application
// ============================================================================
// Este arquivo contem a aplicacao completa. As interfaces foram construidas
// puramente via codigo C++ (sem arquivos .ui) para obter maximo controle do 
// design e alta performance.
// ============================================================================

#include <QApplication>
#include <QMainWindow>
#include <QWidget>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QLabel>
#include <QPainter>
#include <QPainterPath>
#include <QLinearGradient>
#include <QTimer>
#include <QPropertyAnimation>
#include <QEasingCurve>
#include <QPushButton>
#include <QGraphicsDropShadowEffect>
#include <QRandomGenerator>
#include <QStackedWidget>
#include <QButtonGroup>
#include <QTableWidget>
#include <QHeaderView>
#include <QLineEdit>
#include <QComboBox>
#include <QScrollBar>
#include <QDialog>
#include <QFormLayout>
#include <QMessageBox>
#include <QTextEdit>
#include <QFrame>
#include <QFile>
#include <QTextStream>
#include <QFileDialog>
#include <QDir>
#include <deque>
#include <thread>
#include <mutex>
#include <asio.hpp>
#include <windows.h>
#include <QRegularExpression>
#include <QWebSocket>
#include <QListWidget>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLocale>
#include <QUrl>
#include "src/ui/LinkedInDashboard.h"
#include "src/ui/RobotControlPage.h"
#include "src/ui/LinkedInJobsPage.h"


// ============================================================================
// 1. PALETA DE CORES CORPORATIVA (HST Brand Premium)
// ============================================================================
// Variaveis globais de cor usadas em todo o projeto para garantir consistencia visual.
const QColor bgDark("#111216");        // Fundo escuro principal da janela
const QColor cardDark("#1A1D24");      // Fundo um pouco mais claro para os "Cards" (destaques)
const QColor textWhite("#F0F4F8");     // Branco 'gelo' para textos de alto contraste (Titulos)
const QColor textGray("#A39C93");      // Cinza quente para subtitulos e legendas
const QColor hstOrange("#E39B35");     // Laranja/Ouro da letra 'S' da marca (Usado em Botoes e destaques)
const QColor hstTaupe("#857A69");      // Marrom/Taupe das letras 'H' e 'T' da marca
const QColor positiveGreen("#22C55E"); // Verde para status positivos (Ex: "Approved")
const QColor borderDark("#252830");    // Cor sutil para bordas de botoes e divisorias

// ============================================================================
// 2. COMPONENTES BASE (Reutilizaveis em todo o sistema)
// ============================================================================

// --- CardWidget ---
// Usado como painel de fundo para graficos, tabelas e formulários.
// Ele aplica bordas arredondadas e uma leve sombra (DropShadow) no fundo.
class CardWidget : public QWidget {
public:
    CardWidget(QWidget* parent = nullptr) : QWidget(parent) {
        setAttribute(Qt::WA_StyledBackground, true);
        setStyleSheet(QString("CardWidget { background-color: %1; border-radius: 12px; border: 1px solid %2; }")
            .arg(cardDark.name()).arg(borderDark.name()));
        
        // Efeito de sombra projetada (Profundidade/Glassmorphism)
        QGraphicsDropShadowEffect* shadow = new QGraphicsDropShadowEffect(this);
        shadow->setBlurRadius(25);
        shadow->setColor(QColor(0, 0, 0, 90));
        shadow->setOffset(0, 8);
        setGraphicsEffect(shadow);
    }
};

// --- StatCard ---
// Os 4 cards quadrados que ficam no topo da aba Dashboard (Ex: "Secure Transactions").
class StatCard : public CardWidget {
public:
    StatCard(const QString& title, const QString& value, const QString& percentage, QWidget* parent = nullptr) 
        : CardWidget(parent) {
        QVBoxLayout* layout = new QVBoxLayout(this);
        layout->setContentsMargins(20, 20, 20, 20);
        layout->setSpacing(10);
        
        // Titulo do card
        QLabel* titleLabel = new QLabel(title);
        titleLabel->setStyleSheet(QString("color: %1; font-size: 13px; font-weight: bold; background: transparent; border: none;").arg(textGray.name()));
        
        QHBoxLayout* valLayout = new QHBoxLayout();
        
        // Valor principal do card (Ex: "125.8K")
        QLabel* valLabel = new QLabel(value);
        valLabel->setStyleSheet(QString("color: %1; font-size: 26px; font-weight: 800; background: transparent; border: none;").arg(textWhite.name()));
        
        // Badge de porcentagem (Verde)
        QLabel* percLabel = new QLabel(percentage);
        percLabel->setStyleSheet(QString("color: %1; background-color: rgba(34, 197, 94, 0.15); padding: 4px 6px; border-radius: 4px; font-size: 11px; font-weight: bold; border: none;").arg(positiveGreen.name()));
        
        valLayout->addWidget(valLabel);
        valLayout->addWidget(percLabel);
        valLayout->addStretch(); // Empurra itens para a esquerda
        
        layout->addWidget(titleLabel);
        layout->addLayout(valLayout);
        
        setMinimumHeight(100);
        setCursor(Qt::PointingHandCursor); // Muda cursor para "maozinha"
    }

protected:
    // Eventos de Hover (Efeito ao passar o mouse por cima do card)
    void enterEvent(QEnterEvent *event) override {
        setStyleSheet(QString("CardWidget { background-color: #21252D; border-radius: 12px; border: 1px solid #303440; }"));
        QWidget::enterEvent(event);
    }
    void leaveEvent(QEvent *event) override {
        setStyleSheet(QString("CardWidget { background-color: %1; border-radius: 12px; border: 1px solid %2; }")
            .arg(cardDark.name()).arg(borderDark.name()));
        QWidget::leaveEvent(event);
    }
};

// --- SmoothLineChart ---
// Componente que desenha "na mao" graficos de linha animados.
// Nao usa bibliotecas prontas, tudo e desenhado via QPainter (C++ Nativo).
class SmoothLineChart : public QWidget {
    Q_OBJECT
    Q_PROPERTY(qreal animProgress READ animProgress WRITE setAnimProgress)
public:
    SmoothLineChart(bool showGrid, QWidget* parent = nullptr) : QWidget(parent), m_animProgress(0.0), m_showGrid(showGrid) {
        setMinimumHeight(showGrid ? 250 : 100);
        
        // Gerador de dados aleatorios ("Fake Data" para o grafico)
        for(int i=0; i<12; ++i) {
            data1.append(QRandomGenerator::global()->bounded(30, 140));
            data2.append(QRandomGenerator::global()->bounded(10, 110));
        }
        
        // Animacao de carregamento das linhas (sobem do zero)
        QPropertyAnimation* anim = new QPropertyAnimation(this, "animProgress");
        anim->setDuration(2000); // 2 segundos
        anim->setStartValue(0.0);
        anim->setEndValue(1.0);
        anim->setEasingCurve(QEasingCurve::OutCubic);
        anim->start(QAbstractAnimation::DeleteWhenStopped);
    }
    qreal animProgress() const { return m_animProgress; }
    void setAnimProgress(qreal p) { m_animProgress = p; update(); } // Atualiza a tela a cada frame da animacao
    void addData(qreal v1, qreal v2) {
        data1.append(v1); data2.append(v2);
        if(data1.size() > 12) { data1.removeFirst(); data2.removeFirst(); }
        update();
    }

protected:
    // Esse metodo paintEvent e onde a "magica" visual acontece pixel por pixel.
    void paintEvent(QPaintEvent*) override {
        QPainter p(this);
        p.setRenderHint(QPainter::Antialiasing); // Suaviza as bordas do grafico
        QRect r = rect();
        
        // Desenha a grade de fundo e os meses (Eixo X/Y) se solicitado
        if (m_showGrid) {
            r.adjust(40, 20, -20, -30);
            p.setPen(QPen(borderDark, 1));
            for(int i=0; i<=4; ++i) {
                int y = r.bottom() - (r.height() / 4.0) * i;
                p.drawLine(r.left(), y, r.right(), y);
                p.setPen(textGray);
                p.drawText(QRect(0, y-10, 35, 20), Qt::AlignRight | Qt::AlignVCenter, QString::number(i*50) + "K");
                p.setPen(QPen(borderDark, 1));
            }
            QStringList months = {"Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"};
            p.setPen(textGray);
            for(int i=0; i<12; ++i) {
                int x = r.left() + (r.width() / 11.0) * i;
                p.drawText(QRect(x-15, r.bottom()+10, 30, 20), Qt::AlignCenter, months[i]);
            }
        } else {
            r.adjust(10, 10, -10, -10);
        }
        
        // Chama o metodo que desenha efetivamente as linhas das duas series
        drawLine(p, r, data1, hstTaupe);
        drawLine(p, r, data2, hstOrange);
    }
private:
    QList<qreal> data1, data2;
    qreal m_animProgress;
    bool m_showGrid;
    
    void drawLine(QPainter& p, const QRect& r, const QList<qreal>& data, const QColor& color) {
        if(data.isEmpty()) return;
        QPainterPath path;
        QPainterPath fillPath;
        qreal maxVal = 150.0;
        
        qreal startX = r.left();
        qreal startY = r.bottom() - (data[0] / maxVal) * r.height() * m_animProgress;
        path.moveTo(startX, startY);
        fillPath.moveTo(startX, r.bottom());
        fillPath.lineTo(startX, startY);
        
        // Desenha curvas cubicas para dar efeito arredondado ao grafico
        for(int i=1; i<data.size(); ++i) {
            qreal x1 = r.left() + (r.width() / 11.0) * (i - 1);
            qreal y1 = r.bottom() - (data[i-1] / maxVal) * r.height() * m_animProgress;
            qreal x2 = r.left() + (r.width() / 11.0) * i;
            qreal y2 = r.bottom() - (data[i] / maxVal) * r.height() * m_animProgress;
            
            qreal ctrl1X = x1 + (x2 - x1) / 2.0; qreal ctrl1Y = y1;
            qreal ctrl2X = ctrl1X;             qreal ctrl2Y = y2;
            path.cubicTo(ctrl1X, ctrl1Y, ctrl2X, ctrl2Y, x2, y2);
            fillPath.cubicTo(ctrl1X, ctrl1Y, ctrl2X, ctrl2Y, x2, y2);
        }
        fillPath.lineTo(r.right(), r.bottom());
        fillPath.closeSubpath();
        
        // Efeito Gradiente translucido abaixo da linha
        QLinearGradient grad(0, r.top(), 0, r.bottom());
        QColor gradStart = color; gradStart.setAlpha(m_showGrid ? 90 : 50);
        QColor gradEnd = color; gradEnd.setAlpha(0);
        grad.setColorAt(0, gradStart); grad.setColorAt(1, gradEnd);
        p.fillPath(fillPath, grad);
        
        // Efeito de Neon (Glow) pintando a linha multiplas vezes com opacidade baixa
        for (int w = 1; w <= 3; ++w) {
            QColor glowColor = color;
            glowColor.setAlpha(50 / w);
            p.setPen(QPen(glowColor, 2 + w * 2));
            p.drawPath(path);
        }
        
        // Linha Solida Final
        p.setPen(QPen(color, 2.5));
        p.drawPath(path);
        
        // Desenha a "bolinha" no ultimo dado da linha
        qreal endX = r.right();
        qreal endY = r.bottom() - (data.last() / maxVal) * r.height() * m_animProgress;
        p.setBrush(bgDark);
        p.setPen(QPen(color, 2.5));
        p.drawEllipse(QPointF(endX, endY), 5, 5);
    }
};

// --- BarChart ---
// Grafico de Barras animado customizado
class BarChart : public QWidget {
    Q_OBJECT
    Q_PROPERTY(qreal animProgress READ animProgress WRITE setAnimProgress)
public:
    BarChart(QWidget* parent = nullptr) : QWidget(parent), m_animProgress(0.0) {
        setMinimumHeight(150);
        QPropertyAnimation* anim = new QPropertyAnimation(this, "animProgress");
        anim->setDuration(1200);
        anim->setStartValue(0.0); anim->setEndValue(1.0);
        anim->setEasingCurve(QEasingCurve::OutBack); // Animação "elástica"
        anim->start(QAbstractAnimation::DeleteWhenStopped);
        for(int i=0; i<20; ++i) { data.append(QRandomGenerator::global()->bounded(20, 120)); }
    }
    qreal animProgress() const { return m_animProgress; }
    void setAnimProgress(qreal p) { m_animProgress = p; update(); }
    void addData(qreal v) {
        data.append(v);
        if(data.size() > 20) data.removeFirst();
        update();
    }
protected:
    void paintEvent(QPaintEvent*) override {
        QPainter p(this);
        p.setRenderHint(QPainter::Antialiasing);
        QRect r = rect();
        r.adjust(10, 10, -10, -20);
        
        p.setPen(QPen(borderDark, 1));
        p.drawLine(r.left(), r.bottom(), r.right(), r.bottom());
        
        int n = data.size();
        if(n == 0) return;
        
        qreal barWidth = (r.width() / (qreal)n) * 0.5;
        qreal spacing = (r.width() / (qreal)n) * 0.5;
        qreal maxVal = 150.0;
        
        // Loop desenhando cada retangulo (Barra)
        for(int i=0; i<n; ++i) {
            qreal x = r.left() + i * (barWidth + spacing) + spacing/2;
            qreal h = (data[i] / maxVal) * r.height() * m_animProgress;
            qreal y = r.bottom() - h;
            
            QRectF barRect(x, y, barWidth, h);
            QLinearGradient grad(0, y, 0, r.bottom());
            QColor c = (i % 3 == 0) ? hstTaupe : hstOrange; // Intercala as cores corporativas
            QColor cDark = c; cDark.setAlpha(120);
            grad.setColorAt(0, c); grad.setColorAt(1, cDark);
            
            p.setBrush(grad); p.setPen(Qt::NoPen);
            p.drawRoundedRect(barRect, barWidth/2, barWidth/2); // Barra com topo arredondado
        }
    }
private:
    QList<qreal> data;
    qreal m_animProgress;
};

// --- Botoes Customizados ---
class CustomButton : public QPushButton {
public:
    CustomButton(const QString& text, const QColor& bg, QWidget* parent = nullptr) : QPushButton(text, parent) {
        // Estilização com CSS (QSS do Qt)
        setStyleSheet(QString(
            "QPushButton { background-color: %1; color: white; border: none; border-radius: 6px; padding: 8px 18px; font-weight: bold; }"
            "QPushButton:hover { background-color: %2; }"
        ).arg(bg.name()).arg(bg.lighter(115).name()));
        setCursor(Qt::PointingHandCursor);
    }
};

// --- Botão do Menu Lateral ---
class MenuButton : public QPushButton {
    Q_OBJECT
public:
    MenuButton(const QString& icon, const QString& text, QWidget* parent = nullptr) : QPushButton(parent) {
        setCheckable(true); // Permite estado ativo/clicado (Checked)
        setCursor(Qt::PointingHandCursor);
        setFixedHeight(50);
        setText(icon + "    " + text);
        // Design avançado para o hover e clique do botão
        setStyleSheet(QString(
            "QPushButton { text-align: left; padding-left: 30px; color: %1; font-size: 14px; font-weight: 500; background-color: transparent; border: none; border-left: 4px solid transparent; }"
            "QPushButton:hover { background-color: rgba(227, 155, 53, 0.05); color: %2; }"
            "QPushButton:checked { background-color: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 rgba(227, 155, 53, 0.15), stop:1 rgba(227, 155, 53, 0)); color: %2; border-left: 4px solid %3; font-weight: bold; }"
        ).arg(textGray.name()).arg(textWhite.name()).arg(hstOrange.name()));
    }
};

// ============================================================================
// 3. ESTRUTURA GLOBAL: O MENU LATERAL (Sidebar)
// ============================================================================
class Sidebar : public QWidget {
    Q_OBJECT
public:
    Sidebar(QWidget* parent = nullptr) : QWidget(parent) {
        setFixedWidth(260); // Tamanho fixo do painel da esquerda
        setStyleSheet(QString("Sidebar { background-color: rgba(17, 18, 22, 210); border-right: 1px solid %1; }").arg(borderDark.name()));
        setAttribute(Qt::WA_StyledBackground, true);
        
        QVBoxLayout* layout = new QVBoxLayout(this);
        layout->setContentsMargins(0, 35, 0, 30);
        layout->setSpacing(5);
        
        // --- LOGOTIPO HST DA EMPRESA ---
        // Uso de spans HTML (Rich Text) dentro do Qt para pintar cada letra
        QLabel* logo = new QLabel(QString("<span style='color:%1;'>H</span><span style='color:%2;'>S</span><span style='color:%1;'>T</span>")
                                  .arg(hstTaupe.name()).arg(hstOrange.name()));
        logo->setStyleSheet("font-size: 46px; font-weight: 900; padding-left: 28px; margin-bottom: 0px; font-family: 'Segoe UI', Arial; letter-spacing: -2px;");
        QLabel* subtitle = new QLabel("MAKING ELECTRONIC\nTRANSACTIONS SECURE");
        subtitle->setStyleSheet(QString("color: %1; font-size: 10px; font-weight: bold; padding-left: 30px; margin-bottom: 35px; letter-spacing: 0.5px;").arg(hstTaupe.name()));
        
        layout->addWidget(logo);
        layout->addWidget(subtitle);
        
        // O QButtonGroup gerencia a exclusividade (Apenas um botao clicado por vez)
        QButtonGroup* group = new QButtonGroup(this);
        group->setExclusive(true);
        
        auto addMenuBtn = [&](const QString& icon, const QString& text, int id) {
            MenuButton* btn = new MenuButton(icon, text);
            layout->addWidget(btn);
            group->addButton(btn, id);
            return btn;
        };
        
        // Adiciona as opcoes de pagina
        MenuButton* btnDash = addMenuBtn("\xE2\x8A\x9E", "Monitoramento", 0);
        btnDash->setChecked(true);
        addMenuBtn("\xF0\x9F\x94\x97", "LinkedIn Vagas", 1);
        addMenuBtn("\xF0\x9F\xA4\x96", "Controle do Rob\xC3\xB4", 2);
        
        layout->addStretch(); // Empurra tudo pra cima
        
        // --- PERFIL DO USUARIO (Canto Inferior Esquerdo) ---
        QHBoxLayout* profLayout = new QHBoxLayout();
        profLayout->setContentsMargins(30, 0, 30, 0);
        QLabel* avatar = new QLabel("MB");
        avatar->setFixedSize(40, 40);
        avatar->setAlignment(Qt::AlignCenter);
        avatar->setStyleSheet(QString("background-color: %1; color: %2; border-radius: 20px; font-weight: bold; font-size: 15px;").arg(hstTaupe.name()).arg(bgDark.name()));
        
        QVBoxLayout* profText = new QVBoxLayout();
        profText->setSpacing(0);
        QLabel* profName = new QLabel("Michael Berger");
        profName->setStyleSheet(QString("color: %1; font-weight: bold; font-size: 14px;").arg(textWhite.name()));
        QLabel* profRole = new QLabel("Dev Manager");
        profRole->setStyleSheet(QString("color: %1; font-size: 12px;").arg(hstOrange.name()));
        profText->addWidget(profName);
        profText->addWidget(profRole);
        
        profLayout->addWidget(avatar);
        profLayout->addLayout(profText);
        profLayout->addStretch();
        
        layout->addLayout(profLayout);
        
        // Ao clicar num botao, emite um SINAL indicando a ID da pagina clicada
        connect(group, &QButtonGroup::idClicked, this, &Sidebar::pageSelected);
    }
signals:
    void pageSelected(int id); // Sinal escutado la na MainWindow
};

// ============================================================================
// MODAL DE DIALOGO: Formulario Para Gerar Relatorio
// ============================================================================
class ReportDialog : public QDialog {
    Q_OBJECT
public:
    ReportDialog(QWidget* parent = nullptr) : QDialog(parent) {
        setWindowTitle("Generate Custom Report");
        setFixedSize(420, 260);
        setStyleSheet(QString("QDialog { background-color: %1; border: 1px solid %2; border-radius: 8px; }").arg(cardDark.name()).arg(borderDark.name()));
        
        QVBoxLayout* mainLayout = new QVBoxLayout(this);
        mainLayout->setContentsMargins(30, 30, 30, 30);
        
        QLabel* title = new QLabel("New Report Parameters");
        title->setStyleSheet(QString("color: %1; font-size: 18px; font-weight: bold; margin-bottom: 15px;").arg(hstOrange.name()));
        mainLayout->addWidget(title);
        
        QFormLayout* form = new QFormLayout();
        form->setLabelAlignment(Qt::AlignRight | Qt::AlignVCenter);
        form->setSpacing(15);
        
        nameInput = new QLineEdit();
        nameInput->setPlaceholderText("Ex: Q3 Audit Report");
        nameInput->setStyleSheet(QString("QLineEdit { background-color: %1; color: %2; border: 1px solid %3; border-radius: 4px; padding: 8px; font-size: 13px; } QLineEdit:focus { border: 1px solid %4; }").arg(bgDark.name(), textWhite.name(), borderDark.name(), hstOrange.name()));
        
        typeInput = new QComboBox();
        typeInput->addItems({"Transaction Volume", "Security Threats", "Terminal Uptime", "Full Audit"});
        typeInput->setStyleSheet(QString("QComboBox { background-color: %1; color: %2; border: 1px solid %3; border-radius: 4px; padding: 8px; font-size: 13px; } QComboBox::drop-down { border: none; }").arg(bgDark.name(), textWhite.name(), borderDark.name()));
        
        QLabel* l1 = new QLabel("Report Name:"); l1->setStyleSheet(QString("color: %1; font-size: 13px; font-weight: bold;").arg(textGray.name()));
        QLabel* l2 = new QLabel("Report Type:"); l2->setStyleSheet(QString("color: %1; font-size: 13px; font-weight: bold;").arg(textGray.name()));
        
        form->addRow(l1, nameInput);
        form->addRow(l2, typeInput);
        mainLayout->addLayout(form);
        
        mainLayout->addStretch();
        
        // Botoes do Formulario (Salvar e Cancelar)
        QHBoxLayout* btns = new QHBoxLayout();
        btns->addStretch();
        
        QPushButton* cancelBtn = new QPushButton("Cancel");
        cancelBtn->setCursor(Qt::PointingHandCursor);
        cancelBtn->setStyleSheet(QString("QPushButton { background-color: transparent; color: %1; border: 1px solid %2; border-radius: 6px; padding: 8px 20px; font-weight: bold; } QPushButton:hover { background-color: %3; }").arg(textWhite.name(), borderDark.name(), bgDark.name()));
        connect(cancelBtn, &QPushButton::clicked, this, &QDialog::reject); // Fecha sem salvar
        
        QPushButton* saveBtn = new QPushButton("Save & Generate");
        saveBtn->setCursor(Qt::PointingHandCursor);
        saveBtn->setStyleSheet(QString("QPushButton { background-color: %1; color: %2; border: none; border-radius: 6px; padding: 8px 20px; font-weight: bold; } QPushButton:hover { background-color: %3; }").arg(hstOrange.name(), bgDark.name(), hstOrange.lighter(110).name()));
        connect(saveBtn, &QPushButton::clicked, this, &QDialog::accept); // Fecha e indica sucesso
        
        btns->addWidget(cancelBtn);
        btns->addWidget(saveBtn);
        mainLayout->addLayout(btns);
    }
    QString reportName() const { return nameInput->text(); }
    QString reportType() const { return typeInput->currentText(); }
private:
    QLineEdit* nameInput;
    QComboBox* typeInput;
};

// ============================================================================
// 4. TELAS DO SISTEMA (Paginas Individuais)
// ============================================================================

// --- ABA 1: DASHBOARD PRINCIPAL ---
class DashboardPage : public QWidget {
    Q_OBJECT
public:
    SmoothLineChart* revChart;
    BarChart* barChart;
    SmoothLineChart* sessChart;
    QLabel* revVal;
    QLabel* profVal;
    QLabel* sessVal;

    DashboardPage(QWidget* parent = nullptr) : QWidget(parent) {
        QVBoxLayout* mainLayout = new QVBoxLayout(this);
        mainLayout->setContentsMargins(40, 40, 40, 40);
        mainLayout->setSpacing(25);
        
        // --- Cabecalho ---
        QHBoxLayout* headerTopLayout = new QHBoxLayout();
        QVBoxLayout* headerTextLayout = new QVBoxLayout();
        QLabel* title = new QLabel("Welcome back, Michael");
        title->setStyleSheet(QString("color: %1; font-size: 28px; font-weight: bold;").arg(textWhite.name()));
        QLabel* subtitle = new QLabel("Monitor secure transactions and system health.");
        subtitle->setStyleSheet(QString("color: %1; font-size: 14px;").arg(textGray.name()));
        headerTextLayout->addWidget(title);
        headerTextLayout->addWidget(subtitle);
        headerTopLayout->addLayout(headerTextLayout);
        headerTopLayout->addStretch();
        
        headerTopLayout->addWidget(new CustomButton("Export Logs \u2193", borderDark));
        
        // Conectando o Botão de Relatorio ao modal ReportDialog
        CustomButton* generateBtn = new CustomButton("Generate Report", hstOrange);
        headerTopLayout->addWidget(generateBtn);
        connect(generateBtn, &QPushButton::clicked, this, [this]() {
            ReportDialog dlg(this); // Instancia o modal
            if (dlg.exec() == QDialog::Accepted) { // Se clicou em Salvar
                QString name = dlg.reportName();
                QString type = dlg.reportType();
                if (name.isEmpty()) name = "Untitled Report";
                
                QString msg = QString("Successfully saved parameters and generated your report!\n\nName: %1\nType: %2\nStatus: Processing...").arg(name, type);
                QMessageBox msgBox(this); // Mostra o resultado
                msgBox.setWindowTitle("Report Generated");
                msgBox.setText(msg);
                msgBox.setIcon(QMessageBox::Information);
                msgBox.setStyleSheet(QString("QMessageBox { background-color: %1; color: %2; } QLabel { color: %2; font-size: 13px; } QPushButton { background-color: %3; color: %1; padding: 6px 20px; border-radius: 4px; font-weight: bold; }").arg(cardDark.name(), textWhite.name(), hstOrange.name()));
                msgBox.exec();
            }
        });
        mainLayout->addLayout(headerTopLayout);
        
        // --- 4 Cards de Metricas ---
        QHBoxLayout* cardsLayout = new QHBoxLayout();
        cardsLayout->addWidget(new StatCard("Secure Transactions", "125.8K", "+12.4%"));
        cardsLayout->addWidget(new StatCard("Active Terminals", "8.2K", "+5.6%"));
        cardsLayout->addWidget(new StatCard("Threats Blocked", "432", "-2.1%"));
        cardsLayout->addWidget(new StatCard("System Uptime", "99.9%", "Stable"));
        cardsLayout->setSpacing(20);
        mainLayout->addLayout(cardsLayout);
        
        // --- Area dos Graficos ---
        QHBoxLayout* middleLayout = new QHBoxLayout();
        middleLayout->setSpacing(20);
        
        // Grafico Grande Esquerda
        CardWidget* revenueCard = new CardWidget();
        QVBoxLayout* revLayout = new QVBoxLayout(revenueCard);
        revLayout->setContentsMargins(25, 25, 25, 25);
        QHBoxLayout* revHeader = new QHBoxLayout();
        QVBoxLayout* revHeaderLeft = new QVBoxLayout();
        QLabel* revTitle = new QLabel("Remote Server Telemetry (CPU & RAM)");
        revTitle->setStyleSheet(QString("color: %1; font-size: 14px; background: transparent;").arg(textGray.name()));
        revVal = new QLabel("Waiting data...");
        revVal->setStyleSheet(QString("color: %1; font-size: 28px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        revHeaderLeft->addWidget(revTitle);
        revHeaderLeft->addWidget(revVal);
        revHeader->addLayout(revHeaderLeft);
        revHeader->addStretch();
        QLabel* leg1 = new QLabel("\u2022 CPU Load"); leg1->setStyleSheet(QString("color: %1; font-weight: bold; background: transparent;").arg(hstOrange.name()));
        QLabel* leg2 = new QLabel("\u2022 RAM Load"); leg2->setStyleSheet(QString("color: %1; font-weight: bold; background: transparent;").arg(hstTaupe.name()));
        revHeader->addWidget(leg1); revHeader->addWidget(leg2);
        revLayout->addLayout(revHeader);
        revChart = new SmoothLineChart(true);
        revLayout->addWidget(revChart, 1);
        middleLayout->addWidget(revenueCard, 2);
        
        // Graficos Menores a Direita
        QVBoxLayout* rightLayout = new QVBoxLayout();
        rightLayout->setSpacing(20);
        
        CardWidget* profitCard = new CardWidget();
        QVBoxLayout* profLayout = new QVBoxLayout(profitCard);
        profLayout->setContentsMargins(20, 20, 20, 20);
        QLabel* profTitle = new QLabel("Server CPU Histogram"); profTitle->setStyleSheet(QString("color: %1; font-size: 13px; background: transparent;").arg(textGray.name()));
        profVal = new QLabel("--%"); profVal->setStyleSheet(QString("color: %1; font-size: 24px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        profLayout->addWidget(profTitle); profLayout->addWidget(profVal);
        barChart = new BarChart();
        profLayout->addWidget(barChart, 1);
        
        CardWidget* sessionCard = new CardWidget();
        QVBoxLayout* sessLayout = new QVBoxLayout(sessionCard);
        sessLayout->setContentsMargins(20, 20, 20, 20);
        QLabel* sessTitle = new QLabel("Server RAM Stream"); sessTitle->setStyleSheet(QString("color: %1; font-size: 13px; background: transparent;").arg(textGray.name()));
        sessVal = new QLabel("--%"); sessVal->setStyleSheet(QString("color: %1; font-size: 24px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        sessLayout->addWidget(sessTitle); sessLayout->addWidget(sessVal);
        sessChart = new SmoothLineChart(false);
        sessLayout->addWidget(sessChart, 1);
        
        rightLayout->addWidget(profitCard, 1);
        rightLayout->addWidget(sessionCard, 1);
        middleLayout->addLayout(rightLayout, 1);
        mainLayout->addLayout(middleLayout, 1);
        
        mainLayout->addStretch();
    }
public slots:
    void updateTelemetry(double cpu, double ram) {
        revVal->setText(QString("CPU: %1% | RAM: %2%").arg(cpu, 0, 'f', 1).arg(ram, 0, 'f', 1));
        profVal->setText(QString("%1%").arg(cpu, 0, 'f', 1));
        sessVal->setText(QString("%1%").arg(ram, 0, 'f', 1));
        
        revChart->addData(cpu, ram);
        barChart->addData(cpu);
        sessChart->addData(ram, cpu / 2.0); // Apenas preenchendo o segundo array falso
    }
};

// --- ABA 2: TELA DE TABELA (Transactions Log) ---
class TransactionsPage : public QWidget {
    Q_OBJECT
public:
    QTableWidget* table; // Promovido a membro da classe

    TransactionsPage(QWidget* parent = nullptr) : QWidget(parent) {
        QVBoxLayout* mainLayout = new QVBoxLayout(this);
        mainLayout->setContentsMargins(40, 40, 40, 40);
        mainLayout->setSpacing(25);
        
        // Cabecalho e Barra de Pesquisa
        QHBoxLayout* headerTopLayout = new QHBoxLayout();
        QVBoxLayout* headerTextLayout = new QVBoxLayout();
        QLabel* title = new QLabel("Live Transactions (PostgreSQL)");
        title->setStyleSheet(QString("color: %1; font-size: 28px; font-weight: bold;").arg(textWhite.name()));
        QLabel* subtitle = new QLabel("Real-time telemetry showing users stored in Cloud Database.");
        subtitle->setStyleSheet(QString("color: %1; font-size: 14px;").arg(textGray.name()));
        headerTextLayout->addWidget(title); headerTextLayout->addWidget(subtitle);
        headerTopLayout->addLayout(headerTextLayout);
        headerTopLayout->addStretch();
        
        QLineEdit* searchBar = new QLineEdit();
        searchBar->setPlaceholderText("\xF0\x9F\x94\x8D Search by Name or Email...");
        searchBar->setFixedWidth(320);
        searchBar->setStyleSheet(QString("QLineEdit { background-color: %1; color: %2; border: 1px solid %3; border-radius: 8px; padding: 12px 15px; font-size: 13px; } QLineEdit:focus { border: 1px solid %4; }").arg(bgDark.name(), textWhite.name(), borderDark.name(), hstOrange.name()));
        QComboBox* filterBox = new QComboBox();
        filterBox->addItems({"All Status", "Approved", "Blocked", "Review"});
        filterBox->setFixedWidth(160);
        filterBox->setStyleSheet(QString("QComboBox { background-color: %1; color: %2; border: 1px solid %3; border-radius: 8px; padding: 10px 15px; font-size: 13px; } QComboBox::drop-down { border: none; } QComboBox QAbstractItemView { background-color: %1; color: %2; selection-background-color: %4; }").arg(bgDark.name(), textWhite.name(), borderDark.name(), cardDark.name()));
        
        CustomButton* exportBtn = new CustomButton("Export to Excel (CSV) \u2193", hstOrange);
        
        headerTopLayout->addWidget(searchBar);
        headerTopLayout->addSpacing(15);
        headerTopLayout->addWidget(filterBox);
        headerTopLayout->addSpacing(15);
        headerTopLayout->addWidget(exportBtn);
        mainLayout->addLayout(headerTopLayout);
        
        // --- Configuracao da Tabela (QTableWidget) ---
        CardWidget* tableCard = new CardWidget();
        QVBoxLayout* cardLayout = new QVBoxLayout(tableCard);
        cardLayout->setContentsMargins(10, 10, 10, 10);
        
        table = new QTableWidget(0, 5); // Começa com 0 linhas, 5 Colunas
        table->setHorizontalHeaderLabels({"User Name", "Email Address", "Transaction ID", "Amount", "Status"});
        table->horizontalHeader()->setStretchLastSection(true);
        table->horizontalHeader()->setDefaultAlignment(Qt::AlignLeft | Qt::AlignVCenter);
        table->setSelectionBehavior(QAbstractItemView::SelectRows); // Seleciona a linha inteira ao clicar
        table->setEditTriggers(QAbstractItemView::NoEditTriggers); // Bloqueia edicao de texto
        table->setFocusPolicy(Qt::NoFocus);
        table->setShowGrid(false); // Esconde a grade feia padrao
        table->setAlternatingRowColors(true); // Cores intercaladas zebradas
        table->verticalHeader()->setVisible(false);
        
        // Super CSS da Tabela (Oculta scrollbars desnecessarios, estiliza cabecalho e fundo selecionado)
        table->setStyleSheet(QString("QTableWidget { background-color: transparent; color: %1; border: none; alternate-background-color: %2; } QTableWidget::item { padding: 5px 15px; border-bottom: 1px solid %3; } QTableWidget::item:selected { background-color: rgba(227, 155, 53, 0.1); } QHeaderView::section { background-color: transparent; color: %4; font-weight: bold; font-size: 13px; border: none; border-bottom: 2px solid %3; padding: 15px 15px; } QScrollBar:vertical { background: %1; width: 10px; margin: 0px; } QScrollBar::handle:vertical { background: %3; border-radius: 5px; } QScrollBar::add-page:vertical, QScrollBar::sub-page:vertical { background: none; }").arg(textWhite.name(), "#16181E", borderDark.name(), textGray.name()));
        
        table->setColumnWidth(0, 200); table->setColumnWidth(1, 240); table->setColumnWidth(2, 140); table->setColumnWidth(3, 140);

        cardLayout->addWidget(table);
        mainLayout->addWidget(tableCard, 1);
        
        // --- Funcionalidade de Exportacao (CSV para Excel) ---
        connect(exportBtn, &QPushButton::clicked, this, [this]() {
            QString fileName = QFileDialog::getSaveFileName(this, "Export Transactions", "", "Excel CSV (*.csv);;All Files (*)");
            if (fileName.isEmpty()) return;
            
            QFile file(fileName);
            if (!file.open(QIODevice::WriteOnly | QIODevice::Text)) {
                QMessageBox::critical(this, "Error", "Could not open file for writing.");
                return;
            }
            
            QTextStream out(&file);
            // Cabecalho
            for (int c = 0; c < table->columnCount(); ++c) {
                out << "\"" << table->horizontalHeaderItem(c)->text() << "\"";
                if (c < table->columnCount() - 1) out << ",";
            }
            out << "\n";
            
            // Dados
            for (int r = 0; r < table->rowCount(); ++r) {
                for (int c = 0; c < table->columnCount(); ++c) {
                    if (c == 4) { // Status (badge personalizado)
                        QWidget* w = table->cellWidget(r, c);
                        if (w) {
                            QLabel* l = w->findChild<QLabel*>();
                            if (l) out << "\"" << l->text() << "\"";
                        }
                    } else {
                        QTableWidgetItem* item = table->item(r, c);
                        if (item) out << "\"" << item->text() << "\"";
                    }
                    if (c < table->columnCount() - 1) out << ",";
                }
                out << "\n";
            }
            file.close();
            
            QMessageBox msgBox(this);
            msgBox.setWindowTitle("Export Successful");
            msgBox.setText("Transactions exported successfully to Excel/CSV!");
            msgBox.setIcon(QMessageBox::Information);
            msgBox.setStyleSheet("QMessageBox { background-color: #1A1D24; color: #F0F4F8; } QLabel { color: #F0F4F8; font-size: 13px; } QPushButton { background-color: #E39B35; color: #111216; padding: 6px 20px; border-radius: 4px; font-weight: bold; }");
            msgBox.exec();
        });
        
        // --- Paginacao do Banco de Dados ---
        QHBoxLayout* paginationLayout = new QHBoxLayout();
        paginationLayout->setContentsMargins(5, 10, 5, 0);
        QLabel* entriesInfo = new QLabel("Showing 1 to 18 of 2,430 entries");
        entriesInfo->setStyleSheet(QString("color: %1; font-size: 13px;").arg(textGray.name()));
        
        QHBoxLayout* pageButtonsLayout = new QHBoxLayout();
        pageButtonsLayout->setSpacing(8);
        auto createPageBtn = [&](const QString& text, bool active = false) {
            QPushButton* btn = new QPushButton(text);
            btn->setFixedHeight(36);
            if (text.length() <= 2) btn->setFixedWidth(36); else btn->setMinimumWidth(80);
            btn->setCursor(Qt::PointingHandCursor);
            if (active) { btn->setStyleSheet(QString("QPushButton { background-color: %1; color: %2; border-radius: 6px; font-weight: bold; border: none; font-size: 13px; }").arg(hstOrange.name(), bgDark.name())); } 
            else { btn->setStyleSheet(QString("QPushButton { background-color: transparent; color: %1; border-radius: 6px; font-weight: bold; border: 1px solid %2; font-size: 13px; } QPushButton:hover { background-color: %3; border: 1px solid %4; }").arg(textWhite.name(), borderDark.name(), cardDark.name(), hstTaupe.name())); }
            return btn;
        };
        pageButtonsLayout->addWidget(createPageBtn("Previous"));
        pageButtonsLayout->addWidget(createPageBtn("1", true)); // Pagina Ativa fica Laranja
        pageButtonsLayout->addWidget(createPageBtn("2"));
        pageButtonsLayout->addWidget(createPageBtn("3"));
        QLabel* ellipsis = new QLabel("..."); ellipsis->setStyleSheet(QString("color: %1; padding: 0 5px; font-weight: bold; font-size: 16px;").arg(textGray.name()));
        pageButtonsLayout->addWidget(ellipsis);
        pageButtonsLayout->addWidget(createPageBtn("135"));
        pageButtonsLayout->addWidget(createPageBtn("Next"));
        
        paginationLayout->addWidget(entriesInfo);
        paginationLayout->addStretch();
        paginationLayout->addLayout(pageButtonsLayout);
        mainLayout->addLayout(paginationLayout);
    }

public slots:
    void addTransactionRow(const QString& name, const QString& email, const QString& amount, const QString& status) {
        table->insertRow(0); // Insere no topo
        
        QString txid = QString("#TX-%1").arg(QRandomGenerator::global()->bounded(100000, 999999));
        
        table->setItem(0, 0, new QTableWidgetItem(name));
        table->setItem(0, 1, new QTableWidgetItem(email));
        table->setItem(0, 2, new QTableWidgetItem(txid));
        table->setItem(0, 3, new QTableWidgetItem("$ " + amount));
        table->item(0, 0)->setFont(QFont("Segoe UI", 10, QFont::Bold));
        
        QWidget* badgeContainer = new QWidget();
        QHBoxLayout* badgeLayout = new QHBoxLayout(badgeContainer);
        badgeLayout->setContentsMargins(15, 8, 15, 8);
        badgeLayout->setAlignment(Qt::AlignLeft | Qt::AlignVCenter);
        QLabel* badge = new QLabel(status);
        badge->setFont(QFont("Segoe UI", 9, QFont::Bold));
        
        if (status == "Approved") { badge->setStyleSheet(QString("background-color: rgba(34, 197, 94, 0.15); color: %1; border-radius: 6px; padding: 4px 12px;").arg(positiveGreen.name())); } 
        else if (status == "Blocked") { badge->setStyleSheet("background-color: rgba(239, 68, 68, 0.15); color: #EF4444; border-radius: 6px; padding: 4px 12px;"); } 
        else { badge->setStyleSheet(QString("background-color: rgba(227, 155, 53, 0.15); color: %1; border-radius: 6px; padding: 4px 12px;").arg(hstOrange.name())); }
        
        badgeLayout->addWidget(badge);
        table->setCellWidget(0, 4, badgeContainer);
        
        // Mantem maximo de 50 linhas para não travar memoria
        if (table->rowCount() > 50) table->removeRow(50);
    }
};

// --- ABA 3: ACCOUNT PROFILE FORM ---
// Reproducao de mockup HTML transformado em High-End Desktop Qt.
class AccountEditPage : public QWidget {
public:
    AccountEditPage(QWidget* parent = nullptr) : QWidget(parent) {
        QHBoxLayout* mainLayout = new QHBoxLayout(this);
        mainLayout->setContentsMargins(40, 40, 40, 40);
        mainLayout->setSpacing(30);
        
        // === COLUNA ESQUERDA (Perfil e Formulario de Edicao) ===
        CardWidget* leftCard = new CardWidget();
        QVBoxLayout* leftLayout = new QVBoxLayout(leftCard);
        leftLayout->setContentsMargins(40, 40, 40, 40);
        leftLayout->setSpacing(20);
        
        // Cabecalho do Perfil (Avatar + Informacoes em texto)
        QHBoxLayout* profileHeader = new QHBoxLayout();
        QLabel* avatar = new QLabel("MB");
        avatar->setFixedSize(90, 90);
        avatar->setAlignment(Qt::AlignCenter);
        avatar->setStyleSheet(QString("background-color: %1; color: %2; border-radius: 45px; font-weight: 900; font-size: 34px;").arg(hstTaupe.name(), bgDark.name()));
        
        QVBoxLayout* profileInfo = new QVBoxLayout();
        profileInfo->setSpacing(4);
        QLabel* nameLabel = new QLabel("Michael Berger");
        nameLabel->setStyleSheet(QString("color: %1; font-size: 28px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        QLabel* roleLabel = new QLabel("Development manager at HST Inc.");
        roleLabel->setStyleSheet(QString("color: %1; font-size: 14px; font-weight: bold; background: transparent;").arg(hstOrange.name()));
        QLabel* emailLabel = new QLabel("e-mail: michael.berger@hst.com.br");
        emailLabel->setStyleSheet(QString("color: %1; font-size: 13px; background: transparent;").arg(textGray.name()));
        QLabel* phoneLabel = new QLabel("phone: (123) 456-7890");
        phoneLabel->setStyleSheet(QString("color: %1; font-size: 13px; background: transparent;").arg(textGray.name()));
        
        profileInfo->addWidget(nameLabel); profileInfo->addWidget(roleLabel);
        profileInfo->addWidget(emailLabel); profileInfo->addWidget(phoneLabel);
        profileInfo->addStretch();
        
        profileHeader->addWidget(avatar);
        profileHeader->addSpacing(25);
        profileHeader->addLayout(profileInfo);
        profileHeader->addStretch();
        leftLayout->addLayout(profileHeader);
        
        QFrame* line1 = new QFrame(); line1->setFrameShape(QFrame::HLine); line1->setStyleSheet(QString("background-color: %1; margin: 10px 0px;").arg(borderDark.name()));
        leftLayout->addWidget(line1);
        
        // Titulos do Formulario HTML Traduzidos para QT
        QHBoxLayout* titleLayout = new QHBoxLayout();
        QLabel* formTitle = new QLabel("Account edit form");
        formTitle->setStyleSheet(QString("color: %1; font-size: 20px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        QLabel* formLegend = new QLabel("Default legend");
        formLegend->setStyleSheet(QString("color: %1; font-size: 15px; background: transparent;").arg(textGray.name()));
        titleLayout->addWidget(formTitle); titleLayout->addWidget(formLegend); titleLayout->addStretch();
        leftLayout->addLayout(titleLayout);
        
        QFrame* line3 = new QFrame(); line3->setFrameShape(QFrame::HLine); line3->setStyleSheet(QString("background-color: %1; margin-bottom: 20px;").arg(borderDark.name()));
        leftLayout->addWidget(line3);
        
        QLabel* sectionTitle = new QLabel("Personal data");
        sectionTitle->setStyleSheet(QString("color: %1; font-size: 16px; font-weight: bold; background: transparent;").arg(hstOrange.name()));
        leftLayout->addWidget(sectionTitle);
        
        // --- Campos de Inputs (QLineEdit) ---
        auto createInput = []() {
            QLineEdit* edit = new QLineEdit();
            edit->setStyleSheet(QString("QLineEdit { background-color: %1; color: %2; border: 1px solid %3; border-radius: 6px; padding: 10px 15px; font-size: 14px; } QLineEdit:focus { border: 1px solid %4; }").arg(bgDark.name(), textWhite.name(), borderDark.name(), hstOrange.name()));
            edit->setMinimumWidth(300);
            return edit;
        };
        auto createLabel = [](const QString& text) {
            QLabel* lbl = new QLabel(text);
            lbl->setStyleSheet(QString("color: %1; font-size: 14px; font-weight: bold; background: transparent;").arg(textWhite.name()));
            return lbl;
        };
        
        // Utilizando QFormLayout para alinhar automatico a esquerda (Label) e direita (Inputs)
        QFormLayout* form = new QFormLayout(); form->setLabelAlignment(Qt::AlignLeft | Qt::AlignVCenter); form->setHorizontalSpacing(100);
        form->addRow(createLabel("Prefix"), createInput());
        leftLayout->addLayout(form);
        QFrame* dash1 = new QFrame(); dash1->setFrameShape(QFrame::HLine); dash1->setStyleSheet(QString("border-top: 1px dashed %1; background: transparent;").arg(borderDark.name()));
        leftLayout->addWidget(dash1);
        
        QFormLayout* form2 = new QFormLayout(); form2->setLabelAlignment(Qt::AlignLeft | Qt::AlignVCenter); form2->setHorizontalSpacing(100);
        form2->addRow(createLabel("First Name *"), createInput());
        leftLayout->addLayout(form2);
        QFrame* dash2 = new QFrame(); dash2->setFrameShape(QFrame::HLine); dash2->setStyleSheet(QString("border-top: 1px dashed %1; background: transparent;").arg(borderDark.name()));
        leftLayout->addWidget(dash2);
        
        QFormLayout* form3 = new QFormLayout(); form3->setLabelAlignment(Qt::AlignLeft | Qt::AlignVCenter); form3->setHorizontalSpacing(100);
        form3->addRow(createLabel("Last Name *"), createInput());
        leftLayout->addLayout(form3);
        QFrame* dash3 = new QFrame(); dash3->setFrameShape(QFrame::HLine); dash3->setStyleSheet(QString("border-top: 1px dashed %1; background: transparent;").arg(borderDark.name()));
        leftLayout->addWidget(dash3);
        
        QFormLayout* form4 = new QFormLayout(); form4->setLabelAlignment(Qt::AlignLeft | Qt::AlignVCenter); form4->setHorizontalSpacing(100);
        form4->addRow(createLabel("Suffix"), createInput());
        leftLayout->addLayout(form4);
        
        leftLayout->addStretch();
        
        // === COLUNA DIREITA (Anotações do Sistema - Inspirado na Tarja Amarela) ===
        CardWidget* rightCard = new CardWidget();
        rightCard->setFixedWidth(380);
        // Fundo super escuro com destaque (Borda Laranja no topo) para imitar a tarja colorida
        rightCard->setStyleSheet(QString("CardWidget { background-color: #171615; border-radius: 12px; border: 1px solid %1; border-top: 4px solid %2; }").arg(borderDark.name(), hstOrange.name()));
        
        QVBoxLayout* rightLayout = new QVBoxLayout(rightCard);
        rightLayout->setContentsMargins(25, 30, 25, 25);
        rightLayout->setSpacing(12);
        
        QLabel* lblTitle = new QLabel("Title");
        lblTitle->setStyleSheet(QString("color: %1; font-size: 13px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        rightLayout->addWidget(lblTitle);
        
        QLineEdit* titleInput = new QLineEdit();
        titleInput->setStyleSheet(QString("QLineEdit { background-color: %1; color: %2; border: 1px solid %3; border-radius: 6px; padding: 10px; font-size: 14px; } QLineEdit:focus { border: 1px solid %4; }").arg(bgDark.name(), textWhite.name(), borderDark.name(), hstOrange.name()));
        rightLayout->addWidget(titleInput);
        
        rightLayout->addSpacing(5);
        
        QLabel* lblText = new QLabel("Note Text:");
        lblText->setStyleSheet(QString("color: %1; font-size: 13px; font-weight: bold; background: transparent;").arg(textWhite.name()));
        rightLayout->addWidget(lblText);
        
        // QTextEdit e perfeito para blocos de textos grandes (area de observacoes)
        QTextEdit* textEdit = new QTextEdit();
        textEdit->setFixedHeight(120);
        textEdit->setStyleSheet(QString("QTextEdit { background-color: %1; color: %2; border: 1px solid %3; border-radius: 6px; padding: 10px; font-size: 14px; } QTextEdit:focus { border: 1px solid %4; }").arg(bgDark.name(), textWhite.name(), borderDark.name(), hstOrange.name()));
        rightLayout->addWidget(textEdit);
        
        // Botao de envio com gradiente semelhante a imagem
        CustomButton* addNoteBtn = new CustomButton("Add a note", hstOrange);
        addNoteBtn->setStyleSheet(QString("QPushButton { background-color: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 %1, stop:1 %2); color: %3; border: 1px solid %4; border-radius: 4px; padding: 10px; font-weight: bold; font-size: 14px; } QPushButton:hover { background-color: %1; }").arg(hstOrange.lighter(110).name(), hstOrange.darker(120).name(), bgDark.name(), hstOrange.name()));
        rightLayout->addWidget(addNoteBtn);
        
        QFrame* line2 = new QFrame(); line2->setFrameShape(QFrame::HLine); line2->setStyleSheet(QString("background-color: %1; margin-top: 20px; margin-bottom: 20px;").arg(borderDark.name()));
        rightLayout->addWidget(line2);
        
        QLabel* lastNotesTitle = new QLabel("\xF0\x9F\x97\x92 Last Note for Account");
        lastNotesTitle->setStyleSheet(QString("color: %1; font-size: 16px; font-weight: bold; background: transparent; margin-bottom: 10px;").arg(textWhite.name()));
        rightLayout->addWidget(lastNotesTitle);
        
        QFrame* line4 = new QFrame(); line4->setFrameShape(QFrame::HLine); line4->setStyleSheet(QString("background-color: %1; margin-bottom: 10px;").arg(borderDark.name()));
        rightLayout->addWidget(line4);
        
        // Componente "Timeline" ou "Lista de Comentarios"
        auto createNoteWidget = [](const QString& time, const QString& text) {
            QWidget* noteWidget = new QWidget();
            noteWidget->setStyleSheet(QString("background-color: transparent;"));
            QVBoxLayout* nl = new QVBoxLayout(noteWidget);
            nl->setContentsMargins(0, 0, 0, 15);
            
            // Header do Comentario: Data (Laranja) + Ações (Icones)
            QHBoxLayout* header = new QHBoxLayout();
            QLabel* timeLbl = new QLabel("\xF0\x9F\x94\x97 " + time);
            timeLbl->setStyleSheet(QString("color: %1; font-size: 12px; font-weight: bold;").arg(hstOrange.name()));
            header->addWidget(timeLbl);
            header->addStretch();
            QLabel* actions = new QLabel("\xE2\x9C\x8E   \xF0\x9F\x96\xA8   \xF0\x9F\x97\x91");
            actions->setStyleSheet(QString("color: %1; font-size: 14px; cursor: pointer;").arg(textGray.name()));
            header->addWidget(actions);
            nl->addLayout(header);
            
            // Texto do Comentario
            QLabel* textLbl = new QLabel(text);
            textLbl->setWordWrap(true);
            textLbl->setStyleSheet(QString("color: %1; font-size: 13px; line-height: 1.4;").arg(textGray.name()));
            nl->addWidget(textLbl);
            
            QFrame* separator = new QFrame(); separator->setFrameShape(QFrame::HLine); separator->setStyleSheet(QString("background-color: %1; margin-top: 10px;").arg(borderDark.name()));
            nl->addWidget(separator);
            return noteWidget;
        };
        
        rightLayout->addWidget(createNoteWidget("10:30 AM", "Pellentesque malesuada nulla nunc facilisi. Donec tellus nec Donec et. Pellentesque metus dolor neque consequat scelerisque."));
        rightLayout->addWidget(createNoteWidget("09-09-2026 02:30 PM", "Security clearance updated for server access. Requires secondary approval from the administration."));
        
        rightLayout->addStretch();
        
        // Junta o Painel da Esquerda e o Painel da Direita
        mainLayout->addWidget(leftCard, 1);
        mainLayout->addWidget(rightCard);
    }
};

// ============================================================================
// MONITORAMENTO DE SISTEMA NATIVO (WIN32 API)
// Essa classe interage direto com o nucleo do Windows (kernel) para ler
// a porcentagem de uso real da CPU e a quantidade de RAM consumida na maquina.
// ============================================================================
class SystemMonitor {
public:
    SystemMonitor() { GetSystemTimes(&idleTime, &kernelTime, &userTime); }
    double getCpuUsage() {
        FILETIME newIdleTime, newKernelTime, newUserTime;
        GetSystemTimes(&newIdleTime, &newKernelTime, &newUserTime);
        ULONGLONG idle = subtractTimes(newIdleTime, idleTime);
        ULONGLONG kernel = subtractTimes(newKernelTime, kernelTime);
        ULONGLONG user = subtractTimes(newUserTime, userTime);
        idleTime = newIdleTime; kernelTime = newKernelTime; userTime = newUserTime;
        ULONGLONG sys = kernel + user;
        if (sys == 0) return 0.0;
        return (sys - idle) * 100.0 / sys;
    }
    double getRamUsage() {
        MEMORYSTATUSEX memInfo;
        memInfo.dwLength = sizeof(MEMORYSTATUSEX);
        GlobalMemoryStatusEx(&memInfo);
        return memInfo.dwMemoryLoad;
    }
private:
    FILETIME idleTime, kernelTime, userTime;
    ULONGLONG subtractTimes(const FILETIME& a, const FILETIME& b) {
        ULARGE_INTEGER la, lb;
        la.LowPart = a.dwLowDateTime; la.HighPart = a.dwHighDateTime;
        lb.LowPart = b.dwLowDateTime; lb.HighPart = b.dwHighDateTime;
        return la.QuadPart - lb.QuadPart;
    }
};

// ============================================================================
// CLOUD CONNECTION MANAGER COM QWEBSOCKET (Suporte Nativo a WSS/TLS)
// ============================================================================
class CloudManager : public QObject {
    Q_OBJECT
public:
    CloudManager(QObject* parent = nullptr) : QObject(parent) {
        connect(&webSocket, &QWebSocket::connected, this, [this]() {
            emit logMessage("[SUCESSO] Conectado ao Servidor Render (WSS Seguro)!");
        });
        connect(&webSocket, &QWebSocket::disconnected, this, [this]() {
            emit logMessage("[DESCONECTADO] Conexao com a Nuvem encerrada.");
        });
        connect(&webSocket, &QWebSocket::textMessageReceived, this, [this](const QString& msg) {
            emit messageReceived(msg);
        });
        connect(&webSocket, QOverload<QAbstractSocket::SocketError>::of(&QWebSocket::errorOccurred), this, [this](QAbstractSocket::SocketError err) {
            emit logMessage(QString("[ERRO DE REDE] %1").arg(webSocket.errorString()));
        });
        connect(&webSocket, &QWebSocket::sslErrors, this, [this](const QList<QSslError>& errors) {
            for (const auto& err : errors) {
                emit logMessage(QString("[ERRO SSL] %1").arg(err.errorString()));
            }
        });
    }
    
    void connectToCloud(const QString& url) {
        emit logMessage(QString("[CLOUD] Resolvendo criptografia e conectando a %1...").arg(url));
        webSocket.open(QUrl(url));
    }
    
    void sendMessage(const QString& msg) {
        if (webSocket.isValid()) {
            webSocket.sendTextMessage(msg);
        } else {
            emit logMessage("[ERRO] Servidor na Nuvem ainda nao conectado!");
        }
    }

signals:
    void logMessage(QString msg);
    void messageReceived(QString msg);

private:
    QWebSocket webSocket;
};

class NetworkChatPage : public QWidget {
    Q_OBJECT
public:
    NetworkChatPage(QWidget* parent = nullptr) : QWidget(parent) {
        cloudManager = new CloudManager(this);
        telemetryTimer = new QTimer(this);
        
        // Timer de Telemetria: Roda a cada 1 segundo.
        connect(telemetryTimer, &QTimer::timeout, this, [this]() {
            double cpu = sysMonitor.getCpuUsage();
            double ram = sysMonitor.getRamUsage();
            QString msg = QString("[TELEMETRY] CPU:%1|RAM:%2").arg(cpu, 0, 'f', 1).arg(ram, 0, 'f', 1);
            cloudManager->sendMessage(msg);
        });
        
        QVBoxLayout* mainLayout = new QVBoxLayout(this);
        mainLayout->setContentsMargins(40, 40, 40, 40);
        mainLayout->setSpacing(20);
        
        QLabel* title = new QLabel("Render Cloud WebSocket (WSS)");
        title->setStyleSheet("color: #F0F4F8; font-size: 28px; font-weight: bold;");
        mainLayout->addWidget(title);
        
        // Configuracoes
        CardWidget* configCard = new CardWidget();
        QHBoxLayout* configLayout = new QHBoxLayout(configCard);
        configLayout->setContentsMargins(20, 20, 20, 20);
        
        ipInput = new QLineEdit("wss://servidor-3h8i.onrender.com");
        ipInput->setStyleSheet("QLineEdit { background-color: #111216; color: white; border: 1px solid #252830; padding: 8px; border-radius: 4px; }");
        ipInput->setFixedWidth(300);
        
        CustomButton* connectClientBtn = new CustomButton("Connect to Render Cloud", QColor("#22C55E"));
        
        configLayout->addWidget(new QLabel("Cloud URL:")); configLayout->addWidget(ipInput);
        configLayout->addSpacing(20);
        configLayout->addWidget(connectClientBtn);
        configLayout->addStretch();
        mainLayout->addWidget(configCard);
        
        // Chat e Logs
        QHBoxLayout* middleLayout = new QHBoxLayout();
        
        CardWidget* chatCard = new CardWidget();
        QVBoxLayout* chatLayout = new QVBoxLayout(chatCard);
        chatArea = new QTextEdit();
        chatArea->setReadOnly(true);
        chatArea->setStyleSheet("QTextEdit { background-color: #111216; color: white; border: none; font-size: 14px; }");
        chatLayout->addWidget(chatArea, 1);
        
        QHBoxLayout* inputLayout = new QHBoxLayout();
        chatInput = new QLineEdit();
        chatInput->setStyleSheet(ipInput->styleSheet());
        CustomButton* sendBtn = new CustomButton("Send to Cloud", QColor("#E39B35"));
        inputLayout->addWidget(chatInput, 1);
        inputLayout->addWidget(sendBtn);
        chatLayout->addLayout(inputLayout);
        // NOVO: Lista de Clientes Conectados
        CardWidget* clientsCard = new CardWidget();
        QVBoxLayout* clientsLayout = new QVBoxLayout(clientsCard);
        QLabel* clientsTitle = new QLabel("Logged Clients");
        clientsTitle->setStyleSheet("color: #22C55E; font-weight: bold;");
        clientsList = new QListWidget();
        clientsList->setStyleSheet("QListWidget { background-color: #0A0A0C; color: #F0F4F8; border: none; font-size: 13px; padding: 5px; }");
        clientsLayout->addWidget(clientsTitle);
        clientsLayout->addWidget(clientsList, 1);

        // LOG AREA (Restaurado)
        CardWidget* logCard = new CardWidget();
        QVBoxLayout* logLayout = new QVBoxLayout(logCard);
        QLabel* logTitle = new QLabel("Observability Logs (QWebSocket)");
        logTitle->setStyleSheet("color: #E39B35; font-weight: bold;");
        logArea = new QTextEdit();
        logArea->setReadOnly(true);
        logArea->setStyleSheet("QTextEdit { background-color: #0A0A0C; color: #22C55E; border: none; font-family: Consolas; font-size: 12px; }");
        logLayout->addWidget(logTitle);
        logLayout->addWidget(logArea, 1);

        middleLayout->addWidget(clientsCard, 1); // Coluna 1
        middleLayout->addWidget(chatCard, 2);    // Coluna 2 (Maior)
        middleLayout->addWidget(logCard, 1);     // Coluna 3
        
        mainLayout->addLayout(middleLayout, 1);
        
        // Eventos da Interface
        connect(connectClientBtn, &QPushButton::clicked, this, [this]() {
            cloudManager->connectToCloud(ipInput->text());
            telemetryTimer->start(1000); // Comeca a mandar a telemetria apos clicar em conectar
        });
        connect(sendBtn, &QPushButton::clicked, this, &NetworkChatPage::onSendMessage);
        connect(chatInput, &QLineEdit::returnPressed, this, &NetworkChatPage::onSendMessage);
        
        // Sinais vindos do QWebSocket (Cloud)
        connect(cloudManager, &CloudManager::logMessage, this, &NetworkChatPage::appendLog);
        connect(cloudManager, &CloudManager::messageReceived, this, [this](QString msg) {
            if (msg.startsWith("[TELEMETRY]")) {
                QRegularExpression rx("CPU:([0-9\\.]+)\\|RAM:([0-9\\.]+)");
                QRegularExpressionMatch match = rx.match(msg);
                if (match.hasMatch()) {
                    emit telemetryReceived(match.captured(1).toDouble(), match.captured(2).toDouble());
                }
            } else if (msg.startsWith("[CLIENT_LIST]")) {
                // Parse da Lista de Clientes enviada pelo servidor
                QString cleanList = msg.mid(13); // Remove o "[CLIENT_LIST]"
                QStringList clients = cleanList.split(",", Qt::SkipEmptyParts);
                clientsList->clear();
                clientsList->addItems(clients);
            } else if (msg.startsWith("[MSG_FROM:")) {
                int endBracket = msg.indexOf("]");
                QString senderId = msg.mid(10, endBracket - 10);
                QString realMsg = msg.mid(endBracket + 2); // Pula "] "
                chatArea->append("<b style='color:#E39B35'>[User-" + senderId + "]</b> " + realMsg);
            } else if (msg.startsWith("[MAGIC_JSON]")) {
                // --- PARSER DE DADOS DO POSTGRESQL ---
                // O servidor envia um JSON estruturado. Usamos o QJsonDocument do Qt para ler de forma segura.
                QString jsonStr = msg.mid(12); // Pula o prefixo "[MAGIC_JSON]"
                QJsonDocument doc = QJsonDocument::fromJson(jsonStr.toUtf8());
                
                if (!doc.isNull()) {
                    QJsonObject obj = doc.object();
                    QString name = obj["name"].toString();
                    QString email = obj["email"].toString();
                    QString amount = QString::number(obj["amount"].toInt());
                    QString status = obj["status"].toString();
                    
                    chatArea->append("<b style='color:#22C55E'>[JSON Event]</b> User: " + name);
                    
                    // Emite o sinal para que a aba 'Transactions' adicione uma nova linha na tabela
                    emit magicTransactionReceived(name, email, amount, status);
                }
            } else if (msg.startsWith("[SYS_INFO]") || msg.startsWith("[V3_STATUS]")) {
                // Mensagens de sistema enviadas pelo servidor (ex: Status do Banco de Dados)
                chatArea->append("<b style='color:#3B82F6'>[System]</b> " + msg);
            } else {

                chatArea->append("<b style='color:#857A69'>[Cloud Event]</b> " + msg);
            }
        });
    }
signals:
    void telemetryReceived(double cpu, double ram);
    void magicTransactionReceived(QString name, QString email, QString amount, QString status);
private:
    CloudManager* cloudManager;
    QTimer* telemetryTimer;
    SystemMonitor sysMonitor;
    QLineEdit* ipInput;
    QListWidget* clientsList; // Nova lista
    QTextEdit* chatArea; QLineEdit* chatInput; QTextEdit* logArea;

    void onSendMessage() {
        QString msg = chatInput->text();
        if (msg.isEmpty()) return;
        chatArea->append("<b style='color:#E39B35'>[You]</b> " + msg);
        cloudManager->sendMessage(msg);
        chatInput->clear();
    }
    void appendLog(const QString& msg) {
        logArea->append(msg);
        logArea->verticalScrollBar()->setValue(logArea->verticalScrollBar()->maximum());
    }
};

// --- Tela de Carregamento Ficticia (Placeholder) ---
class PlaceholderPage : public QWidget {
public:
    PlaceholderPage(const QString& text, QWidget* parent = nullptr) : QWidget(parent) {
        QVBoxLayout* layout = new QVBoxLayout(this);
        QLabel* label = new QLabel(text);
        label->setAlignment(Qt::AlignCenter);
        label->setStyleSheet(QString("color: %1; font-size: 36px; font-weight: bold;").arg(hstTaupe.name()));
        layout->addWidget(label);
    }
};

// ============================================================================
// 5. JANELA PRINCIPAL (CORE)
// ============================================================================
class MainWindow : public QMainWindow {
public:
    MainWindow() {
        setWindowTitle("HST DASHBOARD V2 - POSTGRES INTEGRATED");

        resize(1400, 850);
        setObjectName("MainWindow");
        setStyleSheet(QString("QMainWindow#MainWindow { border-image: url(fundo.jpg) 0 0 0 0 stretch stretch; }"));
        
        QWidget* central = new QWidget();
        setCentralWidget(central);
        
        QHBoxLayout* mainLayout = new QHBoxLayout(central);
        mainLayout->setContentsMargins(0, 0, 0, 0); // Sem espacos para que a sidebar encoste nos cantos
        mainLayout->setSpacing(0);
        
        // 1. Injeta a Barra Lateral de Navegacao
        Sidebar* sidebar = new Sidebar();
        mainLayout->addWidget(sidebar);
        
        // 2. QStackedWidget funciona como um "Caderno", onde cada QWidget adicionado é uma pagina
        QStackedWidget* stacked = new QStackedWidget();
        
        QString liConn = QString::fromUtf8(qgetenv("WEBCRAWLER_DB_CONNECTION")).trimmed().remove('"');
        auto* liPage = new LinkedInDashboardPage(liConn);
        auto* jobsPage = new LinkedInJobsPage(liConn);
        auto* robotPage = new RobotControlPage();
        
        stacked->addWidget(liPage);                                              // Pagina 0: Monitoramento (Postgres)
        stacked->addWidget(jobsPage);                                            // Pagina 1: LinkedIn Vagas (Tabela)
        stacked->addWidget(robotPage);                                           // Pagina 2: Controle do Robô (API Render)

        mainLayout->addWidget(stacked, 1);
        
        // 3. Conexao de Evento: Quando clicar na Sidebar, muda a tela do Caderno (StackedWidget)
        connect(sidebar, &Sidebar::pageSelected, stacked, &QStackedWidget::setCurrentIndex);
    }
};

// ============================================================================
// ENTRYPOINT C++ (main)
// ============================================================================
int main(int argc, char* argv[]) {
    QApplication app(argc, argv);

    // ── Carrega .env ANTES de criar qualquer widget ───────────
    // Candidatos de caminho (mesmo comportamento do AppConfig.h / C#)
    const QStringList envCandidates = {
        QDir::current().filePath(".env"),
        QDir::current().filePath("../.env"),
        QDir::current().filePath("WebCrawler/.env")
    };
    for (const QString& envPath : envCandidates) {
        QFile envFile(envPath);
        if (!envFile.exists()) continue;
        if (!envFile.open(QIODevice::ReadOnly | QIODevice::Text)) continue;
        QTextStream in(&envFile);
        while (!in.atEnd()) {
            QString line = in.readLine().trimmed();
            if (line.isEmpty() || line.startsWith('#')) continue;
            int eq = line.indexOf('=');
            if (eq <= 0) continue;
            QString key = line.left(eq).trimmed();
            QString val = line.mid(eq + 1).trimmed();
            if (val.startsWith('"') && val.endsWith('"'))
                val = val.mid(1, val.length() - 2);
            if (!key.isEmpty() && qgetenv(key.toUtf8()).isEmpty())
                qputenv(key.toUtf8(), val.toUtf8());
        }
        envFile.close();
        qDebug() << "[ENV] Arquivo carregado:" << envPath;
        break; // Usa o primeiro encontrado
    }
    // ─────────────────────────────────────────────────────────

    // Define a fonte padrao do sistema operacional que seja estetica
    QFont font("Segoe UI", 10);
    app.setFont(font);
    
    // Inicia e mostra a janela
    MainWindow w;
    w.show();
    
    return app.exec(); // Loop eterno de execucao
}

// Necessario pois o CMake faz o processamento automatico das Macros MOC (Metacompilador Qt)
#include "main.moc"
