# 🏗️ Estratégia de Arquitetura — WebCrawler como Produto Comercial

## Visão Geral do Ecossistema

```
┌─────────────────────────────────────────────────────────────┐
│                    CLIENTE (PC do usuário)                   │
│                                                             │
│   ┌──────────────────────────────────┐                      │
│   │     Qt C++ Desktop App           │                      │
│   │  ┌─────────────────────────────┐ │                      │
│   │  │  Tela de Login do Produto   │ │                      │
│   │  │  (email + senha do PRODUTO) │ │                      │
│   │  └─────────────────────────────┘ │                      │
│   │  ┌─────────────────────────────┐ │                      │
│   │  │  Configuração do LinkedIn   │ │                      │
│   │  │  (usuário + senha LinkedIn) │ │                      │
│   │  └─────────────────────────────┘ │                      │
│   │  ┌─────────────────────────────┐ │                      │
│   │  │  Dashboard de Monitoramento │ │                      │
│   │  │  Vagas / Status / Logs      │ │                      │
│   │  └─────────────────────────────┘ │                      │
│   └──────────────┬───────────────────┘                      │
└──────────────────┼──────────────────────────────────────────┘
                   │ HTTPS / WebSocket (TLS)
                   ▼
┌─────────────────────────────────────────────────────────────┐
│                   RENDER.COM (Cloud)                         │
│                                                             │
│   ┌──────────────────────────────────┐                      │
│   │     API Backend (FastAPI/Python) │  ← Orquestrador      │
│   │  • Autenticação dos usuários     │                      │
│   │  • Armazena credenciais cifradas │                      │
│   │  • Dispara/para o crawler        │                      │
│   │  • WebSocket para status live    │                      │
│   └──────────────┬───────────────────┘                      │
│                  │                                           │
│   ┌──────────────▼───────────────────┐                      │
│   │   WebCrawler C# (Background Job) │  ← Worker            │
│   │  • Recebe credenciais via env    │                      │
│   │  • Roda Selenium headless        │                      │
│   │  • Salva vagas no PostgreSQL     │                      │
│   │  • Reporta status via API        │                      │
│   └──────────────┬───────────────────┘                      │
│                  │                                           │
│   ┌──────────────▼───────────────────┐                      │
│   │      PostgreSQL (Render DB)       │                      │
│   │  • 1 schema por usuário/cliente  │                      │
│   │  • Isolamento total de dados     │                      │
│   └──────────────────────────────────┘                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔐 Como Passar Credenciais do Qt → Crawler com Segurança

### Fluxo Completo (passo a passo):

```
1. Usuário digita no Qt App:
   - Email LinkedIn: usuario@email.com
   - Senha LinkedIn: ************

2. Qt App envia para API Backend (HTTPS POST):
   POST https://api.seuapp.render.com/api/credentials
   Authorization: Bearer <token_do_usuario_no_produto>
   Body: {
     "linkedin_user": "usuario@email.com",
     "linkedin_pass": "<cifrado_com_AES_no_cliente>"
   }

3. API Backend:
   - Valida o token JWT do usuário
   - Decifra, re-cifra com chave do servidor
   - Armazena em tabela `user_credentials` no PostgreSQL
   - Nunca salva em texto puro

4. Quando o crawler vai rodar:
   - API injeta credenciais como variáveis de ambiente no processo
   - O C# lê via GetRequiredEnv() (já implementado!)
   - Selenium usa para fazer login
```

---

## 🏛️ Estrutura de Tabelas (Multi-tenant)

```sql
-- Usuários do produto (seus clientes)
CREATE TABLE users (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email       TEXT UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,        -- bcrypt
    plan        TEXT DEFAULT 'free',    -- free / pro / enterprise
    created_at  TIMESTAMP DEFAULT NOW()
);

-- Credenciais LinkedIn cifradas por usuário
CREATE TABLE user_credentials (
    user_id     UUID REFERENCES users(id),
    platform    TEXT DEFAULT 'linkedin',
    username    TEXT NOT NULL,
    password_enc TEXT NOT NULL,         -- AES-256 cifrado
    iv          TEXT NOT NULL,          -- vetor de inicialização
    updated_at  TIMESTAMP DEFAULT NOW(),
    PRIMARY KEY (user_id, platform)
);

-- Jobs de crawler por usuário
CREATE TABLE crawler_jobs (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID REFERENCES users(id),
    status      TEXT DEFAULT 'idle',    -- idle/running/paused/error
    started_at  TIMESTAMP,
    last_heartbeat TIMESTAMP,
    config      JSONB                   -- termos de busca, limites etc
);

-- Vagas por usuário (isoladas)
CREATE TABLE vagas (
    id          BIGSERIAL PRIMARY KEY,
    user_id     UUID REFERENCES users(id),
    titulo      TEXT NOT NULL,
    empresa     TEXT NOT NULL,
    localizacao TEXT NOT NULL,
    link        TEXT NOT NULL,
    data_insercao           TIMESTAMP NOT NULL DEFAULT NOW(),
    candidatura_simplificada BOOLEAN DEFAULT FALSE,
    candidatura_enviada      BOOLEAN DEFAULT FALSE,
    data_candidatura         TIMESTAMP NULL,
    candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE,
    data_envio_sucesso       TIMESTAMP NULL,
    candidatura_indisponivel BOOLEAN DEFAULT FALSE,
    motivo_indisponibilidade TEXT NULL,
    data_indisponibilidade   TIMESTAMP NULL,
    UNIQUE (user_id, link)
);
```

---

## 🖥️ Arquitetura do Qt App (Telas)

### Telas necessárias:

```
1. 🔑 Login do Produto
   - Email + Senha do produto (não do LinkedIn!)
   - "Criar conta" / "Esqueci senha"
   - Salva JWT local cifrado

2. ⚙️ Configuração LinkedIn
   - Campo: "Seu email do LinkedIn"
   - Campo: "Sua senha do LinkedIn" (QLineEdit com echoMode = Password)
   - Botão: "Testar conexão"
   - Botão: "Salvar credenciais" → envia cifrado para API

3. 🎛️ Painel de Controle
   - Botão START / STOP do crawler
   - Status: Rodando / Parado / Erro
   - Termos de busca (QStringList editável)
   - Limite de candidaturas por ciclo
   - Horário ativo (início / fim)

4. 📊 Dashboard de Vagas
   - Tabela com vagas coletadas
   - Filtros: Status, Empresa, Data
   - Contadores: Coletadas / Candidatadas / Sucesso

5. 📋 Log em Tempo Real
   - QTextEdit com auto-scroll
   - WebSocket recebendo logs do crawler ao vivo
```

---

## 🔒 Segurança das Credenciais (Detalhamento)

### No Qt C++ (lado cliente):

```cpp
// Usar QKeychain → armazena no Credential Manager do Windows
// NUNCA armazenar senha em texto puro localmente

#include <QKeychain>

// Salvar credencial no sistema operacional:
QKeychain::WritePasswordJob job("WebCrawlerApp");
job.setKey("linkedin_password");
job.setTextData(password);
job.exec();

// Ler credencial do sistema operacional:
QKeychain::ReadPasswordJob job("WebCrawlerApp");
job.setKey("linkedin_password");
job.exec();
QString password = job.textData();
```

### Na API Backend (lado servidor):

```python
# FastAPI/Python — cifragem com Fernet (AES-128-CBC + HMAC)
import os
from cryptography.fernet import Fernet

SECRET_KEY = os.environ["CREDENTIAL_ENCRYPTION_KEY"]  # nunca no código!
fernet = Fernet(SECRET_KEY)

def encrypt_password(plain: str) -> str:
    return fernet.encrypt(plain.encode()).decode()

def decrypt_password(encrypted: str) -> str:
    return fernet.decrypt(encrypted.encode()).decode()
```

---

## 🚀 Estratégia de Deploy no Render.com

### Serviços necessários:

| Serviço            | Tipo no Render          | Custo estimado |
|--------------------|------------------------|----------------|
| API Backend        | Web Service (Python)   | $7/mês         |
| WebCrawler C#      | Background Worker      | $7/mês         |
| PostgreSQL         | Managed Database       | $7/mês         |
| **Total MVP**      |                        | **~$21/mês**   |

### Variáveis de ambiente no Render (Worker do C#):

```
LINKEDIN_USERNAME=<injetado pela API em tempo de execução>
LINKEDIN_PASSWORD=<injetado pela API em tempo de execução>
WEBCRAWLER_DB_CONNECTION=<connection string do PostgreSQL>
WEBCRAWLER_USER_ID=<UUID do usuário dono do job>
WEBCRAWLER_MAX_APPLY_PER_CYCLE=15
```

> ⚠️ ATENÇÃO: O Selenium no Render precisa de Chrome headless.
> Use Dockerfile com Chrome instalado no worker C#.

---

## 📦 Fases de Desenvolvimento (Roadmap)

### Fase 1 — MVP Local (2-3 semanas)
- [ ] Qt App com telas de Login, Config LinkedIn e Painel
- [ ] API simples (FastAPI) rodando local
- [ ] Qt → API → injeta credenciais → inicia processo C# local
- [ ] PostgreSQL local compartilhado

### Fase 2 — Cloud Single-User (1-2 semanas)
- [ ] Deploy API + Worker C# no Render
- [ ] PostgreSQL no Render
- [ ] Qt App conecta na cloud
- [ ] Credenciais cifradas no banco

### Fase 3 — Multi-tenant / Comercial (3-4 semanas)
- [ ] Sistema de contas (registro, login, planos)
- [ ] Isolamento de dados por usuário
- [ ] Painel de billing / limites por plano
- [ ] Dashboard web (opcional, para usuários sem o Qt App)

---

## ⚡ Próximos Passos Imediatos

1. Criar o Backend API (Python FastAPI)
2. Criar o Qt App com as 5 telas
3. Protocolo REST (comandos) + WebSocket (logs ao vivo)
4. Adaptar o C# para aceitar user_id e multi-tenant

---

Arquivo gerado em: 2026-05-06
Projeto: C:\Users\CLOVIS\Documents\Projeto_Linkdim\WebCrawler
