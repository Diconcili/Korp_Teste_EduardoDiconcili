# Afazeres para continuidade

## Entrega

- [ ] Preparar vídeo de apresentação e detalhamento técnico final: ciclo de vida Angular, RxJS, bibliotecas, LINQ, exceções e segurança.
- [ ] Criar repositório público com o nome `Korp_Teste_SeuNome` e publicar o material de entrega.

## Concluído

- [x] Validar manualmente o fluxo completo pelo navegador: login/MFA, cadastro de produto, criação de nota, fechamento, baixa de estoque, impressão e logout.
- [x] Revisar notas antigas após a limpeza dos produtos e validar a integridade das referências de itens.
- [x] Implementar os microsserviços EstoqueService e FaturamentoService com SQLite persistente.
- [x] Implementar cadastro e consulta de produtos, com validação de código numérico, descrição alfabética e saldo não negativo no frontend e no backend.
- [x] Implementar criação de notas abertas com múltiplos itens, numeração sequencial e expansão dos itens no frontend.
- [x] Implementar impressão real da nota fechada, com geração de documento HTML e acionamento da caixa de impressão do navegador.
- [x] Implementar fechamento da nota com baixa transacional e concorrente de estoque, impedindo saldo negativo.
- [x] Manter a nota aberta e apresentar erro central explicativo quando não houver estoque ou o serviço de estoque estiver indisponível.
- [x] Implementar fila persistente de recuperação com retentativas e backoff após indisponibilidade do EstoqueService, protegida contra baixa duplicada por idempotência.
- [x] Implementar criptografia AES-GCM para armazenamento das notas, senhas com PBKDF2 e autenticação com MFA TOTP.
- [x] Exigir `KORP_ENCRYPTION_KEY` por variável de ambiente, sem chave de demonstração embutida, e isolar a chave usada nos testes automatizados.
- [x] Atualizar os pacotes com alertas de segurança: `Microsoft.OpenApi` e `SQLitePCLRaw.lib.e_sqlite3`.
- [x] Implementar sessão de 30 minutos, persistência controlada no navegador e logout com invalidação do token no serviço.
- [x] Migrar a interface para Angular Material, aplicar Manrope e separar os fluxos em menu, produtos, nova nota e notas fiscais.
- [x] Criar testes automatizados de integração para saldo insuficiente, concorrência, idempotência e indisponibilidade do serviço de estoque.
