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
    private static string AutoFillDefaultFirstName = "Clovis";
    private static string AutoFillDefaultLastName = "Silva";
    private static string AutoFillDefaultPhone = "11999999999";
    private static string AutoFillDefaultLocation = "Sao Paulo";
    private static string AutoFillDefaultEmail = "clovis.eduardosilva23@gmail.com";
    private static string AutoFillDefaultWebsite = "";
    private static string AutoFillDefaultLinkedIn = "";
    private static string AutoFillDefaultGithub = "";
    private static string AutoFillDefaultSalary = "7000";
    private static string AutoFillDefaultGenericText = "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.";
    private static int AutoFillDefaultYearsExperience = 10;
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
            ConfigureHumanization(interactionDelayMinMs, interactionDelayMaxMs, applyDelayMinMs, applyDelayMaxMs, paginationDelayMinMs, paginationDelayMaxMs, activeHoursStart, activeHoursEnd);

            LoadIgnoredJobLinksFromDisk();
            LoadSuccessfulJobsFromDisk();

            Console.WriteLine($"Configuração: TEST_MODE={testMode}, DISABLE_DATABASE={disableDatabase}, MAX_PAGES_PER_CYCLE={maxPagesPerCycle}, MAX_APPLY_PER_CYCLE={maxJobsToApplyPerCycle}, CYCLE_WAIT_MINUTES={cycleWaitMinutes}, USE_JOBS_SEARCH_ENTRY={useJobsSearchEntry}, JOB_SEARCH_TERMS={string.Join(" | ", jobsSearchTerms)}");

            while (true)
            {
                WaitUntilWithinActiveHoursIfNeeded();
                StartRuntimeHeartbeatLoop("running", "Iniciando novo ciclo do crawler.");
                Console.WriteLine("Iniciando nova execução...");

                if (DatabaseEnabled && !ValidateDatabaseConnection())
                {
                    Console.WriteLine("Conexão com o banco indisponível. Aguardando 2 minuto(s)...");
                    StopRuntimeHeartbeatLoop();
                    SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(2), "waiting", "Banco indisponivel.");
                    continue;
                }

                StartSuccessfulJobsCycle();

                var allJobsLines = new List<string>();
                StartSuccessfulJobsCycle();
                var allJobsData = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();
                var isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);

                Console.WriteLine("\n[C#] Abrindo navegador único para o ciclo completo (Economia de RAM)...");
                using (var driver = new ChromeDriver(BuildChromeOptions(usePersistentProfile)))
                {
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                    
                    // 1. AUTENTICAÇÃO
                    UpdateRuntimeHeartbeatLoopState("running", "Autenticando...");
                    if (!EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword)) 
                    {
                        Console.WriteLine("⚠️ Falha na autenticação. Pulando ciclo.");
                    }
                    else 
                    {
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
                                    var currentTermJobs = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();
                                    var dummyLines = new List<string>();
                                    if (TryCollectJobsFromCurrentResults(driver, wait, dummyLines, currentTermJobs, maxPagesPerCycle, $"termo '{searchTerm}'"))
                                    {
                                        allJobsData.AddRange(currentTermJobs);
                                    }
                                }
                                // Pausa leve entre buscas na mesma aba
                                Thread.Sleep(3000);
                            }
                        }

                        // 3. COLEÇÃO EASY APPLY (Se nada foi coletado antes)
                        if (allJobsData.Count == 0)
                        {
                            UpdateRuntimeHeartbeatLoopState("running", "Coletando Coleção Easy Apply.");
                            Console.WriteLine("\n[C#] Nada encontrado na busca. Tentando Coleção Easy Apply...");
                            driver.Navigate().GoToUrl(GetEasyApplyCollectionEntryUrlForCycle());
                            var dummyLines = new List<string>();
                            TryCollectJobsFromCurrentResults(driver, wait, dummyLines, allJobsData, maxPagesPerCycle, "coleção Easy Apply");
                        }

                        // DEDUPLICAÇÃO
                        allJobsData = NormalizeAndDeduplicateJobs(allJobsData);
                        Console.WriteLine($"\nTotal de vagas únicas coletadas: {allJobsData.Count}");

                        // 4. CANDIDATURAS
                        if (maxJobsToApplyPerCycle > 0 && allJobsData.Count > 0)
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
                    }

                    Console.WriteLine("[C#] Ciclo finalizado. Fechando navegador...");
                    driver.Quit();
                }

                PrintSuccessfulJobsCycleSummary();
                PrintVagasTabela(allJobsData);

                Console.WriteLine("Ciclo concluido. Limpando memória...");
                StopRuntimeHeartbeatLoop();
                SleepWithRuntimeHeartbeat(TimeSpan.FromMinutes(cycleWaitMinutes), "waiting", "Aguardando proximo ciclo.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ [C#] ERRO CRÍTICO FATAL NO MOTOR:");
            Console.WriteLine("Mensagem: " + ex.Message);
            Console.WriteLine("StackTrace: " + ex.StackTrace);
        }
    }
}
