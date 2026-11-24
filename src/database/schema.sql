-- ── Esquema de Banco de Dados para o Linkdim WebCrawler ────
-- Rodar este script no banco de dados do Render (dbusers_e04k)

-- Tabela principal de vagas
CREATE TABLE IF NOT EXISTS vagas_linkedin (
    id SERIAL PRIMARY KEY,
    titulo VARCHAR(255),
    empresa VARCHAR(255),
    localizacao VARCHAR(255),
    link_vaga TEXT UNIQUE,
    data_publicacao VARCHAR(100),
    descricao TEXT,
    status VARCHAR(50) DEFAULT 'Pendente', -- Pendente, Enviado, Indisponivel, Bloqueado
    data_cadastro TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    data_envio TIMESTAMP
);

-- Tabela de logs de execução e heartbeat do robô
CREATE TABLE IF NOT EXISTS crawler_jobs (
    id SERIAL PRIMARY KEY,
    start_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_heartbeat TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status VARCHAR(50), -- Running, Stopped, Error
    vagas_processadas INT DEFAULT 0,
    vagas_sucesso INT DEFAULT 0,
    logs TEXT
);

-- Índices para busca rápida no dashboard
CREATE INDEX IF NOT EXISTS idx_status ON vagas_linkedin(status);
CREATE INDEX IF NOT EXISTS idx_data_cadastro ON vagas_linkedin(data_cadastro);
