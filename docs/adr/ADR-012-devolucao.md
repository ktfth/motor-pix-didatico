# ADR-012 — Devolução `pacs.004`: transação nova, papéis invertidos

**Contexto.** O invariante 7 e a nota do diagrama são específicos: a devolução é transação nova, com E2E próprio, referenciando a original; nunca reabre nem altera a liquidada. E a aresta `estados:16` diz que quem vai para `DEVOLVIDA` é a **original**, quando o `pacs.004` liquida.

**Decisão 1 — quem termina em `DEVOLVIDA` é a original; a devolução termina em `LIQUIDADA`.** A devolução entra pela única porta existente (`estados:2`), percorre a mesma máquina e pode ser rejeitada (`estados:10`) ou expirar (`estados:11`). Se ela não liquidar, a original **permanece em `LIQUIDADA`** — o gatilho da aresta é "pacs.004 *liquidada*".

**Decisão 2 — "nunca altera a liquidada" fala de lançamentos.** O crédito de volta entra com a `ChaveIdempotencia` da **devolução**, não a da original; nenhum lançamento da original é tocado, e o log só cresce. O que muda na original é o campo `Estado`, que é exatamente o que o diagrama normativo manda acontecer.

**Decisão 3 — escopo D4: total e única, iniciada só pelo recebedor.** O valor é campo explícito, validado contra o crédito recebido, para que devolução parcial seja aditiva no futuro. Sem essa guarda, uma devolução maior injetaria dinheiro — partidas dobradas e soma por ledger continuariam fechando, e quem pagaria a diferença seria o participante, sem nada acusar.

**Decisão 4 — as duas fontes ambientais são injetadas.** O invariante 9 cobre o tempo; os 11 caracteres finais do E2E são igualmente ambientais. `IFonteAleatoria` existe para que a devolução seja reproduzível e o replay do gate 8 reconstrua os mesmos identificadores. A implementação padrão é um **contador**, não um sorteio: `System.Random` está banido justamente por tornar o replay por semente ilusório, e o ISPB de quem origina já entra no identificador.

## O que a revisão adversarial corrigiu

O caminho feliz **não fechava**, e os dois agentes convergiram no mesmo defeito independentemente:

- **`PspRecebedor.Receber` só sabia ser creditor.** Quando o `pacs.002` da própria devolução chegava, esse PSP era o *debtor* e a conta do `OrgnlTxRef` pertencia ao ledger do outro participante: `PodeCreditar` respondia `false` e o método lançava dentro da drenagem. Como o barramento só tira o envelope da fila após entrega bem-sucedida, a mensagem ficava presa **para sempre**, bloqueando todas as outras de todas as transações. Agora o `Receber` despacha por papel antes de qualquer coisa.
- **O `RJCT` da devolução era descartado.** O débito de quem devolveu nunca era estornado e o índice por original bloqueava nova tentativa: dinheiro destruído do ponto de vista do cliente, com Σ por ledger fechando. Agora a devolução recusada vai a `REJEITADA`, estorna por lançamento novo e **solta** o índice, para ser retentável.
- **`PodeCreditar` do pagador prometia mais do que o handler entregava.** Ao passar a responder `true` para qualquer conta de cliente, reintroduziu o modo de falha que a ADR-009 criou a guarda para impedir. Agora responde `true` só quando existe expectativa real — transação liquidada ou já devolvida naquela conta.
- **A marcação `DEVOLVIDA` era engolida em silêncio** quando a original não estava em `LIQUIDADA` (por exemplo, `EXPIRADA` pelo gate 5). `Transacao.MarcarDevolucao` grava o vínculo mesmo quando a transição ainda não pode ser aplicada, e estado incompatível de verdade vira `DivergenciaPatrimonial` em vez de no-op.
- **O SPI confiava cegamente no `pacs.004`.** Agora exige que a devolução **espelhe** a original: mesma dupla de participantes com papéis trocados, mesmas contas trocadas, mesmo valor, e original com `ACSC` registrado. Era o único ponto em que o SPI aceitava sem conferir, duas linhas acima de onde ele reconsulta `PodeCreditar` justamente porque "a invariante não pode depender da correção de quem enviou".
- **A impressão do dedup do SPI não distinguia tipo de mensagem.** O índice era um espaço único por E2E, então um pagamento cujo identificador colidisse com o de uma devolução futura queimava o E2E dela.

## Dívida declarada, a pagar no gate 7

- **A devolução não participa da varredura de vencidos nem da consulta de status.** `ExpirarVencidos`, `Expiradas`, `ConsultarStatus` e `Pacs002Atrasados` são portas do `PspPagador`; o recebedor não as tem. Uma devolução sem `pacs.002` fica em `ENVIADA_SPI` sem caminho de recuperação — e o critério "nenhuma transação permanece em `EXPIRADA`" é vacuamente verdadeiro para devoluções, porque nenhuma consegue *chegar* lá.
- **Os dois PSPs divergem onde deveriam ser iguais:** só o pagador classifica reentrega × contradição e mantém `Divergencias`; só ele tem timeout e consulta; só ele tem compensação de reivindicação em falha. A correção é promover handler de crédito, classificador de `pacs.002`, varredor e consulta para `MotorPix.Psp.Nucleo` — o kernel de papel que já existe por essa razão.
- **A `ChavePix` sintética de devolução** é duplicada em dois assemblies e nada impede que alguém a vincule de verdade no DICT. O caminho certo é `Pacs008` e `Pacs004` compartilharem uma ordem de liquidação sem chave.
