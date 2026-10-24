# NF-e modelo 55 — SEFAZ/MT

Registro das decisões para a NF-e de mercadorias. Preencher com o contador antes de implementar a integração oficial.

## Cenário (a confirmar)

| Item | Valor | Confirmado por / data |
|---|---|---|
| Inscrição estadual / credenciamento SEFAZ/MT | | |
| CRT | | |
| CFOPs usados (internas 5xxx / interestaduais 6xxx) | | |
| CST/CSOSN, NCM e CEST dos produtos (ex.: perfumes) | | |
| Série e numeração inicial | | |
| Prazo de cancelamento aplicável (padrão configurado: 24 h em `Fiscal:NfeCancelDeadlineHours`) | | |

## Integração

- Portal NF-e SEFAZ/MT: https://www.sefaz.mt.gov.br/portal/nfe/
- Portal Nacional NF-e: https://www.nfe.fazenda.gov.br/portal/
- Implementar como um novo `IFiscalProvider` em um projeto `FiscalPlatform.Integrations.Nfe`, seguindo o modelo de `src/FiscalPlatform.Integrations.Nfse`.

## Pendências para implementar o adaptador

- [ ] MOC, schemas XSD e notas técnicas vigentes
- [ ] URLs de homologação/produção obtidas da documentação oficial
- [ ] Geração do XML, assinatura com o A1, autorização, consulta de protocolo
- [ ] Eventos: cancelamento e carta de correção
- [ ] Contingência e DANFE (hoje a aplicação gera apenas um resumo interno, que não é DANFE)
