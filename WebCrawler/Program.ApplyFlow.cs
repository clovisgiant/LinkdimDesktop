using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

partial class Program
{
    // Tenta selecionar um currículo no popup de candidatura (quando a etapa existir).
    private static void TrySelectResumeInPopup(IWebDriver driver)
    {
        bool IsResumeSelected()
        {
            try
            {
                var selectedInputs = driver.FindElements(By.CssSelector(
                    "input[type='radio'][name*='resume']:checked, " +
                    "input[type='radio'][id*='resume']:checked, " +
                    "input[id*='jobsDocumentCardToggle']:checked, " +
                    "input[name*='jobsDocumentCardToggle']:checked, " +
                    "input[type='radio'][aria-label*='.pdf']:checked, " +
                    "input[type='radio'][checked][aria-label*='.pdf']"));

                if (selectedInputs.Count > 0)
                {
                    return true;
                }

                var selectedLabels = driver.FindElements(By.CssSelector(
                    "label.jobs-document-upload-redesign-card__toggle-label[aria-checked='true'], " +
                    "label.jobs-document-upload-redesign-card__toggle-label[aria-pressed='true'], " +
                    "label.jobs-document-upload-redesign-card__toggle-label.is-selected"));

                return selectedLabels.Any(label =>
                {
                    try { return label.Displayed; } catch { return false; }
                });
            }
            catch
            {
                return false;
            }
        }

        try
        {
            // Suporte tanto para layout clássico quanto para SDUI (onde input[type='radio'] tem opacity:0)
            if (driver is IJavaScriptExecutor jsResume)
            {
                var firstNameResumeToken = (AutoFillDefaultFirstName ?? string.Empty).Trim().ToLowerInvariant();
                var sduiResumeResult = jsResume.ExecuteScript(@"
                    const firstNameToken = arguments[0] || '';
                    const modals = Array.from(document.querySelectorAll('dialog[data-testid=""dialog""][open], dialog[open], [data-testid=""dialog""], div.jobs-easy-apply-modal[role=""dialog""], div.jobs-easy-apply-modal, div[data-sdui-screen*=""easyapply""], div[role=""dialog""]'));
                    const modal = modals.find(m => {
                        const s = window.getComputedStyle(m);
                        const r = m.getBoundingClientRect();
                        return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0 && m.getAttribute('aria-hidden') !== 'true';
                    });
                    if (!modal) return 'no_modal';

                    const radios = Array.from(modal.querySelectorAll('input[type=""radio""]')).filter(r => {
                        const a = (r.getAttribute('aria-label') || '').toLowerCase();
                        const id = (r.id || '').toLowerCase();
                        const nm = (r.name || '').toLowerCase();
                        return a.includes('.pdf') || a.includes('.doc') || id.includes('resume') || nm.includes('resume') || id.includes('jobsdocumentcard');
                    });

                    if (radios.length === 0) return 'no_resume_radios';
                    if (radios.some(r => r.checked)) return 'already_selected';

                    const preferred = (firstNameToken ? radios.find(r => (r.getAttribute('aria-label') || '').toLowerCase().includes(firstNameToken)) : null) || radios[0];
                    const lbl = preferred.id ? modal.querySelector('label[for=""' + CSS.escape(preferred.id) + '""]') : null;
                    (lbl || preferred).scrollIntoView({ block: 'center' });
                    if (lbl) { try { lbl.click(); } catch {} }
                    if (!preferred.checked) { try { preferred.click(); } catch {} }
                    return preferred.checked ? 'selected_via_js' : 'click_attempted';
                ", firstNameResumeToken)?.ToString() ?? string.Empty;

                if (sduiResumeResult == "already_selected")
                {
                    Console.WriteLine("Currículo já selecionado no popup.");
                    return;
                }
                if (sduiResumeResult == "selected_via_js")
                {
                    Console.WriteLine("Currículo selecionado no popup (SDUI/JS).");
                    Thread.Sleep(300);
                    return;
                }
            }

            // Se já existir um currículo selecionado, mantém como está.
            if (IsResumeSelected())
            {
                Console.WriteLine("Currículo já selecionado no popup.");
                return;
            }

            // Prioriza o label do cartão de documento (jobsDocumentCardToggleLabel-*) quando existir.
            var documentToggleLabels = driver.FindElements(By.CssSelector(
                "label.jobs-document-upload-redesign-card__toggle-label[for*='jobsDocumentCardToggle'], " +
                "label[id*='jobsDocumentCardToggleLabel']"));

            foreach (var label in documentToggleLabels)
            {
                try
                {
                    if (!label.Displayed)
                    {
                        continue;
                    }

                    var labelText = label.Text?.Trim() ?? string.Empty;
                    var a11yText = label.FindElements(By.CssSelector(".a11y-text")).FirstOrDefault()?.Text?.Trim() ?? string.Empty;
                    var combined = (labelText + " " + a11yText).ToLowerInvariant();

                    if (!combined.Contains("selecionar") &&
                        !combined.Contains("resume") &&
                        !combined.Contains("currículo") &&
                        !combined.Contains(".pdf"))
                    {
                        continue;
                    }

                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", label);
                    Thread.Sleep(250);
                    ClickElementRobust(driver, label);
                    Thread.Sleep(250);

                    var targetInputId = label.GetAttribute("for")?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(targetInputId))
                    {
                        var relatedInput = driver.FindElements(By.Id(targetInputId)).FirstOrDefault();
                        if (relatedInput != null)
                        {
                            try
                            {
                                if (!relatedInput.Selected)
                                {
                                    ClickElementRobust(driver, relatedInput);
                                    Thread.Sleep(200);
                                }
                            }
                            catch
                            {
                                // Segue com validação final.
                            }
                        }
                    }

                    if (IsResumeSelected())
                    {
                        Console.WriteLine("Currículo selecionado no popup (jobsDocumentCardToggleLabel). ");
                        Thread.Sleep(350);
                        return;
                    }
                }
                catch
                {
                    // Tenta próximo label.
                }
            }

            // Tenta localizar opções de currículo por seletores comuns da tela Easy Apply.
            var resumeCandidates = driver.FindElements(By.CssSelector(
                "label[for*='resume'], label.jobs-document-upload-redesign-card__toggle-label, input[type='radio'][name*='resume'], input[id*='jobsDocumentCardToggle'], [data-test-document-upload-item], div.jobs-document-upload-redesign-card__container, input[type='radio'][aria-label*='.pdf']"));

            if (resumeCandidates.Count == 0)
            {
                Console.WriteLine("Nenhuma opção de currículo encontrada nesta etapa (pode não ser necessária). ");
                return;
            }

            foreach (var resumeOption in resumeCandidates)
            {
                try
                {
                    if (!resumeOption.Displayed)
                    {
                        continue;
                    }

                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", resumeOption);
                    Thread.Sleep(200);
                    ClickElementRobust(driver, resumeOption);
                    Thread.Sleep(250);

                    var targetInputId = resumeOption.GetAttribute("for")?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(targetInputId))
                    {
                        var relatedInput = driver.FindElements(By.Id(targetInputId)).FirstOrDefault();
                        if (relatedInput != null)
                        {
                            try
                            {
                                if (!relatedInput.Selected)
                                {
                                    ClickElementRobust(driver, relatedInput);
                                    Thread.Sleep(200);
                                }
                            }
                            catch
                            {
                                // Segue com validação final.
                            }
                        }
                    }

                    if (IsResumeSelected())
                    {
                        Console.WriteLine("Currículo selecionado no popup.");
                        Thread.Sleep(350);
                        return;
                    }
                }
                catch
                {
                    // Tenta próximo candidato.
                }
            }

            Console.WriteLine("Não foi possível confirmar a seleção do currículo nesta etapa.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Não foi possível selecionar currículo automaticamente: {ex.Message}");
        }
    }

    // Captura uma assinatura leve da etapa atual do modal Easy Apply para validar avanço real após clique.
    private static string CaptureApplyStepSignature(IWebDriver driver)
    {
        try
        {
            var js = (IJavaScriptExecutor)driver;
            var result = js.ExecuteScript(@"
                const modals = Array.from(document.querySelectorAll('dialog[data-testid=""dialog""][open], dialog[open], [data-testid=""dialog""], div.jobs-easy-apply-modal[role=""dialog""], div.jobs-easy-apply-modal, div[data-sdui-screen*=""easyapply""], div[data-testid=""dialog-content""], div[role=""dialog""], dialog'));
                const modal = modals.find(m => {
                    if (!m) return false;
                    const style = window.getComputedStyle(m);
                    const rect = m.getBoundingClientRect();
                    const ariaHidden = (m.getAttribute('aria-hidden') || '').toLowerCase();
                    return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0 && ariaHidden !== 'true';
                });

                if (!modal) {
                    return 'modal:closed';
                }

                const titleEl = modal.querySelector('h1, h2, h3, [id*=""easy-apply""], .jobs-easy-apply-content__title');
                const progressEl = modal.querySelector('[aria-valuenow], [aria-valuemin], [aria-valuemax], .artdeco-completeness-meter-linear__progress-element, .jobs-easy-apply-content__step');

                const footerButtons = Array.from(modal.querySelectorAll('footer button, button.artdeco-button--primary, button'));
                const visibleEnabledButtons = footerButtons.filter(btn => {
                    const s = window.getComputedStyle(btn);
                    const r = btn.getBoundingClientRect();
                    const visible = s.display !== 'none' && s.visibility !== 'hidden' && r.width > 0 && r.height > 0;
                    const enabled = !btn.disabled && btn.getAttribute('aria-disabled') !== 'true';
                    return visible && enabled;
                });

                const buttonSignature = visibleEnabledButtons
                    .slice(0, 3)
                    .map(btn => ((btn.getAttribute('aria-label') || '') + '|' + (btn.innerText || '').trim()))
                    .join('||');

                const contentRoot = modal.querySelector('.jobs-easy-apply-content, form, .jobs-easy-apply-content__content') || modal;
                const contentClone = contentRoot.cloneNode(true);
                contentClone.querySelectorAll('footer, button, script, style').forEach(node => node.remove());
                const contentSignature = (contentClone.innerText || contentClone.textContent || '')
                    .replace(/\s+/g, ' ')
                    .trim()
                    .toLowerCase()
                    .slice(0, 260);

                const title = (titleEl?.textContent || '').trim();
                const progressNow = progressEl?.getAttribute('aria-valuenow') || '';
                const progressText = (progressEl?.textContent || '').trim();

                return `modal:open|title:${title}|progress:${progressNow}|progressText:${progressText}|buttons:${buttonSignature}|content:${contentSignature}`;
            ");

            return result?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    // Confirma que o clique em "Avançar" realmente moveu o fluxo para a próxima etapa.
    private static bool ConfirmNextStepProgress(IWebDriver driver, string link, string stepName, string beforeSignature)
    {
        try
        {
            var progressWait = new WebDriverWait(driver, TimeSpan.FromSeconds(8));
            var progressed = progressWait.Until(d =>
            {
                try
                {
                    var modal = TryFindActiveEasyApplyModal(d);
                    ISearchContext scope = modal != null ? (ISearchContext)modal : d;

                    var submitVisible = scope.FindElements(By.CssSelector("button[data-live-test-easy-apply-submit-button], button[data-easy-apply-submit-button], button[aria-label*='Enviar'], button[aria-label*='Submit']"))
                        .Concat(scope.FindElements(By.XPath(".//button[contains(normalize-space(.),'Enviar candidatura') or contains(normalize-space(.),'Submit application') or normalize-space(.)='Enviar' or normalize-space(.)='Submit']")))
                        .Any(btn =>
                        {
                            try { return btn.Displayed; } catch { return false; }
                        });
                    if (submitVisible)
                    {
                        return true;
                    }

                    var currentSignature = CaptureApplyStepSignature(d);
                    if (string.Equals(currentSignature, "modal:closed", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(beforeSignature) && !string.IsNullOrWhiteSpace(currentSignature))
                    {
                        return !string.Equals(beforeSignature, currentSignature, StringComparison.Ordinal);
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            });

            if (progressed)
            {
                return true;
            }
        }
        catch (WebDriverTimeoutException)
        {
            // Trata abaixo com log e diagnóstico.
        }

        var afterSignature = CaptureApplyStepSignature(driver);
        Console.WriteLine($"[DEBUG NEXT] Clique sem progressão confirmada na etapa '{stepName}'. before='{beforeSignature}' after='{afterSignature}'");
        var diagnostics = SaveFailureDiagnostics(driver, link, $"next_clicked_but_not_progressed_{stepName}");
        LogApplicationStep(link, $"next_not_progressed_{stepName}", false, $"Clique em Avançar sem progressão confirmada. before='{beforeSignature}' after='{afterSignature}'", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
        return false;
    }

    // Localiza e clica no botão "Avançar" (ou "Avaliar"/"Revisar" quando for a transição final) ESTRITAMENTE dentro do modal ativo.
    private static bool TryClickNextButtonInPopup(IWebDriver driver, WebDriverWait wait, string stepName, string link, int autoFillRetryCount = 0)
    {
        IWebElement? nextButton = null;
        IWebElement? activeModal = TryFindActiveEasyApplyModal(driver);
        var beforeStepSignature = CaptureApplyStepSignature(driver);
        const string userProvidedNextXPath = "/html/body/div[1]/div[4]//div/div[1]/div/div/div[2]/div/div[2]/form/footer/div[2]/button";

        if (activeModal == null)
        {
            try
            {
                var quickModalWait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                activeModal = quickModalWait.Until(d => TryFindActiveEasyApplyModal(d));
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine($"Modal de candidatura não encontrado na etapa: {stepName}.");
            }
        }

        // Prioridade máxima: busca ESTRITAMENTE dentro do modal ativo (evita clicar em botões 'Avançar' de carrosséis do feed ao fundo!)
        if (activeModal != null)
        {
            try
            {
                var js = (IJavaScriptExecutor)driver;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    var clickedResult = js.ExecuteScript(@"
                        const modal = arguments[0];
                        if (!modal) return { clicked: false };

                        const isVisibleEnabled = (btn) => {
                            if (!btn) return false;
                            const style = window.getComputedStyle(btn);
                            const rect = btn.getBoundingClientRect();
                            const visible = style && style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                            const enabled = !btn.disabled && btn.getAttribute('aria-disabled') !== 'true';
                            return visible && enabled;
                        };

                        const isExcludedButton = (combined) => {
                            return combined.includes('voltar') ||
                                   combined.includes('back') ||
                                   combined.includes('fechar') ||
                                   combined.includes('close') ||
                                   combined.includes('descartar') ||
                                   combined.includes('discard') ||
                                   combined.includes('salvar') ||
                                   combined.includes('save') ||
                                   combined.includes('cancel');
                        };

                        // 1. Seletores explícitos de Next dentro do modal
                        const explicitNextSelectors = [
                            'footer button[data-easy-apply-next-button]',
                            'footer button[data-live-test-easy-apply-next-button]',
                            'button[data-easy-apply-next-button]',
                            'button[data-live-test-easy-apply-next-button]',
                            'footer button[aria-label*=""Avançar""]',
                            'footer button[aria-label*=""Próximo""]',
                            'footer button[aria-label*=""Next""]',
                            'footer button[aria-label*=""Continuar""]',
                            'footer button[aria-label*=""Continue""]'
                        ];

                        for (const selector of explicitNextSelectors) {
                            const candidates = Array.from(modal.querySelectorAll(selector));
                            for (const candidate of candidates) {
                                if (!isVisibleEnabled(candidate)) continue;
                                candidate.scrollIntoView({ block: 'center' });
                                candidate.click();
                                return {
                                    clicked: true,
                                    selector: selector,
                                    ariaLabel: candidate.getAttribute('aria-label') || '',
                                    text: (candidate.innerText || '').trim(),
                                    id: candidate.id || '',
                                    className: candidate.className || ''
                                };
                            }
                        }

                        // 2. Botões do modal (priorizando footer) cujo texto/aria seja Avançar / Next / Continuar / Revisar / Avaliar
                        const footerBtns = Array.from(modal.querySelectorAll('footer button'));
                        const otherBtns = Array.from(modal.querySelectorAll('button')).filter(b => !footerBtns.includes(b));
                        const orderedButtons = [...footerBtns, ...otherBtns];

                        const nextWords = ['avançar', 'avancar', 'próximo', 'proximo', 'continuar', 'prosseguir', 'next', 'continue'];
                        const reviewWords = ['revisar', 'avaliar', 'evaluar', 'review'];

                        for (const wordSet of [nextWords, reviewWords]) {
                            for (const candidate of orderedButtons) {
                                if (!isVisibleEnabled(candidate)) continue;
                                const btnText = (candidate.innerText || candidate.textContent || '').trim().toLowerCase();
                                const btnAria = (candidate.getAttribute('aria-label') || '').trim().toLowerCase();
                                const combined = (btnText + ' ' + btnAria).trim();
                                if (isExcludedButton(combined)) continue;
                                if (!wordSet.some(w => btnText === w || btnText.includes(w) || btnAria.includes(w))) continue;

                                candidate.scrollIntoView({ block: 'center' });
                                candidate.click();
                                return {
                                    clicked: true,
                                    selector: 'modal-scoped-button:' + btnText,
                                    ariaLabel: candidate.getAttribute('aria-label') || '',
                                    text: (candidate.innerText || '').trim(),
                                    id: candidate.id || '',
                                    className: candidate.className || ''
                                };
                            }
                        }

                        return { clicked: false };
                    ", activeModal);

                    if (clickedResult is IReadOnlyDictionary<string, object> clickedMap &&
                        clickedMap.TryGetValue("clicked", out var clickedValue) &&
                        clickedValue is bool ok && ok)
                    {
                        if (!ConfirmNextStepProgress(driver, link, stepName, beforeStepSignature))
                        {
                            if (autoFillRetryCount < 2)
                            {
                                TrySelectResumeInPopup(driver);
                                TryAutoFillMandatoryFieldsInModal(driver, link, $"next_not_progressed_{stepName}_{autoFillRetryCount}");
                                Thread.Sleep(450);
                                return TryClickNextButtonInPopup(driver, wait, stepName, link, autoFillRetryCount + 1);
                            }
                            return false;
                        }

                        var selectorUsed = clickedMap.TryGetValue("selector", out var selectorObj) ? selectorObj?.ToString() ?? string.Empty : string.Empty;
                        var ariaUsed = clickedMap.TryGetValue("ariaLabel", out var ariaObj) ? ariaObj?.ToString() ?? string.Empty : string.Empty;
                        var textUsed = clickedMap.TryGetValue("text", out var textObj) ? textObj?.ToString() ?? string.Empty : string.Empty;
                        var idUsed = clickedMap.TryGetValue("id", out var idObj) ? idObj?.ToString() ?? string.Empty : string.Empty;
                        var classUsed = clickedMap.TryGetValue("className", out var classObj) ? classObj?.ToString() ?? string.Empty : string.Empty;

                        Console.WriteLine($"Botão '{textUsed}' clicado com sucesso dentro do modal na etapa: {stepName}.");
                        Console.WriteLine($"[DEBUG NEXT] selector='{selectorUsed}', id='{idUsed}', class='{classUsed}', aria-label='{ariaUsed}', text='{textUsed}'");
                        LogApplicationStep(link, $"next_clicked_exact_{stepName}", true, $"Botão de avanço clicado dentro do modal ativo. selector='{selectorUsed}', text='{textUsed}'.");
                        return true;
                    }

                    Thread.Sleep(250);
                }

                // Se activeModal existe, NÃO cai em busca global para jamais clicar em elementos fora do modal!
                Console.WriteLine($"Nenhum botão de avanço habilitado encontrado dentro do modal na etapa: {stepName}.");
                return false;
            }
            catch
            {
                // Se falhar JS pontual, segue para fallback Selenium restrito ao activeModal abaixo.
            }
        }

        // Prioriza o botão exato informado, procurando somente dentro do modal ativo.
        try
        {
            var js = (IJavaScriptExecutor)driver;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var clickedResult = js.ExecuteScript(@"
                    const modal = arguments[0];
                    if (!modal) {
                        return null;
                    }

                    const selectors = [
                        'div.display-flex.justify-flex-end.ph5.pv4 > button[data-easy-apply-next-button][data-live-test-easy-apply-next-button]',
                        'div.display-flex.justify-flex-end.ph5.pv4 > button[data-easy-apply-next-button]',
                        'div.display-flex.justify-flex-end.ph5.pv4 > button[aria-label=""Avançar para próxima etapa""]',
                        'footer button[data-easy-apply-next-button][data-live-test-easy-apply-next-button]',
                        'footer button[data-live-test-easy-apply-next-button]',
                        'footer button[data-easy-apply-next-button]',
                        'button[data-easy-apply-next-button][data-live-test-easy-apply-next-button]',
                        'button[data-live-test-easy-apply-next-button]',
                        'button[data-easy-apply-next-button]',
                        'button[aria-label=""Avançar para próxima etapa""]',
                        'button[aria-label*=""Avançar""]',
                        'button[aria-label*=""Próximo""]',
                        'button[aria-label*=""Next""]',
                        'button[aria-label*=""Continue""]',
                        'button[aria-label*=""Continuar""]',
                        'footer button.artdeco-button--primary',
                        'button.artdeco-button--primary'
                    ];

                    for (const selector of selectors) {
                        const candidates = Array.from(modal.querySelectorAll(selector));
                        for (const candidate of candidates) {
                            const style = window.getComputedStyle(candidate);
                            const rect = candidate.getBoundingClientRect();
                            const visible = style && style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                            const enabled = !candidate.disabled && candidate.getAttribute('aria-disabled') !== 'true';

                            if (visible && enabled) {
                                candidate.scrollIntoView({ block: 'center' });
                                candidate.click();
                                return {
                                    clicked: true,
                                    selector: selector,
                                    ariaLabel: candidate.getAttribute('aria-label') || '',
                                    text: (candidate.innerText || '').trim(),
                                    id: candidate.id || '',
                                    className: candidate.className || ''
                                };
                            }
                        }
                    }

                    // Fallback SDUI dentro do modal por texto do botão (Avançar, Revisar, Avaliar, Next, Review, etc.)
                    const targetTexts = ['avançar', 'revisar', 'avaliar', 'evaluar', 'próximo', 'proximo', 'continuar', 'next', 'review', 'continue'];
                    const modalButtons = Array.from(modal.querySelectorAll('button'));
                    for (const candidate of modalButtons) {
                        const btnText = (candidate.innerText || '').trim().toLowerCase();
                        if (!targetTexts.some(t => btnText.includes(t))) continue;
                        const style = window.getComputedStyle(candidate);
                        const rect = candidate.getBoundingClientRect();
                        const visible = style && style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                        const enabled = !candidate.disabled && candidate.getAttribute('aria-disabled') !== 'true';
                        if (visible && enabled) {
                            candidate.scrollIntoView({ block: 'center' });
                            candidate.click();
                            return {
                                clicked: true,
                                selector: 'sdui-modal-button-text:' + btnText,
                                ariaLabel: candidate.getAttribute('aria-label') || '',
                                text: (candidate.innerText || '').trim(),
                                id: candidate.id || '',
                                className: candidate.className || ''
                            };
                        }
                    }

                    return {
                        clicked: false
                    };
                ", activeModal);

                if (clickedResult is IReadOnlyDictionary<string, object> clickedMap &&
                    clickedMap.TryGetValue("clicked", out var clickedValue) &&
                    clickedValue is bool ok && ok)
                {
                    if (!ConfirmNextStepProgress(driver, link, stepName, beforeStepSignature))
                    {
                        if (autoFillRetryCount < 1 && TryAutoFillMandatoryFieldsInModal(driver, link, $"next_not_progressed_{stepName}"))
                        {
                            Thread.Sleep(500);
                            return TryClickNextButtonInPopup(driver, wait, stepName, link, autoFillRetryCount + 1);
                        }
                        // Evita loop de cliques infinitos quando a etapa não progride por campos obrigatórios.
                        return false;
                    }

                    var selectorUsed = clickedMap.TryGetValue("selector", out var selectorObj) ? selectorObj?.ToString() ?? string.Empty : string.Empty;
                    var ariaUsed = clickedMap.TryGetValue("ariaLabel", out var ariaObj) ? ariaObj?.ToString() ?? string.Empty : string.Empty;
                    var textUsed = clickedMap.TryGetValue("text", out var textObj) ? textObj?.ToString() ?? string.Empty : string.Empty;
                    var idUsed = clickedMap.TryGetValue("id", out var idObj) ? idObj?.ToString() ?? string.Empty : string.Empty;
                    var classUsed = clickedMap.TryGetValue("className", out var classObj) ? classObj?.ToString() ?? string.Empty : string.Empty;

                    Console.WriteLine($"Botão 'Avançar' (seletor exato no modal ativo) clicado com sucesso na etapa: {stepName}.");
                    Console.WriteLine($"[DEBUG NEXT] selector='{selectorUsed}', id='{idUsed}', class='{classUsed}', aria-label='{ariaUsed}', text='{textUsed}'");
                    LogApplicationStep(link, $"next_clicked_exact_{stepName}", true, $"Botão exato de Avançar clicado dentro do modal ativo. selector='{selectorUsed}', id='{idUsed}', aria-label='{ariaUsed}', text='{textUsed}'.");
                    return true;
                }

                Thread.Sleep(300);
            }
        }
        catch
        {
            // Se falhar a estratégia exata por JS, segue para os fallbacks abaixo.
        }

        try
        {
            nextButton = wait.Until(d =>
            {
                ISearchContext searchRoot = activeModal != null ? (ISearchContext)activeModal : d;

                try
                {
                    var xpathCandidates = searchRoot.FindElements(By.XPath(".//button[@data-easy-apply-next-button and @data-live-test-easy-apply-next-button]"));
                    foreach (var candidate in xpathCandidates)
                    {
                        try
                        {
                            if (candidate.Displayed && candidate.Enabled)
                            {
                                return candidate;
                            }
                        }
                        catch
                        {
                            // Tenta próximo candidato.
                        }
                    }

                    var exactCandidates = searchRoot.FindElements(By.CssSelector(
                        "div.display-flex.justify-flex-end.ph5.pv4 > button[data-easy-apply-next-button][data-live-test-easy-apply-next-button], " +
                        "div.display-flex.justify-flex-end.ph5.pv4 > button[data-easy-apply-next-button], " +
                        "div.display-flex.justify-flex-end.ph5.pv4 > button[aria-label='Avançar para próxima etapa'], " +
                        "footer button[data-easy-apply-next-button], " +
                        "footer button[data-live-test-easy-apply-next-button], " +
                        "button[data-easy-apply-next-button], " +
                        "button[data-live-test-easy-apply-next-button], " +
                        "button[aria-label='Avançar para próxima etapa']"
                    ));

                    foreach (var candidate in exactCandidates)
                    {
                        try
                        {
                            if (candidate.Displayed && candidate.Enabled)
                            {
                                return candidate;
                            }
                        }
                        catch
                        {
                            // Tenta próximo candidato.
                        }
                    }

                    var candidates = searchRoot.FindElements(By.CssSelector(
                        "div.display-flex.justify-flex-end.ph5.pv4 > button, " +
                        "footer button[aria-label*='Avançar'], " +
                        "footer button[aria-label*='Próximo'], " +
                        "footer button[aria-label*='Next'], " +
                        "footer button[aria-label*='Continuar'], " +
                        "footer button[aria-label*='Continue'], " +
                        "button[aria-label*='Avançar'], " +
                        "button[aria-label*='Próximo'], " +
                        "button[aria-label*='Next'], " +
                        "button[aria-label*='Continuar'], " +
                        "button[aria-label*='Continue'], " +
                        "button.artdeco-button--primary"
                    ));

                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            var text = candidate.Text?.Trim() ?? string.Empty;
                            var ariaLabel = candidate.GetAttribute("aria-label")?.Trim() ?? string.Empty;
                            var combined = (text + " " + ariaLabel).ToLowerInvariant();

                            if (!candidate.Displayed || !candidate.Enabled)
                            {
                                continue;
                            }

                            if (combined.Contains("avançar") ||
                                combined.Contains("próximo") ||
                                combined.Contains("next") ||
                                combined.Contains("continuar") ||
                                combined.Contains("continue") ||
                                combined.Contains("prosseguir"))
                            {
                                return candidate;
                            }
                        }
                        catch
                        {
                            // Tenta próximo candidato.
                        }
                    }
                }
                catch
                {
                    // Aguarda próxima tentativa do wait.
                }

                return null;
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Fallback por texto/aria-label do botão Avançar/Next/Continuar (PT/EN).
            if (activeModal != null)
            {
                var nextCandidates = activeModal.FindElements(By.XPath(
                    ".//button[contains(@aria-label,'Avançar') or contains(@aria-label,'Next') or contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or " +
                    ".//span[contains(normalize-space(.),'Avançar')] or .//span[contains(normalize-space(.),'Next')] or " +
                    ".//span[contains(normalize-space(.),'Continuar')] or .//span[contains(normalize-space(.),'Continue')] or " +
                    ".//span[contains(normalize-space(.),'Prosseguir')]]"));

                if (nextCandidates.Count > 0)
                {
                    nextButton = nextCandidates[0];
                }
            }
            else
            {
                var userXPathCandidates = driver.FindElements(By.XPath(userProvidedNextXPath));
                foreach (var candidate in userXPathCandidates)
                {
                    try
                    {
                        if (candidate.Displayed && candidate.Enabled)
                        {
                            nextButton = candidate;
                            break;
                        }
                    }
                    catch
                    {
                        // Tenta próximo candidato.
                    }
                }

                var nextCandidates = driver.FindElements(By.XPath(
                    "//button[contains(@aria-label,'Avançar') or contains(@aria-label,'Next') or contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or " +
                    ".//span[contains(normalize-space(.),'Avançar')] or .//span[contains(normalize-space(.),'Next')] or " +
                    ".//span[contains(normalize-space(.),'Continuar')] or .//span[contains(normalize-space(.),'Continue')] or " +
                    ".//span[contains(normalize-space(.),'Prosseguir')]]"));

                if (nextCandidates.Count > 0)
                {
                    nextButton = nextCandidates[0];
                }
            }
        }

        if (nextButton == null)
        {
            // Diferencia: botão não existe vs existe porém está desabilitado (geralmente por campos obrigatórios).
            try
            {
                ISearchContext searchRoot = activeModal != null ? (ISearchContext)activeModal : driver;
                var disabledCandidates = searchRoot.FindElements(By.CssSelector(
                    "footer button[data-easy-apply-next-button], " +
                    "footer button[data-live-test-easy-apply-next-button], " +
                    "footer button[aria-label*='Avançar'], " +
                    "footer button[aria-label*='Próximo'], " +
                    "footer button[aria-label*='Next'], " +
                    "footer button[aria-label*='Continue'], " +
                    "footer button[aria-label*='Continuar'], " +
                    "footer button.artdeco-button--primary, " +
                    "button[data-easy-apply-next-button], " +
                    "button[data-live-test-easy-apply-next-button], " +
                    "button[aria-label*='Avançar'], " +
                    "button[aria-label*='Próximo'], " +
                    "button[aria-label*='Next'], " +
                    "button[aria-label*='Continue'], " +
                    "button[aria-label*='Continuar'], " +
                    "button.artdeco-button--primary"
                ));

                var disabled = disabledCandidates.FirstOrDefault(btn =>
                {
                    try
                    {
                        if (!btn.Displayed)
                        {
                            return false;
                        }

                        var ariaDisabled = (btn.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                        return !btn.Enabled || ariaDisabled.Equals("true", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (disabled != null)
                {
                    var ariaDisabled = disabled.GetAttribute("aria-disabled") ?? string.Empty;
                    var disabledAttr = disabled.GetAttribute("disabled") ?? string.Empty;
                    var disabledDescription = DescribeElementForLog(disabled);
                    var requiredSummary = BuildRequiredFieldsSummary(driver);

                    if (autoFillRetryCount < 1 && TryAutoFillMandatoryFieldsInModal(driver, link, $"next_disabled_{stepName}"))
                    {
                        Console.WriteLine($"[AUTO-FILL] Tentando novo clique em 'Avançar' na etapa: {stepName}.");
                        Thread.Sleep(500);
                        return TryClickNextButtonInPopup(driver, wait, stepName, link, autoFillRetryCount + 1);
                    }

                    Console.WriteLine($"Botão 'Avançar' identificado, porém desabilitado na etapa: {stepName}.");
                    Console.WriteLine($"[DEBUG NEXT DISABLED] aria-disabled='{ariaDisabled}', disabledAttr='{disabledAttr}', {disabledDescription}");
                    Console.WriteLine($"[DEBUG REQUIRED] {requiredSummary}");

                    var diagnosticsDisabled = SaveFailureDiagnostics(driver, link, $"next_disabled_{stepName}");
                    LogApplicationStep(link, $"next_disabled_{stepName}", false, $"Botão Avançar está presente porém desabilitado. aria-disabled='{ariaDisabled}', disabled='{disabledAttr}'. {disabledDescription}. REQUIRED: {requiredSummary}", diagnosticsDisabled.HtmlPath, diagnosticsDisabled.ScreenshotPath);
                    return false;
                }
            }
            catch
            {
                // Ignora e segue para log padrão de não encontrado.
            }

            Console.WriteLine($"Botão 'Avançar' não encontrado na etapa: {stepName}.");
            var diagnostics = SaveFailureDiagnostics(driver, link, $"next_not_found_{stepName}");
            LogApplicationStep(link, $"next_not_found_{stepName}", false, "Botão Avançar não encontrado.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", nextButton);
        Thread.Sleep(300);
        ClickElementRobust(driver, nextButton);

        if (!ConfirmNextStepProgress(driver, link, stepName, beforeStepSignature))
        {
            return false;
        }

        var nextButtonDescription = DescribeElementForLog(nextButton);
        Console.WriteLine($"Botão 'Avançar' clicado com sucesso na etapa: {stepName}.");
        Console.WriteLine($"[DEBUG NEXT] {nextButtonDescription}");
        LogApplicationStep(link, $"next_clicked_{stepName}", true, $"Botão Avançar clicado com sucesso. {nextButtonDescription}");
        return true;
    }

    // Clica no botão "Revisar" quando essa etapa existir no popup.
    private static bool TryClickReviewButtonInPopup(IWebDriver driver, WebDriverWait wait, string link)
    {
        IWebElement? reviewButton = null;
        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            ISearchContext root = modal != null ? (ISearchContext)modal : driver;

            var candidates = root.FindElements(By.CssSelector("button[data-live-test-easy-apply-review-button], button[aria-label*='Revis'], button[aria-label*='Review'], button[aria-label*='Avaliar'], button[aria-label*='Evaluar']"))
                .Concat(root.FindElements(By.XPath(".//button[contains(@aria-label,'Revise sua candidatura') or contains(@aria-label,'Review your application') or contains(@aria-label,'Avaliar') or contains(@aria-label,'Evaluar') or contains(normalize-space(.),'Revisar') or contains(normalize-space(.),'Review') or contains(normalize-space(.),'Avaliar') or contains(normalize-space(.),'Evaluar')]")))
                .ToList();

            reviewButton = candidates.FirstOrDefault(c =>
            {
                try { return c.Displayed && c.Enabled; } catch { return false; }
            });
        }
        catch { }

        if (reviewButton == null)
        {
            Console.WriteLine("Etapa 'Revisar' não apareceu nesta vaga; seguindo fluxo.");
            LogApplicationStep(link, "review_not_present", true, "Etapa Revisar não exigida para esta vaga.");
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", reviewButton);
        Thread.Sleep(300);
        ClickElementRobust(driver, reviewButton);
        Console.WriteLine("Botão 'Revisar' clicado com sucesso.");
        LogApplicationStep(link, "review_clicked", true, "Botão Revisar clicado com sucesso.");

        // Após revisar, o modal pode precisar rolar até o rodapé para expor o botão final de Enviar
        Thread.Sleep(800);
        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal != null)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollTop = arguments[0].scrollHeight;", modal);
            }
        }
        catch { }

        return true;
    }

    // Detecta etapa de perguntas obrigatórias (ex.: Additional / Screening questions) para pular por enquanto.
    private static bool TryDetectScreeningQuestionsGate(IWebDriver driver, out string details)
    {
        details = string.Empty;

        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal == null)
            {
                return false;
            }

            var modalText = (modal.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(modalText))
            {
                return false;
            }

            var normalized = modalText.ToLowerInvariant();

            int CountOccurrences(string token)
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return 0;
                }

                var needle = token.ToLowerInvariant();
                var count = 0;
                var start = 0;

                while (start < normalized.Length)
                {
                    var idx = normalized.IndexOf(needle, start, StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        break;
                    }

                    count++;
                    start = idx + needle.Length;
                }

                return count;
            }

            bool IsPlaceholderSelection(string value, string selectedText)
            {
                var normalizedValue = (value ?? string.Empty).Trim().ToLowerInvariant();
                var normalizedText = (selectedText ?? string.Empty).Trim().ToLowerInvariant();
                var combined = (normalizedValue + " " + normalizedText).Trim();

                return string.IsNullOrWhiteSpace(normalizedValue) ||
                       string.IsNullOrWhiteSpace(normalizedText) ||
                       combined.Contains("select an option") ||
                       combined.Contains("please make a selection") ||
                       combined.Contains("selecione") ||
                       combined.Contains("escolha") ||
                       combined.Contains("choose");
            }

            var totalVisibleSelects = 0;
            var requiredVisibleSelects = 0;
            var unresolvedVisibleSelects = 0;
            var unresolvedRequiredSelects = 0;

            var selectElements = modal.FindElements(By.CssSelector("select"));
            foreach (var select in selectElements)
            {
                try
                {
                    if (!select.Displayed)
                    {
                        continue;
                    }

                    totalVisibleSelects++;

                    var selectedOption = select.FindElements(By.CssSelector("option:checked")).FirstOrDefault();
                    var selectedText = (selectedOption?.Text ?? string.Empty).Trim();
                    var value = (select.GetAttribute("value") ?? string.Empty).Trim();
                    var unresolved = IsPlaceholderSelection(value, selectedText);

                    var requiredAttr = !string.IsNullOrWhiteSpace(select.GetAttribute("required"));
                    var ariaRequired = (select.GetAttribute("aria-required") ?? string.Empty).Trim();
                    var isRequired = requiredAttr || ariaRequired.Equals("true", StringComparison.OrdinalIgnoreCase);

                    if (!isRequired)
                    {
                        // Alguns formulários não marcam o select com atributo required,
                        // mas exibem o rótulo "Obrigatório/Required" no contêiner.
                        try
                        {
                            var container = select.FindElement(By.XPath("./ancestor::*[self::div or self::fieldset][1]"));
                            var containerText = (container.Text ?? string.Empty).ToLowerInvariant();
                            isRequired = containerText.Contains("obrigatório") ||
                                         containerText.Contains("obrigatorio") ||
                                         containerText.Contains("required");
                        }
                        catch
                        {
                            // Se não achar contêiner, mantém somente atributo/aria.
                        }
                    }

                    if (isRequired)
                    {
                        requiredVisibleSelects++;
                    }

                    if (unresolved)
                    {
                        unresolvedVisibleSelects++;
                        if (isRequired)
                        {
                            unresolvedRequiredSelects++;
                        }
                    }
                }
                catch
                {
                    // Ignora select problemático e segue os demais.
                }
            }

            var pleaseCount = CountOccurrences("please make a selection");
            var selectOptionCount = CountOccurrences("select an option");
            var requiredPtCount = CountOccurrences("obrigatório");
            var requiredEnCount = CountOccurrences("required");
            var yearsQuestionCount = CountOccurrences("how many years of experience");
            var additionalCount = CountOccurrences("additional");
            var screeningCount = CountOccurrences("screening questions");
            var hearAboutUsCount = CountOccurrences("how did you hear about us");

            var hasSelectGate =
                unresolvedRequiredSelects >= 1 ||
                (unresolvedVisibleSelects >= 2 && (pleaseCount >= 1 || selectOptionCount >= 1 || additionalCount >= 1 || screeningCount >= 1));

            var hasScreeningGate =
                hasSelectGate ||
                (pleaseCount >= 2 && selectOptionCount >= 2) ||
                (yearsQuestionCount >= 2 && (pleaseCount >= 1 || selectOptionCount >= 1)) ||
                ((additionalCount + screeningCount) >= 1 && (pleaseCount >= 1 || (requiredPtCount + requiredEnCount) >= 3)) ||
                (hearAboutUsCount >= 1 && (pleaseCount >= 1 || selectOptionCount >= 1));

            if (!hasScreeningGate)
            {
                return false;
            }

            details =
                $"screening_gate_detected: visible_selects={totalVisibleSelects}, required_selects={requiredVisibleSelects}, unresolved_selects={unresolvedVisibleSelects}, unresolved_required_selects={unresolvedRequiredSelects}, please={pleaseCount}, select_option={selectOptionCount}, required_pt={requiredPtCount}, required_en={requiredEnCount}, years_q={yearsQuestionCount}, additional={additionalCount}, screening={screeningCount}, hear_about_us={hearAboutUsCount}";
            return true;
        }
        catch (Exception ex)
        {
            details = $"screening_gate_detection_error: {ex.Message}";
            return false;
        }
    }

    // Fecha/descarta o popup Easy Apply para seguir com a próxima vaga quando for etapa não suportada.
    private static bool TryDismissEasyApplyModal(IWebDriver driver, string link, string context)
    {
        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal == null)
            {
                return true;
            }

            IWebElement? dismissButton = null;

            var modalDismissCandidates = modal.FindElements(By.CssSelector(
                "button.artdeco-modal__dismiss, " +
                "button[aria-label*='Fechar'], " +
                "button[aria-label*='Close'], " +
                "button[aria-label*='Descartar'], " +
                "button[aria-label*='Discard']"
            ));

            dismissButton = modalDismissCandidates.FirstOrDefault(btn =>
            {
                try { return btn.Displayed && btn.Enabled; } catch { return false; }
            });

            if (dismissButton == null)
            {
                var globalDismissCandidates = driver.FindElements(By.CssSelector(
                    "button.artdeco-modal__dismiss, " +
                    "button[aria-label*='Fechar'], " +
                    "button[aria-label*='Close'], " +
                    "button[aria-label*='Descartar'], " +
                    "button[aria-label*='Discard']"
                ));

                dismissButton = globalDismissCandidates.FirstOrDefault(btn =>
                {
                    try { return btn.Displayed && btn.Enabled; } catch { return false; }
                });
            }

            if (dismissButton != null)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", dismissButton);
                Thread.Sleep(200);
                ClickElementRobust(driver, dismissButton);
            }

            Thread.Sleep(450);

            // Garante que o diálogo "Salvar esta candidatura? Descartar / Salvar" seja descartado
            if (driver is IJavaScriptExecutor js)
            {
                try
                {
                    js.ExecuteScript(@"
                        const buttons = Array.from(document.querySelectorAll('button, [role=""button""]'));
                        const discardBtn = buttons.find(b => {
                            const t = (b.innerText || b.textContent || '').trim().toLowerCase();
                            const a = (b.getAttribute('aria-label') || '').toLowerCase();
                            const c = (b.getAttribute('data-control-name') || '').toLowerCase();
                            return t === 'descartar' || t === 'discard' || a.includes('descartar') || a.includes('discard') || c.includes('discard');
                        });
                        if (discardBtn && discardBtn.offsetParent !== null) {
                            discardBtn.scrollIntoView({block: 'center'});
                            discardBtn.click();
                        }
                    ");
                }
                catch { }
            }

            var discardConfirmCandidates = driver.FindElements(By.XPath(
                "//button[contains(@aria-label,'Descartar') or contains(@aria-label,'Discard') or " +
                "contains(normalize-space(.),'Descartar') or contains(normalize-space(.),'Discard') or " +
                ".//span[contains(normalize-space(.),'Descartar')] or .//span[contains(normalize-space(.),'Discard')]]"
            ));

            var discardConfirm = discardConfirmCandidates.FirstOrDefault(btn =>
            {
                try { return btn.Displayed && btn.Enabled; } catch { return false; }
            });

            if (discardConfirm != null)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", discardConfirm);
                Thread.Sleep(150);
                ClickElementRobust(driver, discardConfirm);
            }

            Thread.Sleep(450);

            var stillOpen = TryFindActiveEasyApplyModal(driver) != null;
            if (stillOpen)
            {
                try
                {
                    driver.FindElement(By.TagName("body")).SendKeys(Keys.Escape);
                    Thread.Sleep(250);
                }
                catch
                {
                    // Ignora fallback de ESC.
                }

                stillOpen = TryFindActiveEasyApplyModal(driver) != null;
            }

            var closed = !stillOpen;
            LogApplicationStep(link, $"dismiss_modal_{context}", closed, closed ? "Popup Easy Apply fechado/descartado." : "Tentativa de fechar popup sem confirmação de fechamento.");
            return closed;
        }
        catch (Exception ex)
        {
            LogApplicationStep(link, $"dismiss_modal_error_{context}", false, ex.Message);
            return false;
        }
    }

    private static IWebElement? TryFindSubmitButtonRobust(IWebDriver driver)
    {
        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal != null)
            {
                try { ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollTop = arguments[0].scrollHeight;", modal); } catch { }
            }

            ISearchContext searchRoot = modal != null ? (ISearchContext)modal : driver;

            var submitCandidates = searchRoot.FindElements(By.CssSelector(
                "button[data-live-test-easy-apply-submit-button], " +
                "button[data-easy-apply-submit-button], " +
                "button[aria-label*='Enviar'], " +
                "button[aria-label*='Submit']"
            ));

            if (submitCandidates.Count == 0)
            {
                submitCandidates = searchRoot.FindElements(By.XPath(
                    ".//button[" +
                    "contains(@aria-label,'Enviar candidatura') or contains(@aria-label,'Enviar') or contains(@aria-label,'Submit application') or contains(@aria-label,'Submit') or " +
                    "contains(normalize-space(.),'Enviar candidatura') or contains(normalize-space(.),'Enviar') or " +
                    "contains(normalize-space(.),'Submit application') or contains(normalize-space(.),'Submit') or " +
                    ".//span[contains(normalize-space(.),'Enviar candidatura')] or .//span[contains(normalize-space(.),'Enviar')] or " +
                    ".//span[contains(normalize-space(.),'Submit application')] or .//span[contains(normalize-space(.),'Submit')]" +
                    "]"));
            }

            if (submitCandidates.Count == 0)
            {
                return null;
            }

            var candidate = submitCandidates.FirstOrDefault(btn =>
            {
                try
                {
                    if (!btn.Displayed)
                    {
                        return false;
                    }

                    var text = (btn.Text ?? string.Empty).Trim();
                    var aria = (btn.GetAttribute("aria-label") ?? string.Empty).Trim();
                    var combined = (text + " " + aria).ToLowerInvariant();

                    return combined.Contains("enviar") || combined.Contains("submit");
                }
                catch
                {
                    return false;
                }
            }) ?? submitCandidates[0];

            if (candidate != null)
            {
                try { ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", candidate); } catch { }
            }

            return candidate;
        }
        catch
        {
            return null;
        }
    }

    // Em alguns fluxos SDUI não há botão "Avançar"; tenta finalizar por Revisar/Enviar diretamente.
    private static bool TryFinalizeWithoutNextIfPossible(IWebDriver driver, WebDriverWait wait, string link)
    {
        Console.WriteLine("[FALLBACK] Tentando finalizar sem etapa 'Avançar'...");

        // Primeiro tenta ir para revisão (quando houver).
        TryClickReviewButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(4)), link);
        Thread.Sleep(600);

        var submitButton = TryFindSubmitButtonRobust(driver);

        if (submitButton == null)
        {
            LogApplicationStep(link, "fallback_finalize_not_possible", false, "Sem botão Avançar e sem botão de envio direto.");
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", submitButton);
        Thread.Sleep(250);
        ClickElementRobust(driver, submitButton);
        Console.WriteLine("[FALLBACK] Botão 'Enviar candidatura' clicado sem etapa 'Avançar'.");
        LogApplicationStep(link, "fallback_submit_clicked", true, "Envio direto realizado sem etapa Avançar.");
        WaitForAndDismissPostSubmitSuccessModal(driver, link);
        return true;
    }

    // Em alguns layouts, após Easy Apply surge um botão/link "Continuar" antes do primeiro "Avançar".
    private static bool TryClickContinueEntryIfPresent(IWebDriver driver, WebDriverWait wait, string link)
    {
        IWebElement? continueButton = null;

        try
        {
            continueButton = wait.Until(d =>
            {
                var candidates = d.FindElements(By.XPath(
                    "//a[contains(@href,'/apply/?openSDUIApplyFlow=true')] | " +
                    "//button[contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or contains(@aria-label,'Prosseguir')] | " +
                    "//a[contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or contains(@aria-label,'Prosseguir')] | " +
                    "//button[.//span[contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Continue') or contains(normalize-space(.),'Prosseguir')]] | " +
                    "//a[.//span[contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Continue') or contains(normalize-space(.),'Prosseguir')]]"));

                foreach (var candidate in candidates)
                {
                    try
                    {
                        var text = candidate.Text?.Trim() ?? string.Empty;
                        var ariaLabel = candidate.GetAttribute("aria-label")?.Trim() ?? string.Empty;
                        var combined = (text + " " + ariaLabel).ToLowerInvariant();

                        if (candidate.Displayed && candidate.Enabled &&
                            (combined.Contains("continuar") || combined.Contains("continue") || combined.Contains("prosseguir")))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                        // Tenta próximo candidato.
                    }
                }

                return null;
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Não existe etapa de "Continuar" para esta vaga.
        }

        if (continueButton == null)
        {
            return false;
        }

        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", continueButton);
        Thread.Sleep(250);
        ClickElementRobust(driver, continueButton);
        Console.WriteLine("Botão/link 'Continuar' clicado antes da etapa 'Avançar'.");
        LogApplicationStep(link, "continue_entry_clicked", true, "Botão/link Continuar clicado para abrir fluxo de candidatura.");
        Thread.Sleep(1200);

        try
        {
            Console.WriteLine($"URL após 'Continuar': {driver.Url}");
            LogApplicationStep(link, "continue_entry_url", true, driver.Url);
        }
        catch
        {
            // Ignora falha de leitura de URL.
        }

        return true;
    }

    // Aguarda o fluxo de candidatura ficar pronto (modal aberto, botão Avançar visível ou URL SDUI ativa).
    private static bool WaitForApplyFlowReady(IWebDriver driver, WebDriverWait wait, string link)
    {
        Console.WriteLine("[ETAPA] Aguardando carregamento do fluxo de candidatura...");

        try
        {
            var detectedState = wait.Until(d =>
            {
                try
                {
                    var url = d.Url ?? string.Empty;
                    var isSduiUrl = url.Contains("/apply/?openSDUIApplyFlow=true", StringComparison.OrdinalIgnoreCase);

                    var nextButtons = d.FindElements(By.CssSelector(
                        "div.display-flex.justify-flex-end.ph5.pv4 > button[data-easy-apply-next-button][data-live-test-easy-apply-next-button], " +
                        "button[data-easy-apply-next-button][data-live-test-easy-apply-next-button], " +
                        "button[data-live-test-easy-apply-next-button], " +
                        "button[data-easy-apply-next-button], " +
                        "button[aria-label='Avançar para próxima etapa']"
                    ));

                    foreach (var nextButton in nextButtons)
                    {
                        try
                        {
                            if (nextButton.Displayed && nextButton.Enabled)
                            {
                                return "next_button_ready";
                            }
                        }
                        catch
                        {
                            // Ignora e tenta próximos candidatos.
                        }
                    }

                    var modals = d.FindElements(By.CssSelector(
                        "dialog[data-testid='dialog'][open], dialog[open], [data-testid='dialog'], " +
                        "div.jobs-easy-apply-modal[role='dialog'], div.jobs-easy-apply-modal, " +
                        "div[data-sdui-screen*='easyapply'], div[data-testid='dialog-content'], dialog"));
                    foreach (var modal in modals)
                    {
                        try
                        {
                            if (modal.Displayed)
                            {
                                return "modal_visible";
                            }
                        }
                        catch
                        {
                            // Ignora e tenta próximos modais.
                        }
                    }

                    var activeModalNow = TryFindActiveEasyApplyModal(d);
                    if (activeModalNow != null)
                    {
                        return "active_modal_found";
                    }

                    // Em fluxos SDUI, o URL pode mudar antes do UI estar realmente interativa.
                    // Evita seguir adiante só com base na URL: espera existir algum CTA típico do fluxo.
                    if (isSduiUrl)
                    {
                        var sduiCtaCandidates = d.FindElements(By.XPath(
                            "//button[contains(@aria-label,'Avançar') or contains(@aria-label,'Próximo') or contains(@aria-label,'Next') or " +
                            "contains(@aria-label,'Continuar') or contains(@aria-label,'Continue') or contains(@aria-label,'Prosseguir') or " +
                            "contains(@aria-label,'Enviar') or contains(@aria-label,'Submit') or " +
                            ".//span[contains(normalize-space(.),'Avançar') or contains(normalize-space(.),'Próximo') or contains(normalize-space(.),'Next') or " +
                            "contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Continue') or contains(normalize-space(.),'Prosseguir') or " +
                            "contains(normalize-space(.),'Enviar') or contains(normalize-space(.),'Submit')]]"));

                        var hasAnyVisibleCta = sduiCtaCandidates.Any(el =>
                        {
                            try { return el.Displayed; } catch { return false; }
                        });

                        if (hasAnyVisibleCta)
                        {
                            return "sdui_url_with_cta";
                        }
                    }
                }
                catch
                {
                    // Aguarda próxima iteração do wait.
                }

                return null;
            });

            Console.WriteLine($"Fluxo de candidatura pronto: {detectedState}.");
            LogApplicationStep(link, "apply_flow_ready", true, $"Estado detectado após Easy Apply: {detectedState}.");
            return true;
        }
        catch (WebDriverTimeoutException)
        {
            Console.WriteLine("Fluxo de candidatura não ficou pronto no tempo esperado.");
            var diagnostics = SaveFailureDiagnostics(driver, link, "apply_flow_not_ready_after_easy_apply");
            LogApplicationStep(link, "apply_flow_not_ready", false, "Fluxo não ficou pronto após Easy Apply no tempo esperado.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            return false;
        }
    }

    // Verifica se estamos dentro do popup correto (Informações de contato + perfil + botão Avançar no rodapé).
    private static bool VerifyContactPopupBeforeNext(IWebDriver driver, string link)
    {
        Console.WriteLine("[CHECK POPUP] Validando conteúdo do popup antes do botão 'Avançar'...");

        try
        {
            var quickWait = new WebDriverWait(driver, TimeSpan.FromSeconds(12));
            var verified = quickWait.Until(d =>
            {
                try
                {
                    var modal = TryFindActiveEasyApplyModal(d);

                    if (modal == null)
                    {
                        return false;
                    }

                    var contactTitles = modal.FindElements(By.XPath(
                        ".//*[self::h3 or self::h2 or self::h1 or self::span or self::div or self::p][contains(normalize-space(.),'Informações de contato') or contains(normalize-space(.),'Contact information') or contains(normalize-space(.),'Contact info') or contains(normalize-space(.),'Candidate-se') or contains(normalize-space(.),'Apply')]"));

                    var hasContactInfoTitle = contactTitles.Any(e =>
                    {
                        try
                        {
                            return e.Displayed;
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    // Evita validação hard-coded de dados do perfil (nome/local/cargo) para funcionar em qualquer conta.
                    // Em vez disso, confirma a presença de campos comuns de contato (email/telefone) quando existirem.
                    var contactFieldCandidates = modal.FindElements(By.CssSelector(
                        "input, select, textarea"));

                    var hasAnyContactField = contactFieldCandidates.Any(e =>
                    {
                        try { return e.Displayed; } catch { return false; }
                    });

                    var footerNextButtons = modal.FindElements(By.CssSelector(
                        "form footer button, footer button, " +
                        "button[data-easy-apply-next-button], " +
                        "button[aria-label*='Avançar'], button[aria-label*='Next'], " +
                        "button[aria-label*='Continuar'], button[aria-label*='Continue'], " +
                        "button.artdeco-button--primary"
                    ));

                    var hasAnyFooterNextButton = footerNextButtons.Any(btn =>
                    {
                        try { return btn.Displayed; } catch { return false; }
                    });

                    if (!hasAnyFooterNextButton)
                    {
                        hasAnyFooterNextButton = modal.FindElements(By.TagName("button")).Any(btn =>
                        {
                            try
                            {
                                if (!btn.Displayed) return false;
                                var t = (btn.Text ?? string.Empty).Trim();
                                return t.Equals("Avançar", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Revisar", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Avaliar", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Evaluar", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Next", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Review", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Enviar candidatura", StringComparison.OrdinalIgnoreCase)
                                    || t.Equals("Submit application", StringComparison.OrdinalIgnoreCase);
                            }
                            catch { return false; }
                        });
                    }

                    // Considera válido quando o modal de candidatura está presente e tem campos ou botão de avanço
                    return hasAnyFooterNextButton || (hasAnyContactField && hasContactInfoTitle);
                }
                catch
                {
                    return false;
                }
            });

            if (verified)
            {
                Console.WriteLine("[CHECK POPUP] Popup validado com sucesso (Informações de contato + botão Avançar).");
                LogApplicationStep(link, "popup_contact_verified", true, "Popup com Informações de contato e botão Avançar validado antes do clique.");
                return true;
            }
        }
        catch (WebDriverTimeoutException)
        {
            // Segue para tratamento abaixo.
        }

        Console.WriteLine("[CHECK POPUP] Popup esperado não foi validado antes do Avançar.");
        var diagnostics = SaveFailureDiagnostics(driver, link, "popup_contact_info_not_found_before_next");
        LogApplicationStep(link, "popup_contact_not_verified", false, "Não foi possível validar popup com Informações de contato e botão Avançar.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
        return false;
    }

    // Após o primeiro "Avançar", aguarda o popup estabilizar para a próxima ação (currículo/revisar/enviar/novo avançar).
    private static bool WaitForStepTransitionAfterFirstNext(IWebDriver driver, string link)
    {
        try
        {
            var quickWait = new WebDriverWait(driver, TimeSpan.FromSeconds(12));
            var readyState = quickWait.Until(d =>
            {
                try
                {
                    var submitBtn = TryFindSubmitButtonRobust(d);
                    if (submitBtn != null)
                    {
                        try { if (submitBtn.Displayed) return "submit_visible"; } catch { }
                    }

                    var reviewCandidates = d.FindElements(By.CssSelector("button[data-live-test-easy-apply-review-button], button[aria-label*='Review'], button[aria-label*='Revis'], button[aria-label*='Avaliar'], button[aria-label*='Evaluar']"))
                        .Concat(d.FindElements(By.XPath(".//button[contains(@aria-label,'Revise sua candidatura') or contains(@aria-label,'Review your application') or contains(@aria-label,'Avaliar') or contains(@aria-label,'Evaluar') or contains(normalize-space(.),'Revisar') or contains(normalize-space(.),'Review') or contains(normalize-space(.),'Avaliar') or contains(normalize-space(.),'Evaluar')]")));
                    if (reviewCandidates.Any(btn => { try { return btn.Displayed; } catch { return false; } }))
                    {
                        return "review_visible";
                    }

                    var resumeVisible = d.FindElements(By.CssSelector(
                        "input[type='radio'][name*='resume'], input[type='radio'][id*='resume'], label[for*='resume'], [data-test-document-upload-item], div.jobs-document-upload-redesign-card__container, input[type='radio'][aria-label*='.pdf'], input[type='radio'][name*='radio-group'], input[type='file']"))
                        .Concat(d.FindElements(By.XPath(".//button[contains(normalize-space(.),'Carregar currículo') or contains(normalize-space(.),'Upload resume')]")))
                        .Any(el =>
                        {
                            try { return el.Displayed; } catch { return false; }
                        });
                    if (resumeVisible)
                    {
                        return "resume_visible";
                    }

                    var nextVisible = d.FindElements(By.CssSelector(
                        "button[data-easy-apply-next-button], button[data-live-test-easy-apply-next-button], button[aria-label*='Avançar'], button[aria-label*='Next'], button[aria-label*='Continuar'], button[aria-label*='Continue']"))
                        .Concat(d.FindElements(By.XPath(".//button[contains(normalize-space(.),'Avançar') or contains(normalize-space(.),'Next') or contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Voltar') or contains(normalize-space(.),'Back')]")))
                        .Any(btn =>
                        {
                            try { return btn.Displayed; } catch { return false; }
                        });
                    if (nextVisible)
                    {
                        return "next_visible";
                    }

                    var activeModal = TryFindActiveEasyApplyModal(d);
                    if (activeModal != null && activeModal.FindElements(By.CssSelector("input, select, textarea")).Any(el => { try { return el.Displayed; } catch { return false; } }))
                    {
                        return "modal_fields_visible";
                    }
                }
                catch
                {
                    // Aguarda nova iteração.
                }

                return null;
            });

            Console.WriteLine($"[ETAPA] Transição após primeiro Avançar detectada: {readyState}.");
            LogApplicationStep(link, "post_first_next_transition_ready", true, $"Estado após primeiro Avançar: {readyState}.");
            return true;
        }
        catch (WebDriverTimeoutException)
        {
            Console.WriteLine("[ETAPA] Transição após primeiro Avançar não estabilizou no tempo esperado.");
            var diagnostics = SaveFailureDiagnostics(driver, link, "post_first_next_transition_timeout");
            LogApplicationStep(link, "post_first_next_transition_timeout", false, "Sem estabilização clara após primeiro Avançar no tempo esperado.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            return false;
        }
    }

}
