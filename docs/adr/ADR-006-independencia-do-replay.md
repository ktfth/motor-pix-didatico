# ADR-006 — O replay tem de ser uma segunda implementação, não um segundo laço

**Contexto.** O gate 8 promete que "replay do ledger reproduz exatamente as projeções atuais". Isso só prova alguma coisa se as duas implementações puderem discordar. A primeira versão de `ProjecaoSaldos` percorria o log por um caminho diferente do `Ledger`, mas as duas terminavam chamando `Conta.NaturalDe` e `Lancamento.Partidas`.

**O ataque que a revisão construiu.** Inverta o sinal dentro de `Conta.NaturalDe`. `Ledger.Snapshot()` e `ProjecaoSaldos.Snapshot()` passam a produzir exatamente os mesmos valores errados, `EhIgualA` devolve `true`, e todo o gate de replay fica verde — enquanto a guarda de não-negatividade passa a ser avaliada com sinal trocado em `CLIENTE` e `PI`. Nenhuma classe de bug de natureza contábil era alcançável pelo teste.

**Decisão.** `ProjecaoSaldos` acumula `somaDebitos` e `somaCreditos` **separados por conta** e deriva o saldo natural com expressão própria (`Ativo => D − C`, `Passivo => C − D`), sem chamar `Conta.NaturalDe`. A definição fica escrita duas vezes no repositório, de propósito. Além disso, o gate compara três coisas e não duas: ledger materializado, projeção reconstruída e um oráculo de saldos escrito à mão no teste — é o terceiro que pega o erro comum aos dois primeiros.

**Consequência.** Duplicação deliberada de uma regra de três linhas. Quem "limpar" essa duplicação extraindo um método comum reintroduz o ponto cego, e é por isso que este ADR existe: a duplicação é o mecanismo, não um descuido.

**Decisão 2.** `Reconstruir` exige o log completo a partir da sequência 1 e lança exceção de domínio para log com buraco, em vez de `KeyNotFoundException` vinda de um indexador. `IConsultaLedger.Log(desdeSequencia)` devolve exatamente o tipo que `Reconstruir` aceita, então passar um recorte é armadilha de API, não uso exótico. Pelo mesmo motivo, consultar conta ausente lança em vez de devolver zero: devolver zero faria a conta que sumiu da projeção ficar indistinguível da conta que existe e está zerada — que é o defeito que o gate existe para pegar.
