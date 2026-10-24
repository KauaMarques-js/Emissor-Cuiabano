# Como contribuir

## Branches

O repositório segue um fluxo **Git Flow simplificado**:

| Branch | Para quê | Recebe merge de |
|---|---|---|
| `main` | Versão estável, pronta para uso. Cada versão ganha uma tag (`v0.1.0`). | `develop` (via release) e `hotfix/*` |
| `develop` | Integração do trabalho em andamento. | `feature/*` e `fix/*` |
| `feature/<área>` | Funcionalidade nova, criada a partir de `develop`. | — |
| `fix/<assunto>` | Correção de bug, criada a partir de `develop`. | — |
| `hotfix/<assunto>` | Correção urgente, criada a partir de `main`. Volta para `main` e `develop`. | — |

Áreas usadas nas branches de funcionalidade:

- `feature/db`: domínio e persistência (entidades, regras, EF Core, migrações);
- `feature/back`: casos de uso, integrações fiscais, infraestrutura e testes;
- `feature/front`: interface web (ASP.NET Core MVC, views, CSS e JS).

Nunca faça commit direto em `main` ou `develop`. Abra um pull request e use merge sem fast-forward (`--no-ff`), para que o histórico mostre de onde veio cada funcionalidade.

## Commits

Use [Conventional Commits](https://www.conventionalcommits.org/pt-br/v1.0.0/), com a descrição em português e no imperativo:

```text
<tipo>(<escopo>): <descrição curta>
```

- **Tipos:** `feat`, `fix`, `refactor`, `test`, `docs`, `style`, `chore`, `ci`, `build`.
- **Escopos:** `domain`, `db`, `application`, `infra`, `nfse`, `web`, `ci`, `docs`.

Exemplos:

```text
feat(nfse): enviar evento de cancelamento ao Sistema Nacional
fix(web): manter máscara do CEP após erro de validação
test(nfse): validar DPS contra o XSD oficial v1.01
```

## Antes de abrir um pull request

```bash
dotnet build FiscalPlatform.slnx
dotnet test
```

- O build deve terminar **sem avisos**: o analisador acusa `using` sem uso.
- Toda regra fiscal nova precisa de teste.
- Mudanças de leiaute fiscal devem citar a fonte oficial (manual, XSD ou nota técnica) na descrição.
- Mudou o modelo de dados? Gere a migração:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMigracao --project src/FiscalPlatform.Infrastructure --startup-project src/FiscalPlatform.Infrastructure
```

## Versões

A numeração segue [SemVer](https://semver.org/lang/pt-BR/), e as mudanças de cada versão ficam em [CHANGELOG.md](CHANGELOG.md).
