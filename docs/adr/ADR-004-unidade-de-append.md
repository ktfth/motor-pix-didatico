# ADR-004 — O `Commit` é a unidade atômica, e abrir conta é evento do log

**Contexto.** O invariante 3 exige que a soma se mantenha "inclusive com falhas no meio". Num ledger in-memory, "falha no meio" só pode significar exceção entre duas escritas — então a atomicidade precisa ser estrutural, não convencionada.

**Decisão.** `Ledger.Lancar` executa em três fases sob o mesmo lock: (1) valida ledger, contas conhecidas e chaves de idempotência, acumulando deltas sem tocar em estado; (2) aplica os deltas sobre uma cópia e avalia a guarda de descoberto do ato inteiro; (3) só então apenda o `Commit` e atualiza projeção, mínimos e índice de chaves. Nada é escrito antes da última validação. `Abrir` também produz `Commit`: o plano de contas é evento do log, não configuração externa.

**Consequência.** Guarda e append acontecem no mesmo ato atômico — consultar a projeção e apender depois deixaria uma janela em que dois débitos passam pela mesma validação e o saldo fica negativo **sem mover a soma**, violando o invariante 8 com o invariante 3 intacto. E como as contas nascem no log, `ProjecaoSaldos.Reconstruir` reconstrói o *conjunto de contas* junto com os saldos: uma conta que zerou e sumiu da projeção é detectável.

**Consequência 2.** Não existe API de alterar ou remover lançamento. Estorno e correção são lançamentos novos, e a regra é imposta pela ausência de método, não por disciplina de revisão.
