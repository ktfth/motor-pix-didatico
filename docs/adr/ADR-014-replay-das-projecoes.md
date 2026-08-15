# ADR-014 — As projeções, e o que torna o replay uma prova

**Contexto.** O critério de aceite é "replay do ledger reproduz exatamente as projeções atuais". O risco central do gate é entregar isso como tautologia: se os dois lados da comparação vierem da mesma função, o teste prova apenas que a função é determinística.

**Decisão 1 — uma projeção por tipo de estado derivado.** `ProjecaoSaldos` (do log do ledger), `ProjecaoEstados` (do histórico de transições) e `ProjecaoRespostas` (do log de idempotência). Sem a terceira, o replay reconstruiria saldos e estados mas **não o que a API respondeu** — e um reenvio depois do replay devolveria coisa diferente do que devolveu antes, quebrando o invariante 5 justamente onde ele é mais visível para o cliente.

> A revisão do gate acrescentou uma quarta, `ProjecaoDecisoes` — ver Decisão 5. Este documento nasceu falando em três; a quarta é a correção de um defeito que só a revisão expôs, e fica registrada como tal em vez de ser reescrita para trás.

**Decisão 2 — `ProjecaoEstados` reaplica a tabela, não copia o destino gravado.** Para cada transição do histórico ela confere que a origem bate com o estado corrente, consulta a máquina normativa e exige que o destino da tabela coincida com o gravado. Copiar o destino faria o replay concordar com **qualquer** histórico, inclusive um corrompido — e ele existe justamente para não concordar. Histórico incoerente lança `TransicaoInvalidaException`.

**Decisão 3 — `ProjecaoRespostas` respeita a ordem de produção.** `EhIgualA` compara conjunto, valores **e** ordem. Uma projeção que produzisse os mesmos elementos em ordem diferente seria comutativa demais, e a comparação deixaria de provar que o log é a verdade. Quando um E2E aparece mais de uma vez, vale a **primeira** entrada: "replay da resposta" significa a resposta original, não a última.

**Decisão 4 — nenhuma projeção lê o relógio nem gera identificador.** Se precisasse de tempo, o tempo já está gravado no `Commit` e em cada `TransicaoAplicada`. É isso que torna o replay reproduzível — e é a mesma razão pela qual `IFonteAleatoria` foi injetada no gate 6.

**Decisão 5 — o SPI ganha log próprio, e a decisão dele vira a quarta projeção.** As três primeiras reconstroem estado que já era derivável de um agregado vivo. A decisão do SPI não era: uma ordem recusada por participante desconhecido retorna **antes** de lançar no ledger, então nenhum dos três ledgers a registra. Um motor reconstruído só deles responderia `Indeterminada` onde o vivo responde `Rejeitada` — e a transação em `EXPIRADA` perderia sua única saída legítima. É o único estado do motor de que dependem **dois** invariantes: o 5, pelo dedup de mensagem, e o 6, porque a consulta de status responde a partir dele.

`ProjecaoDecisoes.Reconstruir` exige sequência densa começando em 1 — buraco no log é `AtoContabilInvalidoException`, não silêncio — e vale a **primeira** decisão de cada E2E, a mesma regra da Decisão 3. `ProjecaoDecisoes.ConsultarStatus` é a prova propriamente dita: dá a mesma resposta que `ISpi.ConsultarStatus` daria, derivada só do log. Sem ela, "de `EXPIRADA` só se sai por consulta" seria verdade apenas enquanto o processo vivesse.

**O que sustenta a não-tautologia.** Quatro oráculos, e cada um cobre uma fraqueza do anterior:

- **O1, determinismo** — o mais fraco: prova só que a função é determinística.
- **O2, prefixo** — reconstruir os *n* primeiros commits tem de bater com o snapshot capturado naquele ponto. É o que pega projeção que depende do futuro.
- **O3, incremental × fold** — a projeção mantida durante o append contra o fold puro do log. Vale porque as duas são implementações genuinamente independentes (ADR-006): se convergissem na mesma função, inverter o sinal lá dentro produziria o mesmo erro dos dois lados.
- **O4, sensibilidade à ordem** — embaralhar o log tem de mudar o resultado. Se nunca mudasse, a projeção seria comutativa demais e o gate não provaria o que promete.
