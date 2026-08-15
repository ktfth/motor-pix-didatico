# ADR-002 — Plano de contas, saldo natural e ausência de descoberto

**Contexto.** O diagrama mostra três ledgers e rotula os do PSP como "cliente ↔ espelho conta PI" e "espelho conta PI ↔ cliente". Os dois rótulos descrevem o mesmo par de contas em ordem invertida, porque o dinheiro anda em sentidos opostos nos dois papéis.

**Decisão.** Um único tipo `Ledger`, instanciado por `LedgerId`. Quatro classes de conta, declaradas no enum `ClasseConta` que viaja dentro do próprio `ContaId`: `CLIENTE:{conta}` e `PI:{ispb}` são passivo; `ESPELHO_PI` e `ABERTURA` são ativo. A natureza é **derivada da classe** por `Conta.De(ContaId)` — nunca recebida como parâmetro. O armazenamento é o saldo bruto `ΣC − ΣD`; toda regra de negócio lê o saldo natural, dado por `Conta.NaturalDe`: passivo devolve o bruto, ativo devolve o bruto negado.

**Consequência 1 — não existe descoberto.** Toda conta do sistema mantém saldo natural maior ou igual a zero, sem exceção, e a guarda é universal. A primeira versão dava a `ABERTURA` um `bool permiteDescoberto`; a revisão mostrou que isso era um invariante desligável em uma linha — bastava `Conta.Criar(ContaId.Pi(x), Natureza.Passivo, permiteDescoberto: true)` para o invariante 8 desaparecer em silêncio. Modelar `ABERTURA` como ativo resolve o problema pela estrutura: o débito do genesis a deixa positiva, e o `bool` deixa de ser necessário.

**Consequência 2 — a natureza não é escolha do chamador.** Registrar a conta PI como ativo é um erro plausível, já que "PI" é intuitivamente o dinheiro que o participante *tem*. Esse erro inverteria a guarda de não-negatividade exatamente na conta em que o invariante 8 mora, e nenhum teste de aritmética pegaria, porque a aritmética continuaria correta.

**Consequência 3 — `ContaId` carrega o `LedgerId`**, o que transforma "lançamento entre ledgers" em erro detectável. Sem isso o lançamento seria formalmente válido, com débito e crédito batendo e a soma global intacta.

**Nota.** "Conta PI não tem crédito" (invariante 8) é leitura bancária — sem cheque especial —, não contábil. A leitura contábil tornaria o Ledger SPI natimorto, já que a seta 5 credita a PI do recebedor.
