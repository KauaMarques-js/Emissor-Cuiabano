# Emissor Fiscal (Cuiabá/MT)

Aplicação web, para uso no seu computador, que emite **NF-e** (venda de produtos) e **NFS-e** (serviços) para uma ou várias empresas.

> **Situação atual:**
> - **NFS-e:** emissão **real** pelo Sistema Nacional NFS-e (Emissor Nacional, obrigatório em Cuiabá desde 01/11/2026). Veja [Emitir NFS-e de verdade](#7-emitir-nfs-e-de-verdade).
> - **NF-e:** ainda só em **modo simulação** (sem valor fiscal).

---

## 1. O que você precisa instalar (uma vez só)

**.NET SDK 10**
1. Acesse https://dotnet.microsoft.com/download e baixe o **.NET 10 SDK** para Windows (x64).
2. Instale com as opções padrão.
3. Para conferir, abra o **Prompt de Comando** e digite:

   ```bash
   dotnet --version
   ```

   Deve aparecer um número começando com `10.`.

Não é preciso instalar banco de dados: o banco (SQLite) é criado automaticamente.

---

## 2. Como abrir o aplicativo

### Jeito mais fácil (Windows)

1. Abra a pasta do projeto (`C:\Dev\Emissor`) no Explorador de Arquivos.
2. Dê **dois cliques em `iniciar.cmd`**.
3. Uma janela preta vai abrir e, alguns segundos depois, o navegador abre em **http://localhost:5241**.
   - Na primeira vez pode demorar mais um pouco, porque o projeto é compilado.
   - Se o navegador não abrir sozinho, abra-o e acesse http://localhost:5241.

**Deixe a janela preta aberta** enquanto usa o sistema. Para parar, feche a janela ou pressione `Ctrl + C` nela.

### Pelo terminal

Na pasta do projeto:

```bash
dotnet run --project src/FiscalPlatform.Web
```

Depois acesse http://localhost:5241 no navegador.

---

## 3. Primeiro acesso (uma tela só)

Na primeira vez o sistema abre a tela **"Bem-vindo"**, que cria o acesso e deixa a empresa pronta de uma vez:

1. **Seu acesso:** e-mail e senha (mínimo de 8 caracteres). Você vai entrar sempre com esse e-mail.
2. **Sua empresa:** digite o **CNPJ**. Razão social, endereço e, quando a Receita informa, o regime tributário são preenchidos sozinhos. Confira e complete a inscrição municipal.
3. **O que você vai emitir:** marque NFS-e (serviços) e/ou NF-e (produtos) e escolha como emitir:
   - **Simulação:** para conhecer o sistema, sem enviar nada;
   - **Teste no governo (homologação):** envia de verdade, mas sem validade jurídica;
   - **Valendo (produção):** notas reais.
4. **Certificado digital A1:** envie o `.pfx` e a senha. É obrigatório para "teste no governo" e "valendo"; para simulação pode ficar em branco.

Depois é só cadastrar seus **serviços** (ou produtos) e emitir. Tudo pode ser alterado depois em **Empresa**.

---

## 4. Uso do dia a dia

### Emitir uma nota
1. Clique em **Emitir nota** (é a tela inicial).
2. Escolha **NF-e** ou **NFS-e**.
3. Escolha o cliente na lista ou clique em **"Cliente novo? Cadastre aqui"**.
4. Escolha o(s) produto(s) ou o serviço. Preço e total são preenchidos automaticamente.
5. Clique em **Emitir** e confirme.

### O resultado aparece na hora

| Resultado | O que fazer |
|---|---|
| 🟩 **AUTORIZADA** | Baixe o **PDF** e o **XML**. |
| 🟥 **REJEITADA** | Leia o motivo e clique em **Corrigir e emitir de novo**. |
| 🟨 **PENDENTE** | Aguarde um pouco e clique em **Consultar situação**. |
| 🟨 **SEM RESPOSTA** | **Não emita de novo.** Clique em **Consultar situação** para saber se a nota foi autorizada. |
| ⬛ **CANCELADA** | A nota foi cancelada. O PDF continua disponível. |

### Outras ações na tela da nota
- **Baixar PDF** / **Baixar XML**
- **Consultar situação**
- **Cancelar nota** (informe o motivo, com pelo menos 15 letras)
- **Emitir outra igual** (copia a nota para um novo rascunho)

### Encontrar notas antigas
Use **Notas**: dá para filtrar por tipo, situação, data ou cliente, e baixar PDF/XML direto da lista.

> O PDF é um documento auxiliar simplificado e **não substitui o DANFE/DANFSe oficial**.

---

## 5. Onde ficam os dados e como fazer backup

Os dados ficam em `%LOCALAPPDATA%\EmissorFiscal` (geralmente `C:\Users\SEU_USUARIO\AppData\Local\EmissorFiscal`). Lá estão:
- o banco de dados;
- os certificados digitais, criptografados;
- os XMLs das notas;
- os backups.

**Backup:** no menu **Mais → Backup**, clique em **Gerar backup agora** e guarde o arquivo `.zip` fora do computador (pen drive, nuvem).

**Usar outra pasta de dados:** edite `src/FiscalPlatform.Web/appsettings.json`, campo `"DataDirectory"`. Por exemplo:

```json
"App": { "DataDirectory": "D:\\EmissorFiscal" }
```

---

## 6. Problemas comuns

| Problema | Solução |
|---|---|
| "O .NET SDK não foi encontrado" | Instale o .NET SDK 10 (passo 1) e abra o `iniciar.cmd` de novo. |
| "address already in use" / porta 5241 ocupada | O sistema já está aberto em outra janela. Feche a outra janela ou apenas acesse http://localhost:5241. |
| Esqueci a senha | Ainda não existe tela para recuperar a senha. Para começar do zero, apague a pasta `%LOCALAPPDATA%\EmissorFiscal`. **Isso apaga todos os dados**, então faça backup antes. |
| A tela pede para selecionar a empresa | Escolha a empresa no seletor no canto superior direito ou cadastre uma em **Empresa**. |
| A tela diz que a empresa não emite aquele tipo de nota | Em **Empresa**, marque NFS-e ou NF-e em "O que você vai emitir". |

---

## 7. Emitir NFS-e de verdade

A NFS-e é enviada ao **Sistema Nacional NFS-e** (Sefin Nacional), com a nota no leiaute oficial v1.01, assinada e enviada com o certificado digital da empresa.

### O que você precisa ter antes
1. **Certificado digital A1** (e-CNPJ ICP-Brasil, arquivo `.pfx` e senha) da empresa.
2. A empresa **habilitada no Emissor Nacional**: entre em https://www.nfse.gov.br/EmissorNacional com o certificado e confira se consegue acessar.
3. Com o contador, confirme:
   - o **código de tributação nacional** (6 dígitos) de cada serviço;
   - se há **retenção de ISS**;
   - o seu **% aproximado de tributos do Simples Nacional** (se for ME/EPP).

### Passo a passo (primeiro em homologação)
1. Em **Empresa** (ou já no primeiro acesso):
   - em "O que você vai emitir", marque **NFS-e** e escolha **Teste no governo (homologação)**;
   - preencha o **% aproximado de tributos** (pedido conforme o regime);
   - envie o **certificado A1** e a senha. O sistema confere se o CNPJ do certificado é o da empresa;
   - confira a **inscrição municipal** e o endereço.
2. **Serviços:** cadastre o serviço com o **código de tributação nacional** e, se quiser, o NBS (9 dígitos).
3. **Emitir nota:** emita uma nota de teste. Em homologação ela vai de verdade ao sistema nacional, mas **não tem validade jurídica**.
4. Deu **AUTORIZADA**? Em **Empresa**, mude para **Valendo (produção)**. A partir daí as notas são reais.

A numeração (série 1 e próximo número 1) e o regime especial de ISS ficam em "Numeração e opções avançadas". Só mexa neles se já emitiu por outro sistema na mesma série.

### Mensagens comuns

| Mensagem | O que fazer |
|---|---|
| **HTTP 403 / Acesso negado** | O certificado não é aceito: confira se é o A1 ICP-Brasil da própria empresa e se a empresa acessa o Emissor Nacional. |
| **E0010 / série** | A série deve ficar entre 1 e 49999. |
| **E0014 / DPS já existe** | O número já foi usado: o sistema tenta recuperar a nota existente automaticamente. Se não conseguir, aumente o "Próximo número" da NFS-e. |
| **SEM RESPOSTA** | Não emita de novo: clique em **Consultar situação** (o sistema busca a nota pela DPS). |
| Erro sobre alíquota | ME/EPP com ISS retido precisa da alíquota do ISS no cadastro do serviço. Nos outros casos, quem informa a alíquota é o sistema nacional. |

A mensagem completa do governo fica em **Mais → Rejeições e falhas**, junto com o XML enviado.

### Ajustes técnicos (só se o governo mudar algo)
No arquivo `src/FiscalPlatform.Web/appsettings.json`, seção `"NfseNacional"`:
- `HomologacaoBaseUrl` / `ProducaoBaseUrl`: endereços da API.
  - Padrão: `https://sefin.producaorestrita.nfse.gov.br/SefinNacional` e `https://sefin.nfse.gov.br/SefinNacional`.
  - Se aparecer erro **HTTP 404** em todas as emissões, tente `https://sefin.producaorestrita.nfse.gov.br/API/SefinNacional`.
- `SignatureAlgorithm`: `sha256` (padrão) ou `sha1`, caso o governo recuse a assinatura.

### Limitações conhecidas
- **PDF:** o PDF gerado é um documento auxiliar simplificado. Ele **não segue o leiaute oficial do DANFSe** (NT 008/2026, com QR Code). O DANFSe oficial pode ser baixado no Emissor Nacional pela chave de acesso.
- **IBS/CBS (Reforma Tributária):** o grupo IBS/CBS não é enviado. Para optantes do Simples ele só é obrigatório a partir de 2027. Para empresas fora do Simples, confirme com o contador se já é exigido.
- **Substituição:** é enviada com o motivo "99 - Outros".
- **Teste com o seu certificado:** a integração foi testada contra os esquemas oficiais (XSD v1.01) e chegou ao servidor de homologação do governo, mas **ainda não foi testada com o seu certificado**. Faça as primeiras notas em homologação.

---

## Estado das integrações

| Integração | Situação |
|---|---|
| **Simulação** (padrão) | Funciona de ponta a ponta, **sem enviar nada** a serviços oficiais. |
| **NFS-e Nacional (oficial)** | **Implementada**: emitir, consultar (inclusive recuperar após timeout), cancelar e substituir. |
| NF-e SEFAZ/MT (oficial) | **Ainda não implementada.** A NF-e só funciona em simulação. |

Os adaptadores oficiais devem ser escritos a partir da documentação vigente (manuais, XSD, endereços de homologação e produção). Veja [docs/fiscal](docs/fiscal) e o plano completo em [docs/PLANO_PLATAFORMA_FISCAL_CUIABA_MT.md](docs/PLANO_PLATAFORMA_FISCAL_CUIABA_MT.md).

### Testar situações na simulação
Em **Mais opções → Observações** da nota, escreva um destes marcadores:
- `[SIM:REJEITAR]`: a nota volta rejeitada;
- `[SIM:TIMEOUT]`: a nota fica "sem resposta" e a consulta mostra que ela foi autorizada;
- `[SIM:PENDENTE]`: a nota fica em processamento;
- `[SIM:INDISPONIVEL]`: simula o serviço fora do ar.

---

## Para desenvolvedores

Rodar os testes:

```bash
dotnet test
```

Estrutura do projeto:

```text
src/
  FiscalPlatform.Domain/            entidades, estados e regras por documento
  FiscalPlatform.Application/       validações, emissão, lotes
  FiscalPlatform.Infrastructure/    EF Core + SQLite, certificados, PDF, backup, simulação
  FiscalPlatform.Integrations.Nfse/ NFS-e Nacional (DPS, assinatura, API oficial)
  FiscalPlatform.Web/               telas (ASP.NET Core MVC)
tests/FiscalPlatform.UnitTests/
```

Criar uma migração de banco (elas são aplicadas automaticamente ao iniciar):

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMigracao --project src/FiscalPlatform.Infrastructure --startup-project src/FiscalPlatform.Infrastructure
```

### Contribuindo

O fluxo de branches (`main`, `develop`, `feature/*`), o padrão de commits e o checklist de pull request estão em [CONTRIBUTING.md](CONTRIBUTING.md). O CI (GitHub Actions) compila sem avisos e roda os testes a cada push.
