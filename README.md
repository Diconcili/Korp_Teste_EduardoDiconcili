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

### 1. EstoqueService

```powershell
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
dotnet run --project services/FaturamentoService --urls http://localhost:5102
```

### 3. Frontend

```powershell
pnpm --dir frontend start
```

Abra `http://localhost:4200`.

## Acesso de demonstração

O banco novo cria o usuário de demonstração `admin` com a senha `Temp123!`. O segundo fator usa TOTP e deve ser configurado apenas para testes locais. Troque as credenciais e o segredo TOTP antes de qualquer uso fora do ambiente de demonstração.

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

- `KORP_ENCRYPTION_KEY` é obrigatória e não possui valor padrão no código.
- Nunca publique chaves, senhas reais, bancos SQLite, arquivos `.env` ou dados de usuários.
- Em produção, forneça a chave de criptografia por variável de ambiente ou cofre de segredos.
- Antes de publicar, revise `git status` e confirme que arquivos sensíveis não estão preparados para commit.

## Portas locais

| Serviço | Endereço |
|---|---|
| Frontend | `http://localhost:4200` |
| EstoqueService | `http://localhost:5101` |
| FaturamentoService | `http://localhost:5102` |
