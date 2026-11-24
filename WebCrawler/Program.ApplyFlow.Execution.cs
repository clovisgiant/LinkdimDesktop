using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

partial class Program
{
    private static bool IsBudgetExhaustedJobLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        var ebp = TryGetQueryParamValue(link, "eBP") ?? string.Empty;
        return ebp.Equals("BUDGET_EXHAUSTED_JOB", StringComparison.OrdinalIgnoreCase);
    }

    // Executa fluxo completo sem perguntas obrigatórias: Candidatura simplificada -> Avançar -> selecionar currículo -> Avançar -> Enviar candidatura.
    private static bool TryCompleteEasyApplyWithoutQuestions(IWebDriver driver, WebDriverWait wait, string link)
    {
        Console.WriteLine("[ETAPA] Procurando botão de candidatura simplificada...");
        LogApplicationStep(link, "flow_started", true, "Início do fluxo Easy Apply.");

        bool HasVisibleSubmitButton()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                if (modal == null && !(driver.Url ?? string.Empty).Contains("/apply/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                ISearchContext root = modal != null ? (ISearchContext)modal : driver;
                var candidates = root.FindElements(By.CssSelector(
                    "button[data-live-test-easy-apply-submit-button], " +
                    "button[data-easy-apply-submit-button], " +
                    "button[aria-label*='Enviar'], " +
                    "button[aria-label*='Submit']"
                )).Concat(root.FindElements(By.XPath(
                    ".//button[contains(@aria-label,'Enviar') or contains(@aria-label,'Submit') or contains(normalize-space(.),'Enviar') or contains(normalize-space(.),'Submit')]"
                )));

                return candidates.Any(btn =>
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
                });
            }
            catch
            {
                return false;
            }
        }

        bool HasVisibleReviewButton()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                if (modal == null && !(driver.Url ?? string.Empty).Contains("/apply/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                ISearchContext root = modal != null ? (ISearchContext)modal : driver;
                var candidates = root.FindElements(By.XPath(
                    ".//button[contains(@aria-label,'Revise sua candidatura') or contains(@aria-label,'Review your application') or contains(@aria-label,'Revis') or contains(@aria-label,'Review') or contains(@aria-label,'Avaliar') or contains(@aria-label,'Evaluar') or " +
                    "contains(normalize-space(.),'Revisar') or contains(normalize-space(.),'Review') or contains(normalize-space(.),'Avaliar') or contains(normalize-space(.),'Evaluar') or " +
                    ".//span[contains(normalize-space(.),'Revis')] or .//span[contains(normalize-space(.),'Review')] or .//span[contains(normalize-space(.),'Avaliar')] or .//span[contains(normalize-space(.),'Evaluar')]]"
                ));

                return candidates.Any(btn =>
                {
                    try { return btn.Displayed; } catch { return false; }
                });
            }
            catch
            {
                return false;
            }
        }

        bool HasVisibleNextButton()
        {
            try
            {
                var modal = TryFindActiveEasyApplyModal(driver);
                if (modal == null && !(driver.Url ?? string.Empty).Contains("/apply/", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                ISearchContext root = modal != null ? (ISearchContext)modal : driver;
                var candidates = root.FindElements(By.CssSelector(
                    "button[data-easy-apply-next-button], " +
                    "button[data-live-test-easy-apply-next-button], " +
                    "footer button, " +
                    "button.artdeco-button--primary"
                )).Concat(root.FindElements(By.XPath(
                    ".//button[contains(normalize-space(.),'Avançar') or contains(normalize-space(.),'Next') or contains(normalize-space(.),'Continuar') or contains(normalize-space(.),'Próximo')]"
                )));

                foreach (var btn in candidates)
                {
                    try
                    {
                        if (!btn.Displayed || !btn.Enabled)
                        {
                            continue;
                        }

                        var text = (btn.Text ?? string.Empty).Trim();
                        var aria = (btn.GetAttribute("aria-label") ?? string.Empty).Trim();
                        var combined = (text + " " + aria).ToLowerInvariant();

                        var looksLikeNext = combined.Contains("avançar") ||
                                            combined.Contains("próximo") ||
                                            combined.Contains("proximo") ||
                                            combined.Contains("next") ||
                                            combined.Contains("continuar") ||
                                            combined.Contains("continue") ||
                                            combined.Contains("prosseguir");

                        var looksLikeSubmitOrReview = combined.Contains("enviar") ||
                                                      combined.Contains("submit") ||
                                                      combined.Contains("revis") ||
                                                      combined.Contains("review") ||
                                                      combined.Contains("avaliar") ||
                                                      combined.Contains("evaluar");

                        if (looksLikeNext && !looksLikeSubmitOrReview)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Tenta próximo botão.
                    }
                }
            }
            catch
            {
                // Ignora e considera que não há botão de próximo.
            }

            return false;
        }

        bool TryHandleMandatoryGate(string stageContext)
        {
            Console.WriteLine($"[AUTO-FILL] Identificando e preenchendo campos obrigatórios/questionário na etapa '{stageContext}'...");
            TryAutoFillMandatoryFieldsInModal(driver, link, stageContext);
            Thread.Sleep(450);

            // Tenta avançar se houver botão 'Avançar'
            if (HasVisibleNextButton())
            {
                if (TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(8)), $"autofill_next_{stageContext}", link))
                {
                    Thread.Sleep(600);
                    return false;
                }
            }

            // Tenta avançar se houver botão 'Revisar'
            if (HasVisibleReviewButton())
            {
                if (TryClickReviewButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(8)), link))
                {
                    Thread.Sleep(600);
                    return false;
                }
            }

            // Segunda passada de preenchimento com tentativa adicional
            TryAutoFillMandatoryFieldsInModal(driver, link, $"{stageContext}_retry");
            Thread.Sleep(400);

            if (HasVisibleNextButton() && TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(8)), $"autofill_next_retry_{stageContext}", link))
            {
                Thread.Sleep(600);
                return false;
            }

            if (HasVisibleReviewButton() && TryClickReviewButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(8)), link))
            {
                Thread.Sleep(600);
                return false;
            }

            // Retorna false para não abortar o fluxo e permitir que etapas seguintes tentem avançar
            return false;
        }

        // Localiza rapidamente o botão real de Candidatura Simplificada (clássico ou SDUI), sem confundir com filtros do topo ou cards laterais!
        IWebElement? applyButton = null;
        var alreadyAppliedOrClosed = string.Empty;
        try
        {
            var applyWait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            applyButton = applyWait.Until(d =>
            {
                // 1. Seletores de alta precisão (SDUI aria-label específico da vaga e IDs clássicos)
                var exactCandidates = d.FindElements(By.CssSelector(
                    "#jobs-apply-button-id, " +
                    "button[aria-label*='Usar a candidatura simplificada'], " +
                    "a[aria-label*='Usar a candidatura simplificada'], " +
                    "button[aria-label*='Easy Apply to'], " +
                    "a[aria-label*='Easy Apply to'], " +
                    "button.jobs-apply-button, a.jobs-apply-button, " +
                    "button[data-live-test-job-apply-button], a[data-live-test-job-apply-button], " +
                    "a[data-view-name='job-apply-button'], button[data-view-name='job-apply-button'], " +
                    "a[href*='/apply/?openSDUIApplyFlow=true']"
                ));

                foreach (var c in exactCandidates)
                {
                    try
                    {
                        if (c.Displayed && c.Enabled)
                        {
                            return c;
                        }
                    }
                    catch { }
                }

                // 2. Fallback para botões <button> reais na área da vaga (ignorando filtros 'Filtrar Candidatura simplificada' e links <a> de cards da lista)
                if (d is IJavaScriptExecutor js)
                {
                    var foundOrStatus = js.ExecuteScript(@"
                        const buttons = Array.from(document.querySelectorAll('button'));
                        for (const b of buttons) {
                            const s = window.getComputedStyle(b);
                            const r = b.getBoundingClientRect();
                            if (s.display === 'none' || s.visibility === 'hidden' || r.width <= 0 || r.height <= 0) continue;
                            if (b.disabled || b.getAttribute('aria-disabled') === 'true') continue;
                            const aria = (b.getAttribute('aria-label') || '').toLowerCase();
                            if (aria.includes('filtrar') || aria.includes('filter')) continue;
                            const txt = (b.innerText || b.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
                            if (txt === 'candidatura simplificada' || txt === 'easy apply') {
                                return b;
                            }
                        }
                        const bodyText = (document.body?.innerText || '').toLowerCase();
                        if (bodyText.includes('não aceita mais candidaturas') || bodyText.includes('no longer accepting applications')) {
                            return 'JOB_CLOSED';
                        }
                        if (bodyText.includes('candidatura enviada') || bodyText.includes('applied ')) {
                            return 'ALREADY_APPLIED';
                        }
                        return null;
                    ");

                    if (foundOrStatus is IWebElement webBtn)
                    {
                        return webBtn;
                    }
                    if (foundOrStatus is string statusStr && !string.IsNullOrWhiteSpace(statusStr))
                    {
                        alreadyAppliedOrClosed = statusStr;
                        return null;
                    }
                }

                return null;
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Tratado logo abaixo.
        }

        if (applyButton == null)
        {
            if (alreadyAppliedOrClosed == "ALREADY_APPLIED")
            {
                Console.WriteLine("Esta vaga já consta como 'Candidatura enviada' no LinkedIn. Marcando no banco e seguindo...");
                MarkJobAsApplied(link);
                IgnoreJobLinkInCurrentRun(link, "ALREADY_APPLIED_ON_LINKEDIN");
                return false;
            }
            if (alreadyAppliedOrClosed == "JOB_CLOSED")
            {
                Console.WriteLine("Vaga encerrada ('Não aceita mais candidaturas'). Marcando como indisponível...");
                MarkJobAsUnavailable(link, "JOB_CLOSED");
                IgnoreJobLinkInCurrentRun(link, "JOB_CLOSED");
                return false;
            }

            Console.WriteLine("Botão de candidatura simplificada não encontrado nesta vaga.");
            var diagnostics = SaveFailureDiagnostics(driver, link, "easy_apply_not_found");
            LogApplicationStep(link, "easy_apply_not_found", false, "Botão inicial Easy Apply não encontrado.", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            IgnoreJobLinkInCurrentRun(link, "EASY_APPLY_NOT_FOUND");
            MarkJobAsUnavailable(link, "EASY_APPLY_NOT_FOUND");
            return false;
        }

        // Scroll até o botão para garantir visibilidade/interação.
        TryScrollElementIntoViewHumanized(driver, applyButton);

        // Regra importante: para manter o comportamento antigo (abrir popup/modal), tentamos SEMPRE clicar primeiro.
        // Alguns fluxos SDUI são links; nesses casos, se o clique não disparar nada, usamos fallback de navegar pelo href.
        var applyHref = (applyButton.GetAttribute("href") ?? string.Empty).Trim();
        var isSduiApplyHref = !string.IsNullOrWhiteSpace(applyHref) &&
            applyHref.Contains("/apply/?openSDUIApplyFlow=true", StringComparison.OrdinalIgnoreCase);
        var ebp = isSduiApplyHref ? (TryGetQueryParamValue(applyHref, "eBP") ?? string.Empty) : string.Empty;

        // 1) Simulação Humana: Lê a vaga e rola a página antes de agir
        SimulateReadingPause();
        HumanizeCollectionEntry(driver); // Reutiliza o scroll irregular para simular inspeção da vaga

        // 2) Clique primeiro (prioriza popup/modal)
        ClickElementRobust(driver, applyButton);
        PauseBetweenFlowSteps("Aguardando abertura do popup após clique");

        // 2) Se o clique não mudou nada e for SDUI, navega direto para o href
        try
        {
            var urlAfterClick = driver.Url ?? string.Empty;
            var hasModalAfterClick = TryFindActiveEasyApplyModal(driver) != null;
            var isAlreadyInApplyUrl = urlAfterClick.Contains("/apply/?openSDUIApplyFlow=true", StringComparison.OrdinalIgnoreCase);

            if (!hasModalAfterClick && !isAlreadyInApplyUrl && isSduiApplyHref)
            {
                if (ebp.Equals("BUDGET_EXHAUSTED_JOB", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Clique não abriu popup e o SDUI href indica BUDGET_EXHAUSTED_JOB (provável candidatura indisponível). Ignorando.");
                    MarkJobAsUnavailable(link, "BUDGET_EXHAUSTED_JOB");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "sdui_apply_blocked_budget_exhausted");
                    LogApplicationStep(link, "sdui_apply_blocked_budget_exhausted", false,
                        $"Clique não abriu popup; SDUI apply href possui eBP=BUDGET_EXHAUSTED_JOB. href='{applyHref}'.",
                        diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    return false;
                }

                Console.WriteLine($"Clique não abriu popup; usando fallback SDUI navigate para: {applyHref}");
                LogApplicationStep(link, "easy_apply_navigate_sdui_href_fallback", true,
                    $"Clique não abriu popup; navegando para href SDUI apply. eBP='{ebp}'. href='{applyHref}'.");
                driver.Navigate().GoToUrl(applyHref);
                PauseBetweenFlowSteps();
            }
        }
        catch
        {
            // Se falhar leitura de URL/modal, segue fluxo.
        }

        Console.WriteLine("Ação de candidatura simplificada executada (click com fallback navigate quando aplicável).");
        LogApplicationStep(link, "easy_apply_clicked", true, "Ação inicial Easy Apply executada (click-first).");

        // Em vagas SDUI, pode existir um botão/link intermediário "Continuar" antes do popup padrão.
        TryClickContinueEntryIfPresent(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(5)), link);

        // Aguarda estado do fluxo antes de buscar o primeiro botão Avançar.
        if (!WaitForApplyFlowReady(driver, wait, link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("apply_flow_not_ready"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_not_ready_fallback", true, "Fluxo concluído por fallback após atraso no carregamento do apply.");
            return true;
        }

        // Antes de clicar em Avançar, valida se o popup certo está aberto com as informações de contato.
        if (!VerifyContactPopupBeforeNext(driver, link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("contact_popup_not_verified"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_popup_check_fallback", true, "Fluxo concluído por fallback após falha na validação do popup de contato.");
            return true;
        }

        // Tenta auto-preencher campos obrigatórios da primeira etapa (ex.: select de email, país, etc.)
        TryAutoFillMandatoryFieldsInModal(driver, link, "primeira etapa_pre");
        Thread.Sleep(300);

        // Primeiro avanço no popup após abrir candidatura simplificada.
        Console.WriteLine("[ETAPA] Tentando primeiro 'Avançar'...");
        if (!TryClickNextButtonInPopup(driver, wait, "primeira etapa", link))
        {
            if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                if (TryHandleMandatoryGate("first_next"))
                {
                    return false;
                }

                return false;
            }

            LogApplicationStep(link, "application_completed_via_fallback", true, "Fluxo concluído por fallback sem etapa Avançar.");
            return true;
        }

        // Aguarda estabilização real da etapa seguinte.
        if (!WaitForStepTransitionAfterFirstNext(driver, link))
        {
            if (TryFinalizeWithoutNextIfPossible(driver, wait, link))
            {
                LogApplicationStep(link, "application_completed_via_post_first_next_fallback", true, "Fluxo concluído por fallback após transição do primeiro Avançar.");
                return true;
            }

            var activeModal = TryFindActiveEasyApplyModal(driver);
            if (activeModal != null)
            {
                Console.WriteLine("[ETAPA] Transição após primeiro Avançar demorou ou mudou layout, mas o modal continua aberto. Prosseguindo para currículo e campos...");
                LogApplicationStep(link, "post_first_next_transition_modal_recovered", true, "Modal ativo após transição do primeiro Avançar; prosseguindo fluxo.");
            }
            else
            {
                Console.WriteLine("[ETAPA] Modal de candidatura não encontrado após primeiro Avançar.");
                if (TryHandleMandatoryGate("post_first_next_transition"))
                {
                    return false;
                }

                return false;
            }
        }

        // Pequena folga adicional para componentes internos do modal renderizarem totalmente.
        PauseBetweenFlowSteps("Processando modal de candidatura");

        // Seleciona currículo na etapa seguinte quando necessário.
        Console.WriteLine("[ETAPA] Tentando selecionar currículo...");
        TrySelectResumeInPopup(driver);
        Thread.Sleep(350);
        LogApplicationStep(link, "resume_step_processed", true, "Etapa de currículo processada.");

        // Preenche campos obrigatórios da etapa de currículo se houver
        TryAutoFillMandatoryFieldsInModal(driver, link, "etapa_curriculo_pre");
        Thread.Sleep(250);

        // Segundo avanço no popup após seleção de currículo.
        Console.WriteLine("[ETAPA] Tentando segundo 'Avançar'...");
        if (!TryClickNextButtonInPopup(driver, wait, "etapa de currículo", link))
        {
            Console.WriteLine("[ETAPA] Segundo 'Avançar' falhou na primeira tentativa; aguardando e tentando novamente...");
            Thread.Sleep(900);
            TrySelectResumeInPopup(driver);
            TryAutoFillMandatoryFieldsInModal(driver, link, "etapa_curriculo_retry");
            Thread.Sleep(350);

            if (!TryClickNextButtonInPopup(driver, wait, "etapa de currículo_retry", link))
            {
                if (TryFinalizeWithoutNextIfPossible(driver, wait, link))
                {
                    LogApplicationStep(link, "application_completed_via_resume_fallback", true, "Fluxo concluído por fallback na etapa de currículo.");
                    return true;
                }

                var activeModal = TryFindActiveEasyApplyModal(driver);
                if (activeModal != null && (HasVisibleNextButton() || HasVisibleReviewButton() || HasVisibleSubmitButton()))
                {
                    Console.WriteLine("[ETAPA] Segundo 'Avançar' não foi clicado, mas modal continua ativo com próximas ações. Prosseguindo...");
                }
                else
                {
                    if (TryHandleMandatoryGate("resume_next_retry"))
                    {
                        return false;
                    }

                    return false;
                }
            }
        }

        // Algumas vagas (ex.: questionários e screening questions) exigem etapas extras com botões Avançar ou Avaliar/Revisar.
        const int maxExtraNextSteps = 12;
        for (var extraStep = 1; extraStep <= maxExtraNextSteps; extraStep++)
        {
            if (HasVisibleSubmitButton())
            {
                break;
            }

            if (!HasVisibleReviewButton() && !HasVisibleNextButton())
            {
                break;
            }

            // SEMPRE seleciona currículo (se aplicável) e auto-preenche campos ANTES de clicar em Avançar ou Revisar/Avaliar!
            TrySelectResumeInPopup(driver);
            TryAutoFillMandatoryFieldsInModal(driver, link, $"extra_step_pre_{extraStep}");
            Thread.Sleep(300);

            if (HasVisibleSubmitButton())
            {
                break;
            }

            if (HasVisibleReviewButton())
            {
                Console.WriteLine($"[ETAPA] Botão 'Revisar/Avaliar' detectado na etapa #{extraStep}. Preenchendo e avançando para revisão...");
                var beforeReviewSig = CaptureApplyStepSignature(driver);
                TryClickReviewButtonInPopup(driver, wait, link);

                if (!ConfirmNextStepProgress(driver, link, $"revisar_extra_{extraStep}", beforeReviewSig))
                {
                    Console.WriteLine($"[ETAPA] 'Revisar/Avaliar' retido por validação na etapa #{extraStep}. Re-preenchendo campos obrigatórios...");
                    TryAutoFillMandatoryFieldsInModal(driver, link, $"revisar_retry_{extraStep}");
                    Thread.Sleep(400);
                    TryClickReviewButtonInPopup(driver, wait, link);
                    Thread.Sleep(700);
                }
                else
                {
                    Thread.Sleep(550);
                }

                if (HasVisibleSubmitButton())
                {
                    break;
                }
                continue;
            }

            Console.WriteLine($"[ETAPA] Detectada etapa adicional #{extraStep}. Tentando 'Avançar'...");
            if (!TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(10)), $"etapa extra {extraStep}", link))
            {
                if (HasVisibleSubmitButton() || HasVisibleReviewButton())
                {
                    continue;
                }

                Console.WriteLine($"[ETAPA] 'Avançar' extra #{extraStep} não pôde ser concluído. Verificando se é possível prosseguir...");
                if (!TryFinalizeWithoutNextIfPossible(driver, wait, link))
                {
                    TryDismissEasyApplyModal(driver, link, $"extra_step_failed_{extraStep}");
                    return false;
                }
                return true;
            }

            Thread.Sleep(650);
            LogApplicationStep(link, $"extra_next_step_{extraStep}_processed", true, $"Etapa adicional #{extraStep} de avanço processada.");
        }

        // Caso ainda haja etapa 'Revisar/Avaliar' visível antes do envio final
        if (!HasVisibleSubmitButton() && HasVisibleReviewButton())
        {
            TryAutoFillMandatoryFieldsInModal(driver, link, "screening_before_review");
            Thread.Sleep(300);
            Console.WriteLine("[ETAPA] Tentando etapa 'Revisar/Avaliar'...");
            if (TryClickReviewButtonInPopup(driver, wait, link))
            {
                PauseBetweenFlowSteps();
            }
        }

        // Aguarda transição para a etapa final de envio.
        PauseBetweenFlowSteps("Preparando envio final");

        IWebElement? FindSubmitButtonCandidate()
        {
            return TryFindSubmitButtonRobust(driver);
        }

        // Tenta localizar botão final de envio da candidatura (wait curto de 6s para não travar 25s em falhas).
        IWebElement? submitButton = null;
        try
        {
            var submitWait = new WebDriverWait(driver, TimeSpan.FromSeconds(6));
            submitButton = submitWait.Until(_ => FindSubmitButtonCandidate());
        }
        catch (WebDriverTimeoutException)
        {
            submitButton = FindSubmitButtonCandidate();
        }

        // Se encontrou o botão de envio, mas ele está desabilitado, captura diagnóstico para entender campos obrigatórios.
        if (submitButton != null)
        {
            try
            {
                var ariaDisabled = (submitButton.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                var disabledAttr = submitButton.GetAttribute("disabled") ?? string.Empty;

                if (!submitButton.Displayed)
                {
                    submitButton = null;
                }
                else if (!submitButton.Enabled || ariaDisabled.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryAutoFillMandatoryFieldsInModal(driver, link, "submit_disabled"))
                    {
                        Thread.Sleep(650);
                        var submitAfterAutoFill = FindSubmitButtonCandidate();
                        if (submitAfterAutoFill != null)
                        {
                            try
                            {
                                var ariaDisabledAfterAutoFill = (submitAfterAutoFill.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                                if (submitAfterAutoFill.Displayed && submitAfterAutoFill.Enabled && !ariaDisabledAfterAutoFill.Equals("true", StringComparison.OrdinalIgnoreCase))
                                {
                                    submitButton = submitAfterAutoFill;
                                    Console.WriteLine("[AUTO-FILL] Botão 'Enviar candidatura' reabilitado após preenchimento automático.");
                                    LogApplicationStep(link, "submit_enabled_after_autofill", true, "Botão Enviar ficou habilitado após preenchimento automático de campos obrigatórios.");
                                }
                            }
                            catch
                            {
                                // Se falhar leitura do estado reavaliado, segue validação padrão.
                            }
                        }
                    }

                    var ariaDisabledAfterRetry = (submitButton.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                    if (submitButton.Displayed && submitButton.Enabled && !ariaDisabledAfterRetry.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        // Submit foi habilitado após preenchimento automático; segue fluxo de envio.
                    }
                    else
                    {
                    var submitDescription = DescribeElementForLog(submitButton);
                    var requiredSummary = BuildRequiredFieldsSummary(driver);

                    Console.WriteLine("Botão de envio identificado, porém desabilitado (provável validação pendente).");
                    Console.WriteLine($"[DEBUG SUBMIT DISABLED] aria-disabled='{ariaDisabled}', disabledAttr='{disabledAttr}', {submitDescription}");
                    Console.WriteLine($"[DEBUG REQUIRED] {requiredSummary}");

                    var diagnosticsDisabled = SaveFailureDiagnostics(driver, link, "submit_disabled_or_validation");
                    LogApplicationStep(link, "submit_disabled_or_validation", false, $"Botão Enviar/Submit presente porém desabilitado. aria-disabled='{ariaDisabled}', disabled='{disabledAttr}'. {submitDescription}. REQUIRED: {requiredSummary}", diagnosticsDisabled.HtmlPath, diagnosticsDisabled.ScreenshotPath);
                    IgnoreJobLinkInCurrentRun(link, "SUBMIT_DISABLED", persistToDisk: false);
                    // Não marca como indisponível definitivo no banco para não queimar a vaga
                    TryDismissEasyApplyModal(driver, link, "mandatory_fields_submit_disabled");
                    return false;
                    }
                }
            }
            catch
            {
                // Se falhar leitura de atributos, segue fluxo normal.
            }
        }

        // Se não encontrou o botão de envio, provavelmente há perguntas/etapas obrigatórias.
        if (submitButton == null)
        {
            var autoFilledBeforeSkip = TryAutoFillMandatoryFieldsInModal(driver, link, "submit_not_found");
            if (autoFilledBeforeSkip)
            {
                Thread.Sleep(650);

                for (var autoFillAdvanceAttempt = 1; autoFillAdvanceAttempt <= 2; autoFillAdvanceAttempt++)
                {
                    if (HasVisibleSubmitButton() || !HasVisibleNextButton())
                    {
                        break;
                    }

                    Console.WriteLine($"[AUTO-FILL] Tentando 'Avançar' após preenchimento automático (tentativa {autoFillAdvanceAttempt}/2)...");
                    if (!TryClickNextButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(10)), $"autofill_no_submit_{autoFillAdvanceAttempt}", link))
                    {
                        break;
                    }

                    Thread.Sleep(600);
                }

                if (!HasVisibleSubmitButton())
                {
                    TryClickReviewButtonInPopup(driver, new WebDriverWait(driver, TimeSpan.FromSeconds(7)), link);
                    Thread.Sleep(500);
                }

                submitButton = FindSubmitButtonCandidate();
                if (submitButton != null)
                {
                    try
                    {
                        var ariaDisabledAfterAutoFill = (submitButton.GetAttribute("aria-disabled") ?? string.Empty).Trim();
                        if (!submitButton.Displayed || !submitButton.Enabled || ariaDisabledAfterAutoFill.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            submitButton = null;
                        }
                        else
                        {
                            Console.WriteLine("[AUTO-FILL] Botão 'Enviar candidatura' encontrado após preenchimento automático.");
                            LogApplicationStep(link, "submit_found_after_autofill", true, "Botão Enviar encontrado após preenchimento automático de campos obrigatórios.");
                        }
                    }
                    catch
                    {
                        submitButton = null;
                    }
                }
            }
        }

        if (submitButton == null)
        {
            Console.WriteLine("Botão de envio direto não encontrado. Tentando verificar se há botões de avanço ou revisão...");
            if (HasVisibleReviewButton())
            {
                TryClickReviewButtonInPopup(driver, wait, link);
                Thread.Sleep(700);
                submitButton = FindSubmitButtonCandidate();
            }
            else if (HasVisibleNextButton())
            {
                TryAutoFillMandatoryFieldsInModal(driver, link, "submit_null_fallback_autofill");
                Thread.Sleep(300);
                TryClickNextButtonInPopup(driver, wait, "submit_null_fallback_advance", link);
                Thread.Sleep(700);
                submitButton = FindSubmitButtonCandidate();
            }
        }

        if (submitButton == null)
        {
            Console.WriteLine("Vaga com etapas não finalizáveis detectada.");
            var requiredSummary = BuildRequiredFieldsSummary(driver);
            Console.WriteLine($"[DEBUG REQUIRED] {requiredSummary}");
            var diagnostics = SaveFailureDiagnostics(driver, link, "submit_not_found");
            LogApplicationStep(link, "submit_not_found", false, $"Botão de envio não localizado após tentativas de avanço. REQUIRED: {requiredSummary}", diagnostics.HtmlPath, diagnostics.ScreenshotPath);
            IgnoreJobLinkInCurrentRun(link, "SUBMIT_NOT_FOUND", persistToDisk: false);
            // Não marca como indisponível definitivo no banco para não queimar a vaga
            TryDismissEasyApplyModal(driver, link, "submit_not_found");
            return false;
        }

        // Scroll e clique no botão final para enviar candidatura.
        TryScrollElementIntoViewHumanized(driver, submitButton);
        ClickElementRobust(driver, submitButton);
        Console.WriteLine("Botão 'Enviar candidatura' clicado com sucesso.");
        LogApplicationStep(link, "submit_clicked", true, "Botão Enviar candidatura clicado com sucesso.");

        // Aguarda confirmação de envio e clica no botão "Concluído" (ou fecha modal)
        WaitForAndDismissPostSubmitSuccessModal(driver, link);

        return true;
    }

    private static void WaitForAndDismissPostSubmitSuccessModal(IWebDriver driver, string link)
    {
        try
        {
            Console.WriteLine("[ETAPA] Aguardando confirmação de envio e fechamento do modal...");
            var deadline = DateTime.UtcNow.AddSeconds(12);
            var startTime = DateTime.UtcNow;
            var clickedDone = false;

            while (DateTime.UtcNow < deadline)
            {
                // 1. Tenta encontrar e clicar no botão "Concluído" / "Done" via JS nativo
                try
                {
                    if (driver is IJavaScriptExecutor js)
                    {
                        var jsResult = js.ExecuteScript(@"
                            const buttons = Array.from(document.querySelectorAll('button, [role=""button""]'));
                            const doneBtn = buttons.find(b => {
                                const t = (b.innerText || b.textContent || '').trim().toLowerCase();
                                const a = (b.getAttribute('aria-label') || '').toLowerCase();
                                return t === 'concluído' || t === 'concluido' || t === 'done' || 
                                       a === 'concluído' || a === 'concluido' || a === 'done' ||
                                       t.includes('concluído') || t.includes('concluido') || a.includes('concluído');
                            });
                            if (doneBtn && doneBtn.offsetParent !== null) {
                                doneBtn.scrollIntoView({block: 'center'});
                                doneBtn.click();
                                return true;
                            }
                            return false;
                        ");

                        if (jsResult is bool clicked && clicked)
                        {
                            Console.WriteLine("[SUCESSO] Botão 'Concluído' clicado com sucesso via JS!");
                            LogApplicationStep(link, "done_button_clicked", true, "Botão Concluído clicado após envio.");
                            clickedDone = true;
                            Thread.Sleep(800);
                            break;
                        }
                    }

                    // 1b. Fallback Selenium XPath para "Concluído"
                    var doneCandidates = driver.FindElements(By.XPath(
                        "//button[contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚÂÊÔÃÕÀ', 'abcdefghijklmnopqrstuvwxyzáéíóúâêôãõà'), 'concluído') or " +
                        "contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZÁÉÍÓÚÂÊÔÃÕÀ', 'abcdefghijklmnopqrstuvwxyzáéíóúâêôãõà'), 'concluido') or " +
                        "contains(translate(normalize-space(.), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'), 'done') or " +
                        "contains(@aria-label,'Concluído') or contains(@aria-label,'Concluido') or contains(@aria-label,'Done')]"
                    ));

                    foreach (var btn in doneCandidates)
                    {
                        if (btn.Displayed && btn.Enabled)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", btn);
                            Thread.Sleep(150);
                            ClickElementRobust(driver, btn);
                            Console.WriteLine("[SUCESSO] Botão 'Concluído' clicado com sucesso via Selenium!");
                            LogApplicationStep(link, "done_button_clicked", true, "Botão Concluído clicado após envio.");
                            clickedDone = true;
                            Thread.Sleep(800);
                            break;
                        }
                    }

                    if (clickedDone)
                    {
                        break;
                    }
                }
                catch { }

                // 2. Se aparecer o prompt "Salvar esta candidatura?", clica em "Descartar"
                try
                {
                    if (driver is IJavaScriptExecutor js)
                    {
                        var jsDiscard = js.ExecuteScript(@"
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
                                return true;
                            }
                            return false;
                        ");
                        if (jsDiscard is bool discarded && discarded)
                        {
                            Console.WriteLine("[POPUP] Botão 'Descartar' clicado via JS.");
                            Thread.Sleep(500);
                            break;
                        }
                    }
                }
                catch { }

                // 3. Só encerra por ausência de modais após aguardar pelo menos 2.5s (dá tempo do LinkedIn renderizar a confirmação de envio)
                if ((DateTime.UtcNow - startTime).TotalSeconds >= 2.5)
                {
                    try
                    {
                        var openModals = driver.FindElements(By.CssSelector("dialog[open], div[role='dialog'], div.jobs-easy-apply-modal"));
                        var anyVisible = openModals.Any(m => { try { return m.Displayed; } catch { return false; } });
                        if (!anyVisible)
                        {
                            break;
                        }
                    }
                    catch { }
                }

                Thread.Sleep(400);
            }

            Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AVISO] Finalização do modal pós-envio: {ex.Message}");
        }
    }

    // Abre cada link vindo do banco e executa o fluxo de candidatura simplificada.
    private static void ApplySimplifiedJobsFromDatabase(IWebDriver driver, int maxJobsToProcess = 0)
    {
        try
        {
            if (maxJobsToProcess <= 0)
            {
                Console.WriteLine("Candidaturas automáticas desabilitadas para este ciclo (maxJobsToProcess <= 0).");
                return;
            }

            // Busca links de vagas simplificadas já persistidas no banco.
            var links = GetSimplifiedJobLinksFromDatabase();
            Console.WriteLine($"Total de links com candidatura simplificada no banco: {links.Count}");

            // Wait usado para localizar o botão de candidatura em cada página.
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));

            int processedJobs = 0;

            // Percorre cada URL retornada no SELECT.
            foreach (var link in links)
            {
                int successfulInCycle;
                lock (SuccessfulJobLinksLock)
                {
                    successfulInCycle = SuccessfulJobLinksCurrentCycle.Count;
                }

                if (maxJobsToProcess > 0 && successfulInCycle >= maxJobsToProcess)
                {
                    Console.WriteLine($"Meta de candidaturas enviadas com sucesso no ciclo atingida ({successfulInCycle}/{maxJobsToProcess}).");
                    break;
                }

                int maxAttemptsLimit = maxJobsToProcess > 0 ? Math.Max(maxJobsToProcess * 5, 80) : 80;
                if (processedJobs >= maxAttemptsLimit)
                {
                    Console.WriteLine($"Limite máximo de tentativas do ciclo atingido ({processedJobs} tentativas, {successfulInCycle} enviadas com sucesso).");
                    break;
                }

                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (IsIgnoredJobLinkInCurrentRun(link))
                {
                    Console.WriteLine("Pulando vaga já marcada como ignorada nesta execução.");
                    continue;
                }

                if (IsBudgetExhaustedJobLink(link))
                {
                    Console.WriteLine("Pulando vaga com eBP=BUDGET_EXHAUSTED_JOB (candidatura indisponível).");
                    MarkJobAsUnavailable(link, "BUDGET_EXHAUSTED_JOB");
                    LogApplicationStep(link, "job_skipped_budget_exhausted", true, "Vaga ignorada por eBP=BUDGET_EXHAUSTED_JOB antes da navegação.");
                    continue;
                }

                processedJobs++;

                try
                {
                    Console.WriteLine($"Abrindo vaga do banco: {link}");
                    LogApplicationStep(link, "job_open_started", true, "Abrindo link da vaga.");

                    // Navega para a página da vaga.
                    driver.Navigate().GoToUrl(link);

                    // Aguarda carregamento e estabilização da página da vaga.
                    WaitForJobPageReady(driver);
                    wait.Until(d => d.FindElement(By.TagName("body")));

                    // Executa fluxo completo até o envio final para vagas sem perguntas obrigatórias.
                    var flowCompleted = TryCompleteEasyApplyWithoutQuestions(driver, wait, link);
                    if (!flowCompleted)
                    {
                        Console.WriteLine("Fluxo de candidatura não concluído para esta vaga (ignorando e seguindo para a próxima).");
                        PauseBetweenApplications(link, flowCompleted: false);
                        continue;
                    }

                    // Marca no banco somente quando o fluxo do popup foi concluído.
                    var appliedMarkedInDatabase = MarkJobAsApplied(link);
                    RegisterSuccessfulApplication(link, "DATABASE_FLOW");

                    if (appliedMarkedInDatabase)
                    {
                        Console.WriteLine("Vaga marcada como candidatura_enviada = TRUE no banco.");
                        LogApplicationStep(link, "job_marked_as_applied", true, "Vaga marcada como candidatura enviada.");
                    }
                    else
                    {
                        Console.WriteLine("[ALERTA] Candidatura enviada, mas nenhuma linha foi atualizada no banco para candidatura_enviada.");
                        LogApplicationStep(link, "job_marked_as_applied", false, "Candidatura enviada no site, mas nenhuma linha foi encontrada para atualizar candidatura_enviada.");
                    }

                    PauseBetweenApplications(link, flowCompleted: true);
                }
                catch (WebDriverTimeoutException ex)
                {
                    Console.WriteLine($"Timeout ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_timeout");
                    LogApplicationStep(link, "job_processing_timeout", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
                catch (StaleElementReferenceException ex)
                {
                    Console.WriteLine($"Elemento stale ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_stale_element");
                    LogApplicationStep(link, "job_processing_stale_element", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
                catch (NoSuchWindowException ex)
                {
                    Console.WriteLine($"Janela do navegador indisponível ao processar vaga {link}: {ex.Message}");
                    LogApplicationStep(link, "job_processing_no_window", false, ex.Message);
                    break;
                }
                catch (WebDriverException ex)
                {
                    Console.WriteLine($"Erro de WebDriver ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_webdriver_exception");
                    LogApplicationStep(link, "job_processing_webdriver_exception", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar vaga {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_exception");
                    LogApplicationStep(link, "job_processing_exception", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            // Loga erro geral da etapa de candidatura automática por links do banco.
            Console.WriteLine($"Erro na rotina de candidatura por banco: {ex.Message}");
        }
    }

    // Executa candidatura direta a partir dos links coletados no ciclo atual (sem usar banco).
    private static void ApplySimplifiedJobsFromLinks(IWebDriver driver, List<string> links, int maxJobsToProcess = 0)
    {
        try
        {
            if (maxJobsToProcess <= 0)
            {
                Console.WriteLine("Candidaturas automáticas desabilitadas para este ciclo (maxJobsToProcess <= 0).");
                return;
            }

            Console.WriteLine($"Total de links para candidatura direta: {links.Count}");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(25));
            int processedJobs = 0;

            foreach (var link in links)
            {
                int successfulInCycle;
                lock (SuccessfulJobLinksLock)
                {
                    successfulInCycle = SuccessfulJobLinksCurrentCycle.Count;
                }

                if (maxJobsToProcess > 0 && successfulInCycle >= maxJobsToProcess)
                {
                    Console.WriteLine($"Meta de candidaturas enviadas com sucesso no ciclo atingida ({successfulInCycle}/{maxJobsToProcess}).");
                    break;
                }

                int maxAttemptsLimit = maxJobsToProcess > 0 ? Math.Max(maxJobsToProcess * 5, 80) : 80;
                if (processedJobs >= maxAttemptsLimit)
                {
                    Console.WriteLine($"Limite máximo de tentativas do ciclo atingido ({processedJobs} tentativas, {successfulInCycle} enviadas com sucesso).");
                    break;
                }

                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (IsIgnoredJobLinkInCurrentRun(link))
                {
                    Console.WriteLine("Pulando vaga já marcada como ignorada nesta execução.");
                    continue;
                }

                if (IsBudgetExhaustedJobLink(link))
                {
                    Console.WriteLine("Pulando vaga com eBP=BUDGET_EXHAUSTED_JOB (candidatura indisponível).");
                    MarkJobAsUnavailable(link, "BUDGET_EXHAUSTED_JOB");
                    LogApplicationStep(link, "job_skipped_budget_exhausted", true, "Vaga ignorada por eBP=BUDGET_EXHAUSTED_JOB antes da navegação.");
                    continue;
                }

                processedJobs++;

                try
                {
                    Console.WriteLine($"Abrindo vaga da lista direta: {link}");
                    driver.Navigate().GoToUrl(link);
                    WaitForJobPageReady(driver);
                    wait.Until(d => d.FindElement(By.TagName("body")));

                    var flowCompleted = TryCompleteEasyApplyWithoutQuestions(driver, wait, link);
                    if (!flowCompleted)
                    {
                        Console.WriteLine("Fluxo de candidatura não concluído para esta vaga (seguindo para a próxima).");
                        PauseBetweenApplications(link, flowCompleted: false);
                        continue;
                    }

                    RegisterSuccessfulApplication(link, "DIRECT_LIST_FLOW");
                    Console.WriteLine("Fluxo concluído para a vaga na lista direta.");
                    PauseBetweenApplications(link, flowCompleted: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar vaga (lista direta) {link}: {ex.Message}");
                    var diagnostics = SaveFailureDiagnostics(driver, link, "job_processing_exception_direct_list");
                    LogApplicationStep(link, "job_processing_exception_direct_list", false, ex.Message, diagnostics.HtmlPath, diagnostics.ScreenshotPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro na rotina de candidatura direta por lista: {ex.Message}");
        }
    }
}
