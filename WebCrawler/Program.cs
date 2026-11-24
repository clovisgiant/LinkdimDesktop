using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenQA.Selenium;

partial class Program
{
    private static bool DatabaseEnabled = true;
    private static bool PersistIgnoredLinksToFile = true;
    private static bool PersistSuccessfulLinksToFile = true;
    private static bool AutoFillMandatoryFieldsEnabled = true;
    private static string AutoFillDefaultFirstName = string.Empty;
    private static string AutoFillDefaultLastName = string.Empty;
    private static string AutoFillDefaultPhone = string.Empty;
    private static string AutoFillDefaultLocation = "São Paulo";
    private static string AutoFillDefaultCity = "São Paulo";
    private static string AutoFillDefaultState = "São Paulo";
    private static string AutoFillDefaultAddress = string.Empty;
    private static string AutoFillDefaultZip = string.Empty;
    private static string AutoFillDefaultEmail = string.Empty;
    private static string AutoFillDefaultWebsite = string.Empty;
    private static string AutoFillDefaultLinkedIn = string.Empty;
    private static string AutoFillDefaultGithub = string.Empty;
    private static string AutoFillDefaultEducation = string.Empty;
    private static string AutoFillDefaultDegree = "Superior completo";
    private static string AutoFillDefaultJobTitle = "Desenvolvedor Full Stack";
    private static string AutoFillDefaultCurrentCompany = string.Empty;
    private static string AutoFillDefaultSalary = "8000";
    private static string AutoFillDefaultGenericText = "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.";
    private static int AutoFillDefaultYearsExperience = 5;
    private static bool AutoFillDefaultCheckboxTrue = true;
    private static bool AutoFillDefaultWorkAuthorization = true;
    private static bool AutoFillDefaultNeedVisaSponsorship = false;
    private static readonly HashSet<string> IgnoredJobLinksInRun = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IgnoredJobLinksInRunLock = new();
    private static readonly HashSet<string> SuccessfulJobLinksPersisted = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SuccessfulJobLinksCurrentCycle = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SuccessfulJobLinksLock = new();
    private const string LinkedInFeedUrl = "https://www.linkedin.com/feed/";
    private const string LinkedInLoginUrl = "https://www.linkedin.com/login";
    private const string LinkedInJobsSearchUrl = "https://www.linkedin.com/jobs/search/";
    private const string LinkedInEasyApplyCollectionUrl = "https://www.linkedin.com/jobs/collections/easy-apply/?discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII&start=0";
    private const string JobsOutputFileName = "vagas_linkedin.txt";
    private const string IgnoredJobsFileName = "ignored_job_links.txt";
    private const string SuccessfulJobsCycleFileName = "vagas_enviadas_sucesso.txt";
    private const string SuccessfulJobsHistoryFileName = "vagas_enviadas_sucesso_historico.txt";
    private static readonly object JobSearchTermSelectionLock = new();
    private static int JobSearchTermSelectionIndex;
    private static readonly string[] DefaultJobSearchTerms =
    {
        "Desenvolvedor Backend .NET - Pleno/Senior",
        "Desenvolvedor C#",
        "Engenheiro de Software",
        "C#",
        "Python",
        "PostgreSQL"
    };

    static void Main()
    {
        try
        {
            Console.WriteLine("========================================");
            Console.WriteLine("🚀 [C#] ROBÔ LINKDIM INICIADO COM SUCESSO!");
            Console.WriteLine($"📅 DATA/HORA: {DateTime.Now}");
            Console.WriteLine("========================================");

            LoadEnvFileIfExists();

            using var singleInstanceMutex = TryAcquireSingleInstanceMutex();
            if (singleInstanceMutex == null)
            {
                Console.WriteLine("Outra instancia do WebCrawler ja esta em execucao. Encerrando esta inicializacao.");
                return;
            }

            var linkedinUsername = GetRequiredEnv("LINKEDIN_USERNAME");
            var linkedinPassword = GetRequiredEnv("LINKEDIN_PASSWORD");

            var testMode = GetOptionalBoolEnv("WEBCRAWLER_TEST_MODE", false);
            var disableDatabase = GetOptionalBoolEnv("WEBCRAWLER_DISABLE_DATABASE", true);
            var maxPagesPerCycle = GetOptionalIntEnv("WEBCRAWLER_MAX_PAGES_PER_CYCLE", testMode ? 1 : 2);
            var maxJobsToApplyPerCycle = GetOptionalIntEnv("WEBCRAWLER_MAX_APPLY_PER_CYCLE", testMode ? 2 : 15);
            var cycleWaitMinutes = GetOptionalIntEnv("WEBCRAWLER_CYCLE_WAIT_MINUTES", testMode ? 1 : 45);
            var usePersistentProfile = GetOptionalBoolEnv("WEBCRAWLER_USE_PERSISTENT_PROFILE", true);
            var headless = GetOptionalBoolEnv("WEBCRAWLER_HEADLESS", false);
            var persistIgnoredLinks = GetOptionalBoolEnv("WEBCRAWLER_PERSIST_IGNORED_LINKS", true);
            var persistSuccessfulLinks = GetOptionalBoolEnv("WEBCRAWLER_PERSIST_SUCCESSFUL_LINKS", true);
            var autoFillMandatoryFields = GetOptionalBoolEnv("WEBCRAWLER_AUTO_FILL_MANDATORY_FIELDS", true);
            var interactionDelayMinMs = GetOptionalIntEnv("WEBCRAWLER_INTERACTION_DELAY_MIN_MS", testMode ? 200 : 800);
            var interactionDelayMaxMs = GetOptionalIntEnv("WEBCRAWLER_INTERACTION_DELAY_MAX_MS", testMode ? 600 : 2500);
            var applyDelayMinMs = GetOptionalIntEnv("WEBCRAWLER_APPLY_DELAY_MIN_MS", testMode ? 800 : 5000);
            var applyDelayMaxMs = GetOptionalIntEnv("WEBCRAWLER_APPLY_DELAY_MAX_MS", testMode ? 1500 : 15000);
            var paginationDelayMinMs = GetOptionalIntEnv("WEBCRAWLER_PAGINATION_DELAY_MIN_MS", testMode ? 500 : 1200);
            var paginationDelayMaxMs = GetOptionalIntEnv("WEBCRAWLER_PAGINATION_DELAY_MAX_MS", testMode ? 1200 : 2800);
            var activeHoursStart = GetOptionalTimeOfDayEnv("WEBCRAWLER_ACTIVE_HOURS_START");
            var activeHoursEnd = GetOptionalTimeOfDayEnv("WEBCRAWLER_ACTIVE_HOURS_END");

            if (!activeHoursStart.HasValue && !activeHoursEnd.HasValue)
            {
                Console.WriteLine("📅 [C#] MODO 24/7 ATIVADO (Padrão): O robô não irá dormir.");
            }
            else if (activeHoursStart.HasValue && activeHoursEnd.HasValue && activeHoursStart.Value == activeHoursEnd.Value)
            {
                Console.WriteLine($"📅 [C#] MODO 24/7 ATIVADO ({activeHoursStart.Value.Hours:D2}:00): O robô não irá dormir.");
            }
            else
            {
                Console.WriteLine($"📅 [C#] Janela ativa configurada: {(activeHoursStart?.Hours ?? 0):D2}:00 ate {(activeHoursEnd?.Hours ?? 0):D2}:00");
            }
            
            ConfigureHumanization(interactionDelayMinMs, interactionDelayMaxMs, applyDelayMinMs, applyDelayMaxMs, paginationDelayMinMs, paginationDelayMaxMs, activeHoursStart, activeHoursEnd);

            var useJobsSearchEntry = GetOptionalBoolEnv("WEBCRAWLER_USE_JOBS_SEARCH_ENTRY", true);
            var jobsSearchTerms = GetOptionalCsvEnvList("WEBCRAWLER_JOB_SEARCH_TERMS", DefaultJobSearchTerms);

            DatabaseEnabled = !disableDatabase;
            PersistIgnoredLinksToFile = persistIgnoredLinks;
            PersistSuccessfulLinksToFile = persistSuccessfulLinks;
            AutoFillMandatoryFieldsEnabled = autoFillMandatoryFields;
            AutoFillDefaultEmail = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_EMAIL", Environment.GetEnvironmentVariable("LINKEDIN_USERNAME") ?? string.Empty);
            AutoFillDefaultFirstName = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_FIRST_NAME", string.Empty);
            AutoFillDefaultLastName = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_LAST_NAME", string.Empty);
            AutoFillDefaultPhone = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_PHONE", string.Empty);
            AutoFillDefaultLocation = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_LOCATION", "São Paulo");
            AutoFillDefaultCity = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_CITY", "São Paulo");
            AutoFillDefaultState = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_STATE", "São Paulo");
            AutoFillDefaultAddress = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_ADDRESS", string.Empty);
            AutoFillDefaultZip = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_ZIP", string.Empty);
            AutoFillDefaultSalary = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_SALARY", "8000");
            AutoFillDefaultYearsExperience = GetOptionalIntEnv("WEBCRAWLER_DEFAULT_YEARS_EXPERIENCE", 5);
            AutoFillDefaultWebsite = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_WEBSITE", string.Empty);
            AutoFillDefaultLinkedIn = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_LINKEDIN_URL", string.Empty);
            AutoFillDefaultGithub = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_GITHUB_URL", string.Empty);
            AutoFillDefaultEducation = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_EDUCATION", string.Empty);
            AutoFillDefaultDegree = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_DEGREE", "Superior completo");
            AutoFillDefaultJobTitle = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_JOB_TITLE", "Desenvolvedor Full Stack");
            AutoFillDefaultCurrentCompany = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_CURRENT_COMPANY", string.Empty);
            AutoFillDefaultGenericText = GetOptionalStringEnv("WEBCRAWLER_DEFAULT_GENERIC_TEXT", "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.");
            ConfigureHumanization(interactionDelayMinMs, interactionDelayMaxMs, applyDelayMinMs, applyDelayMaxMs, paginationDelayMinMs, paginationDelayMaxMs, activeHoursStart, activeHoursEnd);

            LoadIgnoredJobLinksFromDisk();
            LoadSuccessfulJobsFromDisk();

            Console.WriteLine($"Configuração: TEST_MODE={testMode}, DISABLE_DATABASE={disableDatabase}, MAX_PAGES_PER_CYCLE={maxPagesPerCycle}, MAX_APPLY_PER_CYCLE={maxJobsToApplyPerCycle}, CYCLE_WAIT_MINUTES={cycleWaitMinutes}, USE_JOBS_SEARCH_ENTRY={useJobsSearchEntry}, JOB_SEARCH_TERMS={string.Join(" | ", jobsSearchTerms)}");

            while (true)
            {
                try 
                {
                    WaitUntilWithinActiveHoursIfNeeded();
                    StartRuntimeHeartbeatLoop("running", "Iniciando novo ciclo do crawler.");
                    Console.WriteLine("\n[MOTOR] Iniciando nova execução...");

                    if (DatabaseEnabled && !ValidateDatabaseConnection())
                    {
                        Console.WriteLine("Conexão com o banco indisponível. Aguardando 2 minuto(s)...");
                        StopRuntimeHeartbeatLoop();
                        SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(2), "waiting", "Banco indisponivel.");
                        continue;
                    }

                    StartSuccessfulJobsCycle();
                    var allJobsData = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();

                    Console.WriteLine("[MOTOR] Abrindo navegador único para o ciclo completo...");
                    var service = ChromeDriverService.CreateDefaultService();
                    service.SuppressInitialDiagnosticInformation = true;
                    service.HideCommandPromptWindow = true;
                    
                    using (var driver = CreateRobustChromeDriver(service, BuildChromeOptions(headless, usePersistentProfile), TimeSpan.FromMinutes(3)))
                    {
                        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(30));
                        
                        // 1. AUTENTICAÇÃO
                        UpdateRuntimeHeartbeatLoopState("running", "Autenticando...");
                        if (!EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword)) 
                        {
                            ApplyExponentialBackoff("Falha na autenticação ou desafio de segurança (CAPTCHA) não resolvido.");
                            driver.Quit();
                            continue;
                        }

                        // Verificação de segurança pós-login
                        if (DetectBotInterruption(driver))
                        {
                            ApplyExponentialBackoff("LinkedIn detectou atividade suspeita logo após o login.");
                            driver.Quit();
                            continue;
                        }

                        // 2. BUSCA POR TERMOS
                        if (useJobsSearchEntry)
                        {
                            var cycleSearchTerms = GetJobSearchTermsForCurrentCycle(jobsSearchTerms);
                            foreach (var searchTerm in cycleSearchTerms)
                            {
                                UpdateRuntimeHeartbeatLoopState("running", $"Buscando '{searchTerm}'...");
                                Console.WriteLine($"\n[C#] Buscando: '{searchTerm}'...");

                                if (TryPrepareJobsSearchEntry(driver, searchTerm))
                                {
                                    if (DetectBotInterruption(driver)) break;

                                    var currentTermJobs = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();
                                    var dummyLines = new List<string>();
                                    if (TryCollectJobsFromCurrentResults(driver, wait, dummyLines, currentTermJobs, maxPagesPerCycle, $"termo '{searchTerm}'"))
                                    {
                                        allJobsData.AddRange(currentTermJobs);
                                    }
                                }
                                
                                // Pausa humana entre termos de busca
                                PauseBetweenFlowSteps("Intervalo entre termos de busca", log: true);
                            }
                        }

                        // 3. COLEÇÃO EASY APPLY (Fallback)
                        if (allJobsData.Count == 0 && !DetectBotInterruption(driver))
                        {
                            UpdateRuntimeHeartbeatLoopState("running", "Coletando Coleção Easy Apply.");
                            Console.WriteLine("\n[MOTOR] Nada encontrado na busca. Tentando Coleção Easy Apply...");
                            driver.Navigate().GoToUrl(GetEasyApplyCollectionEntryUrlForCycle());
                            var dummyLines = new List<string>();
                            TryCollectJobsFromCurrentResults(driver, wait, dummyLines, allJobsData, maxPagesPerCycle, "coleção Easy Apply");
                        }

                        // DEDUPLICAÇÃO
                        allJobsData = NormalizeAndDeduplicateJobs(allJobsData);
                        Console.WriteLine($"\n[SUCESSO] Total de vagas únicas coletadas: {allJobsData.Count}");

                        // 4. CANDIDATURAS
                        if (maxJobsToApplyPerCycle > 0 && allJobsData.Count > 0 && !DetectBotInterruption(driver))
                        {
                            UpdateRuntimeHeartbeatLoopState("running", $"Candidatando ({allJobsData.Count} vagas)...");
                            if (!disableDatabase)
                            {
                                SaveCollectedJobsToDatabase(allJobsData);
                                ApplySimplifiedJobsFromDatabase(driver, maxJobsToApplyPerCycle);
                            }
                            else
                            {
                                var links = allJobsData.Select(v => v.Link).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();
                                ApplySimplifiedJobsFromLinks(driver, links, maxJobsToApplyPerCycle);
                            }
                        }

                        Console.WriteLine("[C#] Ciclo finalizado com sucesso. Fechando navegador...");
                        ResetFailures(); // Sucesso limpa o histórico de erros para o Backoff
                        driver.Quit();
                    }

                    PrintSuccessfulJobsCycleSummary();
                    PrintVagasTabela(allJobsData);

                    Console.WriteLine("[MOTOR] Ciclo concluido. Limpando memória...");
                    StopRuntimeHeartbeatLoop();
                    SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(cycleWaitMinutes), "waiting", "Aguardando proximo ciclo.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("\n❌ [C#] ERRO NO CICLO DE EXECUÇÃO:");
                    Console.WriteLine("Mensagem: " + ex.Message);
                    if (ex.Message.Contains("429") || ex.Message.Contains("security")) 
                    {
                        ApplyExponentialBackoff("Erro HTTP 429 ou bloqueio de rede detectado.");
                    }
                    else 
                    {
                        Thread.Sleep(10000); // Erro genérico, espera 10s antes de tentar de novo
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ [C#] ERRO CRÍTICO FATAL NO MOTOR (FORA DO LOOP):");
            Console.WriteLine("Mensagem: " + ex.Message);
            Console.WriteLine("StackTrace: " + ex.StackTrace);
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "fatal_error.log");
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n");
            }
            catch { }

            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("\nPressione ENTER para fechar...");
                try { Console.ReadLine(); } catch { }
            }
        }
    }

    private static bool DetectBotInterruption(IWebDriver driver)
    {
        try 
        {
            var url = (driver.Url ?? "").ToLowerInvariant();
            var title = (driver.Title ?? "").ToLowerInvariant();

            // 1. Verifica se a URL caiu em rotas clássicas de desafio ou bloqueio
            bool isCheckpointUrl = url.Contains("/checkpoint/") || 
                                   url.Contains("/challenge/") || 
                                   url.Contains("/uas/consumer-captcha") ||
                                   url.Contains("/security-check");

            // 2. Verifica se o título da página indica explicitamente bloqueio ou erro HTTP
            bool isBlockedTitle = title.Contains("security verification") || 
                                  title.Contains("verificação de segurança") || 
                                  title.Contains("quick verification") ||
                                  title.Contains("challenge | linkedin") ||
                                  title.Contains("429") || 
                                  title.Contains("too many requests") || 
                                  title.Contains("bloqueio temporário");

            // 3. Verifica se há elementos visíveis de CAPTCHA ou desafio no DOM (não busca dentro de scripts minificados)
            bool hasVisibleChallengeElement = false;
            try
            {
                var challengeElements = driver.FindElements(By.CssSelector(
                    "iframe[src*='arkose'], iframe[src*='captcha'], iframe[src*='challenge'], #captcha-internal, .checkpoint-container, .challenge-dialog, input#input__email_verification_pin, div[data-test-id*='challenge']"
                ));
                hasVisibleChallengeElement = challengeElements.Any(el => { try { return el.Displayed; } catch { return false; } });
            }
            catch { }

            // Se não há indicativo real de bloqueio: sessão normal
            if (!isCheckpointUrl && !isBlockedTitle && !hasVisibleChallengeElement)
            {
                return false;
            }

            Console.WriteLine($"\n⚠️ [AVISO] Possível desafio ou verificação detectada (URL: {url}, Título: {title}).");
            
            // Dá oportunidade de 2 minutos para resolução manual no navegador aberto se for desafio interativo
            WaitForManualLoginIfNeeded(driver, TimeSpan.FromMinutes(2));

            // Reavalia após a janela de espera
            if (IsLoggedIn(driver))
            {
                Console.WriteLine("✅ [C#] Sessão normalizada após verificação! Prosseguindo...");
                ResetFailures();
                return false;
            }

            return true;
        } 
        catch 
        { 
            return false; 
        }
    }
}
