# ADR-015 — Especificações executáveis com SpecFlow

**Contexto.** O projeto tinha cobertura alta (824 testes) escrita inteiramente em C#. Foi pedida a adoção de SpecFlow, para que o comportamento do motor também exista em linguagem de domínio, legível por quem não lê C#.

**A recomendação foi contrária, e o registro é honesto quanto a isso.** O argumento contra: a especificação deste motor já está formalizada em dois `.mermaid` normativos, mais precisos que prosa; e a máquina de estados já tem vínculo mecânico com o código — o `.mermaid` entra como recurso embarcado e um teste o confronta com a tabela escrita à mão. Uma `.feature` seria uma terceira redação do mesmo conteúdo, sem vínculo mecânico com nada, portanto mais uma fonte para dessincronizar. **A decisão do dono do projeto foi adotar assim mesmo, e é ela que vale.** Esta ADR registra a decisão e as condições em que ela foi tomada, não a discussão.

**Decisão 1 — SpecFlow 3.9.74, com a data de validade escrita no `Directory.Packages.props`.** O produto foi descontinuado e esta é a última versão publicada: não há para onde atualizar e não haverá correção. O sucessor é o **Reqnroll**, fork do autor original — a rota de saída está registrada junto da versão, para que quem encontrar o toolchain quebrado num SDK futuro não perca tempo procurando patch que não virá.

**Decisão 2 — foi sondado antes de adotado.** Antes de tocar no repositório, uma sonda isolada confirmou que o pacote restaura em `net8.0`, gera o code-behind, executa, e — o que mais importava — **sobrevive ao regime estrito do projeto**: `TreatWarningsAsErrors`, `Nullable=enable` e o `BannedApiAnalyzers` com o mesmo `BannedSymbols.txt`, sem um único aviso. Adotar primeiro e descobrir depois teria significado desmontar um projeto já verde.

**Decisão 3 — o dinheiro do cenário vira centavos sem passar por `decimal`.** O caminho óbvio seria declarar o parâmetro do passo como `decimal` e deixar o SpecFlow converter — e isso introduziria `decimal` no projeto pela porta dos fundos, exatamente onde o invariante 1 o proíbe. `Dinheiro.EmCentavos` lê `"1.000,00"` dígito a dígito e devolve `long`, sem tipo de ponto flutuante no meio. Exige dois dígitos de centavos: `"250,5"` é recusado, porque "cinco centavos" e "cinquenta centavos" não podem depender de quem lê.

**Decisão 4 — os nomes do diagrama são traduzidos por nome, nunca por valor.** `Vocabulario.Enumerado<T>("ENVIADA_SPI")` casa pelo nome normalizado do membro do enum. Casar por posição faria um cenário continuar verde depois de alguém reordenar o enum, e o texto passaria a afirmar outra coisa sem que uma linha dele mudasse. Nome desconhecido lança com a lista do que era aceito — cenário com estado escrito errado morre como erro de vocabulário, em vez de ser silenciosamente ignorado.

**Decisão 5 — as especificações entram pelo composition root, como os testes de fluxo.** Nenhuma referência a módulo individual, e o projeto foi acrescentado à whitelist do teste de arquitetura de forma explícita. **Um cenário escrito em linguagem de negócio não ganha permissão que um `[Fact]` não tem.** A rede funcionou como projetada: assim que o projeto nasceu, o teste de estrutura reprovou por ele não estar declarado.

**O que estas especificações são, e o que não são.** Elas **não** substituem nenhuma suíte existente e nada foi removido. As propriedades contábeis, a matriz exaustiva 7×9, os oráculos de replay e as regressões continuam onde estavam, porque nenhuma delas é exprimível em Gherkin sem perder força. O que as `.feature` acrescentam é uma segunda leitura do mesmo comportamento, na linguagem de quem usa o motor.

**Decisão 6 — os cenários foram medidos por mutação, não por contagem.** 104 cenários verdes não provam nada até que se saiba o que os deixa vermelhos. Duas mutações foram plantadas no motor e revertidas:

| Mutação plantada | Cenários | Suíte C# |
|---|---|---|
| Sinal invertido no resíduo de órfãs da conciliação (o defeito histórico) | **pegou** — 5 falhas, incluindo o esquema "a divergência vale o pagamento, e não o dobro dele" | pega |
| `pacs.002` ACSC atrasado deixa de ser registrado em `_pacs002Atrasados` | **não pegou** | pega — 5 falhas |

A segunda é o achado, e fica registrada em vez de escondida: a máquina de estados barra a transição indevida de qualquer jeito, então o estado observável não muda; o que muda é a lista de atrasados, que nenhum cenário afirma. **As especificações são uma leitura mais fraca do que a suíte em C# em pelo menos um ponto conhecido** — e é exatamente por isso que nada foi removido.

**O risco que fica, declarado.** Um cenário Gherkin não tem vínculo mecânico com os `.mermaid`: se o diagrama mudar, a `.feature` não quebra sozinha — só quebra se o comportamento também mudar. É a mesma fragilidade que o diagrama de arquitetura já tem, agora numa segunda superfície. Quem editar um diagrama normativo continua tendo de procurar o texto afetado à mão.

**Uma armadilha operacional.** As duas suítes usam traits diferentes: `Trait("gate", "N")` nos `[Fact]`, e `Trait("Category", "gateN")` gerado a partir da tag `@gateN`. `--filter "gate=5"` roda os testes em C# e **nenhum cenário**, em silêncio. Está no README, porque é o tipo de coisa que faz alguém concluir que um gate passou inteiro quando metade dele não rodou.
