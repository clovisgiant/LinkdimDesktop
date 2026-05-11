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
                var allJobsData = new List<(string Titulo, string Empresa, string Localizacao, string Link)>();
                var collectedFromJobsSearch = false;

                if (useJobsSearchEntry)
                {
                    var cycleSearchTerms = GetJobSearchTermsForCurrentCycle(jobsSearchTerms);
                    foreach (var searchTerm in cycleSearchTerms)
                    {
                        UpdateRuntimeHeartbeatLoopState("running", $"Iniciando busca para '{searchTerm}'.");
                        Console.WriteLine($"\n[C#] Iniciando novo navegador para o termo: '{searchTerm}'...");

                        using (var driver = new ChromeDriver(BuildChromeOptions(usePersistentProfile)))
                        {
                            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                            if (!EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword)) continue;

                            if (!TryPrepareJobsSearchEntry(driver, searchTerm)) continue;
                            if (!TryCollectJobsFromCurrentResults(driver, wait, allJobsLines, allJobsData, maxPagesPerCycle, $"termo '{searchTerm}'")) continue;
                            
                            collectedFromJobsSearch = true;
                            Console.WriteLine($"[C#] Busca para '{searchTerm}' concluida. Fechando navegador para limpar RAM...");
                            driver.Quit();
                        }
                        
                        // Pequena pausa entre termos para o SO limpar a RAM
                        GC.Collect();
                        Thread.Sleep(5000);
                    }
                }

                if (!collectedFromJobsSearch)
                {
                    UpdateRuntimeHeartbeatLoopState("running", "Coletando da colecao Easy Apply.");
                    Console.WriteLine("\n[C#] Abrindo navegador para colecao Easy Apply...");
                    
                    using (var driver = new ChromeDriver(BuildChromeOptions(usePersistentProfile)))
                    {
                        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
                        if (EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword))
                        {
                            driver.Navigate().GoToUrl(GetEasyApplyCollectionEntryUrlForCycle());
                            TryCollectJobsFromCurrentResults(driver, wait, allJobsLines, allJobsData, maxPagesPerCycle, "colecao Easy Apply");
                        }
                        driver.Quit();
                    }
                }

                allJobsData = NormalizeAndDeduplicateJobs(allJobsData);
                allJobsLines = allJobsData.Select(BuildJobLine).ToList();

                Console.WriteLine($"\nTotal de vagas coletadas: {allJobsLines.Count}");
                File.WriteAllLines(JobsOutputFileName, allJobsLines);

                if (maxJobsToApplyPerCycle > 0 && allJobsData.Count > 0)
                {
                    UpdateRuntimeHeartbeatLoopState("running", $"Iniciando candidaturas ({allJobsData.Count} vagas).");
                    Console.WriteLine($"\n[C#] Abrindo navegador para candidaturas...");
                    
                    using (var driver = new ChromeDriver(BuildChromeOptions(usePersistentProfile)))
                    {
                        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                        if (EnsureAuthenticatedSession(driver, wait, linkedinUsername, linkedinPassword))
                        {
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
                        driver.Quit();
                    }
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
