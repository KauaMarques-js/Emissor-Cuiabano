# NFS-e — Cuiabá/MT

Registro das decisões tributárias e técnicas da NFS-e (etapa 1 do plano). Preencher com o contador antes de implementar a integração oficial.

## Cenário tributário (a confirmar)

| Item | Valor | Confirmado por / data |
|---|---|---|
| CNPJ / inscrição municipal | | |
| Regime (MEI / Simples / outro) | | |
| CNAEs | | |
| Serviços emitidos e código de tributação nacional (cTribNac) | | |
| Alíquota de ISS e retenções | | |
| Município de incidência | 5103403 (Cuiabá), salvo exceções | |

## Integração

- Comunicado da prefeitura: emissor municipal (webservice e Free Nota) até 31/10/2026; a partir de 01/11/2026 emissões exclusivamente pelo Emissor Nacional. Confirmar comunicado mais recente.
- Documentação técnica atual: https://www.gov.br/nfse/pt-br/biblioteca/documentacao-tecnica/documentacao-atual
- Implementar em `src/FiscalPlatform.Integrations.Nfse/NfseNacionalProvider.cs` (contrato `IFiscalProvider`).

## Implementação (24/10/2026)

Fontes usadas, todas do portal oficial (gov.br/nfse → Documentação atual):
- `NFSe-ESQUEMAS_XSD-v1.01-20260209` (os XSD estão copiados em `tests/FiscalPlatform.UnitTests/Schemas/nfse-1.01`; a DPS gerada é validada contra eles nos testes);
- `ANEXO_I-SEFIN_ADN-DPS_NFSe-SNNFSe-v1.01-20260209`: regras de opSimpNac, regApTribSN, pAliq, totTrib, série e Id da DPS;
- `ANEXO_II-SEFIN_ADN-PEDREGEVT_EVT-SNNFSe-v1.01-20260122`: evento 101101 de cancelamento;
- Manual dos Contribuintes, Emissor Público API v1.2: POST /nfse, GET /nfse/{chave}, GET /dps/{id}, POST /nfse/{chave}/eventos.

Pontos sem confirmação oficial nos documentos acima. Todos podem ser ajustados no `appsettings.json` ou no código:
- endereço base `…/SefinNacional`: confirmado por relatos de uso real; a página do Swagger cita `…/API/SefinNacional`;
- nomes dos campos JSON `dpsXmlGZipB64`, `pedidoRegistroEventoXmlGZipB64`, `chaveAcesso`, `erros[].Codigo/Descricao`: o Swagger oficial exige certificado para abrir;
- algoritmo de assinatura: SHA-256 por padrão, com SHA-1 configurável.

Código: `src/FiscalPlatform.Integrations.Nfse` (`DpsXmlBuilder`, `XmlSigner`, `NfseNacionalProvider`) e as regras em `src/FiscalPlatform.Application/NfseRules.cs`.

## Pendências

- [ ] Primeira emissão em homologação com o certificado real da empresa
- [ ] DANFSe no leiaute da NT 008/2026 (hoje o PDF é simplificado)
- [ ] Grupo IBS/CBS quando passar a ser exigido para o regime da empresa
- [ ] Escolha do motivo da substituição (hoje é sempre "99 - Outros")
