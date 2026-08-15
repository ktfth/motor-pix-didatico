# ADR-008 — Outbox drenado explicitamente, e a transição antes do efeito

**Contexto.** As setas 4 e 6 do diagrama de arquitetura formam um ciclo: o PSP pagador manda `pacs.008` ao SPI, e o SPI devolve `pacs.002` ao pagador. Implementado como chamada direta, esse ciclo é reentrante — a resposta chega dentro da própria pilha do envio.

**O que a chamada direta quebraria.** O `pacs.002` chegaria ao pagador com a transação ainda em `VALIDADA`, porque a transição para `ENVIADA_SPI` só se completa depois que o envio retorna. O par `(VALIDADA, Pacs002Acsc)` não existe no diagrama normativo: o caminho feliz terminaria em `TransicaoInvalidaException`.

**Decisão 1 — despacho por outbox.** `IBarramento.Enfileirar` apenas registra; a entrega acontece em `Drenar()`, chamado explicitamente pelo host ou pelo teste. Não existe scheduler: o diagrama não tem esse nó, e inventar um *hosted service* criaria um módulo ausente do desenho. A drenagem processa também o que for enfileirado durante ela mesma — a seta 4 produz a seta 6 —, mas em rodadas, para que a ordem de entrega seja a ordem de produção e não a de uma pilha de chamadas. Drenar de dentro de uma entrega é recusado.

**Decisão 2 — persistir a transição antes de produzir o efeito externo**, para todo par (transição, efeito). Com as duas decisões juntas, `(VALIDADA, Pacs002*)` deixa de ser uma célula que o teste precisa cobrir por sorte e passa a ser inalcançável por construção — embora continue testada.

**Ganhos de tabela.** O cenário do gate 5 (`pacs.002` que nunca chega) fica expressável sem nenhum mecanismo novo: basta não drenar. E o ponto de entrega vira a costura natural para injetar falha, sem que o domínio saiba que falha existe.

**Custo.** Quem usa o motor precisa lembrar de drenar. É um custo real, aceito porque o alvo é estudo: o controle explícito do instante de entrega é justamente o que torna os testes determinísticos sem `Task.Delay`.
