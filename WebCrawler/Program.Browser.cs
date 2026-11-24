using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.IO;
using System.Linq;
using System.Threading;

partial class Program
{
    private static void WaitForJobPageReady(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
        try
        {
            wait.Until(d => {
                var url = d.Url?.ToLower() ?? "";
                if (url.Contains("/jobs/view/") || url.Contains("/jobs/search/"))
                {
                    return d.FindElements(By.TagName("body")).Any();
                }
                return true;
            });
            Thread.Sleep(2000);
        }
        catch { }
    }

    private static bool IsLoggedIn(IWebDriver driver)
    {
        try
        {
            var url = (driver.Url ?? string.Empty).ToLowerInvariant();
            var title = (driver.Title ?? string.Empty).ToLowerInvariant();

            // Se estiver em rotas explícitas de auth ou bloqueio/desafio, não está logado
            var isAuthRoute = url.Contains("/login") || url.Contains("/checkpoint") || url.Contains("/challenge") || url.EndsWith("/hp") || url.Contains("/uas/consumer-captcha");
            if (isAuthRoute) return false;

            var isBlockedTitle = title.Contains("security check") || 
                                 title.Contains("verificação de segurança") || 
                                 title.Contains("unusual activity") ||
                                 title.Contains("challenge | linkedin");
            if (isBlockedTitle) return false;

            try
            {
                var liAt = driver.Manage().Cookies.GetCookieNamed("li_at");
                var hasSessionCookie = liAt != null && !string.IsNullOrWhiteSpace(liAt.Value);

                if (hasSessionCookie)
                {
                    // Verifica se não há desafio ativo visível na tela
                    var hasVisibleChallenge = driver.FindElements(By.CssSelector("iframe[src*='arkose'], iframe[src*='captcha'], #captcha-internal, .checkpoint-container"))
                        .Any(e => { try { return e.Displayed; } catch { return false; } });

                    if (!hasVisibleChallenge)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignora erro de cookie
            }

            if (url.Contains("/feed", StringComparison.OrdinalIgnoreCase) || url.Contains("/jobs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var navCandidates = driver.FindElements(By.CssSelector("button.nav-item__link, a.nav-item__link, .global-nav__primary-link, #global-nav"));
            return navCandidates.Any(e => { try { return e.Displayed; } catch { return false; } });
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Digita texto com cadência humana (50-180ms por tecla + microvariações).
    /// LinkedIn rastreia velocidade de input — digitação instantânea é red flag.
    /// </summary>
    private static void HumanType(IWebDriver driver, IWebElement element, string text)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", element);
            SleepRandomDelay(300, 700);
            element.Click();
            SleepRandomDelay(200, 500);
            element.Clear();
            SleepRandomDelay(150, 350);

            foreach (var ch in text)
            {
                element.SendKeys(ch.ToString());
                // Cadência base por tecla
                int delay = NextRandomInt(60, 180);
                // Pausas ocasionais (como se o usuário pensasse / olhasse o teclado)
                if (NextRandomInt(1, 100) <= 8) delay += NextRandomInt(200, 600);
                Thread.Sleep(delay);
            }
        }
        catch
        {
            // Fallback compatível com inputs controlados pelo React (useId / React 18+)
            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                const val = arguments[1];
                const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                setter.call(el, val);
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", element, text);
        }
    }

    /// <summary>
    /// Faz scrolls e pequenos movimentos ANTES do login — simula um humano lendo
    /// a página de entrada / homepage em vez de ir direto digitar credenciais.
    /// </summary>
    private static void WarmupOrganic(IWebDriver driver)
    {
        try
        {
            // Scroll suave pra baixo
            for (int i = 0; i < NextRandomInt(2, 4); i++)
            {
                var step = NextRandomInt(180, 420);
                ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy({{top: {step}, behavior: 'smooth'}});");
                SleepRandomDelay(600, 1400);
            }
            // Volta pro topo
            ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo({top: 0, behavior: 'smooth'});");
            SleepRandomDelay(500, 1100);
        }
        catch { /* sem warmup não é fatal */ }
    }

    private static bool EnsureAuthenticatedSession(IWebDriver driver, WebDriverWait wait, string linkedinUsername, string linkedinPassword)
    {
        Console.WriteLine("[C#] Iniciando validação de sessão (estratégia anti-bloqueio)...");

        try
        {
            // 0. Aplica patches stealth ANTES de qualquer navegação (executam em cada nova página)
            ApplyStealthPatches(driver);

            // 1. Carrega a home com pausa humana e warmup (scroll / leitura)
            driver.Navigate().GoToUrl("https://www.linkedin.com");
            SleepRandomDelay(3500, 6500, "carregando home LinkedIn", log: true);
            WarmupOrganic(driver);

            // 2. Tenta injetar Cookie de sessão (.env) — caminho preferido, evita login form
            var sessionCookie = Environment.GetEnvironmentVariable("LINKEDIN_SESSION_COOKIE");
            if (!string.IsNullOrEmpty(sessionCookie) && !IsLoggedIn(driver))
            {
                try
                {
                    Console.WriteLine("🍪 [C#] Restaurando sessão via Cookie li_at...");
                    driver.Manage().Cookies.AddCookie(new Cookie("li_at", sessionCookie, ".linkedin.com", "/", DateTime.Now.AddDays(30)));
                    driver.Navigate().GoToUrl(LinkedInFeedUrl);
                    SleepRandomDelay(4000, 7000, "validando cookie", log: true);
                }
                catch { /* segue para login manual se o cookie falhar */ }
            }

            // 3. Já logado? Curtos passos humanos e segue.
            if (IsLoggedIn(driver))
            {
                Console.WriteLine("✅ [C#] Login confirmado! Indo para vagas...");
                ResetFailures();
                SleepRandomDelay(1200, 2800);
                driver.Navigate().GoToUrl(LinkedInJobsSearchUrl);
                SleepRandomDelay(2500, 4500);
                return true;
            }

            // 4. Login manual necessário — abre a tela de login com humanização
            Console.WriteLine("🚀 [C#] Login manual necessário. Abrindo página de login...");
            driver.Navigate().GoToUrl(LinkedInLoginUrl);
            SleepRandomDelay(3500, 6000, "carregando tela de login", log: true);

            // Pequeno scroll na própria tela de login (humanos olham antes de clicar)
            try { ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollBy({top: 80, behavior: 'smooth'});"); } catch { }
            SleepRandomDelay(600, 1400);

            // Localiza campo de e-mail (apenas elemento visível e habilitado)
            IWebElement? emailField = null;
            try
            {
                emailField = wait.Until(d => d.FindElements(By.CssSelector("input[type='email'], input[autocomplete*='username'], #username, input[name='session_key'], form input[type='text']"))
                    .FirstOrDefault(e => e.Displayed && e.Enabled));
            }
            catch { }

            if (emailField == null)
            {
                Console.WriteLine("⚠️ [C#] Campo de e-mail não localizado automaticamente. Aguardando login manual...");
                WaitForManualLoginIfNeeded(driver, TimeSpan.FromMinutes(3));
                return IsLoggedIn(driver);
            }

            // Digitação humana do e-mail
            HumanType(driver, emailField, linkedinUsername);
            SleepRandomDelay(500, 1400, "transição email→senha");

            // Localiza campo de senha (apenas elemento visível e habilitado)
            var passwordField = driver.FindElements(By.CssSelector("input[type='password'], input[autocomplete*='current-password'], #password, input[name='session_password']"))
                .FirstOrDefault(e => e.Displayed && e.Enabled);

            if (passwordField == null)
            {
                Console.WriteLine("⚠️ [C#] Campo de senha não localizado automaticamente. Aguardando login manual...");
                WaitForManualLoginIfNeeded(driver, TimeSpan.FromMinutes(3));
                return IsLoggedIn(driver);
            }

            // Digitação humana da senha
            HumanType(driver, passwordField, linkedinPassword);
            SleepRandomDelay(800, 1800, "pausa antes de submeter");

            // Submete preferencialmente pelo botão de submit do formulário (evitando "Entrar com Microsoft/Apple")
            bool submitted = false;
            string[] btnXPaths = {
                "//button[@type='submit']",
                "//button[@data-litms-control-urn='login-submit']",
                "//button[normalize-space(.)='Entrar']",
                "//button[normalize-space(.)='Sign in']",
                "//button[@componentkey='129064cc-ea63-408d-9ff4-db6f8056405b']"
            };
            foreach (var xpath in btnXPaths)
            {
                try
                {
                    var btn = driver.FindElement(By.XPath(xpath));
                    if (btn.Displayed && btn.Enabled)
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", btn);
                        SleepRandomDelay(300, 700);
                        btn.Click();
                        submitted = true;
                        break;
                    }
                }
                catch { }
            }

            // Se nenhum botão aceitou clique, fallback para Enter
            if (!submitted)
            {
                passwordField.SendKeys(Keys.Enter);
            }

            // Aguarda redirecionamento inicial (login OK ou desafio/checkpoint)
            try
            {
                wait.Timeout = TimeSpan.FromSeconds(20);
                wait.Until(d => IsLoggedIn(d) || d.Url.Contains("checkpoint") || d.Url.Contains("challenge"));
            }
            catch { }

            if (!IsLoggedIn(driver))
            {
                WaitForManualLoginIfNeeded(driver, TimeSpan.FromMinutes(3));
            }

            // Pausa humana pós-login (humanos não navegam imediatamente)
            if (IsLoggedIn(driver))
            {
                SleepRandomDelay(2500, 5000, "estabilização pós-login");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[C#] Falha no processo de autenticação: {ex.Message}");
            return false;
        }
    }

    private static void WaitForManualLoginIfNeeded(IWebDriver driver, TimeSpan timeout)
    {
        Console.WriteLine($"⏳ [C#] Aguardando autenticação manual / 2FA no navegador (até {(int)timeout.TotalMinutes} min)...");
        Console.WriteLine("👉 Conclua o login na janela do Chrome (pode usar 'Continuar com o Google' ou código 2FA). A sessão ficará salva para os próximos ciclos.");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (driver.WindowHandles.Count == 0) return;
                if (IsLoggedIn(driver))
                {
                    Console.WriteLine("✅ [C#] Login manual detectado! Prosseguindo...");
                    return;
                }
            }
            catch
            {
                return;
            }

            Thread.Sleep(2000);
        }

        Console.WriteLine("⌛ [C#] Tempo limite de autenticação manual atingido.");
    }

    private static void ClickElementRobust(IWebDriver driver, IWebElement element)
    {
        TryScrollElementIntoViewHumanized(driver, element);
        PauseBeforeClick();

        try
        {
            element.Click();
        }
        catch
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", element);
        }
    }

    private static string? GetInstalledChromeMajorVersion()
    {
        try
        {
            var candidates = new[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            };

            foreach (var chromePath in candidates)
            {
                if (!File.Exists(chromePath)) continue;
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(chromePath);
                if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                {
                    var major = info.ProductVersion.Split('.')[0];
                    if (int.TryParse(major, out _)) return major;
                }
            }
        }
        catch { }
        return null;
    }

    private static void CleanupOrphanedBrowserProcesses(bool forceKillChrome = false)
    {
        try
        {
            // 1. Encerra processos chromedriver órfãos de ciclos anteriores
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("chromedriver"))
            {
                try { proc.Kill(); proc.WaitForExit(1000); } catch { }
            }

            // 2. Remove lockfile residual do diretório de profile do Chrome
            var profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebCrawler", "ChromeProfile");
            var lockFilePath = Path.Combine(profileDir, "lockfile");
            if (File.Exists(lockFilePath))
            {
                try { File.Delete(lockFilePath); } catch { }
            }

            if (forceKillChrome)
            {
                try
                {
                    var defaultLock = Path.Combine(profileDir, "Default", "LOCK");
                    if (File.Exists(defaultLock)) File.Delete(defaultLock);
                }
                catch { }
            }
        }
        catch { }
    }

    private static IWebDriver CreateRobustChromeDriver(ChromeDriverService service, ChromeOptions options, TimeSpan timeout)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                CleanupOrphanedBrowserProcesses();
                return new ChromeDriver(service, options, timeout);
            }
            catch (Exception ex) when (attempt == 1 && (ex.Message.Contains("session not created") || ex.Message.Contains("Chrome instance exited")))
            {
                Console.WriteLine($"[AVISO] Inicialização do Chrome falhou na primeira tentativa. Limpando processos órfãos e tentando novamente...");
                CleanupOrphanedBrowserProcesses(forceKillChrome: true);
                Thread.Sleep(2000);
            }
        }

        return new ChromeDriver(service, options, timeout);
    }

    private static ChromeOptions BuildChromeOptions(bool headless, bool usePersistentProfile = true)
    {
        var options = new ChromeOptions();

        // Anti-detecção (sinais clássicos do Selenium)
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalChromeOption("useAutomationExtension", false);
        options.AddArgument("--log-level=3");
        options.AddArgument("--disable-logging");

        options.AddArgument("--disable-infobars");
        options.AddArgument("--disable-notifications");
        options.AddArgument("--disable-popup-blocking");
        options.AddArgument("--disable-save-password-bubble");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--no-first-run");
        options.AddArgument("--password-store=basic");
        options.AddArgument("--lang=pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
        options.AddArgument("--accept-lang=pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");

        if (headless)
        {
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
        }

        // Resoluções reais comuns (evita combinações suspeitas)
        var commonResolutions = new (int w, int h)[]
        {
            (1920, 1080), (1536, 864), (1440, 900), (1600, 900), (1366, 768)
        };
        var pick = commonResolutions[new Random().Next(commonResolutions.Length)];
        options.AddArgument($"--window-size={pick.w},{pick.h}");
        options.AddArgument($"--window-position={new Random().Next(0, 100)},{new Random().Next(0, 80)}");

        // User-Agent real alinhado à versão instalada do Chrome
        var chromeMajorVersion = GetInstalledChromeMajorVersion() ?? "157";
        options.AddArgument($"--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromeMajorVersion}.0.0.0 Safari/537.36");

        if (usePersistentProfile)
        {
            // Perfil persistente para evitar 2FA toda vez
            var profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebCrawler", "ChromeProfile");
            Directory.CreateDirectory(profileDir);
            options.AddArgument($"--user-data-dir={profileDir}");
            options.AddArgument("--profile-directory=Default");
        }

        // Preferências para parecer um Chrome de usuário (não-automation)
        options.AddUserProfilePreference("credentials_enable_service", false);
        options.AddUserProfilePreference("profile.password_manager_enabled", false);
        options.AddUserProfilePreference("intl.accept_languages", "pt-BR,pt,en-US,en");

        return options;
    }

    /// <summary>
    /// Injeta scripts stealth via Chrome DevTools Protocol ANTES de qualquer página carregar.
    /// Mascara sinais que o LinkedIn usa para detectar Selenium/automação.
    /// Deve ser chamado IMEDIATAMENTE após criar o ChromeDriver, antes de qualquer Navigate().
    /// </summary>
    private static void ApplyStealthPatches(IWebDriver driver)
    {
        try
        {
            var stealthScript = @"
                // 1. Remover navigator.webdriver
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });

                // 2. Plugins falsos (Chrome real tem plugins)
                Object.defineProperty(navigator, 'plugins', {
                    get: () => {
                        return [
                            { name: 'PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                            { name: 'Chrome PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                            { name: 'Chromium PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                            { name: 'Microsoft Edge PDF Viewer', description: 'Portable Document Format', filename: 'internal-pdf-viewer' },
                            { name: 'WebKit built-in PDF', description: 'Portable Document Format', filename: 'internal-pdf-viewer' }
                        ];
                    }
                });

                // 3. Idiomas reais
                Object.defineProperty(navigator, 'languages', { get: () => ['pt-BR', 'pt', 'en-US', 'en'] });

                // 4. Mascarar permissions.query (detector clássico)
                const originalQuery = window.navigator.permissions && window.navigator.permissions.query;
                if (originalQuery) {
                    window.navigator.permissions.query = (parameters) => (
                        parameters.name === 'notifications'
                            ? Promise.resolve({ state: Notification.permission })
                            : originalQuery(parameters)
                    );
                }

                // 5. Objeto chrome (Chrome real tem window.chrome populado)
                if (!window.chrome) { window.chrome = {}; }
                window.chrome.runtime = window.chrome.runtime || {};
                window.chrome.app = window.chrome.app || { isInstalled: false, InstallState: { DISABLED: 'disabled', INSTALLED: 'installed', NOT_INSTALLED: 'not_installed' }, RunningState: { CANNOT_RUN: 'cannot_run', READY_TO_RUN: 'ready_to_run', RUNNING: 'running' } };
                window.chrome.csi = window.chrome.csi || function() {};
                window.chrome.loadTimes = window.chrome.loadTimes || function() {};

                // 6. WebGL Vendor/Renderer (Selenium vaza 'SwiftShader')
                const getParameter = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(parameter) {
                    if (parameter === 37445) { return 'Intel Inc.'; }
                    if (parameter === 37446) { return 'Intel Iris OpenGL Engine'; }
                    return getParameter.apply(this, [parameter]);
                };

                // 7. Hardware Concurrency / Memory plausíveis
                Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 8 });
                Object.defineProperty(navigator, 'deviceMemory', { get: () => 8 });

                // 8. Platform consistente com UA
                Object.defineProperty(navigator, 'platform', { get: () => 'Win32' });

                // 9. Esconder propriedades CDC do ChromeDriver (cdc_adoQpoasnfa76pfcZLmcfl_*)
                for (const key of Object.keys(window)) {
                    if (key.startsWith('cdc_') || key.startsWith('$cdc_')) {
                        try { delete window[key]; } catch (e) {}
                    }
                }

                // 10. Mascarar 'outerHeight === 0' / 'outerWidth === 0' do headless
                if (window.outerHeight === 0) {
                    Object.defineProperty(window, 'outerHeight', { get: () => window.innerHeight });
                    Object.defineProperty(window, 'outerWidth', { get: () => window.innerWidth });
                }
            ";

            // Tenta via CDP (preferido — roda ANTES da página carregar)
            try
            {
                var chromeDriver = driver as ChromeDriver;
                if (chromeDriver != null)
                {
                    var cmdParams = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "source", stealthScript }
                    };
                    chromeDriver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", cmdParams);
                    Console.WriteLine("🛡️ [STEALTH] Patches injetados via CDP (executam antes de cada navegação).");
                    return;
                }
            }
            catch (Exception cdpEx)
            {
                Console.WriteLine($"⚠️ [STEALTH] CDP falhou ({cdpEx.Message}), aplicando fallback runtime.");
            }

            // Fallback: aplica no contexto atual (não pega navegações futuras)
            ((IJavaScriptExecutor)driver).ExecuteScript(stealthScript);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [STEALTH] Falha ao aplicar patches: {ex.Message}");
        }
    }
}
