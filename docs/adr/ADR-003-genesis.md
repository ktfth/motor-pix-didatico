# ADR-003 — Genesis, a constante do pool, e a emissão selada

**Contexto.** A propriedade de soma constante só tem conteúdo se existir uma constante fixada em algum momento. Sem genesis explícito, "soma constante" é verdadeira por vacuidade num sistema que começa vazio.

**Decisão.** Dois lançamentos de abertura, ambos gravados no log como qualquer outro:

- Genesis do PSP: `D ESPELHO_PI / C CLIENTE:{a}` — o PSP passa a ter no SPI exatamente o que deve ao cliente.
- Genesis do SPI: `D ABERTURA / C PI:{p}` — fixa `M = Σ SaldoNatural(PI)`, com `ABERTURA` fora da soma. Sendo `ABERTURA` uma conta de ativo, o débito a deixa com saldo natural **positivo**.

**Decisão 2 — o genesis é um prefixo auto-selante do log.** Um ato cujos lançamentos têm todos `EtapaLancamento.Genesis` é um ato de genesis. O `Ledger` impõe três regras: ato de genesis depois de qualquer ato operacional é recusado (`GenesisAposOperacaoException`); ato operacional que toque a contra-conta de abertura é recusado (`AberturaAposGenesisException`); e ato que misture as duas naturezas é recusado. A primeira versão não tinha nenhuma dessas guardas, e um lançamento `D ABERTURA / C PI` posterior criaria dinheiro no pool sem quebrar partidas dobradas — débito e crédito batem, e só a conservação do pool denunciaria, tarde.

**Por que prefixo, e não uma chamada de "fechar abertura".** Um método explícito de selar seria mais legível e teria o defeito de precisar ser chamado: quem esquecesse deixaria a porta de emissão aberta, sem nenhum sintoma. Derivar o selo da forma do log elimina a possibilidade do esquecimento.

**Consequência.** No estado inicial, `SaldoNatural(PI:{p}) − SaldoNatural(ESPELHO_PI de p) = 0` para todo participante. A partir daí, qualquer diferença é dinheiro em trânsito com um E2E que a explique — ou defeito. É esta identidade, e não a soma global, que a propriedade P3 verifica.

**Sobre "a soma é constante".** A formulação literal vale para o saldo **bruto**: `Σ SaldoBruto == 0` por ledger, por partidas dobradas. A soma dos saldos **naturais** não é constante — na seta 3, cliente e espelho caem juntos —, então quem escrever a propriedade sobre naturais obtém uma falha falsa. `Ledger.SaldoBruto` existe para que a propriedade possa ser escrita como ela é verdadeira.
