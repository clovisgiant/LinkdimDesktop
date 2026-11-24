using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

partial class Program
{
    private static bool TryAutoFillMandatoryFieldsInModal(IWebDriver driver, string link, string context)
    {
        if (!AutoFillMandatoryFieldsEnabled)
        {
            return false;
        }

        try
        {
            var modal = TryFindActiveEasyApplyModal(driver);
            if (modal == null)
            {
                return false;
            }

            // 1. Primeiro resolve grupos de radio e checkboxes (incluindo inputs SDUI com opacity:0 onde field.Displayed é false)
            var updatedFields = 0;
            var updatedKeys = new List<string>();

            updatedFields += TryAutoFillAllRadioGroupsInModal(driver, modal, updatedKeys);
            updatedFields += TryAutoFillAllCheckboxesInModal(driver, modal, updatedKeys);

            // 2. Busca todos os campos interativos de texto/select/textarea no modal ativo
            var formFields = modal.FindElements(By.CssSelector(
                "input:not([type='hidden']):not([type='submit']):not([type='button']):not([type='file']):not([type='radio']):not([type='checkbox']), select, textarea"));

            foreach (var field in formFields)
            {
                try
                {
                    if (IsElementDisabledOrReadonly(field))
                    {
                        continue;
                    }

                    if (!IsElementVisibleOrRenderable(driver, field))
                    {
                        continue;
                    }

                    var tag = (field.TagName ?? string.Empty).Trim().ToLowerInvariant();
                    var type = (field.GetAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();
                    var hints = BuildFieldHints(field, modal);
                    var fieldKey = BuildFieldKey(field, tag, type, hints);

                    if (tag == "select")
                    {
                        if (TryFillRequiredSelectField(driver, field, hints))
                        {
                            updatedFields++;
                            updatedKeys.Add(fieldKey);
                        }

                        continue;
                    }

                    if (type == "hidden" ||
                        type == "file" ||
                        type == "submit" ||
                        type == "button")
                    {
                        continue;
                    }

                    if (!IsFieldValueEmpty(field))
                    {
                        continue;
                    }

                    var value = ResolveMandatoryFieldValue(hints, tag, type);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        // Fallback universal para nunca travar em campos vazios não mapeados
                        value = (type == "number" || IsYearsExperienceQuestion(NormalizeForMatch(hints)))
                            ? Math.Max(1, AutoFillDefaultYearsExperience).ToString()
                            : (!string.IsNullOrWhiteSpace(AutoFillDefaultGenericText) ? AutoFillDefaultGenericText : "Sim");
                    }

                    if (TrySetInputValue(driver, field, value))
                    {
                        updatedFields++;
                        updatedKeys.Add(fieldKey);
                    }
                }
                catch
                {
                    // Ignora campo pontual e segue tentativa nos demais
                }
            }

            if (updatedFields <= 0)
            {
                return false;
            }

            Thread.Sleep(350);

            var keySummary = string.Join("; ", updatedKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10));

            Console.WriteLine($"[AUTO-FILL] Campos obrigatórios preenchidos automaticamente ({updatedFields}) em '{context}'.");
            LogApplicationStep(link,
                $"mandatory_fields_autofilled_{context}",
                true,
                $"Campos obrigatórios preenchidos automaticamente ({updatedFields}). Campos: {keySummary}");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTO-FILL] Falha ao tentar preencher campos obrigatórios: {ex.Message}");
            return false;
        }
    }

    private static bool TryFillRequiredSelectField(IWebDriver driver, IWebElement selectField, string hints)
    {
        try
        {
            var selectElement = new SelectElement(selectField);
            var currentOption = selectElement.SelectedOption;
            var normalizedHints = NormalizeForMatch(hints);

            var candidates = selectElement.Options
                .Where(opt =>
                {
                    try
                    {
                        var disabledAttr = opt.GetAttribute("disabled") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(disabledAttr))
                        {
                            return false;
                        }

                        var text = (opt.Text ?? string.Empty).Trim();
                        var value = (opt.GetAttribute("value") ?? string.Empty).Trim();
                        return !IsPlaceholderOption(text, value);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            if (candidates.Count == 0)
            {
                return false;
            }

            var isEmailQuestion = candidates.Any(opt => (opt.Text ?? string.Empty).Contains("@") || (opt.GetAttribute("value") ?? string.Empty).Contains("@")) ||
                                  normalizedHints.Contains("email", StringComparison.Ordinal) || 
                                  normalizedHints.Contains("e-mail", StringComparison.Ordinal);

            if (currentOption != null)
            {
                var currentText = (currentOption.Text ?? string.Empty).Trim();
                var currentValue = (currentOption.GetAttribute("value") ?? string.Empty).Trim();

                if (isEmailQuestion)
                {
                    if (!string.IsNullOrWhiteSpace(AutoFillDefaultEmail) &&
                        (currentText.Contains(AutoFillDefaultEmail, StringComparison.OrdinalIgnoreCase) ||
                         currentValue.Contains(AutoFillDefaultEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }
                    else if (string.IsNullOrWhiteSpace(AutoFillDefaultEmail) && !IsPlaceholderOption(currentText, currentValue))
                    {
                        return false;
                    }
                }
                else if (!IsPlaceholderOption(currentText, currentValue))
                {
                    return false;
                }
            }

            IWebElement? chosen = null;

            // 1. Sim ou Não -> sempre Sim
            if (HasYesNoOptions(candidates))
            {
                chosen = FindYesOption(candidates);
            }
            // 2. Pergunta de e-mail (dropdown com emails vinculados da conta)
            else if (isEmailQuestion)
            {
                if (!string.IsNullOrWhiteSpace(AutoFillDefaultEmail))
                {
                    chosen = FindOptionContaining(candidates, AutoFillDefaultEmail);
                }

                if (chosen == null && !string.IsNullOrWhiteSpace(AutoFillDefaultFirstName))
                {
                    var fnLower = AutoFillDefaultFirstName.ToLowerInvariant();
                    chosen = candidates.FirstOrDefault(opt =>
                    {
                        var txt = (opt.Text ?? string.Empty).ToLowerInvariant();
                        var val = (opt.GetAttribute("value") ?? string.Empty).ToLowerInvariant();
                        return (txt.Contains(fnLower) || val.Contains(fnLower)) && (txt.Contains("@") || val.Contains("@"));
                    });
                }
            }
            // 3. País / DDI do telefone (+55 Brasil)
            else if (IsCountryQuestion(normalizedHints) || normalizedHints.Contains("codigo", StringComparison.Ordinal) || normalizedHints.Contains("pais", StringComparison.Ordinal) || normalizedHints.Contains("phone", StringComparison.Ordinal) || normalizedHints.Contains("tel", StringComparison.Ordinal))
            {
                chosen = FindOptionContaining(candidates, "brasil", "brazil", "+55");
            }
            // 4. Dados sensíveis (gênero, raça, deficiência)
            else if (IsSensitiveDisclosureField(normalizedHints))
            {
                chosen = FindOptionContaining(candidates, "prefiro nao", "prefer not", "nao informar", "not say");
            }
            // 5. Proficiência / Idiomas
            else if (normalizedHints.Contains("proficiency", StringComparison.Ordinal) || normalizedHints.Contains("proficiencia", StringComparison.Ordinal) || normalizedHints.Contains("idioma", StringComparison.Ordinal) || normalizedHints.Contains("ingles", StringComparison.Ordinal) || normalizedHints.Contains("english", StringComparison.Ordinal))
            {
                chosen = FindOptionContaining(candidates, "fluent", "fluente", "native", "nativo", "advanced", "avancado", "professional");
            }
            // 6. Anos de experiência em select
            else if (IsYearsExperienceQuestion(normalizedHints))
            {
                chosen = FindBestYearsOption(candidates, AutoFillDefaultYearsExperience);
            }
            // 7. Autorização de trabalho / Visto
            else if (IsWorkAuthorizationQuestion(normalizedHints))
            {
                chosen = AutoFillDefaultWorkAuthorization ? FindYesOption(candidates) : FindNoOption(candidates);
            }
            else if (IsVisaSponsorshipQuestion(normalizedHints))
            {
                chosen = AutoFillDefaultNeedVisaSponsorship ? FindYesOption(candidates) : FindNoOption(candidates);
            }
            // 8. Escolaridade / Formação
            else if (normalizedHints.Contains("escolaridade", StringComparison.Ordinal) || normalizedHints.Contains("education", StringComparison.Ordinal) || normalizedHints.Contains("grau", StringComparison.Ordinal) || normalizedHints.Contains("degree", StringComparison.Ordinal))
            {
                chosen = FindOptionContaining(candidates, "superior", "graduacao", "bacharel", "bachelor", "pos", "master");
            }
            // 9. Como soube da vaga
            else if (normalizedHints.Contains("hear about us", StringComparison.Ordinal) || normalizedHints.Contains("como soube", StringComparison.Ordinal) || normalizedHints.Contains("onde viu", StringComparison.Ordinal))
            {
                chosen = FindOptionContaining(candidates, "linkedin", "site", "internet");
            }

            // Fallback universal para campos obrigatórios: seleciona a primeira opção válida para não travar o avanço
            if (chosen == null && candidates.Count > 0)
            {
                chosen = candidates[0];
            }

            if (chosen == null)
            {
                return false;
            }

            var chosenValue = (chosen.GetAttribute("value") ?? string.Empty).Trim();
            var chosenText = (chosen.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(chosenValue))
            {
                selectElement.SelectByValue(chosenValue);
            }
            else
            {
                selectElement.SelectByText(chosenText);
            }

            // Garante despacho nativo de eventos do React/SDUI via JavaScript
            try
            {
                if (driver is IJavaScriptExecutor js)
                {
                    js.ExecuteScript(@"
                        const select = arguments[0];
                        const targetVal = arguments[1];
                        const targetText = arguments[2];
                        if (select) {
                            const setter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')?.set;
                            if (setter && targetVal) {
                                setter.call(select, targetVal);
                            } else if (targetVal) {
                                select.value = targetVal;
                            }
                            for (let i = 0; i < select.options.length; i++) {
                                if ((targetVal && select.options[i].value === targetVal) || 
                                    (targetText && select.options[i].text.trim() === targetText.trim())) {
                                    select.selectedIndex = i;
                                    select.options[i].selected = true;
                                    break;
                                }
                            }
                            select.dispatchEvent(new Event('input', { bubbles: true }));
                            select.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                    ", selectField, chosenValue, chosenText);
                }
            }
            catch { }

            var selectedNow = selectElement.SelectedOption;
            if (selectedNow == null)
            {
                return false;
            }

            var selectedText = (selectedNow.Text ?? string.Empty).Trim();
            var selectedValue = (selectedNow.GetAttribute("value") ?? string.Empty).Trim();
            return !IsPlaceholderOption(selectedText, selectedValue);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsElementVisibleOrRenderable(IWebDriver driver, IWebElement element)
    {
        try
        {
            if (element.Displayed)
            {
                return true;
            }

            if (driver is IJavaScriptExecutor js)
            {
                var res = js.ExecuteScript(@"
                    const el = arguments[0];
                    if (!el) return false;
                    if (el.closest('[hidden], [aria-hidden=""true""]')) return false;
                    const s = window.getComputedStyle(el);
                    if (s.display === 'none' || s.visibility === 'hidden') return false;
                    el.scrollIntoView({ block: 'center' });
                    const r = el.getBoundingClientRect();
                    return r.width > 0 && r.height > 0;
                ", element);
                return res is bool b && b;
            }
        }
        catch { }

        return false;
    }

    private static int TryAutoFillAllRadioGroupsInModal(IWebDriver driver, IWebElement modal, List<string> updatedKeys)
    {
        try
        {
            if (driver is not IJavaScriptExecutor js)
            {
                return 0;
            }

            var preferWorkAuth = AutoFillDefaultWorkAuthorization;
            var needVisa = AutoFillDefaultNeedVisaSponsorship;
            var firstNameToken = (AutoFillDefaultFirstName ?? string.Empty).Trim().ToLowerInvariant();

            var result = js.ExecuteScript(@"
                const modal = arguments[0];
                const preferWorkAuth = arguments[1];
                const needVisa = arguments[2];
                const firstNameToken = arguments[3] || '';
                if (!modal) return [];

                const norm = (s) => (s || '')
                    .toString()
                    .normalize('NFD')
                    .replace(/[\u0300-\u036f]/g, '')
                    .toLowerCase()
                    .replace(/\s+/g, ' ')
                    .trim();

                const isVisibleGroupItem = (r) => {
                    if (!r || r.disabled || r.getAttribute('aria-disabled') === 'true') return false;
                    if (r.closest('[hidden], [aria-hidden=""true""]')) return false;
                    const lbl = r.id ? modal.querySelector('label[for=""' + CSS.escape(r.id) + '""]') : null;
                    const target = lbl || r.parentElement || r;
                    const s = window.getComputedStyle(target);
                    if (s.display === 'none' || s.visibility === 'hidden') return false;
                    const rect = target.getBoundingClientRect();
                    return rect.width > 0 && rect.height > 0;
                };

                const getOptionText = (r) => {
                    if (r.id) {
                        const lbl = modal.querySelector('label[for=""' + CSS.escape(r.id) + '""]');
                        if (lbl) {
                            const lt = (lbl.innerText || lbl.textContent || '').trim();
                            if (lt) return lt;
                        }
                    }
                    let p = r.parentElement;
                    while (p && p !== modal && p.tagName !== 'FIELDSET') {
                        const radiosInside = p.querySelectorAll('input[type=""radio""]').length;
                        if (radiosInside === 1) {
                            const txt = (p.innerText || p.textContent || '').trim();
                            if (txt) return txt;
                        }
                        p = p.parentElement;
                    }
                    return (r.getAttribute('value') || r.getAttribute('aria-label') || '').trim();
                };

                const getQuestionText = (firstRadio, fs) => {
                    const parts = [];
                    const aria = firstRadio.getAttribute('aria-label') || '';
                    if (aria) parts.push(aria);
                    if (fs) {
                        const leg = fs.querySelector('legend');
                        if (leg) parts.push(leg.innerText || leg.textContent || '');
                        const prev = fs.previousElementSibling;
                        if (prev) parts.push(prev.innerText || prev.textContent || '');
                        if (fs.parentElement) {
                            const pEl = fs.parentElement.querySelector('p, label, span');
                            if (pEl && !fs.contains(pEl)) parts.push(pEl.innerText || pEl.textContent || '');
                        }
                    }
                    return parts.join(' ').trim();
                };

                const allRadios = Array.from(modal.querySelectorAll('input[type=""radio""]'));
                const groups = new Map();
                let anonIdx = 0;
                const fsMap = new Map();

                for (const r of allRadios) {
                    if (!isVisibleGroupItem(r)) continue;
                    const fs = r.closest('fieldset');
                    let key = (r.getAttribute('name') || '').trim();
                    if (!key) {
                        if (fs) {
                            if (!fsMap.has(fs)) fsMap.set(fs, 'fs_' + (anonIdx++));
                            key = fsMap.get(fs);
                        } else {
                            key = 'single_' + (anonIdx++);
                        }
                    }
                    if (!groups.has(key)) groups.set(key, { radios: [], fs });
                    groups.get(key).radios.push(r);
                }

                const filled = [];

                for (const [key, grp] of groups.entries()) {
                    const radios = grp.radios;
                    if (radios.length === 0) continue;

                    // Se algum radio do grupo já está selecionado (ex.: currículo padrão), não mexe
                    if (radios.some(r => r.checked)) continue;

                    const qNorm = norm(getQuestionText(radios[0], grp.fs));
                    const opts = radios.map(r => ({
                        el: r,
                        raw: getOptionText(r),
                        n: norm(getOptionText(r)),
                        aria: norm(r.getAttribute('aria-label') || '')
                    }));

                    // Caso currículo (.pdf / .doc)
                    if (opts.some(o => o.aria.includes('.pdf') || o.n.includes('.pdf') || o.aria.includes('.doc'))) {
                        let chosenResume = (firstNameToken ? opts.find(o => o.aria.includes(firstNameToken) || o.n.includes(firstNameToken)) : null) || opts[0];
                        if (chosenResume) {
                            const r = chosenResume.el;
                            const lbl = r.id ? modal.querySelector('label[for=""' + CSS.escape(r.id) + '""]') : null;
                            (lbl || r).scrollIntoView({ block: 'center' });
                            if (lbl) lbl.click();
                            if (!r.checked) r.click();
                            if (r.checked) filled.push('radio:resume:' + (chosenResume.raw || key).slice(0, 50));
                        }
                        continue;
                    }

                    let chosen = null;

                    const isNo = (s) => s === 'nao' || s === 'no' || s === 'false' || s.startsWith('nao ') || s.startsWith('nao,') || s.startsWith('no ') || s.startsWith('no,');
                    const isYes = (s) => s === 'sim' || s === 'yes' || s === 'true' || s.startsWith('sim ') || s.startsWith('sim,') || s.startsWith('yes ') || s.startsWith('yes,') || s.includes('concordo') || s.includes('aceito') || s.includes('autorizo');

                    // 1. Visto / Sponsorship -> Não
                    if (qNorm.includes('sponsor') || qNorm.includes('patrocin') || qNorm.includes('visa')) {
                        chosen = needVisa ? opts.find(o => isYes(o.n)) : opts.find(o => isNo(o.n));
                    }
                    // 2. Autorização de trabalho -> Sim
                    else if (qNorm.includes('authorized') || qNorm.includes('autoriza') || qNorm.includes('work permit') || qNorm.includes('direito de trabalhar')) {
                        chosen = preferWorkAuth ? opts.find(o => isYes(o.n)) : opts.find(o => isNo(o.n));
                    }
                    // 3. Dados sensíveis / PCD / diversidade -> Prefiro não informar ou Não (para PCD/veterano)
                    else if (qNorm.includes('gender') || qNorm.includes('genero') || qNorm.includes('sexo') || qNorm.includes('race') || qNorm.includes('raca') || qNorm.includes('ethnic') || qNorm.includes('etnia') || qNorm.includes('veteran') || qNorm.includes('disability') || qNorm.includes('deficiencia') || qNorm.includes('pcd')) {
                        chosen = opts.find(o => o.n.includes('prefiro nao') || o.n.includes('prefer not') || o.n.includes('nao informar') || o.n.includes('decline'));
                        if (!chosen && (qNorm.includes('disability') || qNorm.includes('deficiencia') || qNorm.includes('pcd') || qNorm.includes('veteran'))) {
                            chosen = opts.find(o => isNo(o.n));
                        }
                    }
                    // 4. Inglês / Idioma / Proficiência (ex.: A1/A2, B1/B2, B2+, C1/C2, Avançado, Intermediário)
                    else if (qNorm.includes('english') || qNorm.includes('ingles') || qNorm.includes('idioma') || qNorm.includes('language') || qNorm.includes('proficiency') || qNorm.includes('proficiencia') || opts.some(o => o.n === 'b1/b2' || o.n === 'b2+' || o.n === 'c1/c2')) {
                        const langPrefs = ['b2+', 'c1', 'avancado', 'advanced', 'fluent', 'fluente', 'professional', 'profissional', 'b2', 'b1/b2', 'intermediario', 'intermediate'];
                        for (const p of langPrefs) {
                            chosen = opts.find(o => o.n.includes(p));
                            if (chosen) break;
                        }
                    }
                    // 5. Disponibilidade / Início / Notice period
                    else if (qNorm.includes('available to start') || qNorm.includes('when would you') || qNorm.includes('quando pode') || qNorm.includes('disponibilidade') || qNorm.includes('notice') || qNorm.includes('aviso prev')) {
                        const startPrefs = ['imediata', 'immediate', 'now', 'agora', '1 week', '1 semana', 'two weeks', '2 weeks', 'duas semanas', '15 dias'];
                        for (const p of startPrefs) {
                            chosen = opts.find(o => o.n.includes(p));
                            if (chosen) break;
                        }
                        if (!chosen) chosen = opts[0];
                    }

                    // 6. Sim / Yes explícito
                    if (!chosen) {
                        chosen = opts.find(o => isYes(o.n));
                    }

                    // 7. Opções de frequência/experiência positiva (evita 'Nunca', 'Não', 'No', 'Never')
                    if (!chosen) {
                        const positivePrefs = ['frequentemente', 'significativa', 'extensa', 'avancad', 'senior', 'pleno', 'strong', 'extensive'];
                        for (const p of positivePrefs) {
                            chosen = opts.find(o => o.n.includes(p));
                            if (chosen) break;
                        }
                    }

                    if (!chosen) {
                        chosen = opts.find(o => !isNo(o.n) && !o.n.includes('nunca') && !o.n.includes('never') && !o.n.includes('nao tenho') && !o.n.includes('nao possuo') && !o.n.includes('none'));
                    }

                    if (!chosen) {
                        chosen = opts[0];
                    }

                    if (chosen && chosen.el) {
                        const r = chosen.el;
                        const lbl = r.id ? modal.querySelector('label[for=""' + CSS.escape(r.id) + '""]') : null;
                        (lbl || r).scrollIntoView({ block: 'center' });
                        if (lbl) {
                            try { lbl.click(); } catch {}
                        }
                        if (!r.checked) {
                            try { r.click(); } catch {}
                        }
                        if (!r.checked) {
                            const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'checked')?.set;
                            if (setter) setter.call(r, true);
                            else r.checked = true;
                            r.dispatchEvent(new MouseEvent('click', { bubbles: true }));
                            r.dispatchEvent(new Event('input', { bubbles: true }));
                            r.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                        if (r.checked) {
                            filled.push('radio:' + (qNorm || key).slice(0, 40) + '=' + (chosen.raw || '').slice(0, 30));
                        }
                    }
                }

                return filled;
            ", modal, preferWorkAuth, needVisa, firstNameToken);

            if (result is IReadOnlyList<object> list && list.Count > 0)
            {
                foreach (var item in list)
                {
                    var k = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(k))
                    {
                        updatedKeys.Add(k);
                    }
                }
                return list.Count;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTO-FILL] Aviso em grupos de radio: {ex.Message}");
        }

        return 0;
    }

    private static int TryAutoFillAllCheckboxesInModal(IWebDriver driver, IWebElement modal, List<string> updatedKeys)
    {
        try
        {
            if (driver is not IJavaScriptExecutor js)
            {
                return 0;
            }

            var defaultCheckSingle = AutoFillDefaultCheckboxTrue;

            var result = js.ExecuteScript(@"
                const modal = arguments[0];
                const defaultCheckSingle = arguments[1];
                if (!modal) return [];

                const norm = (s) => (s || '')
                    .toString()
                    .normalize('NFD')
                    .replace(/[\u0300-\u036f]/g, '')
                    .toLowerCase()
                    .replace(/\s+/g, ' ')
                    .trim();

                const isVisibleItem = (cb) => {
                    if (!cb || cb.disabled || cb.getAttribute('aria-disabled') === 'true') return false;
                    if (cb.closest('[hidden], [aria-hidden=""true""]')) return false;
                    const lbl = cb.id ? modal.querySelector('label[for=""' + CSS.escape(cb.id) + '""]') : null;
                    const target = lbl || cb.parentElement || cb;
                    const s = window.getComputedStyle(target);
                    if (s.display === 'none' || s.visibility === 'hidden') return false;
                    const rect = target.getBoundingClientRect();
                    return rect.width > 0 && rect.height > 0;
                };

                const getOptionText = (cb) => {
                    if (cb.id) {
                        const lbl = modal.querySelector('label[for=""' + CSS.escape(cb.id) + '""]');
                        if (lbl) {
                            const lt = (lbl.innerText || lbl.textContent || '').trim();
                            if (lt) return lt;
                        }
                    }
                    let p = cb.parentElement;
                    while (p && p !== modal && p.tagName !== 'FIELDSET') {
                        const countInside = p.querySelectorAll('input[type=""checkbox""]').length;
                        if (countInside === 1) {
                            const txt = (p.innerText || p.textContent || '').trim();
                            if (txt) return txt;
                        }
                        p = p.parentElement;
                    }
                    return (cb.getAttribute('aria-label') || cb.getAttribute('name') || '').trim();
                };

                const clickCheckbox = (cb) => {
                    const lbl = cb.id ? modal.querySelector('label[for=""' + CSS.escape(cb.id) + '""]') : null;
                    (lbl || cb).scrollIntoView({ block: 'center' });
                    if (lbl) {
                        try { lbl.click(); } catch {}
                    }
                    if (!cb.checked) {
                        try { cb.click(); } catch {}
                    }
                    if (!cb.checked) {
                        const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'checked')?.set;
                        if (setter) setter.call(cb, true);
                        else cb.checked = true;
                        cb.dispatchEvent(new MouseEvent('click', { bubbles: true }));
                        cb.dispatchEvent(new Event('input', { bubbles: true }));
                        cb.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    return cb.checked;
                };

                const allCbs = Array.from(modal.querySelectorAll('input[type=""checkbox""]')).filter(isVisibleItem);
                const fsGroups = new Map();
                const singles = [];

                for (const cb of allCbs) {
                    const fs = cb.closest('fieldset');
                    if (fs) {
                        if (!fsGroups.has(fs)) fsGroups.set(fs, []);
                        fsGroups.get(fs).push(cb);
                    } else {
                        singles.push(cb);
                    }
                }

                const filled = [];
                const skillKeywords = ['aws', 'ec2', 's3', 'sqs', 'api gateway', 'lambda', 'rds', 'azure', 'gcp', 'c#', '.net', 'dotnet', 'python', 'react', 'angular', 'javascript', 'typescript', 'node', 'sql', 'postgres', 'mysql', 'docker', 'kubernetes', 'git', 'ci/cd', 'rest', 'graphql', 'linux', 'scrum', 'agile', 'java', 'spring'];
                const isNegativeOption = (n) => n.includes('nao possuo') || n.includes('nao tenho') || n.includes('nenhuma') || n.includes('none') || n.includes('no experience') || n.includes('sem experiencia') || n.includes('never');

                // 1. Grupos de múltipla escolha em <fieldset> (ex.: Quais clouds/serviços você conhece?*)
                for (const [fs, cbs] of fsGroups.entries()) {
                    if (cbs.length === 1) {
                        singles.push(cbs[0]);
                        continue;
                    }
                    // Se já há pelo menos 1 marcado no grupo, não altera
                    if (cbs.some(c => c.checked)) continue;

                    const opts = cbs.map(cb => ({ el: cb, raw: getOptionText(cb), n: norm(getOptionText(cb)) }));
                    const positiveOpts = opts.filter(o => !isNegativeOption(o.n));
                    const matchedSkills = positiveOpts.filter(o => skillKeywords.some(k => o.n === k || o.n.includes(k)));

                    const toSelect = matchedSkills.length > 0
                        ? matchedSkills.slice(0, 3)
                        : (positiveOpts.length > 0 ? [positiveOpts[0]] : [opts[0]]);

                    for (const pick of toSelect) {
                        if (pick && clickCheckbox(pick.el)) {
                            filled.push('checkbox_group:' + (pick.raw || 'opt').slice(0, 40));
                        }
                    }
                }

                // 2. Checkboxes individuais (termos, consentimento, confirmação)
                for (const cb of singles) {
                    if (cb.checked) continue;
                    const txt = norm(getOptionText(cb));
                    // Não marca alertas de encerrar ou negar comunicação indesejada se for 'não quero'
                    if (txt.includes('nao desejo') || txt.includes('do not')) continue;

                    const isReq = cb.required || cb.getAttribute('aria-required') === 'true';
                    const isFollowCompany = txt.includes('seguir') || txt.includes('follow ');
                    if (isFollowCompany && !isReq) continue;

                    if (isReq || defaultCheckSingle || txt.includes('concordo') || txt.includes('aceito') || txt.includes('agree') || txt.includes('terms') || txt.includes('privac') || txt.includes('consent')) {
                        if (clickCheckbox(cb)) {
                            filled.push('checkbox:' + (txt || 'single').slice(0, 40));
                        }
                    }
                }

                return filled;
            ", modal, defaultCheckSingle);

            if (result is IReadOnlyList<object> list && list.Count > 0)
            {
                foreach (var item in list)
                {
                    var k = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(k))
                    {
                        updatedKeys.Add(k);
                    }
                }
                return list.Count;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTO-FILL] Aviso em checkboxes: {ex.Message}");
        }

        return 0;
    }

    private static bool TrySetRequiredCheckboxField(IWebDriver driver, IWebElement checkbox, IWebElement modal, string hints)
    {
        try
        {
            if (checkbox.Selected)
            {
                return false;
            }

            var normalizedHints = NormalizeForMatch(hints);
            var isRequiredAttr = !string.IsNullOrWhiteSpace(checkbox.GetAttribute("required")) ||
                                 string.Equals(checkbox.GetAttribute("aria-required"), "true", StringComparison.OrdinalIgnoreCase);

            var looksRequiredOrConsent = isRequiredAttr ||
                                         normalizedHints.Contains("concordo", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("aceito", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("autorizo", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("termos", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("privacidade", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("consent", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("agree", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("terms", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("politica", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("obrigatorio", StringComparison.Ordinal) ||
                                         normalizedHints.Contains("required", StringComparison.Ordinal) ||
                                         AutoFillDefaultCheckboxTrue;

            if (!looksRequiredOrConsent)
            {
                return false;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", checkbox);
            Thread.Sleep(120);
            ClickElementRobust(driver, checkbox);
            Thread.Sleep(120);

            if (checkbox.Selected)
            {
                return true;
            }

            var id = (checkbox.GetAttribute("id") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                if (label != null && label.Displayed)
                {
                    ClickElementRobust(driver, label);
                    Thread.Sleep(100);
                }
            }

            if (checkbox.Selected)
            {
                return true;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                el.checked = true;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", checkbox);

            return checkbox.Selected;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetRequiredRadioField(IWebDriver driver, IWebElement radio, IWebElement modal, string hints)
    {
        try
        {
            var fieldName = (radio.GetAttribute("name") ?? string.Empty).Trim();
            var allRadios = modal.FindElements(By.CssSelector("input[type='radio']"));

            var group = allRadios
                .Where(r =>
                {
                    try
                    {
                        if (!r.Displayed || IsElementDisabledOrReadonly(r))
                        {
                            return false;
                        }

                        if (string.IsNullOrWhiteSpace(fieldName))
                        {
                            return true;
                        }

                        var candidateName = (r.GetAttribute("name") ?? string.Empty).Trim();
                        return string.Equals(candidateName, fieldName, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            if (group.Count == 0)
            {
                if (!radio.Displayed || IsElementDisabledOrReadonly(radio))
                {
                    return false;
                }

                group.Add(radio);
            }

            if (group.Any(r =>
                {
                    try { return r.Selected; } catch { return false; }
                }))
            {
                return false;
            }

            var normalizedHints = NormalizeForMatch(hints);
            IWebElement? chosen = null;

            // 1. Pergunta de Visto/Patrocínio -> seleciona SEMPRE "Não"
            if (IsVisaSponsorshipQuestion(normalizedHints))
            {
                chosen = group.FirstOrDefault(r => IsNoLikeOption(BuildRadioOptionHints(r, modal)));
            }
            // 2. Questões afirmativas (Sim/Não) -> seleciona SEMPRE "Sim" (regra do usuário)
            else if (group.Any(r => IsYesLikeOption(BuildRadioOptionHints(r, modal))))
            {
                chosen = group.FirstOrDefault(r => IsYesLikeOption(BuildRadioOptionHints(r, modal)));
            }
            // 3. Informações sensíveis -> prefiro não informar
            else if (IsSensitiveDisclosureField(normalizedHints))
            {
                chosen = group.FirstOrDefault(r => {
                    var h = BuildRadioOptionHints(r, modal);
                    return h.Contains("prefiro nao") || h.Contains("prefer not") || h.Contains("nao informar");
                });
            }

            if (chosen == null)
            {
                // Tenta opção afirmativa
                chosen = group.FirstOrDefault(r => {
                    var h = BuildRadioOptionHints(r, modal).ToLowerInvariant();
                    return !h.Contains("nao") && !h.Contains("não") && !h.Contains("no") && !h.Contains("never");
                });

                // Fallback universal: seleciona a primeira opção do grupo
                chosen ??= group.FirstOrDefault();
            }

            if (chosen == null)
            {
                return false;
            }

            return TryClickRadioOption(driver, chosen, modal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClickRadioOption(IWebDriver driver, IWebElement radio, IWebElement modal)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", radio);
            Thread.Sleep(120);
            ClickElementRobust(driver, radio);
            Thread.Sleep(120);

            if (radio.Selected)
            {
                return true;
            }

            var id = (radio.GetAttribute("id") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                if (label != null)
                {
                    try
                    {
                        if (label.Displayed)
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", label);
                            Thread.Sleep(80);
                            ClickElementRobust(driver, label);
                            Thread.Sleep(120);
                        }
                    }
                    catch
                    {
                        // Segue fallback JS.
                    }
                }
            }

            if (radio.Selected)
            {
                return true;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                el.checked = true;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", radio);

            return radio.Selected;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildRadioOptionHints(IWebElement radio, IWebElement modal)
    {
        string Safe(string? value) => (value ?? string.Empty).Trim();

        var tokens = new List<string>
        {
            Safe(radio.GetAttribute("value")),
            Safe(radio.GetAttribute("name")),
            Safe(radio.GetAttribute("aria-label")),
            Safe(radio.GetAttribute("id"))
        };

        var id = Safe(radio.GetAttribute("id"));
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                var labelText = (label?.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    tokens.Add(labelText);
                }
            }
            catch
            {
                // Ignora falha pontual de label.
            }
        }

        try
        {
            var container = radio.FindElement(By.XPath("./ancestor::*[self::div or self::fieldset][1]"));
            var containerText = (container.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(containerText))
            {
                tokens.Add(containerText.Length > 140 ? containerText.Substring(0, 140) : containerText);
            }
        }
        catch
        {
            // Ignora ausência de container.
        }

        return NormalizeForMatch(string.Join(" ", tokens.Where(t => !string.IsNullOrWhiteSpace(t))));
    }

    private static bool IsYesLikeOption(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        return normalizedText.Contains(" sim", StringComparison.Ordinal) ||
               normalizedText.StartsWith("sim", StringComparison.Ordinal) ||
               normalizedText.Contains(" yes", StringComparison.Ordinal) ||
               normalizedText.StartsWith("yes", StringComparison.Ordinal) ||
               normalizedText.Contains(" true", StringComparison.Ordinal) ||
               normalizedText.StartsWith("true", StringComparison.Ordinal) ||
               normalizedText.Contains(" concordo", StringComparison.Ordinal) ||
               normalizedText.Contains("aceito", StringComparison.Ordinal) ||
               normalizedText.Contains("autorizo", StringComparison.Ordinal);
    }

    private static bool IsNoLikeOption(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        return normalizedText.Contains(" nao", StringComparison.Ordinal) ||
               normalizedText.StartsWith("nao", StringComparison.Ordinal) ||
               normalizedText.Contains(" no", StringComparison.Ordinal) ||
               normalizedText.StartsWith("no", StringComparison.Ordinal) ||
               normalizedText.Contains(" false", StringComparison.Ordinal) ||
               normalizedText.StartsWith("false", StringComparison.Ordinal);
    }

    private static bool HasYesNoOptions(List<IWebElement> options)
    {
        return FindYesOption(options) != null && FindNoOption(options) != null;
    }

    private static IWebElement? FindBestYearsOption(List<IWebElement> options, int targetYears)
    {
        var normalizedTarget = targetYears <= 0 ? 1 : targetYears;
        IWebElement? fallbackNumeric = null;

        foreach (var option in options)
        {
            try
            {
                var normalized = NormalizeForMatch(option.Text ?? string.Empty);
                var digits = new string(normalized.Where(char.IsDigit).ToArray());
                if (string.IsNullOrWhiteSpace(digits))
                {
                    continue;
                }

                if (!int.TryParse(digits, out var parsedYears))
                {
                    continue;
                }

                fallbackNumeric ??= option;
                if (parsedYears >= normalizedTarget)
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return fallbackNumeric;
    }

    private static IWebElement? FindOptionContaining(List<IWebElement> options, params string[] tokens)
    {
        if (tokens == null || tokens.Length == 0)
        {
            return null;
        }

        var normalizedTokens = tokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(NormalizeForMatch)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (normalizedTokens.Count == 0)
        {
            return null;
        }

        foreach (var option in options)
        {
            try
            {
                var text = NormalizeForMatch(option.Text ?? string.Empty);
                if (normalizedTokens.Any(token => text.Contains(token, StringComparison.Ordinal)))
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return null;
    }

    private static IWebElement? FindYesOption(List<IWebElement> options)
    {
        foreach (var option in options)
        {
            try
            {
                var normalized = NormalizeForMatch(option.Text ?? string.Empty);
                if (normalized == "sim" ||
                    normalized == "yes" ||
                    normalized == "true" ||
                    normalized.StartsWith("sim ", StringComparison.Ordinal) ||
                    normalized.StartsWith("yes ", StringComparison.Ordinal) ||
                    normalized.StartsWith("true ", StringComparison.Ordinal))
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return null;
    }

    private static IWebElement? FindNoOption(List<IWebElement> options)
    {
        foreach (var option in options)
        {
            try
            {
                var normalized = NormalizeForMatch(option.Text ?? string.Empty);
                if (normalized == "nao" ||
                    normalized == "no" ||
                    normalized == "false" ||
                    normalized.StartsWith("nao ", StringComparison.Ordinal) ||
                    normalized.StartsWith("no ", StringComparison.Ordinal) ||
                    normalized.StartsWith("false ", StringComparison.Ordinal))
                {
                    return option;
                }
            }
            catch
            {
                // Ignora opção inválida.
            }
        }

        return null;
    }

    private static bool IsPlaceholderOption(string text, string value)
    {
        var normalizedText = NormalizeForMatch(text);
        var normalizedValue = NormalizeForMatch(value);

        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedValue))
        {
            return true;
        }

        return normalizedText.Contains("select an option", StringComparison.Ordinal) ||
               normalizedText.Contains("please make a selection", StringComparison.Ordinal) ||
               normalizedText.Contains("selecione", StringComparison.Ordinal) ||
               normalizedText.Contains("escolha", StringComparison.Ordinal) ||
               normalizedText.Contains("choose", StringComparison.Ordinal) ||
               normalizedText.Contains("selecionar", StringComparison.Ordinal) ||
               normalizedValue == "";
    }

    private static string ResolveMandatoryFieldValue(string hints, string tag, string type)
    {
        var normalizedHints = NormalizeForMatch(hints);

        if (type == "email" || normalizedHints.Contains("email", StringComparison.Ordinal) || normalizedHints.Contains("e-mail", StringComparison.Ordinal))
        {
            return AutoFillDefaultEmail;
        }

        if (type == "tel" ||
            normalizedHints.Contains("telefone", StringComparison.Ordinal) ||
            normalizedHints.Contains("celular", StringComparison.Ordinal) ||
            normalizedHints.Contains("phone", StringComparison.Ordinal) ||
            normalizedHints.Contains("mobile", StringComparison.Ordinal))
        {
            return AutoFillDefaultPhone;
        }

        if (normalizedHints.Contains("last name", StringComparison.Ordinal) ||
            normalizedHints.Contains("surname", StringComparison.Ordinal) ||
            normalizedHints.Contains("sobrenome", StringComparison.Ordinal))
        {
            return AutoFillDefaultLastName;
        }

        if (normalizedHints.Contains("first name", StringComparison.Ordinal) ||
            normalizedHints.Contains("nome", StringComparison.Ordinal))
        {
            if (!normalizedHints.Contains("sobrenome", StringComparison.Ordinal) &&
                !normalizedHints.Contains("last", StringComparison.Ordinal) &&
                !normalizedHints.Contains("surname", StringComparison.Ordinal))
            {
                return AutoFillDefaultFirstName;
            }
        }

        // Regra do usuário: campos de salário pretendido/expectativa -> 8000
        if (IsSalaryQuestion(normalizedHints))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultSalary) ? AutoFillDefaultSalary : "8000";
        }

        if (IsYearsExperienceQuestion(normalizedHints))
        {
            return Math.Max(1, AutoFillDefaultYearsExperience).ToString();
        }

        if (type == "number")
        {
            return Math.Max(1, AutoFillDefaultYearsExperience).ToString();
        }

        if (type == "url" ||
            normalizedHints.Contains("website", StringComparison.Ordinal) ||
            normalizedHints.Contains("portfolio", StringComparison.Ordinal) ||
            normalizedHints.Contains("site", StringComparison.Ordinal) ||
            normalizedHints.Contains("url", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(AutoFillDefaultWebsite))
            {
                return AutoFillDefaultWebsite;
            }

            if (!string.IsNullOrWhiteSpace(AutoFillDefaultLinkedIn))
            {
                return AutoFillDefaultLinkedIn;
            }

            return "https://www.linkedin.com";
        }

        if (normalizedHints.Contains("linkedin", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultLinkedIn) ? AutoFillDefaultLinkedIn : "https://www.linkedin.com";
        }

        if (normalizedHints.Contains("github", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultGithub) ? AutoFillDefaultGithub : "https://github.com";
        }

        if (normalizedHints.Contains("empresa", StringComparison.Ordinal) ||
            normalizedHints.Contains("company", StringComparison.Ordinal) ||
            normalizedHints.Contains("empregador", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultCurrentCompany) ? AutoFillDefaultCurrentCompany : "Confidencial";
        }

        if (normalizedHints.Contains("cargo", StringComparison.Ordinal) ||
            normalizedHints.Contains("titulo", StringComparison.Ordinal) ||
            normalizedHints.Contains("job title", StringComparison.Ordinal) ||
            normalizedHints.Contains("funcao", StringComparison.Ordinal) ||
            normalizedHints.Contains("role", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultJobTitle) ? AutoFillDefaultJobTitle : "Desenvolvedor Full Stack";
        }

        if (normalizedHints.Contains("faculdade", StringComparison.Ordinal) ||
            normalizedHints.Contains("universidade", StringComparison.Ordinal) ||
            normalizedHints.Contains("escola", StringComparison.Ordinal) ||
            normalizedHints.Contains("instituicao", StringComparison.Ordinal) ||
            normalizedHints.Contains("school", StringComparison.Ordinal) ||
            normalizedHints.Contains("college", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultEducation) ? AutoFillDefaultEducation : "Ensino Superior";
        }

        if (normalizedHints.Contains("curso", StringComparison.Ordinal) ||
            normalizedHints.Contains("major", StringComparison.Ordinal) ||
            normalizedHints.Contains("degree", StringComparison.Ordinal) ||
            normalizedHints.Contains("graduacao", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultDegree) ? AutoFillDefaultDegree : "Superior completo";
        }

        if (normalizedHints.Contains("bairro", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultCity) ? AutoFillDefaultCity : "Centro";
        }

        if (normalizedHints.Contains("location", StringComparison.Ordinal) ||
            normalizedHints.Contains("localizacao", StringComparison.Ordinal) ||
            normalizedHints.Contains("cidade", StringComparison.Ordinal) ||
            normalizedHints.Contains("city", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultCity) ? AutoFillDefaultCity : "São Paulo";
        }

        if (normalizedHints.Contains("estado", StringComparison.Ordinal) ||
            normalizedHints.Contains("state", StringComparison.Ordinal) ||
            normalizedHints.Contains("uf", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultState) ? AutoFillDefaultState : "São Paulo";
        }

        if (normalizedHints.Contains("pais", StringComparison.Ordinal) ||
            normalizedHints.Contains("country", StringComparison.Ordinal))
        {
            return "Brasil";
        }

        if (normalizedHints.Contains("endereco", StringComparison.Ordinal) ||
            normalizedHints.Contains("address", StringComparison.Ordinal) ||
            normalizedHints.Contains("rua", StringComparison.Ordinal) ||
            normalizedHints.Contains("street", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultAddress) ? AutoFillDefaultAddress : AutoFillDefaultCity;
        }

        if (normalizedHints.Contains("cep", StringComparison.Ordinal) ||
            normalizedHints.Contains("zip", StringComparison.Ordinal) ||
            normalizedHints.Contains("postal", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(AutoFillDefaultZip) ? AutoFillDefaultZip : "01000-000";
        }

        if (normalizedHints.Contains("aviso", StringComparison.Ordinal) ||
            normalizedHints.Contains("disponibilidade", StringComparison.Ordinal) ||
            normalizedHints.Contains("notice", StringComparison.Ordinal) ||
            normalizedHints.Contains("quando pode", StringComparison.Ordinal) ||
            normalizedHints.Contains("start", StringComparison.Ordinal))
        {
            return "Imediata";
        }

        if (normalizedHints.Contains("ingles", StringComparison.Ordinal) ||
            normalizedHints.Contains("english", StringComparison.Ordinal) ||
            normalizedHints.Contains("idioma", StringComparison.Ordinal))
        {
            return "Avançado";
        }

        if (tag == "textarea" ||
            normalizedHints.Contains("cover letter", StringComparison.Ordinal) ||
            normalizedHints.Contains("carta", StringComparison.Ordinal) ||
            normalizedHints.Contains("mensagem", StringComparison.Ordinal) ||
            normalizedHints.Contains("summary", StringComparison.Ordinal) ||
            normalizedHints.Contains("sobre", StringComparison.Ordinal))
        {
            return AutoFillDefaultGenericText;
        }

        // Fallback para campos numéricos
        if (type == "number" ||
            normalizedHints.Contains("quant", StringComparison.Ordinal) ||
            normalizedHints.Contains("how many", StringComparison.Ordinal) ||
            normalizedHints.Contains("meses", StringComparison.Ordinal) ||
            normalizedHints.Contains("dias", StringComparison.Ordinal) ||
            normalizedHints.Contains("horas", StringComparison.Ordinal) ||
            normalizedHints.Contains("level", StringComparison.Ordinal) ||
            normalizedHints.Contains("escala", StringComparison.Ordinal) ||
            normalizedHints.Contains("nota", StringComparison.Ordinal))
        {
            return Math.Max(1, AutoFillDefaultYearsExperience).ToString();
        }

        // Fallback genérico para qualquer campo de texto
        if (!string.IsNullOrWhiteSpace(AutoFillDefaultGenericText))
        {
            return AutoFillDefaultGenericText;
        }

        return "Tenho experiencia compativel com a vaga e disponibilidade para atuar no escopo solicitado.";
    }

    private static bool TrySetInputValue(IWebDriver driver, IWebElement field, string value)
    {
        try
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block: 'center'});", field);
            Thread.Sleep(120);

            try { field.Clear(); } catch { }
            field.SendKeys(value);

            var finalValue = (field.GetAttribute("value") ?? string.Empty).Trim();
            if (string.Equals(finalValue, value, StringComparison.Ordinal))
            {
                return true;
            }

            ((IJavaScriptExecutor)driver).ExecuteScript(@"
                const el = arguments[0];
                const v = arguments[1];
                const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set
                    || Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value')?.set;
                if (nativeInputValueSetter) {
                    nativeInputValueSetter.call(el, v);
                } else {
                    el.value = v;
                }
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            ", field, value);

            finalValue = (field.GetAttribute("value") ?? string.Empty).Trim();
            return string.Equals(finalValue, value, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildFieldHints(IWebElement field, IWebElement modal)
    {
        string Safe(string? value) => (value ?? string.Empty).Trim();

        var id = Safe(field.GetAttribute("id"));
        var name = Safe(field.GetAttribute("name"));
        var ariaLabel = Safe(field.GetAttribute("aria-label"));
        var placeholder = Safe(field.GetAttribute("placeholder"));
        var dataTest = Safe(field.GetAttribute("data-test-text-entity-list-form-component"));
        var dataControlName = Safe(field.GetAttribute("data-control-name"));

        var tokens = new List<string>
        {
            ariaLabel,
            placeholder,
            name,
            id,
            dataTest,
            dataControlName
        };

        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var label = modal.FindElements(By.CssSelector($"label[for='{id}']")).FirstOrDefault();
                var labelText = (label?.Text ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(labelText))
                {
                    tokens.Add(labelText);
                }
            }
            catch
            {
                // Ignora falha de busca de label por for=id.
            }
        }

        try
        {
            var container = field.FindElement(By.XPath("./ancestor::*[self::div or self::fieldset][1]"));
            var containerText = (container.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(containerText))
            {
                tokens.Add(containerText.Length > 180 ? containerText.Substring(0, 180) : containerText);
            }
        }
        catch
        {
            // Ignora ausência de container compatível.
        }

        return string.Join(" ", tokens.Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private static string BuildFieldKey(IWebElement field, string tag, string type, string hints)
    {
        string Safe(string? value) => (value ?? string.Empty).Trim();

        var id = Safe(field.GetAttribute("id"));
        var name = Safe(field.GetAttribute("name"));
        var ariaLabel = Safe(field.GetAttribute("aria-label"));
        var placeholder = Safe(field.GetAttribute("placeholder"));

        var key = !string.IsNullOrWhiteSpace(ariaLabel) ? ariaLabel
            : !string.IsNullOrWhiteSpace(placeholder) ? placeholder
            : !string.IsNullOrWhiteSpace(name) ? name
            : !string.IsNullOrWhiteSpace(id) ? id
            : hints;

        key = key.Trim();
        if (key.Length > 80)
        {
            key = key.Substring(0, 80);
        }

        return $"{tag}:{type}:{key}";
    }

    private static bool IsElementDisabledOrReadonly(IWebElement field)
    {
        try
        {
            var disabled = field.GetAttribute("disabled") ?? string.Empty;
            var readOnly = field.GetAttribute("readonly") ?? string.Empty;
            var ariaDisabled = (field.GetAttribute("aria-disabled") ?? string.Empty).Trim();

            return !string.IsNullOrWhiteSpace(disabled) ||
                   !string.IsNullOrWhiteSpace(readOnly) ||
                   ariaDisabled.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsFieldValueEmpty(IWebElement field)
    {
        try
        {
            var value = (field.GetAttribute("value") ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWorkAuthorizationQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("authorized", StringComparison.Ordinal) ||
               normalizedHints.Contains("autoriza", StringComparison.Ordinal) ||
               normalizedHints.Contains("work permit", StringComparison.Ordinal) ||
               normalizedHints.Contains("direito de trabalhar", StringComparison.Ordinal);
    }

    private static bool IsVisaSponsorshipQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("sponsor", StringComparison.Ordinal) ||
               normalizedHints.Contains("sponsorship", StringComparison.Ordinal) ||
               normalizedHints.Contains("patrocin", StringComparison.Ordinal) ||
               normalizedHints.Contains("visa", StringComparison.Ordinal);
    }

    private static bool IsCountryQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("country", StringComparison.Ordinal) ||
               normalizedHints.Contains("pais", StringComparison.Ordinal) ||
               normalizedHints.Contains("codigo do pais", StringComparison.Ordinal) ||
               normalizedHints.Contains("codigo de pais", StringComparison.Ordinal) ||
               normalizedHints.Contains("country code", StringComparison.Ordinal);
    }

    private static bool IsYearsExperienceQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("years", StringComparison.Ordinal) ||
               normalizedHints.Contains("anos", StringComparison.Ordinal) ||
               normalizedHints.Contains("experience", StringComparison.Ordinal) ||
               normalizedHints.Contains("experiencia", StringComparison.Ordinal);
    }

    private static bool IsSalaryQuestion(string normalizedHints)
    {
        return normalizedHints.Contains("salary", StringComparison.Ordinal) ||
               normalizedHints.Contains("salario", StringComparison.Ordinal) ||
               normalizedHints.Contains("salar", StringComparison.Ordinal) ||
               normalizedHints.Contains("pretens", StringComparison.Ordinal) ||
               normalizedHints.Contains("expectativa", StringComparison.Ordinal) ||
               normalizedHints.Contains("remuner", StringComparison.Ordinal) ||
               normalizedHints.Contains("compensation", StringComparison.Ordinal) ||
               normalizedHints.Contains("compensac", StringComparison.Ordinal) ||
               normalizedHints.Contains("faixa salarial", StringComparison.Ordinal);
    }

    private static bool IsSensitiveDisclosureField(string normalizedHints)
    {
        return normalizedHints.Contains("gender", StringComparison.Ordinal) ||
               normalizedHints.Contains("genero", StringComparison.Ordinal) ||
               normalizedHints.Contains("sexo", StringComparison.Ordinal) ||
               normalizedHints.Contains("race", StringComparison.Ordinal) ||
               normalizedHints.Contains("raca", StringComparison.Ordinal) ||
               normalizedHints.Contains("ethnicity", StringComparison.Ordinal) ||
               normalizedHints.Contains("etnia", StringComparison.Ordinal) ||
               normalizedHints.Contains("veteran", StringComparison.Ordinal) ||
               normalizedHints.Contains("veterano", StringComparison.Ordinal) ||
               normalizedHints.Contains("disability", StringComparison.Ordinal) ||
               normalizedHints.Contains("deficiencia", StringComparison.Ordinal) ||
               normalizedHints.Contains("pcd", StringComparison.Ordinal);
    }

    private static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
