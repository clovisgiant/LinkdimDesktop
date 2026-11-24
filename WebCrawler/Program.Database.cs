using System;
using System.Collections.Generic;
using MySqlConnector;
using Npgsql;

partial class Program
{
    private static bool ValidateDatabaseConnection()
    {
        if (!DatabaseEnabled)
        {
            return true;
        }

        var provider = DatabaseProvider;
        var mySqlOk = false;
        var postgresOk = false;

        // Valida MySQL se provider for mysql ou dual
        if (provider == "mysql" || provider == "dual")
        {
            try
            {
                using var conn = new MySqlConnection(MySqlConnectionString);
                conn.Open();
                using var cmd = new MySqlCommand("SELECT 1;", conn);
                _ = cmd.ExecuteScalar();
                Console.WriteLine("[BANCO] Conexão com MySQL validada com sucesso!");
                EnsureMySqlJobsSchema(conn);
                EnsureMySqlApplicationTrackingSchema(conn);
                EnsureMySqlRuntimeStatusSchema(conn);
                mySqlOk = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BANCO] Falha ao validar conexão com MySQL: {ex.Message}");
            }
        }

        // Valida PostgreSQL se provider for postgres ou dual
        if (provider == "postgres" || provider == "dual")
        {
            try
            {
                using var conn = new NpgsqlConnection(ConnectionString);
                conn.Open();
                using var cmd = new NpgsqlCommand("SELECT 1;", conn);
                _ = cmd.ExecuteScalar();
                Console.WriteLine("[BANCO] Conexão com PostgreSQL validada com sucesso.");
                EnsureJobsSchema(conn);
                EnsureApplicationTrackingSchema(conn);
                postgresOk = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BANCO] Falha ao validar conexão com PostgreSQL: {ex.Message}");
                PrintDatabaseAuthenticationHint(ex);
            }
        }

        if (provider == "dual")
        {
            // No modo dual, se o MySQL estiver ok, prossegue normalmente
            return mySqlOk || postgresOk;
        }

        return provider == "mysql" ? mySqlOk : postgresOk;
    }

    private static void PrintDatabaseAuthenticationHint(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        if (!message.Contains("No password has been provided but the backend requires one", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Console.WriteLine("[BANCO] O PostgreSQL exigiu senha (SCRAM), mas a conexao foi enviada sem senha.");
        Console.WriteLine("[BANCO] Se quiser usar autenticacao sem senha, ajuste o pg_hba.conf para trust/sspi no host local e reinicie o servico.");
        Console.WriteLine("[BANCO] Opcao recomendada: definir senha no usuario do banco e informar em WEBCRAWLER_DB_CONNECTION (Password=sua_senha).");
    }

    private static string ExtractLinkedInJobId(string? link)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            normalizedLink = link ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            return string.Empty;
        }

        try
        {
            if (!Uri.TryCreate(normalizedLink, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < segments.Length - 2; i++)
            {
                if (!segments[i].Equals("jobs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!segments[i + 1].Equals("view", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var jobId = (segments[i + 2] ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(jobId) ? string.Empty : jobId;
            }
        }
        catch
        {
            // Ignora e retorna vazio.
        }

        return string.Empty;
    }

    #region MySQL Schemas and Methods

    private static void EnsureMySqlJobsSchema(MySqlConnection conn)
    {
        using var cmd = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS vagas (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                titulo VARCHAR(500) NOT NULL,
                empresa VARCHAR(500) NOT NULL,
                localizacao VARCHAR(500) NOT NULL,
                link VARCHAR(768) NOT NULL,
                data_insercao DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                candidatura_simplificada BOOLEAN DEFAULT FALSE,
                candidatura_enviada BOOLEAN DEFAULT FALSE,
                data_candidatura DATETIME NULL,
                candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE,
                data_envio_sucesso DATETIME NULL,
                candidatura_indisponivel BOOLEAN DEFAULT FALSE,
                motivo_indisponibilidade TEXT NULL,
                data_indisponibilidade DATETIME NULL,
                UNIQUE KEY uq_vagas_link (link),
                INDEX idx_vagas_simplificada_status (candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso, candidatura_indisponivel),
                INDEX idx_vagas_data_insercao (data_insercao DESC)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMySqlApplicationTrackingSchema(MySqlConnection conn)
    {
        using var cmd = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS candidatura_etapas (
                id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                link VARCHAR(768) NOT NULL,
                etapa VARCHAR(255) NOT NULL,
                sucesso BOOLEAN NOT NULL,
                detalhe TEXT NULL,
                html_path VARCHAR(500) NULL,
                screenshot_path VARCHAR(500) NULL,
                criado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_candidatura_etapas_link_criado_em (link(255), criado_em DESC)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMySqlRuntimeStatusSchema(MySqlConnection conn)
    {
        using var cmd = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS crawler_runtime_status (
                instance_name VARCHAR(100) NOT NULL PRIMARY KEY,
                state VARCHAR(100) NOT NULL,
                detail TEXT NULL,
                is_running BOOLEAN NOT NULL DEFAULT TRUE,
                process_id INT NULL,
                host_name VARCHAR(255) NULL,
                started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                last_heartbeat DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void LogApplicationStepInMySql(
        string link,
        string etapa,
        bool sucesso,
        string? detalhe,
        string? htmlPath,
        string? screenshotPath)
    {
        try
        {
            using var conn = new MySqlConnection(MySqlConnectionString);
            conn.Open();
            EnsureMySqlApplicationTrackingSchema(conn);

            using var cmd = new MySqlCommand(
                @"INSERT INTO candidatura_etapas (link, etapa, sucesso, detalhe, html_path, screenshot_path)
                  VALUES (@link, @etapa, @sucesso, @detalhe, @html_path, @screenshot_path)",
                conn);

            cmd.Parameters.AddWithValue("@link", link);
            cmd.Parameters.AddWithValue("@etapa", etapa);
            cmd.Parameters.AddWithValue("@sucesso", sucesso);
            cmd.Parameters.AddWithValue("@detalhe", (object?)detalhe ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@html_path", (object?)htmlPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@screenshot_path", (object?)screenshotPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO MySQL] Falha ao registrar etapa '{etapa}': {ex.Message}");
        }
    }

    private static void SaveCollectedJobsToMySql(List<(string Titulo, string Empresa, string Localizacao, string Link)> jobs)
    {
        try
        {
            Console.WriteLine("[BANCO] Salvando no MySQL...");
            using var conn = new MySqlConnection(MySqlConnectionString);
            conn.Open();
            EnsureMySqlJobsSchema(conn);

            foreach (var job in jobs)
            {
                var normalizedLink = NormalizeLinkedInJobLink(job.Link);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                using var cmd = new MySqlCommand(@"
                    INSERT INTO vagas (
                        titulo, empresa, localizacao, link, data_insercao,
                        candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso
                    ) VALUES (
                        @titulo, @empresa, @localizacao, @link, @data_insercao,
                        TRUE, FALSE, FALSE
                    )
                    ON DUPLICATE KEY UPDATE id=id;
                ", conn);
                cmd.Parameters.AddWithValue("@titulo", job.Titulo);
                cmd.Parameters.AddWithValue("@empresa", job.Empresa);
                cmd.Parameters.AddWithValue("@localizacao", job.Localizacao);
                cmd.Parameters.AddWithValue("@link", normalizedLink);
                cmd.Parameters.AddWithValue("@data_insercao", DateTime.Now);
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine("[BANCO] Vagas salvas no banco MySQL (sem duplicidade)!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO] Erro ao salvar no MySQL: {ex.Message}");
        }
    }

    private static List<string> GetSimplifiedJobLinksFromMySql()
    {
        var links = new List<string>();
        var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var conn = new MySqlConnection(MySqlConnectionString);
            conn.Open();
            EnsureMySqlJobsSchema(conn);

            using (var appliedCmd = new MySqlCommand(
                @"SELECT DISTINCT link
                  FROM vagas
                  WHERE (COALESCE(candidatura_enviada, FALSE) = TRUE OR COALESCE(candidatura_enviada_sucesso, FALSE) = TRUE)
                    AND link IS NOT NULL
                    AND link <> ''",
                conn))
            using (var appliedReader = appliedCmd.ExecuteReader())
            {
                while (appliedReader.Read())
                {
                    var rawAppliedLink = appliedReader.GetString(0);
                    var normalizedAppliedLink = NormalizeLinkedInJobLink(rawAppliedLink);
                    if (!string.IsNullOrWhiteSpace(normalizedAppliedLink))
                    {
                        appliedLinks.Add(normalizedAppliedLink);
                    }

                    var appliedJobId = ExtractLinkedInJobId(rawAppliedLink);
                    if (!string.IsNullOrWhiteSpace(appliedJobId))
                    {
                        appliedJobIds.Add(appliedJobId);
                    }
                }
            }

            MarkExhaustedJobsAsUnavailableInMySql(conn);

            using var cmd = new MySqlCommand(
                @"SELECT link
                  FROM vagas
                  WHERE candidatura_simplificada = TRUE
                    AND COALESCE(candidatura_enviada, FALSE) = FALSE
                    AND COALESCE(candidatura_enviada_sucesso, FALSE) = FALSE
                    AND COALESCE(candidatura_indisponivel, FALSE) = FALSE
                    AND link IS NOT NULL
                    AND link <> ''
                  ORDER BY data_insercao DESC",
                conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var rawLink = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(rawLink))
                {
                    continue;
                }

                if (IsBudgetExhaustedJobLink(rawLink))
                {
                    continue;
                }

                var normalizedLink = NormalizeLinkedInJobLink(rawLink);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                if (HasSuccessfulApplicationRecorded(normalizedLink))
                {
                    continue;
                }

                var jobId = ExtractLinkedInJobId(normalizedLink);
                if (appliedLinks.Contains(normalizedLink))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(jobId) && appliedJobIds.Contains(jobId))
                {
                    continue;
                }

                if (seenLinks.Add(normalizedLink))
                {
                    links.Add(normalizedLink);
                }
            }

            return links;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO] Erro ao buscar vagas pendentes no MySQL: {ex.Message}");
            return links;
        }
    }

    private static bool MarkJobAsAppliedInMySql(string link)
    {
        if (string.IsNullOrWhiteSpace(link)) return false;
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink)) normalizedLink = link;
        var jobId = ExtractLinkedInJobId(normalizedLink);

        try
        {
            using var conn = new MySqlConnection(MySqlConnectionString);
            conn.Open();
            EnsureMySqlJobsSchema(conn);

            var appliedAt = DateTime.Now;

            using var cmd = new MySqlCommand(@"
                UPDATE vagas
                SET candidatura_enviada = TRUE,
                    data_candidatura = @data_candidatura,
                    candidatura_enviada_sucesso = TRUE,
                    data_envio_sucesso = @data_candidatura,
                    candidatura_indisponivel = FALSE,
                    motivo_indisponibilidade = NULL,
                    data_indisponibilidade = NULL
                WHERE link = @link
                   OR link = @normalized_link
                   OR SUBSTRING_INDEX(link, '?', 1) = SUBSTRING_INDEX(@link, '?', 1)
                   OR SUBSTRING_INDEX(link, '?', 1) = SUBSTRING_INDEX(@normalized_link, '?', 1)
                   OR TRIM(TRAILING '/' FROM SUBSTRING_INDEX(link, '?', 1)) = TRIM(TRAILING '/' FROM SUBSTRING_INDEX(@link, '?', 1))
                   OR TRIM(TRAILING '/' FROM SUBSTRING_INDEX(link, '?', 1)) = TRIM(TRAILING '/' FROM SUBSTRING_INDEX(@normalized_link, '?', 1))
                   OR (@job_id <> '' AND SUBSTRING_INDEX(link, '?', 1) LIKE CONCAT('%/jobs/view/', @job_id, '%'))
            ", conn);

            cmd.Parameters.AddWithValue("@data_candidatura", appliedAt);
            cmd.Parameters.AddWithValue("@link", link);
            cmd.Parameters.AddWithValue("@normalized_link", normalizedLink);
            cmd.Parameters.AddWithValue("@job_id", jobId);

            var affectedRows = cmd.ExecuteNonQuery();
            if (affectedRows <= 0)
            {
                using var upsertCmd = new MySqlCommand(@"
                    INSERT INTO vagas (
                        titulo, empresa, localizacao, link, data_insercao,
                        candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso,
                        data_candidatura, data_envio_sucesso, candidatura_indisponivel,
                        motivo_indisponibilidade, data_indisponibilidade
                    ) VALUES (
                        @titulo, @empresa, @localizacao, @normalized_link, @data_insercao,
                        TRUE, TRUE, TRUE, @data_candidatura, @data_candidatura, FALSE, NULL, NULL
                    )
                    ON DUPLICATE KEY UPDATE
                        candidatura_enviada = TRUE,
                        candidatura_enviada_sucesso = TRUE,
                        data_candidatura = VALUES(data_candidatura),
                        data_envio_sucesso = VALUES(data_envio_sucesso),
                        candidatura_indisponivel = FALSE,
                        motivo_indisponibilidade = NULL,
                        data_indisponibilidade = NULL;
                ", conn);
                upsertCmd.Parameters.AddWithValue("@titulo", "(titulo indisponivel)");
                upsertCmd.Parameters.AddWithValue("@empresa", "(empresa indisponivel)");
                upsertCmd.Parameters.AddWithValue("@localizacao", "(localizacao indisponivel)");
                upsertCmd.Parameters.AddWithValue("@normalized_link", normalizedLink);
                upsertCmd.Parameters.AddWithValue("@data_insercao", appliedAt);
                upsertCmd.Parameters.AddWithValue("@data_candidatura", appliedAt);

                affectedRows = upsertCmd.ExecuteNonQuery();
            }

            Console.WriteLine($"[BANCO MySQL] candidatura_enviada atualizada para {affectedRows} registro(s). link='{normalizedLink}'");
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO MySQL] Falha ao atualizar candidatura_enviada: {ex.Message}");
            return false;
        }
    }

    private static void MarkJobAsUnavailableInMySql(string link, string reason)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink)) normalizedLink = link;

        try
        {
            using var conn = new MySqlConnection(MySqlConnectionString);
            conn.Open();
            EnsureMySqlJobsSchema(conn);

            using var cmd = new MySqlCommand(@"
                UPDATE vagas
                SET candidatura_indisponivel = TRUE,
                    motivo_indisponibilidade = @motivo,
                    data_indisponibilidade = @data_indisponibilidade
                WHERE link = @link
                   OR link = @normalized_link
                   OR SUBSTRING_INDEX(link, '?', 1) = SUBSTRING_INDEX(@link, '?', 1)
                   OR SUBSTRING_INDEX(link, '?', 1) = SUBSTRING_INDEX(@normalized_link, '?', 1)
            ", conn);

            cmd.Parameters.AddWithValue("@motivo", string.IsNullOrWhiteSpace(reason) ? "indisponivel" : reason);
            cmd.Parameters.AddWithValue("@data_indisponibilidade", DateTime.Now);
            cmd.Parameters.AddWithValue("@link", link);
            cmd.Parameters.AddWithValue("@normalized_link", normalizedLink);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO MySQL] Falha ao marcar vaga como indisponível: {ex.Message}");
        }
    }

    private static void MarkExhaustedJobsAsUnavailableInMySql(MySqlConnection conn)
    {
        try
        {
            using var cmd = new MySqlCommand(@"
                UPDATE vagas v
                JOIN (
                    SELECT link, COUNT(*) as cnt
                    FROM candidatura_etapas
                    WHERE sucesso = FALSE
                    GROUP BY link
                    HAVING cnt >= 12
                ) bad ON bad.link = v.link
                SET v.candidatura_indisponivel = TRUE,
                    v.motivo_indisponibilidade = 'MAX_RETRY_EXCEEDED',
                    v.data_indisponibilidade = NOW()
                WHERE v.candidatura_simplificada = TRUE
                  AND COALESCE(v.candidatura_enviada, FALSE) = FALSE
                  AND COALESCE(v.candidatura_enviada_sucesso, FALSE) = FALSE
                  AND COALESCE(v.candidatura_indisponivel, FALSE) = FALSE;
            ", conn);

            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
            {
                Console.WriteLine($"[BANCO MySQL] {affected} vaga(s) marcada(s) como MAX_RETRY_EXCEEDED (>=12 falhas em candidatura_etapas).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO MySQL] Falha ao limpar vagas exauridas: {ex.Message}");
        }
    }

    #endregion

    #region PostgreSQL Schemas and Methods

    private static void EnsureSuccessTrackingColumns(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE;
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_envio_sucesso TIMESTAMP NULL;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureJobsSchema(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS vagas (
                id BIGSERIAL PRIMARY KEY,
                titulo TEXT NOT NULL,
                empresa TEXT NOT NULL,
                localizacao TEXT NOT NULL,
                link TEXT NOT NULL UNIQUE,
                data_insercao TIMESTAMP NOT NULL DEFAULT NOW(),
                candidatura_simplificada BOOLEAN DEFAULT FALSE,
                candidatura_enviada BOOLEAN DEFAULT FALSE,
                data_candidatura TIMESTAMP NULL,
                candidatura_enviada_sucesso BOOLEAN DEFAULT FALSE,
                data_envio_sucesso TIMESTAMP NULL,
                candidatura_indisponivel BOOLEAN DEFAULT FALSE,
                motivo_indisponibilidade TEXT NULL,
                data_indisponibilidade TIMESTAMP NULL
            );
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureUnavailableTrackingColumns(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_indisponivel BOOLEAN DEFAULT FALSE;
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS motivo_indisponibilidade TEXT NULL;
            ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_indisponibilidade TIMESTAMP NULL;
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureApplicationTrackingSchema(NpgsqlConnection conn)
    {
        using var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS candidatura_etapas (
                id BIGSERIAL PRIMARY KEY,
                link TEXT NOT NULL,
                etapa TEXT NOT NULL,
                sucesso BOOLEAN NOT NULL,
                detalhe TEXT NULL,
                html_path TEXT NULL,
                screenshot_path TEXT NULL,
                criado_em TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_candidatura_etapas_link_criado_em
            ON candidatura_etapas (link, criado_em DESC);
        ", conn);
        cmd.ExecuteNonQuery();
    }

    private static void LogApplicationStepInPostgres(
        string link,
        string etapa,
        bool sucesso,
        string? detalhe = null,
        string? htmlPath = null,
        string? screenshotPath = null)
    {
        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();
            EnsureApplicationTrackingSchema(conn);

            using var cmd = new NpgsqlCommand(
                @"INSERT INTO candidatura_etapas (link, etapa, sucesso, detalhe, html_path, screenshot_path)
                  VALUES (@link, @etapa, @sucesso, @detalhe, @html_path, @screenshot_path)",
                conn);

            cmd.Parameters.AddWithValue("link", link);
            cmd.Parameters.AddWithValue("etapa", etapa);
            cmd.Parameters.AddWithValue("sucesso", sucesso);
            cmd.Parameters.AddWithValue("detalhe", (object?)detalhe ?? DBNull.Value);
            cmd.Parameters.AddWithValue("html_path", (object?)htmlPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("screenshot_path", (object?)screenshotPath ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO PostgreSQL] Falha ao registrar etapa '{etapa}': {ex.Message}");
            PrintDatabaseAuthenticationHint(ex);
        }
    }

    private static void SaveCollectedJobsToPostgres(List<(string Titulo, string Empresa, string Localizacao, string Link)> jobs)
    {
        try
        {
            Console.WriteLine("[BANCO] Salvando no PostgreSQL...");

            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();

            EnsureJobsSchema(conn);

            using (var ensureColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_simplificada BOOLEAN;", conn))
            {
                ensureColumn.ExecuteNonQuery();
            }

            using (var ensureAppliedColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;", conn))
            {
                ensureAppliedColumn.ExecuteNonQuery();
            }

            using (var ensureAppliedAtColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;", conn))
            {
                ensureAppliedAtColumn.ExecuteNonQuery();
            }

            EnsureSuccessTrackingColumns(conn);
            EnsureUnavailableTrackingColumns(conn);
            EnsureApplicationTrackingSchema(conn);

            foreach (var job in jobs)
            {
                var normalizedLink = NormalizeLinkedInJobLink(job.Link);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                using var cmd = new NpgsqlCommand(
                    "INSERT INTO vagas (titulo, empresa, localizacao, link, data_insercao, candidatura_simplificada, candidatura_enviada, candidatura_enviada_sucesso) VALUES (@titulo, @empresa, @localizacao, @link, @data_insercao, @candidatura_simplificada, @candidatura_enviada, @candidatura_enviada_sucesso) ON CONFLICT (link) DO NOTHING", conn);
                cmd.Parameters.AddWithValue("titulo", job.Titulo);
                cmd.Parameters.AddWithValue("empresa", job.Empresa);
                cmd.Parameters.AddWithValue("localizacao", job.Localizacao);
                cmd.Parameters.AddWithValue("link", normalizedLink);
                cmd.Parameters.AddWithValue("data_insercao", DateTime.Now);
                cmd.Parameters.AddWithValue("candidatura_simplificada", true);
                cmd.Parameters.AddWithValue("candidatura_enviada", false);
                cmd.Parameters.AddWithValue("candidatura_enviada_sucesso", false);
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine("[BANCO] Vagas salvas no PostgreSQL (sem duplicidade)!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO] Erro ao salvar no PostgreSQL: {ex.Message}");
            PrintDatabaseAuthenticationHint(ex);
        }
    }

    private static List<string> GetSimplifiedJobLinksFromPostgres()
    {
        var links = new List<string>();
        var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedJobIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();

            EnsureJobsSchema(conn);

            using (var ensureAppliedColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;",
                conn))
            {
                ensureAppliedColumn.ExecuteNonQuery();
            }

            using (var ensureAppliedAtColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;",
                conn))
            {
                ensureAppliedAtColumn.ExecuteNonQuery();
            }

            EnsureUnavailableTrackingColumns(conn);
            EnsureSuccessTrackingColumns(conn);
            EnsureApplicationTrackingSchema(conn);

            using (var appliedCmd = new NpgsqlCommand(
                @"SELECT DISTINCT link
                  FROM vagas
                  WHERE (COALESCE(candidatura_enviada, FALSE) = TRUE OR COALESCE(candidatura_enviada_sucesso, FALSE) = TRUE)
                    AND link IS NOT NULL
                    AND link <> ''",
                conn))
            using (var appliedReader = appliedCmd.ExecuteReader())
            {
                while (appliedReader.Read())
                {
                    var rawAppliedLink = appliedReader.GetString(0);
                    var normalizedAppliedLink = NormalizeLinkedInJobLink(rawAppliedLink);
                    if (!string.IsNullOrWhiteSpace(normalizedAppliedLink))
                    {
                        appliedLinks.Add(normalizedAppliedLink);
                    }

                    var appliedJobId = ExtractLinkedInJobId(rawAppliedLink);
                    if (!string.IsNullOrWhiteSpace(appliedJobId))
                    {
                        appliedJobIds.Add(appliedJobId);
                    }
                }
            }

            MarkExhaustedJobsAsUnavailableInPostgres(conn);

            using var cmd = new NpgsqlCommand(
                @"SELECT link
                  FROM vagas
                  WHERE candidatura_simplificada = TRUE
                    AND COALESCE(candidatura_enviada, FALSE) = FALSE
                    AND COALESCE(candidatura_enviada_sucesso, FALSE) = FALSE
                    AND COALESCE(candidatura_indisponivel, FALSE) = FALSE
                    AND link IS NOT NULL
                    AND link <> ''
                  ORDER BY data_insercao DESC",
                conn);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var rawLink = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(rawLink))
                {
                    continue;
                }

                if (IsBudgetExhaustedJobLink(rawLink))
                {
                    continue;
                }

                var normalizedLink = NormalizeLinkedInJobLink(rawLink);
                if (string.IsNullOrWhiteSpace(normalizedLink))
                {
                    continue;
                }

                if (HasSuccessfulApplicationRecorded(normalizedLink))
                {
                    continue;
                }

                var jobId = ExtractLinkedInJobId(normalizedLink);
                if (appliedLinks.Contains(normalizedLink))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(jobId) && appliedJobIds.Contains(jobId))
                {
                    continue;
                }

                if (seenLinks.Add(normalizedLink))
                {
                    links.Add(normalizedLink);
                }
            }

            return links;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO] Erro ao buscar vagas pendentes no PostgreSQL: {ex.Message}");
            PrintDatabaseAuthenticationHint(ex);
            return links;
        }
    }

    private static bool MarkJobAsAppliedInPostgres(string link)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            normalizedLink = link;
        }

        var jobId = ExtractLinkedInJobId(normalizedLink);

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();

            EnsureJobsSchema(conn);

            using (var ensureAppliedColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS candidatura_enviada BOOLEAN DEFAULT FALSE;",
                conn))
            {
                ensureAppliedColumn.ExecuteNonQuery();
            }

            using (var ensureAppliedAtColumn = new NpgsqlCommand(
                "ALTER TABLE vagas ADD COLUMN IF NOT EXISTS data_candidatura TIMESTAMP NULL;",
                conn))
            {
                ensureAppliedAtColumn.ExecuteNonQuery();
            }

            EnsureUnavailableTrackingColumns(conn);
            EnsureSuccessTrackingColumns(conn);

            var appliedAt = DateTime.Now;

            using var cmd = new NpgsqlCommand(
                @"UPDATE vagas
                  SET candidatura_enviada = TRUE,
                      data_candidatura = @data_candidatura,
                      candidatura_enviada_sucesso = TRUE,
                      data_envio_sucesso = @data_candidatura,
                      candidatura_indisponivel = FALSE,
                      motivo_indisponibilidade = NULL,
                      data_indisponibilidade = NULL
                  WHERE link = @link
                     OR link = @normalized_link
                     OR split_part(link, '?', 1) = split_part(@link, '?', 1)
                     OR split_part(link, '?', 1) = split_part(@normalized_link, '?', 1)
                     OR regexp_replace(split_part(link, '?', 1), '/+$', '') = regexp_replace(split_part(@link, '?', 1), '/+$', '')
                     OR regexp_replace(split_part(link, '?', 1), '/+$', '') = regexp_replace(split_part(@normalized_link, '?', 1), '/+$', '')
                     OR (@job_id <> '' AND split_part(link, '?', 1) LIKE ('%/jobs/view/' || @job_id || '%'))",
                conn);
            cmd.Parameters.AddWithValue("data_candidatura", appliedAt);
            cmd.Parameters.AddWithValue("link", link);
            cmd.Parameters.AddWithValue("normalized_link", normalizedLink);
            cmd.Parameters.AddWithValue("job_id", jobId);

            var affectedRows = cmd.ExecuteNonQuery();
            if (affectedRows <= 0)
            {
                using var upsertCmd = new NpgsqlCommand(
                    @"INSERT INTO vagas (
                        titulo,
                        empresa,
                        localizacao,
                        link,
                        data_insercao,
                        candidatura_simplificada,
                        candidatura_enviada,
                        candidatura_enviada_sucesso,
                        data_candidatura,
                        data_envio_sucesso,
                        candidatura_indisponivel,
                        motivo_indisponibilidade,
                        data_indisponibilidade)
                      VALUES (
                        @titulo,
                        @empresa,
                        @localizacao,
                        @normalized_link,
                        @data_insercao,
                        TRUE,
                        TRUE,
                        TRUE,
                        @data_candidatura,
                        @data_candidatura,
                        FALSE,
                        NULL,
                        NULL)
                      ON CONFLICT (link) DO UPDATE
                      SET candidatura_enviada = TRUE,
                          candidatura_enviada_sucesso = TRUE,
                          data_candidatura = @data_candidatura,
                          data_envio_sucesso = @data_candidatura,
                          candidatura_indisponivel = FALSE,
                          motivo_indisponibilidade = NULL,
                          data_indisponibilidade = NULL",
                    conn);
                upsertCmd.Parameters.AddWithValue("titulo", "(titulo indisponivel)");
                upsertCmd.Parameters.AddWithValue("empresa", "(empresa indisponivel)");
                upsertCmd.Parameters.AddWithValue("localizacao", "(localizacao indisponivel)");
                upsertCmd.Parameters.AddWithValue("normalized_link", normalizedLink);
                upsertCmd.Parameters.AddWithValue("data_insercao", appliedAt);
                upsertCmd.Parameters.AddWithValue("data_candidatura", appliedAt);

                affectedRows = upsertCmd.ExecuteNonQuery();
            }

            Console.WriteLine($"[BANCO PostgreSQL] candidatura_enviada atualizada para {affectedRows} registro(s). link='{normalizedLink}'");
            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO PostgreSQL] Falha ao atualizar candidatura_enviada: {ex.Message}");
            PrintDatabaseAuthenticationHint(ex);
            return false;
        }
    }

    private static void MarkJobAsUnavailableInPostgres(string link, string reason)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link);
        if (string.IsNullOrWhiteSpace(normalizedLink))
        {
            normalizedLink = link;
        }

        try
        {
            using var conn = new NpgsqlConnection(ConnectionString);
            conn.Open();

            EnsureJobsSchema(conn);
            EnsureUnavailableTrackingColumns(conn);

            using var cmd = new NpgsqlCommand(
                @"UPDATE vagas
                  SET candidatura_indisponivel = TRUE,
                      motivo_indisponibilidade = @motivo,
                      data_indisponibilidade = @data_indisponibilidade
                  WHERE link = @link
                     OR link = @normalized_link
                     OR split_part(link, '?', 1) = split_part(@link, '?', 1)
                     OR split_part(link, '?', 1) = split_part(@normalized_link, '?', 1)",
                conn);
            cmd.Parameters.AddWithValue("motivo", string.IsNullOrWhiteSpace(reason) ? "indisponivel" : reason);
            cmd.Parameters.AddWithValue("data_indisponibilidade", DateTime.Now);
            cmd.Parameters.AddWithValue("link", link);
            cmd.Parameters.AddWithValue("normalized_link", normalizedLink);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO PostgreSQL] Falha ao marcar vaga como indisponível: {ex.Message}");
            PrintDatabaseAuthenticationHint(ex);
        }
    }

    private static void MarkExhaustedJobsAsUnavailableInPostgres(NpgsqlConnection conn)
    {
        try
        {
            using var cmd = new NpgsqlCommand(
                @"UPDATE vagas
                  SET candidatura_indisponivel = TRUE,
                      motivo_indisponibilidade = 'MAX_RETRY_EXCEEDED',
                      data_indisponibilidade = NOW()
                  WHERE candidatura_simplificada = TRUE
                    AND COALESCE(candidatura_enviada, FALSE) = FALSE
                    AND COALESCE(candidatura_enviada_sucesso, FALSE) = FALSE
                    AND COALESCE(candidatura_indisponivel, FALSE) = FALSE
                    AND link IS NOT NULL
                    AND link <> ''
                    AND (
                        SELECT COUNT(*)
                        FROM candidatura_etapas ce
                        WHERE ce.link = vagas.link
                          AND ce.sucesso = FALSE
                    ) >= 12",
                conn);

            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
            {
                Console.WriteLine($"[BANCO PostgreSQL] {affected} vaga(s) marcada(s) como MAX_RETRY_EXCEEDED (>=12 falhas em candidatura_etapas).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BANCO PostgreSQL] Falha ao limpar vagas exauridas: {ex.Message}");
        }
    }

    #endregion

    #region Public Facade Methods (Dual / MySQL / PostgreSQL)

    private static void LogApplicationStep(
        string link,
        string etapa,
        bool sucesso,
        string? detalhe = null,
        string? htmlPath = null,
        string? screenshotPath = null)
    {
        if (!DatabaseEnabled || string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(etapa))
        {
            return;
        }

        var provider = DatabaseProvider;
        if (provider == "mysql" || provider == "dual")
        {
            LogApplicationStepInMySql(link, etapa, sucesso, detalhe, htmlPath, screenshotPath);
        }

        if (provider == "postgres" || provider == "dual")
        {
            LogApplicationStepInPostgres(link, etapa, sucesso, detalhe, htmlPath, screenshotPath);
        }
    }

    private static void SaveCollectedJobsToDatabase(List<(string Titulo, string Empresa, string Localizacao, string Link)> jobs)
    {
        if (jobs == null || jobs.Count == 0) return;
        var provider = DatabaseProvider;

        if (provider == "mysql" || provider == "dual")
        {
            SaveCollectedJobsToMySql(jobs);
        }

        if (provider == "postgres" || provider == "dual")
        {
            SaveCollectedJobsToPostgres(jobs);
        }
    }

    private static List<string> GetSimplifiedJobLinksFromDatabase()
    {
        var provider = DatabaseProvider;

        if (provider == "mysql" || provider == "dual")
        {
            var mySqlLinks = GetSimplifiedJobLinksFromMySql();
            if (mySqlLinks.Count > 0 || provider == "mysql")
            {
                return mySqlLinks;
            }
        }

        return GetSimplifiedJobLinksFromPostgres();
    }

    private static bool MarkJobAsApplied(string link)
    {
        if (!DatabaseEnabled || string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        var provider = DatabaseProvider;
        bool result = false;

        if (provider == "mysql" || provider == "dual")
        {
            result = MarkJobAsAppliedInMySql(link) || result;
        }

        if (provider == "postgres" || provider == "dual")
        {
            result = MarkJobAsAppliedInPostgres(link) || result;
        }

        return result;
    }

    private static void MarkJobAsUnavailable(string link, string reason)
    {
        if (!DatabaseEnabled || string.IsNullOrWhiteSpace(link))
        {
            return;
        }

        var provider = DatabaseProvider;

        if (provider == "mysql" || provider == "dual")
        {
            MarkJobAsUnavailableInMySql(link, reason);
        }

        if (provider == "postgres" || provider == "dual")
        {
            MarkJobAsUnavailableInPostgres(link, reason);
        }
    }

    #endregion
}
