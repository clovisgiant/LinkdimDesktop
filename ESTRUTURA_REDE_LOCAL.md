# 🌐 Guia de Simulação em Rede Local - LinkDim

Este guia explica como configurar a estrutura que deixamos preparada para testar o sistema em dois computadores da sua rede local. Isso permite separar a carga de processamento e validar a comunicação entre o **Servidor (Backend + DB)** e o **Painel (Dashboard Qt)**.

---

## 🏗️ Estrutura Recomendada

*   **PC 1 (Servidor):** Rodará o Banco de Dados PostgreSQL e o Orquestrador Python (FastAPI). É quem "manda" no robô.
*   **PC 2 (Cliente):** Rodará o Painel C++ (Dashboard) para você monitorar e enviar comandos.

---

## 🛠️ Passo 1: Configurar o Servidor (PC 1)

Neste computador, você deve ter o projeto completo.

1.  **Banco de Dados**:
    *   Certifique-se que o PostgreSQL está rodando.
    *   Crie um banco chamado `ProjetoDb`.
2.  **Ajuste o Script**:
    *   Abra o arquivo `iniciar_servidor_local.ps1`.
    *   (Já ajustado): Senha: `2748` | Banco: `ProjetoDb`.
    *   (Já ajustado): IP do Servidor: `192.168.1.70`.
3.  **Compilar o Robô**:
    *   O Backend precisa do executável do crawler pronto.
    *   Rode: `.\rodar_crawler.ps1` uma vez para garantir que o `LinkdimCrawler.exe` foi gerado na pasta `build_crawler`.
4.  **Iniciar o Servidor**:
    *   No terminal, execute:
        ```powershell
        .\iniciar_servidor_local.ps1
        ```
    *   O terminal mostrará: `📢 SEU IP LOCAL: http://192.168.1.XX:8000`.

---

## 💻 Passo 2: Configurar o Dashboard (PC 2)

Neste computador, você só precisa do executável do Dashboard.

1.  **Copiar o App**:
    *   No PC 1, rode `.\gerar_deploy.ps1` para criar a pasta `dist`.
    *   Copie a pasta `dist` inteira para o PC 2 via Pendrive ou rede.
2.  **Conectar**:
    *   Abra o `LinkdimDashboard.exe` no PC 2.
    *   No campo **API URL**, digite o endereço que apareceu no PC 1: `http://192.168.1.XX:8000`.
    *   Clique em **Conectar**.

---

## 🧪 O que testar?

1.  **Logs Remotos**: Clique em "Start Crawler" no PC 2 e veja os logs aparecendo em tempo real, mesmo com o robô rodando no PC 1.
2.  **Banco de Dados**: Verifique se as vagas encontradas estão sendo salvas corretamente no Postgres do PC 1.
3.  **Estabilidade**: Observe se o PC 2 continua fluido enquanto o PC 1 faz o trabalho pesado de navegação.

---
**Dica:** Se o PC 2 não conseguir conectar, verifique se o **Firewall do Windows** no PC 1 está permitindo conexões na porta `8000`.
