# ADR-011 — Timeout por varredura explícita, e a saída única de EXPIRADA

**Contexto.** O invariante 6 é categórico: "timeout ≠ falha; de `EXPIRADA` só se sai por consulta de status ao SPI; proibido estornar ou confirmar por suposição". A nota do diagrama completa: "o que a consulta não fechar, a conciliação fecha".

**Decisão 1 — a varredura expira, não o relógio.** `ExpirarVencidos()` é uma porta explícita, chamada pelo host ou pelo teste. Não existe scheduler: o diagrama de arquitetura não tem esse nó, e inventar um *hosted service* criaria um módulo ausente do desenho — além de tornar o teste dependente de relógio de parede.

**Consequência deliberada.** Um `pacs.002 ACSC` que chegue **depois do prazo mas antes da varredura** fecha a transação por `ENVIADA_SPI → LIQUIDADA` (`estados:9`), que é uma aresta legítima. Isso é contra-intuitivo e está correto: o prazo não é um evento, é um predicado. O determinismo do teste vem da ordem em que o teste chama as operações, não de quanto o relógio andou.

**Decisão 2 — a contagem parte de `ENVIADA_SPI`**, instante lido do `IClock` naquela transição e guardado em `AtualizadaEm`. Nunca do timestamp embutido no `EndToEndId`: aquele é metadado de quem emitiu a mensagem, e usá-lo poria o vencimento nas mãos de fora, violando o invariante 9.

**Decisão 3 — fronteira fechada:** `decorrido >= Limite` vence. A escolha entre aberto e fechado é arbitrária; escolher e **não** documentar é o que produz o teste intermitente.

**Decisão 4 — o limite é parâmetro injetado**, com 30 segundos de relógio lógico como default. Nenhum invariante nem critério de aceite depende da magnitude, e nenhum teste depende do número.

**Decisão 5 — `Indeterminada` é resposta de primeira classe.** Um E2E que o SPI nunca viu devolve `Indeterminada`, e não `Rejeitada`. A diferença é o coração do invariante 6: o `pacs.008` pode simplesmente não ter chegado — que é exatamente o caso que produz `EXPIRADA` —, e responder "rejeitada" convidaria o pagador a estornar um pagamento que ainda pode liquidar. Consulta indeterminada é **no-op**: permanece `EXPIRADA`, zero lançamento, zero evento, zero exceção. Não transicionar não é transição.

**Decisão 6 — `pacs.002` atrasado registra, não transiciona.** Chegando sobre uma transação já `EXPIRADA`, a mensagem é anotada em `Pacs002Atrasados` e nada mais; é a consulta subsequente que move a transação, pelas arestas `estados:13` ou `estados:14`. As duas alternativas são piores: transicionar pela mensagem violaria o invariante 6 ao pé da letra, e descartá-la por purismo jogaria fora a evidência autoritativa do SPI e faria a conciliação redescobrir sozinha um fato já sabido.

**Decisão 7 — a consulta ao SPI acontece fora do lock do PSP.** Segurar o próprio lock enquanto se chama o SPI criaria a aresta `Pagador → Spi` na ordem de aquisição, e o SPI já chama de volta os participantes durante a validação (`PodeCreditar`). É assim que um deadlock nasce. O preço é que o estado precisa ser reconferido depois de retomar o lock, e o `switch` faz isso por guarda explícita.

**Decisão 8 — a API entrega o que a conciliação vai precisar, e nada além.** Três ajustes vindos da revisão:

- `ExpirarVencidos()` devolve **quais** E2E expiraram, não quantos. Com a contagem só, o ciclo "expirar então consultar" não era expressável pela API pública: quem recebesse `3` teria de manter por fora o índice que o PSP já tem, e o critério de aceite do gate 7 — "nenhuma transação permanece em `EXPIRADA`" — não seria nem executável nem verificável. `Expiradas` complementa, listando todas as que estão nesse estado agora.
- `TryTransacao` devolve `VistaDaTransacao`, uma fotografia imutável, e não a entidade viva. Com a entidade na mão e `Transacao.Aplicar` público, uma linha fecharia um caso `EXPIRADA` sem consultar o SPI, sem estornar o débito — o estorno mora no PSP, não na entidade — e fora do lock. O invariante 6 passa a ser imposto pelo tipo, não por convenção. Tornar `Aplicar` internal não resolveria: núcleo e PSP são assemblies diferentes, e expor internals de produção para produção é proibido pelo teste de arquitetura.
- `Pacs002Atrasados` é uma **worklist**, e portanto encolhe: o E2E sai dela quando a consulta o fecha. Enquanto só crescia, o laço óbvio do host — consultar enquanto houver atrasados — nunca terminava, e um E2E já liquidado continuava respondendo "pendente" para quem lesse a lista.
