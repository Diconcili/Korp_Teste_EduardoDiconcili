# Korp — Sistema de Estoque e Notas Fiscais

Aplicação full stack para cadastro de produtos, criação de notas fiscais e controle transacional de estoque. O projeto foi desenvolvido como teste técnico e mantido como demonstração de portfólio, com foco em segurança, consistência de dados e experiência de uso.

## Recursos

- Cadastro de produtos com código numérico, descrição alfabética e saldo inicial maior que zero.
- Atualização de saldo ao cadastrar novamente o mesmo código e descrição.
- Prevenção de códigos ou descrições duplicados com dados divergentes.
- Criação de notas abertas com múltiplos itens.
- Fechamento de notas com baixa atômica de estoque, sem permitir saldo negativo.
- Consulta expansível dos itens de cada nota e impressão pelo navegador após o fechamento.
- Retentativas automáticas com backoff quando o serviço de estoque estiver indisponível.
- Idempotência na baixa de estoque para impedir desconto duplicado durante retentativas.
- Autenticação com senha, MFA TOTP e sessões de 30 minutos.
- Criptografia AES-GCM das notas em repouso e senhas armazenadas com PBKDF2-SHA256 e salt.

## Arquitetura

| Componente | Tecnologia | Responsabilidade |
|---|---|---|
| Frontend | Angular 19 e Angular Material | Interface, autenticação, produtos, notas e impressão |
| EstoqueService | ASP.NET Core | Produtos, saldo e consumo transacional do estoque |
| FaturamentoService | ASP.NET Core | MFA, sessões, notas criptografadas e fila de recuperação |
| Persistência | SQLite | Bancos locais separados para estoque e faturamento |

Os bancos ficam em `data/estoque.db` e `data/faturamento.db`. Eles são dados locais e não devem ser enviados ao repositório.

## Pré-requisitos

- .NET SDK 10
- Node.js
- pnpm

## Executar localmente

Abra três terminais na raiz do projeto.

Antes de iniciar, gere duas chaves e copie os valores exibidos. Os mesmos valores devem ser usados nos dois serviços:

```powershell
$authKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$stockKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$authKey
$stockKey
```

### 1. EstoqueService

```powershell
$env:KORP_AUTH_SIGNING_KEY = '<valor de authKey>'
$env:KORP_STOCK_SERVICE_KEY = '<valor de stockKey>'
dotnet run --project services/EstoqueService --urls http://localhost:5101
```

### 2. FaturamentoService

Defina uma chave local exclusiva, com ao menos 32 caracteres, no mesmo terminal antes de iniciar o serviço:

```powershell
$keyBytes = New-Object byte[] 32
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$random.GetBytes($keyBytes)
$random.Dispose()
$env:KORP_ENCRYPTION_KEY = [Convert]::ToBase64String($keyBytes)
$env:KORP_AUTH_SIGNING_KEY = '<mesmo valor de authKey>'
$env:KORP_STOCK_SERVICE_KEY = '<mesmo valor de stockKey>'
$env:KORP_BOOTSTRAP_ADMIN_USERNAME = '<usuário administrador inicial>'
$env:KORP_BOOTSTRAP_ADMIN_PASSWORD = '<senha inicial com ao menos 12 caracteres>'
$env:KORP_BOOTSTRAP_ADMIN_TOTP_SECRET = '<segredo Base32 exclusivo do autenticador>'
dotnet run --project services/FaturamentoService --urls http://localhost:5102
```

### 3. Frontend

```powershell
pnpm --dir frontend start
```

Abra `http://localhost:4200`.

## Primeiro acesso

Quando o banco ainda não possui usuários, o FaturamentoService cria o primeiro administrador usando as três variáveis `KORP_BOOTSTRAP_ADMIN_*`. A senha deve possuir ao menos 12 caracteres e o segredo TOTP deve ser um valor Base32 exclusivo configurado no aplicativo autenticador. Em bancos que já possuem usuários, essas variáveis não alteram as contas existentes.

## Testes automatizados

Com os serviços encerrados, execute:

```powershell
dotnet test tests\Korp.IntegrationTests\Korp.IntegrationTests.csproj
```

A suíte usa bancos SQLite temporários e cobre:

- Saldo insuficiente sem alteração do estoque;
- Concorrência na disputa pela última unidade;
- Idempotência na criação de notas;
- Indisponibilidade do EstoqueService e agendamento de recuperação;
- Idempotência da baixa de estoque.

## Segurança e publicação

- `KORP_ENCRYPTION_KEY`, `KORP_AUTH_SIGNING_KEY` e `KORP_STOCK_SERVICE_KEY` são obrigatórias e não possuem valor padrão no código.
- O EstoqueService valida sessões localmente e usa uma credencial separada para chamadas internas do FaturamentoService.
- Login e MFA possuem limitação por origem; cinco falhas na mesma combinação de origem e usuário causam bloqueio temporário, e cinco falhas invalidam o desafio MFA.
- Os serviços aceitam requisições do navegador somente das origens listadas em `AllowedOrigins`. Para outro endereço de frontend, configure por exemplo `AllowedOrigins__0` no ambiente dos dois serviços.

## Portas locais

| Serviço | Endereço |
|---|---|
| Frontend | `http://localhost:4200` |
| EstoqueService | `http://localhost:5101` |
| FaturamentoService | `http://localhost:5102` |
