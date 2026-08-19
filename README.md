# Korp — Sistema de Estoque e Notas Fiscais

Aplicação full stack para cadastro de produtos, criação de notas fiscais e controle transacional de estoque. O projeto foi desenvolvido como teste técnico e mantido como demonstração de portfólio, com foco em segurança, consistência de dados e experiência de uso.

## Recursos

- Cadastro de produtos com código numérico, descrição alfabética e saldo inicial maior que zero.
- Atualização de saldo ao cadastrar novamente o mesmo código e descrição.
- Prevenção de códigos ou descrições duplicados com dados divergentes.
- Criação de notas abertas com múltiplos itens.
- Consulta paginada de notas, com até 50 registros por requisição.
- Filtros de notas por status e produto, com ordenação por data ou número.
- Fechamento de notas com baixa atômica de estoque, sem permitir saldo negativo.
- Consulta expansível dos itens de cada nota e impressão pelo navegador após o fechamento.
- Retentativas automáticas com backoff quando o serviço de estoque estiver indisponível.
- Idempotência na baixa de estoque para impedir desconto duplicado durante retentativas.
- Autenticação com senha, MFA TOTP e sessões de 30 minutos.
- Proteção das operações de estoque por token assinado e credencial exclusiva entre serviços.
- Limitação de tentativas de login e MFA, com bloqueio temporário.
- Criptografia AES-GCM das notas em repouso e senhas armazenadas com PBKDF2-SHA256 e salt.

## Arquitetura

| Componente | Tecnologia | Responsabilidade |
|---|---|---|
| Frontend | Angular 19 e Angular Material | Interface e orquestração de serviços dedicados a sessão, estoque, notas e impressão |
| EstoqueService | ASP.NET Core | Produtos, saldo e consumo transacional do estoque |
| FaturamentoService | ASP.NET Core | MFA, sessões, notas criptografadas e fila de recuperação |
| Persistência | SQLite | Bancos locais separados para estoque e faturamento |

Os bancos ficam em `data/estoque.db` e `data/faturamento.db`. Eles são dados locais e não devem ser enviados ao repositório.

O navegador acessa os dois serviços. O FaturamentoService emite a sessão após o MFA, e o EstoqueService valida sua assinatura localmente, sem consultar o serviço de faturamento. Para baixar o estoque, o FaturamentoService usa uma credencial interna separada; assim, o worker de recuperação não depende de uma sessão de usuário.

## Pré-requisitos

- .NET SDK 10
- Node.js
- pnpm

## Executar localmente

Na primeira execução, instale as dependências do frontend:

```powershell
pnpm --dir frontend install
```

Depois, abra três terminais na raiz do projeto.

### 1. Gerar as chaves locais

Gere as três chaves e copie os valores exibidos. Cada chave deve ser mantida em segredo e ter um valor diferente:

```powershell
function New-RandomKey {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        [Convert]::ToBase64String($bytes)
    }
    finally {
        $generator.Dispose()
    }
}
$authKey = New-RandomKey
$stockKey = New-RandomKey
$encryptionKey = New-RandomKey
$authKey
$stockKey
$encryptionKey
```

`authKey` e `stockKey` devem ter exatamente os mesmos valores nos terminais dos dois serviços. `encryptionKey` pertence somente ao FaturamentoService.

Para um banco novo, gere também um segredo Base32 para o primeiro administrador:

```powershell
function New-Base32Secret {
    $alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
    $bytes = New-Object byte[] 20
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    $buffer = 0
    $bits = 0
    $result = ''
    foreach ($byte in $bytes) {
        $buffer = ($buffer -shl 8) -bor $byte
        $bits += 8
        while ($bits -ge 5) {
            $bits -= 5
            $result += $alphabet[($buffer -shr $bits) -band 31]
        }
    }
    $result
}
$totpSecret = New-Base32Secret
$totpSecret
```

Copie esse valor para o aplicativo autenticador e para `KORP_BOOTSTRAP_ADMIN_TOTP_SECRET`.

### 2. Salvar os segredos localmente

O projeto usa o Secret Manager do .NET em `Development`. Configure os valores uma única vez; eles ficam fora do repositório e serão carregados automaticamente nas próximas execuções.

```powershell
dotnet user-secrets set "KORP_AUTH_SIGNING_KEY" "<valor de authKey>" --project services/EstoqueService
dotnet user-secrets set "KORP_STOCK_SERVICE_KEY" "<valor de stockKey>" --project services/EstoqueService

dotnet user-secrets set "KORP_ENCRYPTION_KEY" "<valor de encryptionKey>" --project services/FaturamentoService
dotnet user-secrets set "KORP_AUTH_SIGNING_KEY" "<mesmo valor de authKey>" --project services/FaturamentoService
dotnet user-secrets set "KORP_STOCK_SERVICE_KEY" "<mesmo valor de stockKey>" --project services/FaturamentoService
dotnet user-secrets set "KORP_BOOTSTRAP_ADMIN_USERNAME" "<usuário administrador inicial>" --project services/FaturamentoService
dotnet user-secrets set "KORP_BOOTSTRAP_ADMIN_PASSWORD" "<senha inicial com ao menos 12 caracteres>" --project services/FaturamentoService
dotnet user-secrets set "KORP_BOOTSTRAP_ADMIN_TOTP_SECRET" "<valor de totpSecret>" --project services/FaturamentoService
```

Não é necessário executar `dotnet user-secrets init`, pois os dois projetos já possuem identificadores próprios. Variáveis de ambiente com os mesmos nomes continuam aceitas e têm precedência sobre o Secret Manager.

### 3. EstoqueService

```powershell
dotnet run --project services/EstoqueService --urls http://localhost:5101
```

### 4. FaturamentoService

Em um banco novo, informe também os dados do primeiro administrador. O segredo TOTP deve ser Base32, ter ao menos 16 caracteres e estar cadastrado no aplicativo autenticador do usuário.

```powershell
dotnet run --project services/FaturamentoService --urls http://localhost:5102
```

Se `data/faturamento.db` já possuir um usuário, as três variáveis `KORP_BOOTSTRAP_ADMIN_*` podem ser omitidas. Elas não substituem nem alteram contas existentes.

### 5. Frontend

```powershell
pnpm --dir frontend start
```

Abra `http://localhost:4200`.

## Primeiro acesso

Quando o banco ainda não possui usuários, o FaturamentoService cria o administrador com as variáveis `KORP_BOOTSTRAP_ADMIN_*`. Antes de fazer login, cadastre manualmente o valor de `KORP_BOOTSTRAP_ADMIN_TOTP_SECRET` em um aplicativo autenticador compatível com TOTP.

O acesso ocorre em duas etapas:

1. Informe o usuário e a senha configurados no bootstrap.
2. Informe o código de seis dígitos gerado pelo aplicativo autenticador.

A sessão resultante expira após 30 minutos. O logout invalida a sessão no FaturamentoService, e dados locais inválidos ou expirados são descartados pelo frontend.

### Redefinir o MFA em um clone local

O segredo de bootstrap só é usado na criação do primeiro usuário. Depois disso, ele fica armazenado no banco; alterar `KORP_BOOTSTRAP_ADMIN_TOTP_SECRET` não altera o MFA de uma conta já criada. Se o autenticador tiver sido configurado com outro segredo, ou se for necessário reiniciar o acesso em um clone de desenvolvimento sem dados importantes, encerre o FaturamentoService e remova somente `data/faturamento.db` (e os arquivos `faturamento.db-wal` e `faturamento.db-shm`, se existirem). Na próxima inicialização, o serviço criará novamente o administrador usando os segredos atuais. Em seguida, exclua a entrada anterior do aplicativo autenticador e cadastre exatamente o valor atual de `KORP_BOOTSTRAP_ADMIN_TOTP_SECRET` como TOTP de 6 dígitos, SHA-1 e período de 30 segundos.

## Configuração

| Variável | Serviço | Obrigatória | Finalidade |
|---|---|---|---|
| `KORP_ENCRYPTION_KEY` | Faturamento | Sempre | Criptografia AES-GCM das notas persistidas |
| `KORP_AUTH_SIGNING_KEY` | Ambos | Sempre | Emissão e validação local dos tokens de sessão |
| `KORP_STOCK_SERVICE_KEY` | Ambos | Sempre | Autenticação das baixas internas de estoque |
| `KORP_BOOTSTRAP_ADMIN_USERNAME` | Faturamento | Banco sem usuários | Usuário do primeiro administrador |
| `KORP_BOOTSTRAP_ADMIN_PASSWORD` | Faturamento | Banco sem usuários | Senha inicial, com no mínimo 12 caracteres |
| `KORP_BOOTSTRAP_ADMIN_TOTP_SECRET` | Faturamento | Banco sem usuários | Segredo Base32 exclusivo para o MFA |
| `AllowedOrigins__0` | Ambos | Não | Substitui a origem padrão `http://localhost:4200` |

As chaves precisam ter pelo menos 32 caracteres. Se uma chave obrigatória estiver ausente, o serviço correspondente interrompe a inicialização com uma mensagem explicativa. Se as chaves compartilhadas tiverem valores diferentes, os serviços iniciam, mas tokens de usuário ou baixas internas serão recusados.

## Testes automatizados

Com os serviços encerrados, execute:

```powershell
dotnet test tests\Korp.IntegrationTests\Korp.IntegrationTests.csproj
```

A suíte usa bancos SQLite temporários e cobre:

- Saldo insuficiente sem alteração do estoque;
- Concorrência na disputa pela última unidade;
- Idempotência na criação de notas;
- Paginação das notas em ordem decrescente;
- Filtros combinados e normalização de páginas fora do intervalo;
- Atualização dos metadados de consulta em bancos criados por versões anteriores;
- Indisponibilidade do EstoqueService e agendamento de recuperação;
- Idempotência da baixa de estoque;
- Rejeição de consumo sem itens;
- Validação de tokens assinados, adulterados e expirados;
- Validação da credencial interna entre serviços;
- Bloqueio e limpeza das tentativas de autenticação;
- Validação do formato Base32 do segredo TOTP.

Para validar também a compilação do frontend:

```powershell
pnpm --dir frontend run build
```

## Segurança e publicação

- As chaves e credenciais não possuem valores padrão no código ou no frontend.
- O EstoqueService valida sessões localmente e usa uma credencial separada para chamadas internas do FaturamentoService.
- Login e MFA aceitam até 20 requisições por origem a cada minuto. Cinco falhas na mesma combinação de origem e usuário causam bloqueio por cinco minutos; cinco falhas de MFA invalidam o desafio.
- Os serviços aceitam requisições do navegador somente das origens listadas em `AllowedOrigins`. Para várias origens, use índices sequenciais como `AllowedOrigins__0` e `AllowedOrigins__1` nos dois serviços.
- A leitura de produtos é pública. Cadastro e alteração exigem uma sessão válida; criação, consulta e fechamento de notas também exigem autenticação.
- O endpoint interno de consumo de estoque aceita apenas a credencial enviada pelo FaturamentoService.

## Portas locais

| Serviço | Endereço |
|---|---|
| Frontend | `http://localhost:4200` |
| EstoqueService | `http://localhost:5101` |
| FaturamentoService | `http://localhost:5102` |
