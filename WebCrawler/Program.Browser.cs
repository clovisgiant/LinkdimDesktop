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
                if (hasSessionCookie && !isAuthRoute)
                {
                    return true;
                }
            }
            catch
            {
                // Ignora erro de leitura de cookie.
            }

            if (url.Contains("/feed", StringComparison.OrdinalIgnoreCase))
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
        Console.WriteLine("Abrindo LinkedIn...");
        driver.Navigate().GoToUrl(LinkedInFeedUrl);

        var pageTitle = driver.Title;
        Console.WriteLine("Título da página: " + pageTitle);

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

        IWebElement loginButton;
        try
        {
            loginButton = driver.FindElement(By.XPath("//button[@type='submit']"));
        }
        catch
        {
            loginButton = driver.FindElement(By.CssSelector("button[data-litms-control-urn]"));
        }
        Console.WriteLine("Botão de login encontrado. Clicando...");
        loginButton.Click();
        Console.WriteLine("Login enviado, aguardando redirecionamento...");

        try
        {
            wait.Until(d =>
            {
                var url = d.Url ?? string.Empty;
                if (url.Contains("feed") || url.Contains("linkedin.com/in"))
                {
                    return "logged_in";
                }

                return null;
            });

            return true;
        }
        catch (WebDriverTimeoutException)
        {
            Console.WriteLine("Timeout no redirecionamento de login. Pode haver lentidão.");
            var diagnostics = SaveFailureDiagnostics(driver, LinkedInLoginUrl, "login_redirect_timeout");
            LogApplicationStep(LinkedInLoginUrl, "login_redirect_timeout", false, "Timeout aguardando redirecionamento após submit de login.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
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
