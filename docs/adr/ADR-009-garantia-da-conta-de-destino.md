# ADR-009 — Quem garante que a conta de destino existe

**Contexto.** A revisão do gate 2 achou um buraco que só apareceria aqui: se uma chave estivesse vinculada a uma conta que o PSP recebedor nunca abriu, o fluxo liquidaria no SPI (seta 5) e só a seta 7 estouraria. Dinheiro liquidado sem contrapartida — e a soma por ledger continuaria fechando em zero bruto, então **nenhuma propriedade contábil acusaria**. É exatamente a divergência órfã que a conciliação do gate 7 existe para achar.

**Restrição.** O módulo `Dict` não conhece ledgers, e não deve conhecer: conhecê-los o acoplaria a `Spi` e `PspRecebedor` e o tornaria autoridade sobre algo que não é dele.

**Decisão — duas guardas, em momentos diferentes.**

1. **No registro.** Quem vincula uma chave é o `PspRecebedor`, através de `RegistrarChave`, que confere no próprio ledger se a conta existe antes de chamar `IRegistroDeChaves.Vincular`. É o único ponto em que alguém sabe, ao mesmo tempo, qual é a chave e qual é o ledger. Isso também é fiel ao domínio real: no Pix quem registra a chave é o PSP do correntista.
2. **Antes de liquidar.** O SPI pergunta ao participante creditor, por `IParticipante.PodeCreditar`, se a conta aceita crédito — e só então lança a seta 5. Se a resposta for negativa, o resultado é `pacs.002 RJCT` com `ContaDeDestinoIndisponivel`, não uma exceção depois da liquidação.

**Por que as duas, e não só a primeira.** O registro fecha o caminho normal, mas não impede que alguém vincule direto no diretório no bootstrap, nem cobre a hipótese futura de conta encerrada. A segunda guarda é a que impede que a recusa aconteça depois de o dinheiro ter se movido.

**Emenda, após a revisão do gate 3.** A garantia acima só vale se `PodeCreditar` responder pela capacidade **real** de creditar. `PspPagador` implementa `IParticipante` mas não tem handler de crédito — `Receber` só trata transações que ele mesmo originou. Responder `true` ali autorizaria o SPI a liquidar contra um destino que ninguém honraria: dinheiro movido no ledger do SPI, zero contrapartida no pagador, e a soma por ledger continuaria fechando. Por isso `PspPagador.PodeCreditar` retorna `false` enquanto não houver handler; quando o gate 6 acrescentar o crédito de devolução, a resposta muda junto — e não antes. Um teste de regressão fixa isso.

**Consequência colateral, verificada em teste.** Com essa emenda, um pagamento cuja chave resolva para outra conta do próprio pagador é recusado por `ContaDeDestinoIndisponivel` **antes** de o SPI tentar o lançamento intra-participante que `Lancamento.Criar` recusaria. As duas guardas se sobrepõem de propósito: a de fora dá o diagnóstico melhor, a de dentro garante que nada passa se a de fora for removida.

**Sobre desvio de diagrama.** `IParticipante.PodeCreditar` não é uma seta nova: é parte do nó `VAL` ("Validação pacs.008 + dedup por EndToEndId"), que o diagrama já coloca antes de `LIQ`. Validar a ordem inclui validar se ela é entregável. Nenhuma mensagem ISO nova trafega e nenhuma aresta de projeto é criada — a consulta passa pela mesma interface `IParticipante` pela qual o SPI já entrega o `pacs.002`. Por isso não foi submetido como desvio; se o dono discordar dessa leitura, a alternativa é mover a checagem para dentro do PspPagador antes da seta 4, ao custo de o pagador passar a perguntar coisas sobre o ledger alheio.

**Separação de portas.** `IRegistroDeChaves` é separada de `IDiretorioChaves`: quem resolve não registra.
