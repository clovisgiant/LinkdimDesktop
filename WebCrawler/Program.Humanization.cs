using OpenQA.Selenium;
using System;
using System.Threading;

partial class Program
{
    private static readonly Random HumanizationRandom = new();
    private static readonly object HumanizationRandomLock = new();
    private static readonly string[] EasyApplyCollectionEntryUrlVariants =
    {
        LinkedInEasyApplyCollectionUrl,
        "https://www.linkedin.com/jobs/collections/easy-apply/?discover=recommended&discoveryOrigin=JOBS_HOME_JYMBII",
        "https://www.linkedin.com/jobs/collections/easy-apply/?discoveryOrigin=JOBS_HOME_JYMBII&discover=recommended&start=0"
    };

    private static int InteractionDelayMinMs = 800;
    private static int InteractionDelayMaxMs = 2500;
    private static int BetweenApplicationsDelayMinMs = 5000;
    private static int BetweenApplicationsDelayMaxMs = 15000;
    private static int PaginationDelayMinMs = 1200;
    private static int PaginationDelayMaxMs = 2800;
    private static int CollectionEntryScrollMinPx = 120;
    private static int CollectionEntryScrollMaxPx = 720;
    private static TimeSpan? ActiveHoursStartLocal;
    private static TimeSpan? ActiveHoursEndLocal;

    private static void ConfigureHumanization(
        int interactionDelayMinMs,
        int interactionDelayMaxMs,
        int betweenApplicationsDelayMinMs,
        int betweenApplicationsDelayMaxMs,
        int paginationDelayMinMs,
        int paginationDelayMaxMs,
        TimeSpan? activeHoursStart,
        TimeSpan? activeHoursEnd)
    {
        InteractionDelayMinMs = Math.Max(150, interactionDelayMinMs);
        InteractionDelayMaxMs = Math.Max(InteractionDelayMinMs, interactionDelayMaxMs);
        BetweenApplicationsDelayMinMs = Math.Max(500, betweenApplicationsDelayMinMs);
        BetweenApplicationsDelayMaxMs = Math.Max(BetweenApplicationsDelayMinMs, betweenApplicationsDelayMaxMs);
        PaginationDelayMinMs = Math.Max(250, paginationDelayMinMs);
        PaginationDelayMaxMs = Math.Max(PaginationDelayMinMs, paginationDelayMaxMs);
        CollectionEntryScrollMaxPx = Math.Max(CollectionEntryScrollMinPx, CollectionEntryScrollMaxPx);

        if (activeHoursStart.HasValue && activeHoursEnd.HasValue && activeHoursStart.Value != activeHoursEnd.Value)
        {
            ActiveHoursStartLocal = activeHoursStart.Value;
            ActiveHoursEndLocal = activeHoursEnd.Value;
            return;
        }

        ActiveHoursStartLocal = null;
        ActiveHoursEndLocal = null;
    }

    private static int NextRandomInt(int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            (minValue, maxValue) = (maxValue, minValue);
        }

        lock (HumanizationRandomLock)
        {
            return HumanizationRandom.Next(minValue, maxValue + 1);
        }
    }

    private static int _consecutiveFailures = 0;
    private const int MaxBackoffMinutes = 120; 

    private static void ResetFailures() => _consecutiveFailures = 0;

    private static void ApplyExponentialBackoff(string reason)
    {
        _consecutiveFailures++;
        int minutesToWait = (int)Math.Min(Math.Pow(2, _consecutiveFailures), MaxBackoffMinutes);
        
        Console.WriteLine($"[!!] DETECÇÃO/ERRO: {reason}");
        Console.WriteLine($"[!!] Aplicando Backoff Exponencial: Aguardando {minutesToWait} minutos...");
        
        for(int i = 0; i < minutesToWait; i++)
        {
            if (i > 0 && i % 5 == 0) Console.WriteLine($"... ainda aguardando ({minutesToWait - i} min restantes)");
            Thread.Sleep(60000); 
        }
    }

    private static void SleepRandomDelay(int minMs, int maxMs, string? label = null, bool log = false)
    {
        double jitter = 1.0 + (HumanizationRandom.NextDouble() * 0.3 - 0.15); 
        var delayMs = (int)(NextRandomInt(minMs, maxMs) * jitter);

        if (log && !string.IsNullOrWhiteSpace(label))
        {
            Console.WriteLine($"[HUMAN] {label}: aguardando {delayMs} ms.");
        }

        Thread.Sleep(delayMs);
    }

    private static void SimulateReadingPause()
    {
        Console.WriteLine($"[HUMAN] Analisando detalhes da vaga...");
        SleepRandomDelay(1500, 3200);
    }

    private static void PauseBeforeClick()
    {
        SleepRandomDelay(250, 750);
    }

    private static void PauseBetweenFlowSteps(string? label = null, bool log = false)
    {
        SleepRandomDelay(InteractionDelayMinMs, InteractionDelayMaxMs, label, log);
    }

    private static void PauseAfterPaginationAdvance()
    {
        SleepRandomDelay(PaginationDelayMinMs, PaginationDelayMaxMs);
    }

    private static void PauseBetweenApplications(string link, bool flowCompleted)
    {
        var normalizedLink = NormalizeLinkedInJobLink(link) ?? link;
        var description = flowCompleted
            ? $"intervalo apos candidatura concluída para {normalizedLink}"
            : $"intervalo apos tentativa falha para {normalizedLink}";

        int min = flowCompleted ? BetweenApplicationsDelayMinMs : (BetweenApplicationsDelayMinMs / 2);
        int max = flowCompleted ? BetweenApplicationsDelayMaxMs : (BetweenApplicationsDelayMaxMs / 2);

        SleepRandomDelay(min, max, description, log: true);
    }

    private static string GetEasyApplyCollectionEntryUrlForCycle()
    {
        var variantIndex = NextRandomInt(0, EasyApplyCollectionEntryUrlVariants.Length - 1);
        return EasyApplyCollectionEntryUrlVariants[variantIndex];
    }

    private static void HumanizeCollectionEntry(IWebDriver driver)
    {
        try
        {
            for (int i = 0; i < NextRandomInt(2, 4); i++)
            {
                var scrollStep = NextRandomInt(150, 400);
                ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy({{ top: {scrollStep}, behavior: 'smooth' }});");
                SleepRandomDelay(400, 1200);
            }
        }
        catch { }
    }

    private static void TryScrollElementIntoViewHumanized(IWebDriver driver, IWebElement element)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({ behavior: 'smooth', block: 'center', inline: 'nearest' });", element);
            SleepRandomDelay(180, 420);
        }
        catch
        {
            try
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({ block: 'center' });", element);
            }
            catch
            {
                // Elemento pode ter ficado stale; o clique tentará do mesmo jeito.
            }
        }
    }

    private static string DescribeActiveHoursWindow()
    {
        if (!ActiveHoursStartLocal.HasValue || !ActiveHoursEndLocal.HasValue)
        {
            return "desativada";
        }

        return $"{ActiveHoursStartLocal.Value:hh\\:mm}-{ActiveHoursEndLocal.Value:hh\\:mm}";
    }

    private static bool IsWithinActiveHours()
    {
        // Configuração cortada: O robô agora trabalha 24/7 sem exceções.
        return true;
    }

    private static void WaitUntilWithinActiveHoursIfNeeded()
    {
        // Configuração cortada: O robô não espera mais por janelas de horário.
    }

    private static bool IsWithinActiveHours(TimeSpan currentTime, TimeSpan startTime, TimeSpan endTime)
    {
        if (startTime == endTime)
        {
            return true;
        }

        if (startTime < endTime)
        {
            return currentTime >= startTime && currentTime < endTime;
        }

        return currentTime >= startTime || currentTime < endTime;
    }

    private static DateTime GetNextActiveWindowStart(DateTime now, TimeSpan startTime)
    {
        var todayStart = now.Date.Add(startTime);
        return todayStart > now ? todayStart : todayStart.AddDays(1);
    }
}