<?php
/**
 * LinkDim Web API - Backend em PHP para Hospedagem Compartilhada / Apache
 * Conecta diretamente ao MySQL via config.local.php ou variáveis de ambiente
 */

// Desativa erros HTML diretos para nunca corromper a resposta JSON
error_reporting(E_ALL);
ini_set('display_errors', '0');

header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization, Pragma, Cache-Control');
header('Cache-Control: no-cache, no-store, must-revalidate');

if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

// Configurações do Banco MySQL (carregadas de config.local.php ou variáveis de ambiente)
$dbHost = getenv('DB_HOST') ?: 'localhost';
$dbName = getenv('DB_NAME') ?: '';
$dbUser = getenv('DB_USER') ?: '';
$dbPass = getenv('DB_PASS') ?: '';
$dbPort = (int)(getenv('DB_PORT') ?: 3306);

$localConfigFile = __DIR__ . '/config.local.php';
if (file_exists($localConfigFile)) {
    require $localConfigFile;
}

function getDbConnection($host, $name, $user, $pass, $port) {
    static $pdo = null;
    if ($pdo === null) {
        $dsn = "mysql:host={$host};port={$port};dbname={$name};charset=utf8mb4";
        $options = [
            PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES   => false,
            PDO::MYSQL_ATTR_INIT_COMMAND => "SET NAMES utf8mb4"
        ];
        $pdo = new PDO($dsn, $user, $pass, $options);
    }
    return $pdo;
}

// Determina o endpoint solicitado (suporta ?endpoint=..., /linkdin/api/... ou direto)
$endpoint = isset($_GET['endpoint']) ? trim($_GET['endpoint'], '/') : '';
if (empty($endpoint)) {
    $requestUri = parse_url($_SERVER['REQUEST_URI'] ?? '', PHP_URL_PATH);
    if (preg_match('#/api(?:/index\.php)?/(.+)#i', $requestUri, $matches)) {
        $endpoint = trim($matches[1], '/');
    }
}

if (empty($endpoint) || $endpoint === 'index.php') {
    $endpoint = 'dashboard';
}

try {
    $pdo = getDbConnection($dbHost, $dbName, $dbUser, $dbPass, $dbPort);

    if ($endpoint === 'health') {
        $stmt = $pdo->query("SELECT 1;");
        echo json_encode([
            'ok' => true,
            'database' => 'connected',
            'provider' => 'mysql'
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    }

    if ($endpoint === 'crawler-status') {
        // 1. Heartbeat registrado pelo WebCrawler
        $heartbeat = null;
        try {
            $stmt = $pdo->query("
                SELECT state, detail, is_running, process_id, host_name, started_at, last_heartbeat, updated_at
                FROM crawler_runtime_status
                WHERE instance_name = 'default'
                LIMIT 1
            ");
            $heartbeat = $stmt->fetch();
        } catch (Exception $e) {}

        // 2. Última atividade detectada em qualquer tabela
        $lastActivity = null;
        try {
            $stmt = $pdo->query("
                SELECT MAX(happened_at) AS last_activity FROM (
                    SELECT MAX(criado_em) AS happened_at FROM candidatura_etapas
                    UNION ALL
                    SELECT MAX(data_insercao) AS happened_at FROM vagas
                    UNION ALL
                    SELECT MAX(last_heartbeat) AS happened_at FROM crawler_runtime_status WHERE instance_name = 'default'
                ) AS activity_events
            ");
            $actRow = $stmt->fetch();
            if ($actRow && !empty($actRow['last_activity'])) {
                $lastActivity = $actRow['last_activity'];
            }
        } catch (Exception $e) {}

        $nowTs = time();
        $minutesSince = null;
        if ($lastActivity) {
            $actTs = strtotime($lastActivity);
            if ($actTs) {
                $minutesSince = ($nowTs - $actTs) / 60;
            }
        }

        $heartbeatState = null;
        if (!empty($heartbeat['state'])) {
            $hbSt = strtolower(trim($heartbeat['state']));
            if ($hbSt === 'waiting' || $hbSt === 'running') {
                $heartbeatState = $hbSt;
            }
        }

        $heartbeatMinutesSince = null;
        if (!empty($heartbeat['last_heartbeat'])) {
            $hbTs = strtotime($heartbeat['last_heartbeat']);
            if ($hbTs) {
                $heartbeatMinutesSince = ($nowTs - $hbTs) / 60;
            }
        }

        $heartbeatFresh = ($heartbeatMinutesSince !== null && $heartbeatMinutesSince <= 3.5);

        if ($heartbeatFresh && $heartbeatState) {
            $state = $heartbeatState;
        } elseif ($minutesSince !== null && $minutesSince <= 3) {
            $state = "running";
        } elseif ($minutesSince !== null && $minutesSince <= 60) {
            $state = "waiting";
        } else {
            $state = "offline";
        }

        echo json_encode([
            'state' => $state,
            'running' => $heartbeatFresh,
            'dbProvider' => 'mysql',
            'lastActivity' => $lastActivity,
            'minutesSinceActivity' => $minutesSince !== null ? round($minutesSince) : null,
            'heartbeatState' => $heartbeatState,
            'heartbeatDetail' => isset($heartbeat['detail']) ? $heartbeat['detail'] : null,
            'heartbeatAt' => isset($heartbeat['last_heartbeat']) ? $heartbeat['last_heartbeat'] : null,
            'minutesSinceHeartbeat' => $heartbeatMinutesSince !== null ? round($heartbeatMinutesSince) : null
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    }

    if ($endpoint === 'dashboard') {
        // Resumo geral das vagas
        $summaryStmt = $pdo->query("
            SELECT
                COUNT(*) AS total,
                COALESCE(SUM(CASE WHEN candidatura_enviada_sucesso = 1 THEN 1 ELSE 0 END), 0) AS sucesso,
                COALESCE(SUM(
                    CASE
                        WHEN candidatura_enviada = 1
                            AND (candidatura_enviada_sucesso IS NULL OR candidatura_enviada_sucesso = 0)
                            AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
                        THEN 1
                        ELSE 0
                    END
                ), 0) AS enviada_sem_confirmacao,
                COALESCE(SUM(CASE WHEN candidatura_indisponivel = 1 THEN 1 ELSE 0 END), 0) AS indisponiveis,
                COALESCE(SUM(
                    CASE
                        WHEN (candidatura_enviada IS NULL OR candidatura_enviada = 0)
                            AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
                        THEN 1
                        ELSE 0
                    END
                ), 0) AS pendentes
            FROM vagas;
        ");
        $summaryRow = $summaryStmt->fetch();

        // Vagas com sucesso
        $succStmt = $pdo->query("
            SELECT titulo, empresa, localizacao, link, data_envio_sucesso
            FROM vagas
            WHERE candidatura_enviada_sucesso = 1
            ORDER BY data_envio_sucesso DESC
            LIMIT 50;
        ");
        $successfulJobs = $succStmt->fetchAll();

        // Vagas indisponíveis
        $unavStmt = $pdo->query("
            SELECT titulo, empresa, localizacao, link, motivo_indisponibilidade, data_indisponibilidade
            FROM vagas
            WHERE candidatura_indisponivel = 1
            ORDER BY data_indisponibilidade DESC
            LIMIT 50;
        ");
        $unavailableJobs = $unavStmt->fetchAll();

        // Candidaturas sem confirmação
        $pendStmt = $pdo->query("
            SELECT titulo, empresa, localizacao, link, data_candidatura
            FROM vagas
            WHERE candidatura_enviada = 1
                AND (candidatura_enviada_sucesso IS NULL OR candidatura_enviada_sucesso = 0)
                AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
            ORDER BY data_candidatura DESC
            LIMIT 50;
        ");
        $pendingConfirmation = $pendStmt->fetchAll();

        // Etapas recentes
        $stepsStmt = $pdo->query("
            SELECT link, etapa, sucesso, detalhe, criado_em
            FROM candidatura_etapas
            ORDER BY criado_em DESC
            LIMIT 80;
        ");
        $recentSteps = $stepsStmt->fetchAll();

        // Linha do tempo dos últimos 60 dias
        $timeStmt = $pdo->query("
            SELECT
                DATE(data_insercao) AS day,
                COUNT(*) AS total,
                COALESCE(SUM(CASE WHEN candidatura_enviada_sucesso = 1 THEN 1 ELSE 0 END), 0) AS sucesso,
                COALESCE(SUM(CASE WHEN candidatura_indisponivel = 1 THEN 1 ELSE 0 END), 0) AS indisponiveis,
                COALESCE(SUM(
                    CASE
                        WHEN (candidatura_enviada IS NULL OR candidatura_enviada = 0)
                            AND (candidatura_indisponivel IS NULL OR candidatura_indisponivel = 0)
                        THEN 1
                        ELSE 0
                    END
                ), 0) AS pendentes
            FROM vagas
            WHERE data_insercao >= DATE_SUB(NOW(), INTERVAL 60 DAY)
            GROUP BY DATE(data_insercao)
            ORDER BY day ASC;
        ");
        $timeline = $timeStmt->fetchAll();

        echo json_encode([
            'generatedAt' => gmdate('c'),
            'provider' => 'mysql',
            'summary' => [
                'total' => (int)($summaryRow['total'] ?? 0),
                'sucesso' => (int)($summaryRow['sucesso'] ?? 0),
                'enviada_sem_confirmacao' => (int)($summaryRow['enviada_sem_confirmacao'] ?? 0),
                'indisponiveis' => (int)($summaryRow['indisponiveis'] ?? 0),
                'pendentes' => (int)($summaryRow['pendentes'] ?? 0)
            ],
            'successfulJobs' => $successfulJobs ?: [],
            'unavailableJobs' => $unavailableJobs ?: [],
            'pendingConfirmation' => $pendingConfirmation ?: [],
            'recentSteps' => $recentSteps ?: [],
            'timeline' => $timeline ?: []
        ], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    }

    http_response_code(404);
    echo json_encode(['error' => 'Endpoint não encontrado', 'endpoint' => $endpoint]);
} catch (Exception $e) {
    http_response_code(500);
    echo json_encode(['ok' => false, 'error' => $e->getMessage()]);
}
