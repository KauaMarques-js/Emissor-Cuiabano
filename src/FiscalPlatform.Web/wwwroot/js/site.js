// Comportamentos compartilhados, ativados por atributos data-* (sem dependências).
(() => {
    const digits = v => (v || '').replace(/\D/g, '');
    const brl = v => v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    const num = s => { const v = parseFloat((s || '').replace(/[^\d,-]/g, '').replace(',', '.')); return isNaN(v) ? 0 : v; };

    // Máscaras: data-mask="cnpj | document | cep | phone"
    const masks = {
        cnpj: d => d.slice(0, 14).replace(/^(\d{2})(\d)/, '$1.$2').replace(/^(\d{2})\.(\d{3})(\d)/, '$1.$2.$3')
            .replace(/\.(\d{3})(\d)/, '.$1/$2').replace(/(\d{4})(\d)/, '$1-$2'),
        cpf: d => d.slice(0, 11).replace(/(\d{3})(\d)/, '$1.$2').replace(/(\d{3})(\d)/, '$1.$2').replace(/(\d{3})(\d{1,2})$/, '$1-$2'),
        document: d => d.length > 11 ? masks.cnpj(d) : masks.cpf(d),
        cep: d => d.slice(0, 8).replace(/(\d{5})(\d)/, '$1-$2'),
        phone: d => d.slice(0, 11).replace(/^(\d{2})(\d)/, '($1) $2').replace(/(\d)(\d{4})$/, '$1-$2'),
    };
    const applyMask = el => { if (el.value) el.value = masks[el.dataset.mask](digits(el.value)); };
    document.querySelectorAll('[data-mask]').forEach(applyMask);
    document.addEventListener('input', e => { if (masks[e.target.dataset.mask]) applyMask(e.target); });

    // Campos condicionais: data-show-if="NomeDoCampo=valor1|valor2"
    const valueOf = form => name => {
        const els = [...form.querySelectorAll(`[name="${name}"]`)];
        const el = els.find(x => x.type === 'radio' ? x.checked : x.type !== 'hidden') || els[0];
        if (!el) return '';
        return el.type === 'checkbox' ? String(el.checked) : el.type === 'radio' && !el.checked ? '' : el.value;
    };
    const applyConditions = form => form.querySelectorAll('[data-show-if]').forEach(el => {
        const [name, values] = el.dataset.showIf.split('=');
        const visible = values.split('|').includes(valueOf(form)(name));
        el.hidden = !visible;
        el.querySelectorAll('input, select, textarea').forEach(i => i.disabled = !visible);
    });
    document.querySelectorAll('form').forEach(form => {
        applyConditions(form);
        form.addEventListener('change', () => applyConditions(form));
    });

    // Preenchimento automático: data-lookup="cnpj | cep" dentro de [data-lookup-scope]; campos com data-field.
    const fill = (scope, data, keys) => {
        keys.forEach(key => {
            const el = scope.querySelector(`[data-field="${key}"]`);
            if (!el || data[key] == null || data[key] === '') return;
            if (el.tagName === 'SELECT' && ![...el.options].some(o => o.value === data[key])) return;
            el.value = el.dataset.mask ? masks[el.dataset.mask](digits(data[key])) : data[key];
            // O valor já veio de uma busca: não dispara a busca do próprio campo (ex.: CEP vindo do CNPJ).
            if (el.dataset.lookup) el.dataset.lastLookup = digits(data[key]);
            el.dispatchEvent(new Event('change', { bubbles: true }));
        });
    };
    const lookup = async (input, kind) => {
        const scope = input.closest('[data-lookup-scope]') || document;
        const status = scope.querySelector(`[data-lookup-status="${kind}"]`);
        const value = digits(input.value);
        if ((kind === 'cnpj' && value.length !== 14) || (kind === 'cep' && value.length !== 8)) return;
        if (input.dataset.lastLookup === value) return;
        input.dataset.lastLookup = value;
        if (status) status.textContent = 'Buscando…';
        try {
            const response = await fetch(`/Lookup/${kind === 'cnpj' ? 'Cnpj' : 'Cep'}?value=${value}`, { headers: { Accept: 'application/json' } });
            const data = await response.json();
            if (!response.ok) { if (status) status.textContent = data.message || 'Não encontrado.'; return; }
            const address = ['zipCode', 'street', 'number', 'complement', 'district', 'city', 'state', 'cityIbgeCode'];
            if (kind === 'cnpj') {
                fill(scope, data, ['name', 'tradeName', 'email', 'phone', 'taxRegime', ...address]);
                if (status) status.textContent = `Dados preenchidos (${data.situation || 'situação não informada'}). Confira antes de salvar.`;
            } else {
                fill(scope, data, address.filter(k => k !== 'number' && k !== 'complement'));
                if (status) status.textContent = 'Endereço preenchido. Informe o número.';
                scope.querySelector('[data-field="number"]')?.focus();
            }
        } catch {
            if (status) status.textContent = 'Sem conexão para buscar agora. Preencha manualmente.';
        }
    };
    document.addEventListener('change', e => { if (e.target.dataset.lookup) lookup(e.target, e.target.dataset.lookup); });
    document.addEventListener('click', e => {
        const button = e.target.closest('[data-lookup-button]');
        if (!button) return;
        const input = button.closest('[data-lookup-scope]').querySelector(`[data-lookup="${button.dataset.lookupButton}"]`);
        delete input.dataset.lastLookup;
        lookup(input, button.dataset.lookupButton);
    });

    // Confirmação de ações irreversíveis e envio único.
    document.addEventListener('submit', e => {
        const message = e.submitter?.dataset.confirm || e.target.dataset.confirm;
        if (message && !confirm(message)) { e.preventDefault(); return; }
        e.target.querySelectorAll('button[type=submit], button:not([type])').forEach(b => setTimeout(() => b.disabled = true));
    });

    // Mostrar/ocultar senha.
    document.addEventListener('click', e => {
        const toggle = e.target.closest('[data-toggle-password]');
        if (!toggle) return;
        const input = document.getElementById(toggle.dataset.togglePassword);
        input.type = input.type === 'password' ? 'text' : 'password';
        toggle.textContent = input.type === 'password' ? 'Mostrar' : 'Ocultar';
    });

    // Formulário de nota: linhas de itens e total ao vivo.
    const noteForm = document.getElementById('note-form');
    if (noteForm) {
        const body = noteForm.querySelector('#lines tbody');
        const freight = noteForm.querySelector('[name=Freight]');
        const description = noteForm.querySelector('[name=ServiceDescription]');
        const withheld = noteForm.querySelector('[name=IssWithheld][type=checkbox]');
        const recalc = () => {
            let total = 0;
            body.querySelectorAll('tr').forEach(tr => {
                const option = tr.querySelector('select').selectedOptions[0];
                const cell = tr.querySelector('.line-total');
                if (!option?.value) { cell.textContent = '—'; return; }
                const qty = tr.querySelector('[name=LineQty]:not([type=hidden])') ? num(tr.querySelector('[name=LineQty]').value) : 1;
                const line = qty * num(tr.querySelector('[name=LinePrice]').value || option.dataset.price) - num(tr.querySelector('[name=LineDiscount]').value);
                cell.textContent = brl(line);
                total += line;
            });
            if (freight) total += num(freight.value);
            noteForm.querySelector('#note-total').textContent = brl(total);
        };
        body.addEventListener('change', e => {
            if (e.target.tagName !== 'SELECT') return;
            const option = e.target.selectedOptions[0];
            e.target.closest('tr').querySelector('[name=LinePrice]').value = option?.value ? option.dataset.price : '';
            if (description && option?.dataset.description && !description.value.trim()) description.value = option.dataset.description;
            if (withheld && option?.dataset.withheld) withheld.checked = option.dataset.withheld === 'true';
        });
        noteForm.addEventListener('input', recalc);
        noteForm.addEventListener('change', recalc);
        noteForm.querySelector('#add-line')?.addEventListener('click', () => {
            const row = body.querySelector('tr').cloneNode(true);
            row.querySelectorAll('input').forEach(i => i.value = i.name === 'LineQty' ? '1' : '');
            row.querySelector('select').selectedIndex = 0;
            body.appendChild(row);
            row.querySelector('select').focus();
            recalc();
        });
        body.addEventListener('click', e => {
            if (!e.target.closest('.remove-line')) return;
            if (body.rows.length > 1) e.target.closest('tr').remove();
            recalc();
        });
        recalc();
    }
})();
