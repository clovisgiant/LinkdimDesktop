#include <QCoreApplication>
#include <QWebSocket>
#include <QObject>
#include <QDebug>
#include <QSslError>
#include <QTimer>

class ClienteObservavel : public QObject {
    Q_OBJECT
public:
    explicit ClienteObservavel(QObject *parent = nullptr) : QObject(parent) {
        // Conexão dos Sinais do WebSocket com as nossas funções de Observabilidade
        connect(&webSocket, &QWebSocket::connected, this, &ClienteObservavel::onConnected);
        connect(&webSocket, &QWebSocket::disconnected, this, &ClienteObservavel::onDisconnected);
        connect(&webSocket, &QWebSocket::textMessageReceived, this, &ClienteObservavel::onTextMessageReceived);
        connect(&webSocket, &QWebSocket::stateChanged, this, &ClienteObservavel::onStateChanged);
        
        // Captura de Erros!
        connect(&webSocket, QOverload<QAbstractSocket::SocketError>::of(&QWebSocket::errorOccurred),
            this, &ClienteObservavel::onError);
        connect(&webSocket, &QWebSocket::sslErrors, this, &ClienteObservavel::onSslErrors);

        qDebug() << "[OBSERVABILIDADE] Cliente Qt instanciado com sucesso. Aguardando inicializacao...";
    }

    void iniciarConexao(const QString &url) {
        qDebug() << "[OBSERVABILIDADE] Iniciando tentativa de conexao com o servidor Render em:" << url;
        qDebug() << "[OBSERVABILIDADE] O Qt cuidara do TLS/SSL usando as bibliotecas criptograficas do Windows automaticamente.";
        webSocket.open(QUrl(url));
    }

private slots:
    // Acompanha passo-a-passo os estados internos do Socket do Qt
    void onStateChanged(QAbstractSocket::SocketState state) {
        qDebug() << "[ESTADO DO SOCKET] Mudou para:" << state;
    }

    void onConnected() {
        qDebug() << "========================================================";
        qDebug() << " [SUCESSO] CONEXAO WSS (Segura) ESTABELECIDA NO RENDER! ";
        qDebug() << "========================================================";

        QString telemetria = "{ \"CPU_Uso\": 38.4, \"RAM_Uso\": 7120, \"Aviso\": \"QWebSocket rodando com sucesso na Nuvem!\" }";
        qDebug() << "[ENVIO] Transmitindo JSON de Telemetria:" << telemetria;
        
        webSocket.sendTextMessage(telemetria);
    }

    void onTextMessageReceived(QString message) {
        qDebug() << "========================================================";
        qDebug() << "[RECEBIDO] O Servidor Render respondeu (ECHO):";
        qDebug() << message;
        qDebug() << "========================================================";

        // Desconecta e fecha o aplicativo apos receber a resposta para finalizar o teste
        qDebug() << "[OBSERVABILIDADE] Teste finalizado com sucesso. Encerrando conexao...";
        webSocket.close();
    }

    void onDisconnected() {
        qDebug() << "[OBSERVABILIDADE] Conexao encerrada pelo servidor ou pelo proprio cliente.";
        QCoreApplication::quit(); // Fecha o terminal e o app
    }

    void onError(QAbstractSocket::SocketError error) {
        qDebug() << "[ERRO DE SOCKET] Detectamos um problema de rede:" << webSocket.errorString();
    }

    void onSslErrors(const QList<QSslError> &errors) {
        qDebug() << "[ERRO DE SSL/TLS] Tivemos problemas com o certificado HTTPS do Render:";
        for (const QSslError &error : errors) {
            qDebug() << "  -> " << error.errorString();
        }
        
        // Em um cenario real de producao a gente investiga, mas num teste voce poderia usar:
        // webSocket.ignoreSslErrors(); 
        // Porem, como o Render usa certificados validos (Let's Encrypt), isso nao deve acontecer!
    }

private:
    QWebSocket webSocket;
};

int main(int argc, char *argv[]) {
    QCoreApplication a(argc, argv);

    qDebug() << "\n--- INICIANDO TESTE DE OBSERVABILIDADE WEBSOCKET (QT) ---";

    ClienteObservavel cliente;
    
    // ATENCAO: "wss://" e o prefixo para WebSocket Seguro (criptografia TLS, trafega na porta 443)
    cliente.iniciarConexao("wss://servidor-3h8i.onrender.com");

    return a.exec();
}

// Necessario para que o CMake compile a classe 'ClienteObservavel' corretamente
// num arquivo de codigo unico (.cpp)
#include "cliente_qt.moc"
