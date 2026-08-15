# ADR-001 — IDs tipados como `sealed record` com fábrica (decisão D1)

**Contexto.** O prompt pede `readonly record struct` para value objects e validação no construtor para os IDs. Para tipos apoiados em string as duas exigências são incompatíveis: todo struct tem construtor sem parâmetros implícito, então `default(EndToEndId)` produziria uma instância nunca validada — e dois `default` são iguais entre si pela igualdade gerada, o que fura a idempotência sem lançar nada.

**Decisão.** `EndToEndId`, `Ispb`, `LedgerId`, `ContaId` e `ChavePix` são `sealed record` (ou `abstract record` com filhas seladas) de construtor privado, criados só por `Criar`/`TryCriar`. `Valor` e `Saldo` continuam `readonly record struct`. Desvio declarado da letra de `prompt:27`, aprovado pelo dono antes do gate 1.

**Consequência.** `default(EndToEndId)` deixa de existir: é `null`, e o compilador reclama sob nullable enable. Em troca, os IDs viram tipos de referência — irrelevante num motor didático. `default(Valor)` continua representável e vale zero centavos, que **não** é um `Valor` válido; quem consome (`Lancamento.Criar`) barra explicitamente via `EhPositivo`.

**Reversível.** Como nenhum ID nasce por `new`, voltar a struct seria uma edição de uma linha por tipo, sem tocar em chamador.
