# ADR-013 — O canal de saída da conciliação (decisão D5)

**Status: aprovada pelo dono do projeto.** Este é o único desvio de um arquivo normativo em todo o roteiro, e o `motor-pix-arquitetura.mermaid` foi alterado para refleti-lo.

**Contexto.** O prompt manda a conciliação "fechar as EXPIRADA remanescentes" (`prompt:43`) e o diagrama descreve `MATCH` como "Ledgers PSP × Ledger SPI — fecha os casos EXPIRADA". Mas as únicas arestas de `MATCH` eram de **entrada**, vindas dos três ledgers. Como desenhado, o nó que deveria fechar casos só podia emitir relatório.

**Alternativas descartadas.**

- **Decidir pelos ledgers.** A conciliação compararia PI e espelho e concluiria sozinha se liquidou. Viola o invariante 6 ao pé da letra — "proibido estornar ou confirmar por suposição" —, porque a decisão viria de evidência **indireta e local**, enquanto o SPI, que é a autoridade, está a uma chamada de distância.
- **Só reportar.** Sem desvio de diagrama, mas "fecha os casos EXPIRADA" viraria figura de linguagem e o gate ficaria sem critério de aceite objetivo: alguém de fora do motor teria de agir sobre o relatório para que qualquer coisa fechasse.

**Decisão.** A conciliação **detecta e pergunta**; quem decide continua sendo o SPI. Duas arestas pontilhadas novas:

- `MATCH -.-> VAL` — consulta de status por E2E divergente. A transição acontece pelas arestas que **já existem** (`estados:13` e `estados:14`), então nenhuma transição nova é criada e o invariante 6 fica intacto.
- `MATCH -.-> SM_R` — reprocessamento do crédito pendente, idempotente pela chave do ledger.

**Por que isto não é "a conciliação decidindo".** O relatório classifica; o fechamento vem da resposta do SPI. Uma divergência que a consulta responda `Indeterminada` **permanece aberta** — e é exatamente para isso que a nota do diagrama de estados existe ao dizer "o que a consulta não fechar, a conciliação fecha": a conciliação fecha reapresentando a pergunta, não respondendo por conta própria.

**Critério de aceite que isso torna possível.** "Após consultar e conciliar até estabilizar, nenhuma transação permanece em `EXPIRADA`." Com a alternativa "só reportar", esse critério não seria nem executável nem verificável pela API pública — que é a mesma razão pela qual, no gate 5, `ExpirarVencidos` passou a devolver *quais* E2E expiraram em vez de quantos.

**Divergência órfã continua sendo alarme.** Uma diferença entre PI e espelho que **nenhum** E2E explique não é fechável por consulta: é dinheiro criado ou destruído, e o relatório a marca como órfã. Nenhuma automação a resolve — ela existe para ser vista.

## O que a revisão adversarial corrigiu

Três defeitos, dois deles críticos, e o bug de sinal foi encontrado **independentemente** pelos dois agentes do fan-out:

- **O sinal da pendência do pagador estava invertido.** As duas classes de pendência contribuem com o **mesmo** sinal, e a intuição erra aqui: na pendência do pagador o espelho já caiu (seta 3) e a conta PI ainda não (seta 5); na do recebedor a PI já subiu (seta 5) e o espelho ainda não (seta 7). Nos dois casos `PI − espelho = +V` — o SPI segura mais do que o espelho reflete. Somando a primeira com sinal negativo, o resíduo saía `2V` e **toda pendência legítima do pagador virava uma órfã do dobro do valor**. É o alarme falso que treina o operador a ignorar o alarme, e é o pior desfecho possível para um mecanismo cuja única função é ser levado a sério.
- **A órfã fabricava um `EndToEndId` que não existe.** O marcador tinha os 32 caracteres e passava no ISPB, mas o miolo `yyyyMMddHHmm` era `000000000000` — ano 0000, mês 00 —, então `EndToEndId.Criar` lançava, e como o campo era `static readonly` a exceção saía embrulhada em `TypeInitializationException`. A primeira órfã derrubava a conciliação inteira: exatamente o caminho que o gate 7 existe para exercitar. `DivergenciaPorE2E.E2E` passou a ser anulável, o que também é mais honesto — órfã é, por definição, diferença que **nenhum** E2E explica.
- **Metade do desvio aprovado não estava implementada.** `ACreditar` existia sem consumidor: `Fechar` só emitia consulta, e `IPspConciliavel` não tinha porta de recrédito. No cenário canônico do gate 5 — `pacs.002` ACSC perdido — a consulta fechava o pagador em `LIQUIDADA`, o recebedor seguia com a pendência, `AConsultar` esvaziava, `Fechar` devolvia zero e o laço parava: **quiescência inalcançável para sempre**, com o dinheiro parado na conta PI do recebedor sem nunca chegar ao cliente dele. A aresta `MATCH -.-> SM_R` foi implementada como `ReprocessarCredito`, que **busca a confirmação no SPI** e a reentrega ao próprio PSP — idempotente pela chave do ledger. O PSP não inventa o crédito a partir do relatório: pede a mensagem de volta a quem a emitiu, que é o oposto de decidir por conta própria.

## Correção de rota no desenho das arestas

A revisão do gate 8 apontou que as duas arestas tinham sido desenhadas nos nós errados. O rótulo dizia a intenção certa, mas `MATCH -.-> VAL` afirmava que a conciliação pergunta ao SPI — e o código pergunta ao **PSP** (`psp.ConsultarStatus`), que por sua vez usa a seta `SM_P -.-> VAL`, já existente desde o desenho original. E o reprocessamento de crédito alcança os dois PSPs, não só o recebedor.

O diagrama passou a mostrar `MATCH -.-> SM_P` e `MATCH -.-> SM_R`: a conciliação **manda consultar**, e quem consulta o SPI é o PSP, pela aresta que o diagrama já tinha. Isso é mais fiel e, de quebra, mais econômico — o desvio aprovado deixa de introduzir um caminho novo até o SPI.

Corrigida junto uma frase do comentário do `Conciliador` que afirmava mais do que o tipo entrega: `IPspConciliavel.ConsultarStatus` **transiciona**, como efeito da resposta. O que a conciliação não consegue é *escolher* o desfecho.
