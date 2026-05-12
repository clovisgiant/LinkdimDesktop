# 📐 Arquitetura Técnica - Sistema LinkDim

Este diagrama ilustra como os componentes do sistema interagem entre o seu computador local e a infraestrutura na nuvem (Render).

```mermaid
graph TD
    subgraph "Sua Casa (Desktop)"
        QT[Painel Qt6 C++] -- "Comandos (JSON/REST)" --> PY
        PY -- "Logs em Tempo Real (WebSockets)" --> QT
    end

    subgraph "Nuvem (Render - Docker Container)"
        PY[Orquestrador Python - FastAPI] -- "Inicia / Monitora" --> CS
        CS[Motor WebCrawler - C# .NET] -- "Controla" --> CH
        CH[Chromium Headless - Modo Pluma] -- "Lê/Clica" --> LI
        
        CS -- "Salva Vagas" --> DB[(PostgreSQL)]
        PY -- "Diagnóstico" --> DIAG[Endpoints /diag]
    end

    subgraph "Internet"
        LI[LinkedIn Web]
    end

    style QT fill:#2c3e50,stroke:#3498db,stroke-width:2px,color:#fff
    style PY fill:#27ae60,stroke:#2ecc71,stroke-width:2px,color:#fff
    style CS fill:#8e44ad,stroke:#9b59b6,stroke-width:2px,color:#fff
    style CH fill:#e67e22,stroke:#f39c12,stroke-width:2px,color:#fff
    style DB fill:#c0392b,stroke:#e74c3c,stroke-width:2px,color:#fff
    style LI fill:#0077b5,stroke:#00a0dc,stroke-width:2px,color:#fff
```

## 🛠️ Detalhes das Camadas

1.  **Painel Qt (C++ / Desktop)**: Sua interface de comando. Ela não precisa de muita internet, apenas envia as ordens (Start/Stop) e exibe os logs que vêm da nuvem.
2.  **Orquestrador (Python / FastAPI)**: É o "gerente". Ele fica de plantão 24h no Render recebendo seus comandos e garantindo que o robô em C# esteja rodando.
3.  **Motor Crawler (C# / .NET 8)**: É a "força bruta". Ele carrega a inteligência de busca, navegação humana e bypass de segurança.
4.  **Chromium (Modo Pluma)**: O navegador "invisível". Configuramos ele para ser ultra-leve (sem imagens) para caber na memória do Render.
5.  **PostgreSQL**: Onde a sua lista de vagas fica guardada com segurança para que você nunca perca uma oportunidade.

---
**Status da Obra:** Engenharia concluída. Pronto para escala! 👊🤖💎🚀
