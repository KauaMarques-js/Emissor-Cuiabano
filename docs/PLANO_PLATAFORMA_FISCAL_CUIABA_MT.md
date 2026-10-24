# Plataforma Própria de Emissão Fiscal — Cuiabá/MT

## 1. Objetivo

Desenvolver uma plataforma web própria para cadastrar e gerenciar **várias empresas/CNPJs**, seus dados fiscais e certificados digitais, além de clientes, produtos e serviços reutilizáveis. A plataforma deve emitir documentos fiscais, acompanhar autorizações e consultar o histórico fiscal de cada empresa separadamente.

A aplicação será independente da Spedy: **não utilizará APIs, SDKs, credenciais, infraestrutura ou serviços da Spedy**. A comunicação fiscal será feita diretamente com os serviços oficiais aplicáveis (SEFAZ/MT para NF-e e plataforma nacional da NFS-e, conforme as regras vigentes).

A primeira localidade atendida será **Cuiabá, Mato Grosso, Brasil**.

## 2. Escopo fiscal inicial

- **NF-e modelo 55:** venda de mercadorias, como perfumes, conforme o enquadramento tributário da empresa.
- **NFS-e:** prestação de serviços, como serviços tecnológicos, quando aplicável.
- **NFC-e:** fora do escopo inicial.
- **MDF-e, CT-e e outros documentos:** fora do escopo inicial.

> Importante: a aplicação deve confirmar com contador e fontes oficiais quais documentos e regras se aplicam ao CNPJ, regime tributário, atividade, inscrição estadual/municipal e operação. A plataforma não deve presumir que toda venda de produto ou todo serviço usa o mesmo documento.

## 3. Tecnologia e arquitetura

- **Backend:** C# com ASP.NET Core.
- **Interface:** ASP.NET Core MVC com Razor Views; priorizar uma interface simples, clara e responsiva.
- **ORM:** Entity Framework Core.
- **Banco inicial:** SQLite, adequado para um primeiro uso individual/local.
- **Migração futura:** manter o acesso a dados organizado para permitir migrar para PostgreSQL se a plataforma for hospedada, tiver mais usuários ou precisar de maior concorrência.
- **Arquitetura:** monólito modular, sem microsserviços desnecessários.
- **Testes:** testes unitários para regras fiscais e testes de integração para os adaptadores dos serviços oficiais.

Separar, no mínimo, os módulos `Companies`, `Customers`, `Products`, `Services`, `FiscalDocuments`, `Nfe`, `Nfse`, `FiscalIntegrations`, `Certificates`, `Audit` e `Shared`.

## 4. Integrações oficiais: não confundir os dois documentos

### 4.1 NF-e modelo 55 — SEFAZ de Mato Grosso

A NF-e de mercadorias é autorizada pela administração tributária estadual, utilizando os serviços oficiais de NF-e e os leiautes nacionais aplicáveis. A implementação deve consultar a documentação atual do Portal da NF-e e do Portal da SEFAZ/MT antes de codificar endpoints, schemas ou regras.

Fontes oficiais:
- Portal da NF-e da SEFAZ/MT: https://www.sefaz.mt.gov.br/portal/nfe/
- Credenciamento para emissão de NF-e: https://www.sefaz.mt.gov.br/portal/nfe/
- Documentação nacional da NF-e (acessível a partir do portal oficial): https://www.nfe.fazenda.gov.br/portal/

**Não presumir URLs de produção/homologação nem criar endpoints manualmente.** Obter os endereços, serviços disponíveis, versões de leiaute e regras vigentes da documentação oficial.

### 4.2 NFS-e — serviços prestados em Cuiabá/MT

**Não implementar a integração como se a API municipal antiga fosse continuar aceitando emissões indefinidamente.** Em notícia publicada pela Prefeitura de Cuiabá, foi informado que o emissor municipal, incluindo o webservice local e o Free Nota, permaneceria disponível até **31/10/2026**, e que, a partir de **01/11/2026**, novas emissões deveriam ocorrer exclusivamente pelo Emissor Nacional. Como a regra pode ser atualizada, confirmar o comunicado oficial mais recente antes da implantação.

Para uma implementação nova, planejar a NFS-e pela **API do Sistema Nacional da NFS-e / Emissor Público Nacional**, seguindo a documentação técnica atual, e validar as condições específicas do município e do contribuinte.

Fontes oficiais:
- Comunicado da Prefeitura de Cuiabá sobre a transição: https://cuiaba.mt.gov.br/noticias/cuiaba-prorroga-prazo-para-emissao-de-nfs-e-pelo-emissor-nacional
- Documentação técnica atual da NFS-e Nacional: https://www.gov.br/nfse/pt-br/biblioteca/documentacao-tecnica/documentacao-atual
- Portal oficial da NFS-e: https://www.gov.br/nfse/pt-br

> A NFS-e não deve ser enviada para a SEFAZ/MT como se fosse uma NF-e modelo 55. São documentos e integrações diferentes. O município e o regime tributário do emitente influenciam o fluxo aplicável.

## 5. Passo a passo para implementar a integração de NFS-e (Cuiabá)

### Etapa 1 — Confirmar o cenário tributário

1. Identificar CNPJ, inscrição municipal, atividade/CNAE, regime tributário e condição de MEI/Simples Nacional ou outro regime.
2. Confirmar com o contador quais serviços serão emitidos e quais códigos de serviço, retenções e regras tributárias devem ser informados.
3. Consultar a Prefeitura de Cuiabá e a documentação nacional atual para confirmar a regra aplicável ao CNPJ.
4. Registrar essas decisões em `docs/fiscal/cuiaba-nfse.md`; não embutir premissas tributárias no código sem documentação.

### Etapa 2 — Ler a documentação técnica atual

1. Abrir a página oficial de documentação atual da NFS-e Nacional.
2. Baixar o **Manual de Contribuintes / Emissor Público API**, os leiautes e esquemas XSD aplicáveis e os anexos atuais.
3. Conferir autenticação, certificados, assinatura digital, formato de requisição, respostas, consulta, cancelamento/eventos e ambientes disponíveis.
4. Verificar notas técnicas e alterações relacionadas à Reforma Tributária vigentes na data da implementação.
5. Não copiar exemplos de versões antigas sem validar se ainda são compatíveis com a versão de produção.

### Etapa 3 — Preparar acesso e ambiente de testes

1. Seguir o procedimento oficial de acesso/credenciamento exigido para o contribuinte e para a API.
2. Preparar certificado digital e credenciais somente se forem exigidos pelo fluxo documentado.
3. Guardar segredos fora do repositório e nunca registrar senhas, tokens ou conteúdo privado de certificados nos logs.
4. Configurar ambientes de teste/homologação e produção separadamente, com URLs obtidas da documentação oficial.
5. Se não houver ambiente de teste habilitado para o perfil do contribuinte, documentar a limitação e não simular autorização real.

### Etapa 4 — Criar o adaptador de integração

Criar uma interface interna, por exemplo `INfseProvider`, com operações conceituais como:

- validar os dados antes do envio;
- preparar a DPS conforme o leiaute oficial vigente;
- assinar o documento quando exigido;
- transmitir a DPS;
- consultar o resultado/protocolo;
- consultar uma NFS-e já emitida;
- solicitar cancelamento ou evento permitido, conforme regras oficiais.

A interface acima é um **contrato interno sugerido**, não uma lista de nomes de endpoints oficiais. Os métodos HTTP, caminhos, cabeçalhos, payloads e códigos de resposta devem ser implementados apenas depois de conferidos no manual vigente.

### Etapa 5 — Montar e validar a DPS

1. Mapear os dados do formulário para o modelo de domínio da aplicação.
2. Validar CNPJ/CPF, município, código de serviço, valores, alíquotas, retenções e campos obrigatórios segundo o regime e o leiaute atual.
3. Gerar a DPS com o schema oficial correto.
4. Validar o XML/JSON gerado conforme o formato e os esquemas oficiais aplicáveis.
5. Assinar digitalmente quando exigido.
6. Manter uma cópia da requisição fiscal enviada e da resposta recebida, protegendo dados sensíveis.

### Etapa 6 — Transmitir e tratar o resultado

1. Enviar a requisição ao ambiente correto.
2. Interpretar a resposta conforme a documentação oficial: autorizada, rejeitada, pendente/em processamento ou falha de comunicação.
3. Salvar protocolo, identificador, número fiscal, chave/código de verificação e XML/documentos retornados quando existirem.
4. Se ocorrer timeout, **não reenviar cegamente**: consultar primeiro o estado da operação para evitar duplicidade.
5. Guardar código e mensagem da rejeição para correção pelo usuário.
6. Só exibir “Emitida/Autorizada” depois de confirmação oficial; resposta HTTP 200 isolada não é prova suficiente.

### Etapa 7 — Homologar casos de ponta a ponta

Testar, no mínimo:

- emissão válida;
- campo obrigatório ausente;
- código de serviço inválido;
- dados fiscais incompatíveis;
- rejeição oficial;
- timeout após transmissão;
- consulta posterior;
- cancelamento/evento, se permitido;
- certificado expirado ou indisponível;
- indisponibilidade temporária do serviço.

Manter evidências de cada teste e validar com o contador antes de usar em produção.

## 6. Passo a passo para NF-e modelo 55 (mercadorias)

1. Confirmar com contador a necessidade e o enquadramento da NF-e 55 para a empresa e as operações pretendidas.
2. Verificar inscrição estadual e realizar o credenciamento exigido pela SEFAZ/MT, seguindo o portal oficial e o procedimento aplicável.
3. Obter certificado digital ICP-Brasil adequado e configurar seu uso de forma segura.
4. Consultar o Manual de Orientação do Contribuinte, schemas XML, notas técnicas, regras de validação e serviços vigentes.
5. Implementar a geração do XML da NF-e e assinatura digital conforme especificação oficial.
6. Implementar transmissão, consulta de recibo/protocolo, autorização, rejeição e contingência somente conforme regras oficiais vigentes.
7. Gerar e armazenar XML autorizado e DANFE quando aplicável.
8. Implementar cancelamento e carta de correção apenas conforme os eventos e prazos autorizados.
9. Testar em homologação antes de produção.

Referências oficiais:
- SEFAZ/MT — Portal NF-e: https://www.sefaz.mt.gov.br/portal/nfe/
- Portal Nacional NF-e: https://www.nfe.fazenda.gov.br/portal/

## 7. Banco de dados e entidades

Começar com SQLite e EF Core. Criar entidades semelhantes a:

- `User`
- `Company` (uma plataforma pode gerenciar várias empresas/CNPJs)
- `CompanyAddress` (endereços e estabelecimentos, se aplicável)
- `CompanyFiscalSettings`
- `CompanyTaxRegimeHistory` (alterações de regime/parametrização com vigência)
- `Customer`
- `Product`
- `Service`
- `FiscalDocument`
- `FiscalDocumentItem`
- `FiscalEvent`
- `TransmissionAttempt`
- `FiscalProviderConfiguration`
- `CertificateMetadata`
- `AuditLog`

Cada documento fiscal deve manter seu tipo (`NFE` ou `NFSE`), ambiente, estado interno, identificador oficial, protocolo, datas, tentativas, mensagens de rejeição e referências aos arquivos fiscais. Não misturar campos exclusivos de NF-e com os de NFS-e numa única estrutura rígida; usar entidades relacionadas ou payloads tipados e versionados.

Armazenar XML, PDF/DANFE e respostas fiscais em armazenamento de arquivos protegido, com referências no banco e política de backup. SQLite não deve ser a única cópia dos documentos fiscais.

## 8. Cadastro e gerenciamento de múltiplas empresas

A plataforma deve permitir cadastrar várias empresas/CNPJs na mesma instalação. Cada empresa terá configurações, certificados, clientes/produtos/serviços (com possibilidade de compartilhamento controlado, se desejado) e documentos fiscais próprios. O usuário deve selecionar claramente a empresa ativa antes de emitir qualquer nota.

### Dados da empresa

Criar um fluxo de cadastro dividido em etapas, com salvamento parcial e indicação do que falta configurar:

1. **Identificação:** CNPJ, razão social, nome fantasia, situação cadastral quando consultável, natureza jurídica e dados de contato.
2. **Endereço:** CEP, logradouro, número, complemento, bairro, município, UF e código IBGE do município.
3. **Inscrições:** inscrição estadual, inscrição municipal, indicador de contribuinte e demais registros exigidos para o documento e a atividade.
4. **Enquadramento fiscal:** regime tributário, CRT quando aplicável, CNAEs, atividades, códigos de serviço, configurações fiscais e vigência. Não presumir alíquotas ou regras: devem ser conferidas com contador e fontes oficiais.
5. **NF-e:** série, numeração inicial/controle, ambiente de homologação ou produção, configurações exigidas pela SEFAZ/MT e dados necessários para emissão de mercadorias.
6. **NFS-e:** município de incidência/estabelecimento, inscrição municipal, códigos de serviço, parâmetros tributários e credenciais/certificado conforme exigência da integração oficial vigente.
7. **Certificado digital:** cadastrar certificado A1 da empresa, validade, titular, CNPJ associado, status de validação e data de expiração. O certificado precisa ser validado para a empresa selecionada antes de permitir operações que dependam dele.
8. **Checklist de prontidão:** mostrar itens pendentes, por exemplo cadastro incompleto, certificado ausente/expirado, credenciamento não confirmado, ambiente não configurado ou dados fiscais obrigatórios faltando.

Não permitir que um documento seja emitido usando acidentalmente os dados ou certificado de outra empresa. Cada documento deve registrar o `CompanyId`, o estabelecimento emissor quando aplicável e a configuração fiscal usada no momento da emissão, para preservar o histórico mesmo que o cadastro seja alterado depois.

### Gestão do certificado digital A1

Criar uma tela de certificados vinculados a cada empresa:

- Importar arquivo `.pfx`/`.p12` do certificado A1.
- Solicitar a senha durante a importação e validar se o arquivo abre, se a cadeia é utilizável e se a validade está dentro do prazo.
- Conferir, quando tecnicamente possível, se o CNPJ/titular do certificado corresponde à empresa selecionada; apresentar aviso claro se não corresponder.
- Exibir apenas metadados necessários: titular, CNPJ/identificador, autoridade emissora, início/fim da validade e estado (válido, próximo do vencimento, expirado, inválido ou não testado).
- Permitir substituir/renovar certificado e manter histórico de validade sem conservar cópias antigas desnecessárias.
- Avisar com antecedência configurável sobre vencimento e impedir emissão que dependa de certificado inválido/expirado.
- Nunca mostrar nem registrar a senha em logs. Não salvar senha em texto puro no banco. Proteger o arquivo em repouso com criptografia e controle de acesso; manter chave de criptografia fora do banco e do repositório. Em hospedagem, usar serviço de segredos/gerenciamento de chaves adequado.
- Não disponibilizar o certificado para download após a importação, salvo se existir uma necessidade administrativa explícita e protegida.
- Não presumir que todo fluxo de NFS-e exige o mesmo certificado ou que qualquer A1 será aceito: confirmar o requisito no manual oficial vigente para cada integração.

Para a primeira versão local de uso individual, o certificado pode ficar em diretório de dados protegido fora da pasta pública da aplicação, com permissões restritas e backup criptografado. Se a plataforma for hospedada ou acessada por outros dispositivos, revisar a arquitetura de segredos e a proteção do certificado antes de colocar em produção.

### Isolamento por empresa

- Filtrar listas, pesquisas, documentos, arquivos, configurações e histórico pela empresa selecionada.
- Validar no servidor que o documento e o certificado pertencem à mesma empresa; não confiar apenas no identificador enviado pela interface.
- Incluir a empresa/estabelecimento nos logs de auditoria e tentativas de transmissão.
- Evitar reutilizar automaticamente série, numeração, inscrição, regime, certificado ou parâmetros fiscais entre CNPJs.
- Se houver matriz e filiais, modelar estabelecimentos separadamente quando a regra fiscal exigir, sem tratar todo CNPJ como uma única configuração indistinta.

## 9. Fluxo de emissão, edição, cancelamento e rejeições

A interface deve ser simples, com ações explícitas e condicionadas ao estado do documento:

- **Nova nota:** escolher empresa, tipo de documento (NF-e 55 ou NFS-e), cliente/tomador e itens.
- **Salvar rascunho:** guardar os dados incompletos para continuar depois, sem transmitir.
- **Editar:** permitir edição livre de rascunhos e correção de documentos rejeitados antes de uma nova tentativa. Para documentos já autorizados, não oferecer edição direta do documento fiscal original; mostrar os procedimentos legais disponíveis (cancelamento, carta de correção, substituição ou outro evento) conforme o tipo de nota e as regras oficiais.
- **Emitir:** validar cadastro, certificado, credenciamento, campos fiscais e ambiente; pedir confirmação antes da transmissão.
- **Cancelar:** habilitar apenas quando o estado, prazo e regras do documento permitirem. Exibir motivo obrigatório quando exigido e guardar protocolo/resposta oficial do evento.
- **Duplicar para nova nota:** criar um novo rascunho baseado em uma nota anterior, sem copiar chave, protocolo, autorização ou estado fiscal; recalcular/validar dados atuais.
- **Emitir em massa:** selecionar rascunhos elegíveis da mesma empresa e do mesmo tipo de documento, validar cada item antes de enfileirar e mostrar progresso/resultados individuais. Não misturar silenciosamente empresas, tipos de documento ou ambientes no mesmo lote.
- **Consultar situação:** consultar o serviço oficial quando disponível, especialmente após timeout ou estado pendente.

### Central de rejeições

Para cada tentativa, guardar e mostrar:

- tipo de documento e empresa emissora;
- data/hora, ambiente e operação executada;
- código oficial de rejeição/erro, quando fornecido;
- mensagem completa recebida do serviço fiscal, sem substituir por texto genérico;
- detalhes adicionais/campos apontados na resposta, quando houver;
- resumo legível para o usuário e sugestão de correção, claramente identificados como orientação da aplicação;
- conteúdo técnico de requisição/resposta necessário para auditoria, com segredos e dados sensíveis protegidos/redigidos;
- histórico de correções e novas tentativas.

Separar rejeição fiscal de erro de conexão, timeout, falha de certificado e erro interno. Um timeout não significa rejeição nem autorização. Após falha de comunicação, consultar a situação antes de tentar novamente para evitar duplicidade. A mensagem original do serviço oficial deve continuar acessível mesmo se a aplicação oferecer uma explicação simplificada.

## 10. Segurança e confiabilidade

- Nunca salvar senha de certificado em texto puro.
- Não versionar certificados, tokens, senhas, XMLs reais ou dados pessoais no Git.
- Usar secrets/environment variables em desenvolvimento e armazenamento protegido apropriado em produção.
- Proteger endpoints administrativos com autenticação e autorização.
- Registrar auditoria de emissão, cancelamento, alterações de cadastro e ações críticas.
- Aplicar idempotência e bloqueios para impedir emissão duplicada.
- Separar configuração de homologação e produção.
- Fazer backup e testar restauração dos dados e documentos fiscais.
- Não considerar a emissão concluída antes da confirmação do serviço fiscal oficial.

## 11. Interface mínima

1. Login.
2. Painel com status das últimas emissões.
3. Cadastro e seleção de múltiplas empresas/CNPJs e configurações fiscais independentes.
4. Cadastro de clientes/tomadores.
5. Cadastro de produtos e serviços.
6. Nova NF-e.
7. Nova NFS-e.
8. Lista e detalhes de documentos fiscais.
9. Tela de rejeições e tentativas de transmissão.
10. Configuração/renovação de certificados A1 por empresa e gestão de ambientes.
11. Histórico de eventos e auditoria.

A interface deve ser objetiva, sem excesso de gradientes, bordas arredondadas, animações ou componentes decorativos. Priorizar mensagens de erro acionáveis, acessibilidade e formulários com validação clara.

## 12. Organização sugerida do projeto

```text
src/
  FiscalPlatform.Web/              # ASP.NET Core MVC e interface
  FiscalPlatform.Application/      # casos de uso e validações
  FiscalPlatform.Domain/           # entidades e regras de domínio
  FiscalPlatform.Infrastructure/  # EF Core, SQLite, arquivos, segurança
  FiscalPlatform.Integrations.Nfse/ # adaptador NFS-e Nacional
  FiscalPlatform.Integrations.Nfe/  # adaptador NF-e SEFAZ
 tests/
  FiscalPlatform.UnitTests/
  FiscalPlatform.IntegrationTests/
docs/
  fiscal/
    cuiaba-nfse.md
    mt-nfe.md
    homologacao.md
```

Para um MVP individual, os projetos podem ser simplificados inicialmente, mas as responsabilidades devem permanecer separadas.

## 13. Ordem de desenvolvimento

1. Confirmar o cenário tributário com contador e fontes oficiais.
2. Criar solução ASP.NET Core MVC, EF Core e SQLite.
3. Implementar cadastro de múltiplas empresas, dados fiscais, certificados A1, clientes, produtos e serviços.
4. Criar domínio e histórico de documentos fiscais sem transmitir documentos reais.
5. Implementar primeiro **um** fluxo fiscal de ponta a ponta em homologação (recomenda-se começar pelo documento mais urgente para a operação real).
6. Adicionar tratamento de autorização, rejeição, timeout e consulta.
7. Implementar o segundo tipo de documento com adaptador separado.
8. Homologar, revisar segurança e realizar backup/restauração.
9. Só então habilitar produção.

## 14. Critérios de aceite

- Cadastro e gerenciamento de várias empresas/CNPJs com isolamento de dados fiscais.
- Certificado A1 vinculado à empresa correta, com validade, proteção de arquivo e alertas de vencimento.
- Nenhuma chamada, dependência ou credencial da Spedy.
- Integrações separadas para NF-e 55 e NFS-e.
- Endpoints e schemas obtidos da documentação oficial atual, nunca inventados.
- A aplicação diferencia rascunho, enviando, pendente, autorizada, rejeitada, cancelada e falha de comunicação.
- Timeout não gera reenvio cego nem duplicidade.
- Documentos e respostas oficiais ficam armazenados e consultáveis.
- Rejeições exibem o código e a mensagem original recebida, com histórico de tentativas.
- Rascunhos podem ser editados; documentos autorizados seguem os eventos fiscais legais, sem edição direta.
- Emissão em massa apresenta resultado individual por documento e evita reenvio duplicado.
- Homologação documentada antes de produção.
- Regras tributárias configuráveis e validadas com contador.

## 15. Aviso de atualização normativa

A emissão fiscal é regulada e os endpoints, leiautes, credenciamentos e regras podem mudar. Antes de cada implantação, conferir as versões atuais da SEFAZ/MT, Portal Nacional da NF-e, Prefeitura de Cuiabá e Portal Nacional da NFS-e. Este documento é um plano técnico de implementação, não substitui orientação contábil ou tributária.

## 16. Requisitos funcionais obrigatórios da plataforma

A plataforma deve ser **simples de usar, mas não limitada a uma tela de emissão**. A interface pode ser enxuta; o sistema precisa cobrir o ciclo completo de cadastro, emissão, acompanhamento, correção e consulta, respeitando as diferenças legais e técnicas entre NF-e e NFS-e.

### 16.1 Cadastro reutilizável

- Cadastrar, editar, inativar e consultar clientes/tomadores, com CPF/CNPJ, razão/nome, inscrição estadual ou municipal quando aplicável, e-mail, telefone e endereços.
- Cadastrar produtos uma única vez e reutilizá-los em futuras NF-e: descrição, código/SKU, unidade, NCM, CEST quando aplicável, origem, GTIN quando houver, preço padrão e dados tributários necessários.
- Cadastrar serviços uma única vez e reutilizá-los em futuras NFS-e: descrição, código de serviço aplicável, município de incidência quando pertinente, preço padrão e configurações tributárias necessárias.
- Permitir alterar os cadastros sem modificar retroativamente documentos fiscais já emitidos. Cada documento deve guardar uma **fotografia dos dados e valores usados na emissão**, para preservar o histórico.
- Permitir duplicar uma nota anterior como rascunho, reutilizando cliente, itens e configurações permitidas; sempre revalidar os dados antes de emitir.
- Oferecer busca, filtros, paginação e importação/exportação de cadastros quando isso puder ser feito com segurança.

### 16.2 Criar e editar documentos

- Botão **Nova NF-e** e botão **Nova NFS-e**, com formulários diferentes e campos condicionais de acordo com o tipo de documento.
- Salvar como rascunho a qualquer momento, sem transmitir à autoridade fiscal.
- Editar, duplicar, excluir ou arquivar rascunhos conforme permissões e regras internas.
- Validar campos obrigatórios e regras conhecidas antes de transmitir; apontar o campo e explicar o que precisa ser corrigido.
- Calcular totais a partir dos itens e regras fiscais configuradas, sem depender exclusivamente de valores digitados manualmente. Não presumir alíquotas ou enquadramento: as regras devem ser configuráveis e validadas conforme regime tributário, operação e documentação oficial.
- Exibir uma prévia/resumo para conferência antes da transmissão.
- Após a autorização fiscal, bloquear a edição direta dos dados fiscais transmitidos. Não se pode “editar a nota autorizada” como se fosse um rascunho: mudanças devem seguir os mecanismos oficiais disponíveis, como cancelamento, carta de correção ou emissão de documento substituto, quando legalmente permitido para aquele documento e situação.

### 16.3 Ações visíveis por documento

A tela de detalhes e a listagem devem exibir as ações compatíveis com o estado do documento:

- **Visualizar detalhes** e consultar o histórico completo.
- **Editar rascunho**.
- **Validar** antes da emissão.
- **Emitir / Transmitir**.
- **Consultar situação na SEFAZ/serviço nacional**.
- **Baixar XML, DANFE ou PDF/documento auxiliar**, quando disponível e aplicável.
- **Duplicar como rascunho**.
- **Cancelar**, somente quando permitido pelas regras, prazos, eventos e situação oficial do documento.
- **Carta de correção ou outro evento corretivo**, somente quando aplicável à NF-e e permitido pela legislação; não disponibilizar como ação universal para NFS-e.
- **Substituir NFS-e**, quando esse procedimento for suportado pelo sistema nacional e permitido para o caso.
- **Tentar novamente**, apenas após determinar que a tentativa anterior não resultou em autorização e que é seguro fazê-lo.
- **Ver rejeição**, com código, mensagem oficial completa, detalhes técnicos úteis e orientação para corrigir os dados.

A aplicação deve ocultar ou desabilitar ações que não sejam válidas para aquele tipo de documento, estado, município, ambiente ou regra fiscal. Antes de cancelar, mostrar confirmação com o documento, a justificativa quando exigida e o impacto da ação.

### 16.4 Emissão em massa

Implementar emissão em massa para um conjunto selecionado de rascunhos/documentos previamente validados. A emissão em massa **não pode ser um único envio cego**: cada documento tem processamento e resultado individual.

Requisitos:

1. Selecionar várias notas por checkbox, filtros ou seleção de lote.
2. Validar todos os documentos antes de iniciar e mostrar quantos estão aptos e quantos têm erros.
3. Permitir remover do lote documentos inválidos sem bloquear os demais.
4. Confirmar o lote, tipo fiscal, empresa emissora e ambiente (homologação/produção) antes de transmitir.
5. Processar em fila, com limite de concorrência configurável, respeitando os limites e as regras do serviço oficial.
6. Exibir progresso e resultado individual: aguardando, processando, autorizada, rejeitada, pendente de consulta ou falha de comunicação.
7. Permitir abrir cada resultado, baixar documentos autorizados e exportar um relatório do lote.
8. Permitir filtrar apenas rejeitadas ou falhas para corrigir e reprocessar com segurança.
9. Usar chave de idempotência/controle interno e consultar o estado oficial após timeout antes de repetir transmissão, evitando duplicidade.
10. Registrar usuário/operador, horário, itens do lote, tentativas e resultados no histórico de auditoria.
11. Não misturar documentos de empresas emissoras diferentes no mesmo lote, a menos que o sistema tenha contexto explícito e validação separada por empresa.
12. Não executar cancelamentos em massa por padrão. Se essa função for adicionada futuramente, exigir seleção explícita, validação individual e confirmação reforçada.

### 16.5 Tratamento completo de rejeições e falhas

Toda tentativa de transmissão deve gerar um registro de processamento, inclusive quando houver erro de comunicação. Para cada rejeição, armazenar e mostrar:

- Tipo de documento (NF-e ou NFS-e), ambiente e empresa emissora.
- Data/hora da tentativa e identificador interno da operação.
- Código de rejeição/erro exatamente como retornado, quando fornecido.
- **Mensagem/comentário oficial completo da rejeição**, sem substituir por uma mensagem genérica.
- Detalhes estruturados da resposta, protocolo/recibo e identificadores fiscais quando existirem.
- Resumo amigável para o usuário e, quando possível, indicação do campo ou cadastro que precisa ser corrigido.
- Payload/XML enviado e resposta técnica, quando permitido e necessário para auditoria, protegendo credenciais, certificados e dados sensíveis.
- Estado da operação: rejeitada oficialmente, falha local de validação, falha de autenticação/certificado, indisponibilidade, timeout ou resultado desconhecido aguardando consulta.

A tela de rejeições deve ter filtros por data, documento, tipo, código, cliente, lote e estado. Deve permitir abrir a nota, corrigir o rascunho ou criar uma nova versão de emissão, e tentar novamente após validação. Manter o histórico das tentativas; não sobrescrever a rejeição anterior quando uma nova tentativa for feita.

**Distinção importante:** rejeição fiscal é uma resposta oficial que informa que o documento não foi autorizado. Timeout ou queda de conexão não significa rejeição: o resultado pode ser desconhecido. Nesse caso, consultar o serviço oficial antes de reenviar.

### 16.6 Histórico, pesquisa e relatórios

- Listagem unificada com filtros por tipo (NF-e/NFS-e), número, cliente, período, status, lote e ambiente.
- Detalhes com linha do tempo: criação, edição de rascunho, validação, transmissão, resposta, consulta, cancelamento/substituição e downloads.
- Busca por número interno e identificador/chave/protocolo oficial quando existir.
- Relatórios de emitidas, rejeitadas, canceladas, pendentes e falhas por período.
- Exportação CSV/Excel de listagens operacionais, sem substituir os arquivos fiscais oficiais.
- Guardar os XMLs e documentos auxiliares retornados pela autoridade fiscal em armazenamento protegido, com backup e política de retenção compatível com as obrigações legais.

## 17. Fluxos de interface por tipo de nota

### NF-e modelo 55 — venda de mercadorias

1. Acessar **Produtos** e cadastrar os produtos que serão vendidos.
2. Acessar **Clientes** e cadastrar o destinatário.
3. Clicar em **Nova NF-e**.
4. Selecionar cliente, adicionar produtos cadastrados, informar quantidades, valores, frete/desconto e demais dados da operação quando aplicáveis.
5. Preencher/confirmar natureza da operação, CFOP, NCM e tributação conforme orientação contábil e regras oficiais.
6. Salvar como rascunho e validar.
7. Conferir o resumo e clicar em **Emitir NF-e**.
8. Acompanhar o resultado: autorizada, rejeitada, pendente ou falha de comunicação.
9. Se autorizada, disponibilizar XML e DANFE. Se rejeitada, mostrar código e mensagem integral, apontar o que precisa ser corrigido e permitir nova tentativa segura.
10. Se for necessário corrigir uma NF-e autorizada, oferecer apenas as ações fiscais legalmente aplicáveis; não alterar silenciosamente o documento autorizado.

### NFS-e — prestação de serviços em Cuiabá/MT

1. Acessar **Serviços** e cadastrar os serviços usados com frequência.
2. Acessar **Clientes/Tomadores** e cadastrar o tomador.
3. Clicar em **Nova NFS-e**.
4. Selecionar tomador, serviço cadastrado, descrição, valores e demais informações exigidas pelo leiaute e pela situação tributária.
5. Preencher/confirmar código de serviço, município, retenções e tributação conforme as regras aplicáveis.
6. Salvar como rascunho e validar.
7. Conferir o resumo e clicar em **Emitir NFS-e**.
8. Consultar a resposta da integração oficial nacional aplicável a Cuiabá na data de implantação.
9. Se autorizada, guardar o identificador e o documento/XML/PDF fornecido pela integração. Se rejeitada, mostrar o código e a mensagem oficial integral.
10. Para corrigir ou desfazer uma NFS-e autorizada, disponibilizar somente cancelamento, substituição ou evento que a plataforma oficial permitir para o caso.

## 18. Telas previstas

1. **Painel:** totais recentes por status, falhas que exigem ação e atalhos para Nova NF-e/NFS-e.
2. **Clientes/Tomadores:** cadastrar, editar, pesquisar e inativar.
3. **Produtos:** cadastro completo para reutilização em NF-e.
4. **Serviços:** cadastro completo para reutilização em NFS-e.
5. **Notas fiscais:** listagem, filtros, seleção múltipla e ações por status.
6. **Nova NF-e:** formulário específico para mercadorias.
7. **Nova NFS-e:** formulário específico para serviços.
8. **Detalhes da nota:** dados fiscais congelados, status, protocolo, arquivos, eventos e tentativas.
9. **Central de rejeições:** código, mensagem oficial completa, explicação e acesso para corrigir.
10. **Lotes de emissão:** progresso, resultado por documento, erros e relatório exportável.
11. **Configurações fiscais:** empresa, certificado, credenciamento, ambiente e integrações.
12. **Auditoria e histórico:** rastreabilidade de ações importantes.

## 19. Regras de segurança fiscal que não podem ser simplificadas

- A aplicação pode ser simples e destinada a um único usuário, mas emissão, cancelamento e substituição precisam respeitar o estado oficial e as regras legais.
- Não permitir editar silenciosamente XML, valores ou dados de documento autorizado.
- Não mostrar “emitida” antes de receber confirmação fiscal oficial.
- Não repetir uma transmissão de resultado desconhecido sem consultar primeiro o estado oficial, quando a integração permitir.
- Preservar todas as respostas e tentativas relevantes, inclusive as rejeitadas, sem apagar o histórico.
- Não converter erros técnicos em rejeições fiscais genéricas.
- Regras fiscais, campos e ações devem ser específicos por tipo de documento e versionados de acordo com os leiautes oficiais.
- A integração de NF-e deve usar os serviços oficiais da SEFAZ/ambiente nacional aplicável; a NFS-e deve usar a integração oficial aplicável a Cuiabá na data de implantação. Não integrar com a Spedy.

## 20. Critérios de aceite adicionais

- É possível cadastrar um produto/serviço uma vez e reutilizá-lo em documentos futuros.
- É possível salvar, editar e validar rascunhos sem emitir.
- A interface apresenta botões de emitir, consultar, baixar e cancelar quando legalmente permitidos, com comportamento específico para NF-e e NFS-e.
- Documentos autorizados ficam protegidos contra edição direta; correções usam os eventos fiscais permitidos.
- A emissão em massa mostra validação prévia, progresso e resultado individual, sem perder o resultado de cada documento.
- Toda rejeição mostra e persiste o código e a mensagem oficial completa retornada pelo serviço fiscal.
- Falhas de rede/timeout ficam distintas de rejeições e disparam consulta de situação antes de eventual reenvio.
- É possível consultar o histórico de todas as tentativas, incluindo lotes, e baixar os arquivos fiscais disponíveis.
- A aplicação permanece independente da Spedy.
