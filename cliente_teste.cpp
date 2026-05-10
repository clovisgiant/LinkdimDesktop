#include <iostream>
#include <string>

// Includes do WebSocket++
#include <websocketpp/config/asio_no_tls_client.hpp>
#include <websocketpp/client.hpp>

// Alias para simplificar
typedef websocketpp::client<websocketpp::config::asio_client> ws_client;
using websocketpp::connection_hdl;

// Disparado quando a conexão WebSocket é aberta e o handshake concluído
void on_open(ws_client* c, connection_hdl hdl) {
    std::cout << "=================================================\n";
    std::cout << " Conectado ao Servidor Render via WEBSOCKET! \n";
    std::cout << "=================================================\n";

    // Cria o JSON de telemetria
    std::string mensagem = "{ \"CPU_Uso\": 22.5, \"RAM_Uso\": 6100, \"Aviso\": \"Render Proxy Bypass OK!\" }";
    std::cout << "Enviando dados: " << mensagem << std::endl;

    websocketpp::lib::error_code ec;
    // Envia o frame como texto
    c->send(hdl, mensagem, websocketpp::frame::opcode::text, ec);
    
    if (ec) {
        std::cout << "Erro ao enviar: " << ec.message() << std::endl;
    }
}

// Disparado quando recebemos uma mensagem do servidor
void on_message(ws_client* c, connection_hdl hdl, websocketpp::config::asio_client::message_type::ptr msg) {
    std::cout << "\nResposta ecoada pelo Servidor Render:\n";
    std::cout << msg->get_payload() << "\n\n";

    // Fecha a conexão de forma elegante depois de receber a resposta (só para esse teste)
    websocketpp::lib::error_code ec;
    c->close(hdl, websocketpp::close::status::normal, "Teste concluido", ec);
}

// Disparado se a conexão falhar ou for recusada
void on_fail(ws_client* c, connection_hdl hdl) {
    std::cout << "Falha na conexao. O servidor pode nao estar pronto ainda ou a porta esta fechada." << std::endl;
}

int main() {
    ws_client c;

    try {
        // Remove logs excessivos, deixa apenas erros e alertas
        c.clear_access_channels(websocketpp::log::alevel::all);
        c.set_access_channels(websocketpp::log::alevel::connect | websocketpp::log::alevel::disconnect);
        
        c.init_asio();

        // Registra nossas funções
        c.set_open_handler(bind(&on_open, &c, std::placeholders::_1));
        c.set_message_handler(bind(&on_message, &c, std::placeholders::_1, std::placeholders::_2));
        c.set_fail_handler(bind(&on_fail, &c, std::placeholders::_1));

        websocketpp::lib::error_code ec;

        // O Render faz proxy da porta 80 e 443.
        // Se usarmos ws:// na porta 80, ele fará o upgrade para websocket perfeitamente!
        // URL LOCAL PARA TESTE (sem proxy, sem TLS)
        std::string uri = "ws://127.0.0.1:8080";
        
        std::cout << "Tentando conectar em: " << uri << "...\n";

        ws_client::connection_ptr con = c.get_connection(uri, ec);
        if (ec) {
            std::cout << "Erro ao criar conexao: " << ec.message() << std::endl;
            return 0;
        }

        // Pede para conectar
        c.connect(con);

        // Roda o loop assíncrono (bloqueia aqui até terminar o processo)
        c.run();

    } catch (websocketpp::exception const & e) {
        std::cout << "Excecao WebSocket: " << e.what() << std::endl;
    }

    return 0;
}
