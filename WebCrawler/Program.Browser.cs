using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.IO;
using System.Linq;
using System.Threading;

partial class Program
{
    private static bool IsLoggedIn(IWebDriver driver)
    {
        try
        {
            var url = driver.Url ?? string.Empty;

            try
            {
                var liAt = driver.Manage().Cookies.GetCookieNamed("li_at");
                var hasSessionCookie = liAt != null && !string.IsNullOrWhiteSpace(liAt.Value);
                var lowerUrl = url.ToLowerInvariant();
                var isAuthRoute = lowerUrl.Contains("/login") || lowerUrl.Contains("/checkpoint") || lowerUrl.Contains("/challenge");
                
                // Check explícito para telas de bloqueio/bot detection
                var pageSource = (driver.PageSource ?? "").ToLowerInvariant();
                bool isBlocked = pageSource.Contains("security check") || 
                                 pageSource.Contains("verificação de segurança") || 
                                 pageSource.Contains("unusual activity");

                if (hasSessionCookie && !isAuthRoute && !isBlocked)
                {
                    return true;
                }
                
                if (isBlocked) {
                    Console.WriteLine("⚠️ [ALERTA] LinkedIn detectou atividade incomum (Bot Detection/CAPTCHA).");
                    return false;
                }
            }
            catch
            {
                // Ignora erro de leitura de cookie.
            }

            if (url.Contains("/feed", StringComparison.OrdinalIgnoreCase) || url.Contains("/jobs", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var navCandidates = driver.FindElements(By.CssSelector(
                "header.global-nav, nav.global-nav__nav, [data-test-global-nav-link], a[href*='/feed/']"));
            return navCandidates.Any(e =>
            {
                try { return e.Displayed; } catch { return false; }
            });
        }
        catch
        {
            return false;
        }
    }

    private static ChromeOptions BuildChromeOptions(bool usePersistentProfile)
    {
        var options = new ChromeOptions();

        // No Render (Linux), precisamos de flags específicas para rodar em container
        var isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
        
        if (isLinux)
        {
            options.AddArgument("--headless=new");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-gpu");
            
            // --- OTIMIZAÇÕES EXTREMAS DE MEMÓRIA PARA PLANO FREE ---
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-setuid-sandbox");
            options.AddArgument("--no-first-run");
            options.AddArgument("--no-default-browser-check");
            options.AddArgument("--disable-software-rasterizer");
            options.AddArgument("--no-zygote"); // Mata o processo zygote para economizar RAM
            options.AddArgument("--disable-breakpad"); // Desativa crash reporter
            options.AddArgument("--disable-dev-tools"); // Desativa ferramentas de dev
            options.AddArgument("--log-level=3"); // Silencia logs do Chrome
            options.AddArgument("--js-flags=\"--max-old-space-size=96\""); // Limita JS a 96MB
            options.AddArgument("--memory-pressure-thresholds=1,2");
            options.AddArgument("--disable-background-networking");
            options.AddArgument("--disable-sync");
            options.AddArgument("--disable-print-preview");
            options.AddArgument("--disable-speech-api");
            options.AddArgument("--disable-media-session-api");
            options.AddArgument("--disable-features=TranslateUI,BlinkGenPropertyTrees,SpellCheck,AudioServiceOutOfProcess,VisualSearchResults");
            
            // --- BLOQUEIO DE IMAGENS E PESO ---
            options.AddUserProfilePreference("profile.managed_default_content_settings.images", 2); 
            options.AddArgument("--disable-webgl");
            options.AddArgument("--disable-3d-apis");
            options.AddArgument("--blink-settings=imagesEnabled=false");
            
            // User-Agent estável (Chrome 119) em vez de versões experimentais
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36");
            
            // Tenta localizar o binário do Chromium no Render
            var chromePath = Environment.GetEnvironmentVariable("CHROMIUM_PATH") ?? "/usr/bin/chromium";
            if (File.Exists(chromePath))
            {
                options.BinaryLocation = chromePath;
            }
        }
        else if (GetOptionalBoolEnv("WEBCRAWLER_HEADLESS_LOCAL", false))
        {
            options.AddArgument("--headless=new");
        }

        if (usePersistentProfile)
        {
            var profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WebCrawler",
                "ChromeProfile");
            Directory.CreateDirectory(profileDir);
            options.AddArgument($"--user-data-dir={profileDir}");
            options.AddArgument("--profile-directory=Default");
        }

        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddArgument("--window-size=1920,1080");
        return options;
    }

    private static bool EnsureAuthenticatedSession(IWebDriver driver, WebDriverWait wait, string linkedinUsername, string linkedinPassword)
    {
        Console.WriteLine("[C#] Iniciando validação de sessão (estratégia anti-loop)...");
        
        // 1. Vai para a home primeiro para estabelecer o domínio
        driver.Navigate().GoToUrl("https://www.linkedin.com");
        Thread.Sleep(3000);
        
        var sessionCookie = Environment.GetEnvironmentVariable("LINKEDIN_SESSION_COOKIE");
        if (!string.IsNullOrEmpty(sessionCookie))
        {
            Console.WriteLine("🍪 [C#] Injetando Cookie de Sessão...");
            driver.Manage().Cookies.DeleteAllCookies();
            Thread.Sleep(1000);
            
            driver.Manage().Cookies.AddCookie(new OpenQA.Selenium.Cookie("li_at", sessionCookie, ".linkedin.com", "/", DateTime.Now.AddDays(30)));
            
            // 2. Tenta ir para o FEED primeiro (é mais leve que /jobs para validar login)
            Console.WriteLine("🚀 [C#] Validando sessão via /feed...");
            driver.Navigate().GoToUrl("https://www.linkedin.com/feed/");
            Thread.Sleep(5000);

            if (IsLoggedIn(driver))
            {
                Console.WriteLine("✅ [C#] Login confirmado via /feed! Indo para vagas...");
                driver.Navigate().GoToUrl("https://www.linkedin.com/jobs/");
                Thread.Sleep(4000);
                return true;
            }
            else
            {
                Console.WriteLine("⚠️ [C#] Cookie injetado, mas o LinkedIn não reconheceu a sessão (ou pediu CAPTCHA). Tentando login normal...");
            }
        }

        if (!IsLoggedIn(driver))
        {
            Console.WriteLine("Abrindo tela de login...");
            driver.Navigate().GoToUrl(LinkedInLoginUrl);
        }

        if (IsLoggedIn(driver))
        {
            Console.WriteLine("Sessão já autenticada detectada. Pulando preenchimento de login.");
            return true;
        }

        Console.WriteLine("Preenchendo credenciais...");
        
        // Função auxiliar para preenchimento robusto
        Action<IWebElement, string> safeType = (element, text) => {
            try {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", element);
                Thread.Sleep(500);
                element.Click();
                element.Clear();
                element.SendKeys(text);
            } catch {
                // Fallback via JavaScript se a interação normal falhar
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].value = arguments[1];", element, text);
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].dispatchEvent(new Event('input', { bubbles: true }));", element);
            }
        };

        IWebElement emailField;
        try { emailField = wait.Until(d => d.FindElement(By.Id("username"))); }
        catch { emailField = wait.Until(d => d.FindElement(By.CssSelector("input[type='email']"))); }
        
        safeType(emailField, linkedinUsername);
        Console.WriteLine("Email preenchido.");
        Thread.Sleep(500);

        IWebElement passwordField;
        try { passwordField = driver.FindElement(By.Id("password")); }
        catch { passwordField = driver.FindElement(By.CssSelector("input[type='password']")); }
        
        safeType(passwordField, linkedinPassword);
        Console.WriteLine("Senha preenchida.");
        Thread.Sleep(500);

        // --- TENTATIVA 1: Tecla ENTER (Plano B Infalível) ---
        try
        {
            Console.WriteLine("Tentando enviar formulário via tecla ENTER...");
            passwordField.SendKeys(OpenQA.Selenium.Keys.Enter);
            Thread.Sleep(2000);
        }
        catch { /* Segue para o botão se falhar */ }

        // Se ainda estiver na página de login, tenta o botão
        if (driver.Url.Contains("login"))
        {
            IWebElement loginButton = null;
            string[] buttonSelectors = { 
                "//button[@type='submit']", 
                "#login-submit", 
                "button[type='submit']",
                ".login__form_action_container button",
                "//button[contains(., 'Entrar')]",
                "//button[contains(., 'Sign in')]",
                "//div[@role='button'][contains(., 'Entrar')]"
            };

            foreach (var selector in buttonSelectors)
            {
                try
                {
                    if (selector.StartsWith("//"))
                        loginButton = driver.FindElement(By.XPath(selector));
                    else if (selector.StartsWith("#"))
                        loginButton = driver.FindElement(By.Id(selector.Substring(1)));
                    else
                        loginButton = driver.FindElement(By.CssSelector(selector));
                    
                    if (loginButton != null && loginButton.Displayed) break;
                }
                catch { }
            }

            if (loginButton != null)
            {
                Console.WriteLine("Botão de login encontrado. Clicando...");
                try { loginButton.Click(); }
                catch { ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", loginButton); }
            }
        }
        
        Console.WriteLine("Login enviado, aguardando redirecionamento...");

        try
        {
            wait.Timeout = TimeSpan.FromSeconds(30); // Mais paciência para o Render
            wait.Until(d =>
            {
                var url = d.Url?.ToLower() ?? string.Empty;
                
                // Sucesso
                if (url.Contains("feed") || url.Contains("linkedin.com/in")) return "logged_in";
                
                // Desafios de Segurança (2FA, Captcha, Checkpoint)
                if (url.Contains("checkpoint") || url.Contains("challenge") || url.Contains("captcha") || url.Contains("verify"))
                {
                    Console.WriteLine("⚠️ [C#] ALERTA: LinkedIn solicitou Verificação de Segurança (Captcha/2FA).");
                    return "security_challenge";
                }

                return null;
            });

            return driver.Url.Contains("feed") || driver.Url.Contains("linkedin.com/in");
        }
        catch (WebDriverTimeoutException)
        {
            Console.WriteLine("Timeout no redirecionamento de login. Pode haver lentidão ou bloqueio.");
            SaveFailureDiagnostics(driver, LinkedInLoginUrl, "login_redirect_timeout");
            return false;
        }
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

    private static void WaitForJobPageReady(IWebDriver driver)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));
        wait.Until(d =>
        {
            try
            {
                var state = ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString();
                return state == "complete";
            }
            catch
            {
                return false;
            }
        });

        SleepRandomDelay(900, 1800);
    }
}
