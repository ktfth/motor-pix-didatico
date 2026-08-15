# Registros de decisão

Uma ADR por decisão que não é óbvia a partir do código. Cada uma responde à mesma pergunta: **qual defeito concreto esta escolha evita?** Decisão sem defeito nomeado não vira ADR — vira comentário.

Três delas nasceram de perguntas feitas ao usuário (D1, D5) ou de desvio de arquivo normativo (ADR-013). O resto veio da revisão adversarial dos gates, quase sempre de um defeito que passava verde por todas as propriedades contábeis. A [ADR-015](ADR-015-especificacoes-executaveis.md) é de outro tipo: registra uma decisão tomada **contra** a recomendação técnica, com o argumento contrário preservado — porque quem ler daqui a um ano precisa saber que a alternativa foi considerada, não supor que ninguém pensou nela.

| ADR | Decisão | O que ela evita |
|---|---|---|
| [000](ADR-000-idioma-e-convencoes.md) | Idioma e convenções | Código em duas línguas; domínio traduzido pela metade |
| [001](ADR-001-ids-por-fabrica.md) | IDs tipados por `sealed record` com fábrica **(D1)** | `string` circulando como identificador; E2E inválido construível |
| [002](ADR-002-plano-de-contas.md) | Plano de contas, saldo natural, sem descoberto | Natureza recebida por parâmetro — a porta pela qual a conta PI ficaria negativa |
| [003](ADR-003-genesis.md) | Genesis, constante do pool, emissão selada | Dinheiro nascendo no meio da execução e a soma constante virando vacuidade |
| [004](ADR-004-unidade-de-append.md) | `Commit` é a unidade atômica; abrir conta é evento | Meia operação persistida; conta existindo fora do log |
| [005](ADR-005-imposicao-mecanica-dos-invariantes.md) | Invariantes 1 e 9 no compilador — **e onde o analyzer não alcança** | Achar que `BannedSymbols` cobre declaração de campo. Não cobre: foi sondado |
| [006](ADR-006-independencia-do-replay.md) | Replay é segunda implementação, não segundo laço | Sinal invertido produzindo o mesmo erro dos dois lados, com o gate verde |
| [007](ADR-007-profundidade-da-validacao-de-chave.md) | Até onde o motor valida uma chave | Validar de menos e aceitar lixo; validar de mais e virar outro projeto |
| [008](ADR-008-outbox-e-ordem-dos-efeitos.md) | Outbox drenado explicitamente; transição antes do efeito | `pacs.002` chegando dentro da pilha do envio, num par estado×evento que o diagrama não tem |
| [009](ADR-009-garantia-da-conta-de-destino.md) | Quem garante a conta de destino | Chave apontando para conta que o PSP nunca abriu: liquida no SPI, estoura só na seta 7 — e a soma por ledger continua fechando |
| [010](ADR-010-idempotencia-por-e2e.md) | Idempotência: duas camadas, snapshot congelado, compensação **condicionada** | Liberar reivindicação sem condição — apaga o registro de um débito já efetivado |
| [011](ADR-011-timeout-e-consulta-de-status.md) | Timeout por varredura explícita; saída única de `EXPIRADA` | Tratar silêncio como rejeição — o erro que o invariante 6 existe para proibir |
| [012](ADR-012-devolucao.md) | `pacs.004`: transação nova, papéis invertidos | Reabrir a liquidada; devolução da devolução; devolver mais do que entrou — injeção com as partidas ainda fechando |
| [013](ADR-013-canal-de-saida-da-conciliacao.md) | Canal de saída da conciliação **(D5)** — *único desvio de arquivo normativo* | Conciliação decidindo desfecho por evidência indireta e local |
| [014](ADR-014-replay-das-projecoes.md) | As projeções, e o que torna o replay uma prova | Replay tautológico: os dois lados da comparação vindo da mesma função |
| [015](ADR-015-especificacoes-executaveis.md) | Especificações executáveis com SpecFlow | `decimal` entrando pela porta dos fundos num parâmetro de passo; cenário que afirma estado errado e passa calado |

## As três que mais mudaram o código

**[ADR-006](ADR-006-independencia-do-replay.md) — a duplicação é o mecanismo.** `ProjecaoSaldos` deriva o saldo natural com expressão própria em vez de chamar `Conta.NaturalDe`. Parece violação de DRY e é o contrário: se as duas convergissem naquele método, inverter o sinal lá dentro produziria o mesmo erro nos dois lados, e o oráculo O3 do gate 8 ficaria verde sobre um motor errado.

**[ADR-008](ADR-008-outbox-e-ordem-dos-efeitos.md) — nada é entregue na hora.** Além de evitar o par estado×evento inexistente, dá de graça a capacidade de expressar "o `pacs.002` que nunca chega": basta não drenar. Sem outbox, esse cenário exigiria mock; com ele, é a ausência de uma chamada.

**[ADR-013](ADR-013-canal-de-saida-da-conciliacao.md) — o único desvio aprovado.** `motor-pix-arquitetura.mermaid` ganhou duas arestas pontilhadas saindo de `MATCH`. A máquina de estados ficou intacta, como a precedência exige. A conciliação **detecta e manda perguntar**; quem decide continua sendo o SPI, e a transição acontece pelas arestas que já existiam.

## Formato

Sem template pesado. Cada ADR tem contexto, as decisões numeradas, e — quando existe — a alternativa que foi descartada com o motivo. O que **não** pode faltar é o defeito concreto: "seria mais limpo" não é justificativa registrável.

Quando uma decisão posterior contradiz uma ADR, o registro é emendado com nota de quando e por quê, nunca reescrito para parecer que sempre esteve certo. A [ADR-014](ADR-014-replay-das-projecoes.md) é o exemplo: nasceu anunciando três projeções e a revisão do gate 8 exigiu uma quarta.
