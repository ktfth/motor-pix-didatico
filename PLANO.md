# Plano de implementação — Motor Pix

Este é um motor Pix didático em .NET 8, com repositório vazio, cuja única fonte de verdade são três documentos: a máquina de estados (`motor-pix-maquina-estados.mermaid`, normativa — `prompt:7`), a arquitetura (`motor-pix-arquitetura.mermaid`, normativa para módulos, ledgers e o fluxo numerado 1–7 — `prompt:8`) e o prompt de invariantes. O trabalho real não é escrever C#: é impedir que o código invente comportamento que os documentos não autorizam (`prompt:11`) e transformar nove invariantes em prosa (`prompt:16-24`) em falhas de compilação e de teste. Um painel de análise já processou os três arquivos e derrubou oito supostos conflitos; sobraram cinco decisões que dependem do dono, todas com default seguro declarado, e nenhuma delas trava a escrita do kernel contábil por mais de uma sessão.

---

## Decisões que precisam do dono do projeto

Nenhuma das ambiguidades levantadas na análise sobreviveu como conflito irreconciliável entre os três documentos — a lista de ambiguidades confirmadas é vazia. O que segue são cinco pontos em que os documentos são **silenciosos** e em que a escolha errada é cara ou exige desvio formal de um diagrama (`prompt:58`: "para desviar de um diagrama ou deste prompt, proponha o desvio como ADR e aguarde aprovação"). Todas têm default seguro e reversível — o plano **não para** esperando resposta; ele registra a suposição na ADR do gate e segue.

### Status das decisões

Quatro decididas pelo dono em 2026-08-14, todas na opção recomendada. D5 permanece aberta e só é cobrada no gate 7.

| # | Decisão | Status | Escolha |
|---|---|---|---|
| D1 | Forma dos IDs tipados | **Decidida** | (B) `sealed record` + fábrica `Criar`/`TryCriar`; `Valor` continua `readonly record struct`. ADR-001 registra o desvio da letra de `prompt:27`. |
| D2 | Payload do `pacs.002` para o recebedor | **Decidida** | (A) `Pacs002` carrega `OrgnlTxRef` (E2E, valor, ISPB e conta do creditor). Uma única mensagem cruza a fronteira. |
| D3 | Reenvio de E2E com payload divergente | **Decidida** | (A) Impressão digital canônica do pedido; divergência lança `E2eConflitanteException`. Desvio declarado da letra de `prompt:20`. |
| D4 | Escopo da devolução | **Decidida** | (A) Total e única, iniciada só pelo PspRecebedor. Parcial fora de escopo; valor é campo explícito para manter (B) aditiva. |
| D5 | Canal de saída da conciliação | **Aberta** | Default até decisão: gate 7 entrega detecção e classificação completas; o disparo automático de consulta fica atrás de flag desligado. Exige ADR-011 aprovada, por alterar o diagrama de arquitetura. |

### D1 — `readonly record struct` para IDs backed por string fura a validação no construtor

**Conflito.** `prompt:27` manda `readonly record struct` para value objects e `prompt:28` manda "IDs fortemente tipados com validação no construtor". Em C# as duas exigências são incompatíveis para tipos apoiados em string: todo struct tem construtor sem parâmetros implícito e inevitável, então `default(EndToEndId)` produz uma instância com campo nulo que nunca passou por validação — e dois `default` são iguais entre si pela igualdade gerada do record struct.

**Documentos envolvidos.** `prompt:27`, `prompt:28`, `prompt:20` (unique constraint por `EndToEndId`).

**Por que importa.** O dano é específico da idempotência: um `default(EndToEndId)` usado como chave de dicionário colide com qualquer outro `default`, e um gerador de sequências que produza um array não preenchido fura o gate 4 em silêncio.

**Opções.**
- (A) Manter `readonly record struct` e blindar: acessor que lança se o campo interno for nulo, mais uma proibição explícita de `default` nos construtores de `Lancamento` e `Transacao`.
- (B) `sealed record` com construtor privado e fábricas `Criar` / `TryCriar` para `EndToEndId`, `Ispb`, `ContaId` e `ChavePix`; `Valor` continua `readonly record struct` (seu `default` é zero centavos, que é semanticamente correto).

**Recomendação: (B), como ADR-001.** É a única que torna estado ilegal irrepresentável, que é exatamente o estilo que `prompt:28` persegue; (A) mantém a letra da convenção mas empurra a detecção para runtime. `Valor` permanece struct em qualquer cenário.

**Trava o gate 1** — os value objects são os primeiros tipos escritos.

**Destravável.** Sim, e de forma barata: como todos os IDs nascem por fábrica nomeada (`EndToEndId.Criar(...)`), nunca por `new`, trocar `sealed record` por `readonly record struct` depois é uma edição mecânica de uma linha por tipo, sem tocar em nenhum chamador. O plano começa por (B).

### D2 — O `pacs.002 ACSC` entregue ao PspRecebedor não carrega dados para creditar o cliente

**Conflito.** `arquitetura:47` entrega ao `SM_R` apenas `"6. pacs.002 ACSC"`, e `arquitetura:48` exige que ele lance `"7. crédito do cliente"`. Mas o PspRecebedor nunca viu o `pacs.008`: não tem valor, não tem conta de destino, não tem chave.

**Documentos envolvidos.** `arquitetura:43-48`, `prompt:29` ("mensagens nomeadas pelo vocabulário ISO 20022 [...] sem XML, sem assinatura — só o vocabulário").

**Opções.**
- (A) `Pacs002` carrega um bloco de referência da ordem original (E2E, valor, ISPB e conta do creditor), fiel ao `OrgnlTxRef` do ISO 20022 real. Assinatura: `void NotificarLiquidacao(Pacs002 confirmacao)`.
- (B) O `Spi` encaminha o `pacs.008` ao recebedor junto da confirmação: `void Creditar(Pacs008 ordem, Pacs002 confirmacao)`.
- (C) O recebedor consulta o `Spi` para obter a ordem original — acrescenta superfície pública ao `Spi` só para contornar a falta de payload.

**Recomendação: (A).** `prompt:29` nos dá o vocabulário e não a serialização: a forma do record `Pacs002` é nossa, e o `pacs.002` real de fato transporta `OrgnlTxRef`. (A) preserva literalmente o rótulo da seta 6 e mantém uma única mensagem atravessando a fronteira. (C) é rejeitada por inflar a interface pública do `Spi`.

**Trava o gate 3** — define a assinatura da interface pública do PspRecebedor, que é fronteira de módulo (`prompt:30`).

**Destravável.** Sim. (A) não é desvio de diagrama, é definição de record; segue como ADR-005 se não houver resposta.

### D3 — Reenvio do mesmo E2E com payload divergente

**Conflito.** `prompt:20` diz "reenvio retorna a resposta original, sem novo lançamento" e a nota `estados:22-26` diz "E2E repetido nunca cria estado: unique constraint devolve o resultado da transação original". Nenhum documento diz o que fazer quando o mesmo `EndToEndId` volta com **valor, conta de origem ou chave de destino diferentes**.

**Documentos envolvidos.** `prompt:20`, `prompt:49`, `estados:22-26`, `arquitetura:9`.

**Opções.**
- (A) Conflito: guardar uma impressão digital canônica do pedido (`origem|chaveNormalizada|valorEmCentavos`) e lançar `E2eConflitanteException` quando o reenvio divergir.
- (B) Leitura literal: replay cego da resposta original, ignorando o payload novo.

**Recomendação: (A), como desvio declarado da letra de `prompt:20`.** Replay cego significa responder "aceito" a um pagamento diferente daquele que foi executado — o pior modo de falha possível num motor de pagamentos. Se a resposta for (B), a implementação é trivialmente obtida ignorando a comparação.

**Trava o gate 4.**

**Destravável.** Sim, e a suposição é aditiva: a impressão digital é gravada desde o gate 4 em qualquer cenário (custa um campo); ligar ou desligar a comparação é uma linha. Default enquanto não houver resposta: **(A)**, com a exceção documentada na resposta da API.

### D4 — Devolução: total e única? Só o recebedor inicia?

**Conflito.** Nenhum dos três documentos menciona o **valor** da devolução nem admite mais de uma. `estados:16` tem uma única aresta `LIQUIDADA --> DEVOLVIDA` e `estados:20` faz `DEVOLVIDA` terminal; não existe estado de devolução parcial. `arquitetura:52` mostra a devolução partindo do `SM_R`, e `prompt:54` põe "MED completo" fora de escopo — o que insinua, sem afirmar, que algum caminho de devolução iniciada pelo pagador poderia existir em versão reduzida.

**Documentos envolvidos.** `estados:16`, `estados:20`, `arquitetura:52`, `prompt:22`, `prompt:54`.

**Opções.**
- (A) Devolução total e única, iniciada exclusivamente pelo PspRecebedor; devolução parcial declarada fora de escopo.
- (B) Devolução parcial e múltipla, com saldo devolvível acumulado por E2E original — exige um estado `DEVOLVIDA_PARCIALMENTE` que o diagrama normativo não tem, portanto exige editar o arquivo normativo por ADR.
- (C) (A) mais um caminho de devolução solicitada pelo pagador (MED reduzido).

**Recomendação: (A).** É o que o grafo normativo representa; (B) é inalcançável sem alterar o documento de maior precedência e (C) colide com `prompt:54`. Modelagem preparada para (B) ser aditiva: o valor da devolução é **campo explícito** da transação de devolução, validado como igual ao original, nunca uma cópia implícita.

**Trava o gate 6.**

**Destravável.** Sim, com default seguro (A), registrado na ADR do gate 6 como escopo declarado.

### D5 — A Conciliação "fecha" casos, mas o nó MATCH não tem aresta de saída

**Conflito.** `prompt:43` manda a conciliação "fechar as EXPIRADA remanescentes" e `arquitetura:30` descreve `MATCH` como "Ledgers PSP × Ledger SPI — fecha os casos EXPIRADA". Mas as únicas arestas de `MATCH` são de **entrada** (`arquitetura:54-56`). Como está desenhado, o módulo que deveria fechar casos só pode emitir relatório.

**Documentos envolvidos.** `arquitetura:29-31`, `arquitetura:54-56`, `prompt:43`, `prompt:21` (invariante 6).

**Opções.**
- (A) A conciliação detecta divergência e **emite comandos de consulta de status por E2E divergente**; a transição acontece pela aresta já existente (`estados:13-14`). Zero transições novas, invariante 6 intacto. Exige acrescentar `MATCH --> VAL` ao diagrama de arquitetura, e `MATCH --> SM_R` para reprocessar crédito pendente do recebedor.
- (B) A conciliação decide sozinha comparando ledgers — viola `prompt:21` ("proibido estornar ou confirmar por suposição"), porque a decisão viria de evidência indireta local.
- (C) A conciliação só reporta; "fecha" vira figura de linguagem e o gate 7 não tem critério objetivo.

**Recomendação: (A), como desvio formal do diagrama de arquitetura (ADR-011), submetido antes do gate 7.** É a única opção em que o gate 7 tem critério de aceite verificável ("após consultar e conciliar até estabilizar, nenhuma transação permanece em EXPIRADA") sem furar o invariante 6.

**Trava o gate 7**, mas a porta de leitura (`IConsultaLedger`) nasce no gate 1, porque serve também à propriedade Σ e ao replay do gate 8.

**Destravável parcialmente.** O gate 7 pode entregar **detecção e classificação** de divergências sem nenhuma aprovação; só o fechamento automático depende do desvio aprovado. Default: implementar detecção completa e deixar o disparo de consulta atrás de um flag de composição desligado até a aprovação.

---

## Pontos que os documentos já resolvem

Não gaste tempo decidindo o que já está decidido. Cada item abaixo foi levantado como conflito e refutado por leitura dos próprios documentos.

- **`LIQUIDADA --> DEVOLVIDA` é transição da transação ORIGINAL.** O sujeito da aresta `estados:16` é quem está em `LIQUIDADA`, e só a original pode estar. O parêntese "(transação nova ref. E2E original)" nomeia o instrumento, não o sujeito. `DEVOLVIDA` tem uma única aresta de entrada e não existe `[*] --> DEVOLVIDA`: a leitura alternativa tornaria o estado inalcançável.
- **"Não reabre nem altera a liquidada" (`prompt:22`) fala de LANÇAMENTOS, não do campo Estado.** É o mesmo vocabulário de `estados:36-40` ("nunca UPDATE") e de `prompt:17` ("correção e estorno são lançamentos novos"). Notas neste diagrama qualificam arestas; nunca as revogam.
- **A transação de devolução tem ciclo de vida completo e termina em `LIQUIDADA`,** não em `DEVOLVIDA`. Ela entra pela única porta existente (`estados:2`), pode ser rejeitada (`estados:10`) e pode expirar (`estados:11`). Se ela for rejeitada ou expirar, a original **permanece em `LIQUIDADA`** — o gatilho é "pacs.004 **liquidada**".
- **Invariante 8 ("conta PI não fica negativa e não tem crédito", `prompt:23`) é bancário, não contábil:** "sem crédito" = sem limite/cheque especial. `arquitetura:21` cola as duas metades no mesmo predicado ("saldo ≥ 0, sem crédito") uma linha abaixo de `arquitetura:20` ("crédito PI recebedor"). A leitura contábil tornaria o Ledger SPI natimorto, já que ele só contém contas PI.
- **Não existe estado `ESTORNADA` nem `PENDENTE_ESTORNO`.** O estorno é efeito de entrada em `REJEITADA` (`estados:36-40`), condicionado a uma propriedade do **ledger** ("se houve débito interno"), não a um estado novo. Reexecutar um estorno não é transição: o estado permanece `REJEITADA` antes e depois.
- **`EXPIRADA` com consulta inconclusiva é no-op legal.** A nota `estados:28-34` ("o que a consulta não fechar, a conciliação fecha") só existe porque o autor previu consulta não conclusiva. Não transicionar não é transição, logo não lança exceção. `EXPIRADA` não tem aresta para `[*]` — é estado de espera por desenho.
- **A consulta de status não é disparada por timer.** Não há nó de scheduler em `arquitetura:1-71`; o pontilhado significa "fase posterior" (`prompt:8`), não auto-disparo. Com `prompt:24` e `prompt:51`, sobra uma única forma compatível: operação explícita, síncrona, invocada pela aplicação ou pelo teste, lendo `IClock`.
- **O prazo do timeout não é ambiguidade, é parâmetro ausente.** Nenhum invariante nem critério de aceite depende da magnitude (`prompt:51` exige apenas determinismo com `IClock` fake). Entra como política injetada.
- **A conciliação não tem acesso ao estado da transação.** As únicas arestas que entram em `MATCH` vêm dos três ledgers (`arquitetura:54-56`); não existe `SM_P --> MATCH`. Logo ela é um diff de ledgers e não pode ser restringida a `EXPIRADA` nem em tese — "fecha os casos EXPIRADA" nomeia o resultado mais visível, não um filtro de entrada.
- **O `EndToEndId` vem de fora.** `arquitetura:39` ("1. iniciar pagamento (E2E)") parte do ator, e `estados:2` fala em "POST pagamento com E2E inédito". Validar que o ISPB embutido é o do participante pagador, ou conferir o timestamp embutido contra o `IClock`, seria inventar comportamento (`prompt:11`): o value object valida **forma**, e o instante embutido é metadado opaco do emissor, proibido como fonte de tempo (`prompt:24`).
- **A resposta idempotente é snapshot congelado, não projeção viva.** `arquitetura:9` diz literalmente "replay da resposta" e `prompt:49` exige "mesma resposta"; a arquitetura tem precedência sobre o prompt (`prompt:6-9`).
- **A soma constante (`prompt:18`) não é tautologia** desde que medida sobre as **projeções materializadas** que `prompt:44` e `prompt:48` exigem existir ("reconstruir projeções de saldo do zero", "replay reproduz exatamente as projeções atuais"), e não sobre um fold do log. As classes de bug que Σ não pega têm dono declarado em outros critérios (`prompt:49`, `prompt:50`) e no nó de conciliação.

---

## Arquitetura da solução

### Árvore de projetos

```
micro-pix/
  MotorPix.sln
  Directory.Build.props           # net8.0, Nullable=enable, TreatWarningsAsErrors, CheckForOverflowUnderflow
  Directory.Packages.props        # Central Package Management
  BannedSymbols.txt               # invariantes 1 e 9 como erro de compilação
  docs/adr/                       # notas de 3-5 linhas por gate (prompt:57)
  src/
    MotorPix.Dominio/             # Valor, Saldo, IDs, Conta, Lancamento, Commit, Ledger, IClock, exceções
    MotorPix.Mensagens/           # Pacs008, Pacs002, Pacs004
    MotorPix.Contratos/           # ISpi, IDiretorioChaves, IParticipante, IBarramento, IConsultaLedger,
                                  # EstadoTransacao, DTOs de API. Só interface/record/enum, zero comportamento
    MotorPix.Psp.Nucleo/          # Transacao, tabela de transições, LedgerPsp — kernel de PAPEL, não de módulo
    MotorPix.Dict/
    MotorPix.Spi/
    MotorPix.PspPagador/
    MotorPix.PspRecebedor/
    MotorPix.Conciliacao/
    MotorPix.Composicao/          # composition root; ÚNICO projeto autorizado a tocar DateTime
  tests/
    MotorPix.Testes.Comum/        # RelogioFake, IInterruptor, builders, gerador de sequências
    MotorPix.Dominio.Testes/
    MotorPix.Dict.Testes/
    MotorPix.Spi.Testes/
    MotorPix.Psp.Nucleo.Testes/   # matriz exaustiva estado × evento
    MotorPix.Fluxos.Testes/       # gates 3, 5, 6, 7 — só API pública, sem InternalsVisibleTo
    MotorPix.Propriedades.Testes/ # Σ saldos, reconciliação, replay
    MotorPix.Arquitetura.Testes/  # whitelist de referências, banimentos, hierarquia de exceções
```

### Grafo de dependências

- `Dominio` → nada. Kernel contábil e vocabulário; não conhece módulo nenhum.
- `Mensagens` → `Dominio`. Records ISO sobre value objects tipados.
- `Contratos` → `Dominio`, `Mensagens`. As setas do diagrama de arquitetura viram interfaces aqui.
- `Psp.Nucleo` → `Dominio`, `Mensagens`, `Contratos`. Máquina de estados e ledger de PSP, parametrizados por ISPB.
- `Dict`, `Spi`, `Conciliacao` → `Dominio`, `Mensagens`, `Contratos`.
- `PspPagador`, `PspRecebedor` → `Psp.Nucleo` (e transitivos).
- `Composicao` → todos.
- **Zero aresta módulo → módulo.** As setas 4 e 6 do diagrama (`arquitetura:43`, `arquitetura:46-47`) formam ciclo `PspPagador ↔ Spi`; nenhum layout ingênuo de quatro projetos compila. O ciclo é quebrado por interfaces em `Contratos` mais roteamento por ISPB injetado no `Spi` (`IReadOnlyDictionary<Ispb, IParticipante>`).

### Onde vivem os tipos compartilhados

`prompt:30` proíbe um módulo referenciar **tipos internos de outro**. Em C# `internal` é escopo de assembly: um tipo público num assembly neutro não é tipo interno de módulo nenhum — é dependência comum, e dependência comum não cria aresta módulo → módulo. Por isso `Valor`, `EndToEndId`, `Pacs008` e `IClock` moram em `Dominio`/`Mensagens`/`Contratos`, com duas regras de contenção: `Contratos` só contém interface, record e enum (zero comportamento, zero estado), e nenhum módulo publica tipo próprio lá.

`Psp.Nucleo` existe porque o gate 6 exige que o PspRecebedor **origine** uma transação (`arquitetura:52`), e toda transação percorre a máquina normativa a partir da única porta de entrada (`estados:2`). Duplicar a máquina de estados nos dois PSPs seria dois lugares para o mesmo bug; `Psp.Nucleo` é kernel de papel, não um quinto módulo, e os dois PSPs continuam sem se referenciar.

### Isolamento imposto mecanicamente

1. **Assemblies separados com `internal` por padrão** — custo zero e é o único mecanismo que faz o compilador rejeitar a violação. Corolário: projeto único com pastas não serve.
2. **Teste que lê os `.csproj` e compara `ProjectReference` com a whitelist acima** — pega a violação no instante em que é declarada, ao contrário de `Assembly.GetReferencedAssemblies()`, que só enxerga o que já foi usado.
3. **Teste que proíbe `InternalsVisibleTo` para qualquer assembly que não termine em `.Testes`** — fecha a válvula de escape óbvia.
4. **`Microsoft.CodeAnalysis.BannedApiAnalyzers` com `TreatWarningsAsErrors`** nos projetos de domínio, banindo `System.Decimal`, `System.Double`, `System.Single`, `DateTime.UtcNow/Now`, `DateTimeOffset.UtcNow/Now`, `Guid.NewGuid` e `System.Random`, cada um com mensagem apontando a alternativa. `RelogioSistema` mora em `Composicao`, que não carrega o `BannedSymbols.txt`.
5. **`CheckForOverflowUnderflow`** ligado na solução: overflow silencioso de `long` quebraria a soma de saldos sem lançar nada — o pior modo de falha deste projeto.

`NetArchTest`/`ArchUnitNET` são rejeitados: para "módulo X não referencia módulo Y" não entregam nada além do item 2 e adicionam dependência.

### Persistência

**In-memory, atrás de interfaces de repositório**, conforme a primeira opção de `prompt:31`. Justificativa em uma linha por consequência: delegar a "unique constraint" (`prompt:20`) a um índice do SQLite esconde a regra no banco quando o investimento declarado é no domínio (`prompt:31`); testes sem I/O são pré-requisito do determinismo exigido em `prompt:51`; e a atomicidade multi-linha exigida pelo invariante 3 fica estrutural, obtida por append de um `Commit` imutável sob lock por ledger. A interface de repositório existe desde o gate 1 para que os decorators de injeção de falha tenham onde se encaixar sem sujar o domínio.

### Convenção de idioma

Tipos e namespaces de domínio em português (`Valor`, `TransicaoInvalidaException`, `Lancamento`); nomes ISO preservados como estão (`Pacs008`, `Pacs002`, `EndToEndId`). Registrado na ADR-000 para não oscilar entre gates.

---

## Modelo do domínio

### Plano de contas

Um único tipo `LedgerPsp`, instanciado por ISPB — os rótulos `arquitetura:11` ("cliente ↔ espelho conta PI") e `arquitetura:26` ("espelho conta PI ↔ cliente") não descrevem estruturas diferentes; descrevem os dois polos do mesmo ledger em ordem débito → crédito, invertida porque o dinheiro anda em sentidos opostos nos dois papéis. Sem plano de contas simétrico, a devolução não teria onde lançar.

| Ledger | Conta | Natureza | Descoberto |
|---|---|---|---|
| `Psp:{ispb}` | `CLIENTE:{contaId}` | Passivo | não |
| `Psp:{ispb}` | `ESPELHO_PI` | Ativo | não |
| `Spi` | `PI:{ispb}` | Passivo do SPI | **não** (invariante 8) |
| `Spi` | `ABERTURA` | Contra-conta de emissão | sim, só no genesis |

Não existe conta transitória. O fluxo numerado enumera exatamente três lançamentos no caminho feliz — seta 3 em `LEDGER_P` (`arquitetura:42`), seta 5 em `LEDGER_SPI` (`arquitetura:45`), seta 7 em `LEDGER_R` (`arquitetura:48`) — e uma transitória exigiria um quarto lançamento no instante do ACSC, para o qual não existe seta: `arquitetura:46` termina em `SM_P` e nada reentra em `LEDGER_P`.

Lançamentos canônicos:

- Genesis PSP: `D ESPELHO_PI / C CLIENTE:{a}` — o PSP tem no SPI exatamente o que deve ao cliente; fecha com as duas contas existentes.
- Genesis SPI: `D ABERTURA / C PI:{p}` — fixa a constante `M = Σ PI` do invariante de conservação do pool.
- Seta 3 (débito do cliente, pagador): `D CLIENTE:{a} / C ESPELHO_PI`.
- Seta 5 (liquidação, SPI): `D PI:{p} / C PI:{r}`.
- Seta 7 (crédito do cliente, recebedor): `D ESPELHO_PI / C CLIENTE:{b}`.
- Estorno em `REJEITADA` com débito prévio: `D ESPELHO_PI / C CLIENTE:{a}`, lançamento novo, jamais UPDATE (`estados:36-40`, `prompt:17`).

Consequência deliberada: entre a seta 3 e a seta 5 o espelho do pagador já foi reduzido e a conta PI real ainda não; entre a seta 5 e a seta 7 a conta PI do recebedor já subiu e o espelho ainda não. **Essa defasagem é o estado da transação materializado no ledger**, e é exatamente o insumo do nó de conciliação. Se o espelho nunca divergisse do SPI, `MATCH` seria estruturalmente vazio.

Convenção de sinal: o armazenamento usa saldo bruto `ΣC − ΣD`; **toda regra de negócio usa `SaldoNatural(conta) = Natureza == Ativo ? ΣD − ΣC : ΣC − ΣD`**. Sem essa distinção, a guarda de não-negatividade fica invertida em metade das contas e a comparação espelho × PI compara sinais trocados.

### A invariante de soma constante, como será testada

`prompt:18` é implementado como um catálogo de propriedades, medido sobre as **projeções materializadas** (que `prompt:44` e `prompt:48` obrigam a existir), nunca sobre um fold do log — do contrário o teste provaria apenas que `Aggregate` é determinístico.

- **P0 — estrutural, por commit.** Todo lançamento tem exatamente duas pernas, de mesmo `Valor`, em contas distintas do mesmo ledger. Verificado no append; falha rejeita o commit inteiro.
- **P1 — conservação do pool do SPI.** `Σ_p SaldoNatural(PI:{p}) = M`, com `M` fixado pelo genesis e `ABERTURA` excluída. Falsificável: qualquer lançamento que toque `ABERTURA` fora do genesis quebra.
- **P2 — não-negatividade em todo ponto.** `SaldoNatural ≥ 0` para `PI`, `CLIENTE` e `ESPELHO_PI`, avaliada **dentro do mesmo ato atômico do lançamento** (guarda e append sob o mesmo lock, senão o check-then-act deixa saldo negativo sem mover a soma). O modelo de referência guarda o **mínimo corrente** por conta, porque só o saldo final não denuncia um negativo intermediário.
- **P3 — reconciliação cruzada por E2E (a propriedade com conteúdo).** Para todo participante `p`:

  `SaldoNatural(PI:{p}) − SaldoNatural(ESPELHO_PI de p) = Σ dos valores em trânsito de p`

  e **cada parcela em trânsito tem um E2E identificável**, com causa em exatamente uma de duas classes: (a) transação originada por `p` em `ENVIADA_SPI` ou `EXPIRADA` sem lançamento correspondente no Ledger SPI; (b) liquidação no Ledger SPI a crédito de `p` cujo crédito ao cliente ainda não foi aplicado no ledger de `p`. Divergência órfã — sem E2E que a explique — é dinheiro criado ou destruído e falha o teste. Em quiescência, a diferença é **zero** para todo `p`.
- **P4 — fechamento por E2E.** Todo E2E em estado terminal tem efeito líquido canônico: `LIQUIDADA` move exatamente `V` de `CLIENTE:{a}` para `CLIENTE:{b}` e de `PI:{p}` para `PI:{r}`; `REJEITADA` tem efeito líquido zero em todas as contas; `DEVOLVIDA` tem efeito líquido zero somando original e devolução.
- **P5 — idempotência de lançamento.** `ChaveIdempotencia = (EndToEndId, Etapa)` é única por ledger, imposta no append. É esta, e não a idempotência de API, que garante "zero lançamento novo" quando uma mensagem é reentregue.
- **P6 — replay.** Projeção incremental (write-through) igual ao fold do log, comparadas **estruturalmente, incluindo o conjunto de chaves** — uma conta que zerou e some do dicionário reconstruído passa despercebida numa comparação só por valores.

P0 e P2 são baratas e quase estruturais; **P3 é a que pega bugs**, e é ela que o gate 7 reaproveita.

### Value objects e IDs tipados

```csharp
readonly record struct Valor(long Centavos);        // > 0; perna de lançamento; sem operator * nem /
readonly record struct Saldo(long Centavos);        // com sinal; tipo da projeção
sealed record EndToEndId  { public static EndToEndId Criar(string bruto); }
sealed record Ispb        { public static Ispb Criar(string bruto); }
sealed record ContaId(LedgerId Ledger, string Chave);
abstract record ChavePix  { public static ChavePix Criar(TipoChave tipo, string bruto); }
```

- **`Valor`**: `long` em centavos (`prompt:16`), invariante `Centavos > 0` — ele é o tipo da **perna de lançamento**, não do saldo. Operadores `+` e `−` em `checked`, comparações, `Valor.DeCentavos` e `Valor.DeReais` como únicas fábricas (sem conversão implícita de `long`). **Sem `operator *` e sem `operator /`**: não há tarifa, juros, câmbio nem rateio em lugar nenhum dos três documentos, e divisão é a porta pela qual o arredondamento entra. Overflow relança como `EstouroDeValorException` — `OverflowException` é genérica e `prompt:32` exige exceção de domínio.
- **`Saldo`**: tipo separado, com sinal, usado só em projeção. Separá-lo de `Valor` é o que permite `Valor` ser estritamente positivo sem quebrar o fold do replay.
- **`EndToEndId`**: 32 caracteres — `E` + ISPB (8 dígitos) + `yyyyMMddHHmm` (12) + 11 alfanuméricos (`prompt:28`). Data validada com `TryParseExact` e `AssumeUniversal | AdjustToUniversal` (senão o fuso da máquina faz o teste passar local e quebrar na CI). Os 11 finais são `[A-Za-z0-9]`, **case-sensitive**: toda comparação e todo dicionário usam `StringComparer.Ordinal`, e dois E2E que diferem só por caixa são duas transações, não um replay. `InstanteDeclarado` é exposto como metadado do emissor e proibido por revisão como fonte de tempo (`prompt:24`).
- **`Ispb`**: string de 8 dígitos ASCII, jamais `int` — `00000000` é ISPB válido e o E2E é montado por concatenação. Sem checksum (ISPB não tem dígito verificador). Existência do participante é responsabilidade do módulo `Spi`/`Dict`; o VO valida forma.
- **`ContaId`**: carrega o `LedgerId` a que pertence. São três espaços de contas independentes (`arquitetura:11`, `:21`, `:26`); sem isso, um lançamento entre ledgers "funciona" — débito e crédito batem, Σ continua constante — e é lixo semântico que a propriedade principal não pega. `Lancamento` recusa pernas em ledgers distintos e recusa débito e crédito na mesma conta.
- **`ChavePix`**: sum type fechado (`Cpf`, `Cnpj`, `Email`, `Telefone`, `Aleatoria`), com **normalização dentro do VO** (CPF/CNPJ só dígitos, e-mail em minúsculas, telefone em E.164, EVP como GUID minúsculo formato `D`) — se `"123.456.789-09"` e `"12345678909"` não colapsarem no mesmo valor, o gate 2 fica intermitente. Dígito verificador validado para CPF e CNPJ; e-mail e telefone só por forma. **O tipo vem explícito no payload**, nunca inferido: `"12345678909"` é ambíguo entre CPF e telefone, e inferir introduziria regra que nenhum documento autoriza (`prompt:11`).

### Hierarquia de exceções

```
MotorPixException (abstract)
├── TransicaoInvalidaException(EndToEndId, EstadoTransacao, TipoEvento)   // prompt:19
├── TransacaoDesconhecidaException(EndToEndId)
├── IdentificadorInvalidoException (abstract)
│   ├── EndToEndIdInvalidoException / IspbInvalidoException
│   ├── ContaIdInvalidoException / ChavePixInvalidaException
├── ValorInvalidoException / EstouroDeValorException
├── LancamentoDesbalanceadoException / LancamentoEntreLedgersException
├── ChaveIdempotenciaDuplicadaException
├── SaldoInsuficienteException(ContaId, Saldo, Valor)
├── ContaSemDescobertoException(ContaId, Saldo resultante)   // invariante 8
├── ChaveNaoEncontradaNoDictException(ChavePix)
├── E2eConflitanteException(EndToEndId, impressaoNova, impressaoOriginal)  // D3
└── DevolucaoInvalidaException(MotivoDevolucao)
```

Três regras que acompanham: **idempotência não é exceção** (reenvio idêntico é retorno normal com a resposta congelada; só o conflito lança — senão o teste de `prompt:49` acaba testando o `catch`); **rejeição de negócio não é exceção** (`RECEBIDA --> REJEITADA` é transição com `MotivoRejeicao` estruturado, `estados:5`); e **`FalhaInjetadaException` fica fora da hierarquia**, no assembly de testes, para que nenhum teste confunda falha simulada com rejeição legítima.

### Matriz estado × evento

O diagrama tem 7 estados nomeados e **10 arestas rotuladas** (`estados:2,4,5,7,9,10,11,13,14,16`); as três setas para `[*]` (`estados:18-20`) não têm gatilho e não são eventos. O alfabeto de eventos é derivado 1:1 dos rótulos, dez eventos: `PagamentoSolicitado`, `ValidacaoLocalOk`, `ValidacaoLocalFalhou`, `DebitoLancadoEPacs008Despachado`, `Pacs002Acsc`, `Pacs002Rjct`, `TimeoutDetectado`, `ConsultaConfirmaLiquidacao`, `ConsultaConfirmaRejeicao`, `DevolucaoLiquidada`.

Tamanho real, apurado na implementação: **7 × 9 = 63 células, das quais 9 são válidas e 54 lançam `TransicaoInvalidaException`**. A conta original supunha 10 eventos porque contava `PagamentoSolicitado`; ele não existe como `TipoEvento`, porque a aresta `[*] --> RECEBIDA` é construção, não transição — nascer não é um evento aplicável a uma transação existente. Também; mais a construção `[*] --> RECEBIDA`, mais 9 células de evento para E2E inexistente (`TransacaoDesconhecidaException`, no adapter), mais 7 células de reenvio (interceptadas no repositório, terceiro veredito: nem transição nem exceção). Total de casos assertados: 87.

Células não óbvias, que precisam estar nomeadas no teste:

- `(LIQUIDADA, DevolucaoLiquidada)` é **a única célula válida num estado que tem aresta para `[*]`**. `LIQUIDADA` não é terminal; `EhTerminal` derivado das arestas para `[*]` é um bug que rejeitaria o `pacs.004`.
- `(LIQUIDADA, Pacs002Acsc)` e `(REJEITADA, Pacs002Rjct)` — reentrega idempotente: a mensagem concorda com o estado, só chegou duas vezes. Lançam **na máquina**; o dedup por `(E2E, tipo de mensagem)` no adapter impede que a reentrega chegue até lá.
- `(LIQUIDADA, Pacs002Rjct)` e `(REJEITADA, Pacs002Acsc)` — contradição SPI × PSP: lançam, mas o adapter classifica como `DivergenciaPatrimonial` e encaminha à conciliação em vez de logar e engolir. O sintoma aqui é contábil, não de protocolo.
- `(EXPIRADA, Pacs002Acsc)` e `(EXPIRADA, Pacs002Rjct)` — **nunca chegam à máquina**: um `pacs.002` atrasado dispara uma consulta de status e é o resultado dela que transiciona pelas arestas `estados:13-14`. Assim "de EXPIRADA só se sai por consulta de status" (`prompt:21`) vale ao pé da letra e a evidência autoritativa do SPI não é descartada.
- `(EXPIRADA, TimeoutDetectado)` — o varredor só seleciona `ENVIADA_SPI`; a célula lança e é inalcançável por construção.
- `(RECEBIDA, TimeoutDetectado)` e `(VALIDADA, TimeoutDetectado)` — lançam. Garantido por `RECEBIDA` e `VALIDADA` serem **transientes**: todo POST atravessa `RECEBIDA → VALIDADA → ENVIADA_SPI` (ou `→ REJEITADA`) dentro do mesmo commit; nenhuma transação é lida do repositório nesses estados após o retorno da API — isso vira asserção do teste de propriedade.
- `(VALIDADA, Pacs002Acsc/Rjct)` — inalcançáveis pela regra "persistir a transição antes de produzir o efeito externo", combinada com o despacho por outbox drenado explicitamente. Testadas mesmo assim.
- `(DEVOLVIDA, DevolucaoLiquidada)` — segunda devolução lança; e uma transação com `TipoTransacao = Devolucao` não aceita o comando de devolver, o que corta a recursão.

`Transacao` **não é `record` e não tem `init` em `Estado`**: `transacao with { Estado = LIQUIDADA }` é setter público disfarçado e fura `prompt:19`. Entidade é `sealed class`, construtor privado, `EstadoTransacao Estado { get; private set; }`, mutador único `Aplicar(EventoTransacao)`, reidratação por `internal static Reidratar(TransacaoSnapshot)` visível apenas à persistência do próprio módulo.

---

## Roteiro por gates

Cada gate entrega código, testes e uma nota ADR de 3–5 linhas (`prompt:57`). Só se avança com os testes do gate anterior verdes (`prompt:35`); operacionalmente, cada fato leva `[Trait("gate","N")]`.

Duas correções ao roteiro, apuradas durante o gate 1:

- **Filtro de teste.** `dotnet test --filter "gate<=N"` não existe — o filtro do VSTest aceita `=`, `!=`, `~` e os operadores lógicos `|` e `&`, mas não comparação numérica. O comando de avanço é `dotnet test --filter "gate=1|gate=2|...|gate=N"`.
- **Numeração das ADRs.** Os números citados nos gates abaixo eram estimativas feitas antes da implementação. A numeração real é sequencial e atribuída quando a decisão é tomada; o gate 1 consumiu ADR-000 a ADR-005. Leia as menções a "ADR-00X" nos gates seguintes como *conteúdo a registrar*, não como número reservado.

### Gate 1 — Ledger de partidas dobradas + invariantes 1–3

**Objetivo.** Kernel contábil append-only com projeções materializadas, plano de contas, genesis e as guardas de saldo.

**Entregáveis.** Solução e `Directory.Build.props`/`Directory.Packages.props`/`BannedSymbols.txt`; `MotorPix.Dominio` com `Valor`, `Saldo`, `Ispb`, `ContaId`, `LedgerId`, `EndToEndId`, `ChavePix`, `IClock`, `Natureza`, `Conta`, `Partida`, `Lancamento`, `ChaveIdempotencia`, `Commit`, `Ledger`, `ProjecaoSaldos`, `IConsultaLedger` e a hierarquia de exceções; `MotorPix.Arquitetura.Testes`.

```csharp
interface ILedger {
    Commit Lancar(params Lancamento[] lancamentos);   // ato atômico: guardas + append + projeção
    Saldo SaldoNatural(ContaId conta);
}
interface IConsultaLedger { IReadOnlyList<Commit> Log(long desdeSequencia = 0); }
```

**Testes que fecham.** P0, P1, P2 (com mínimo corrente), P5 e P6 (determinismo e prefixo); lançamento desbalanceado, entre ledgers, ou com débito e crédito na mesma conta rejeitado; ausência de API de UPDATE/DELETE; estorno gera entradas novas; `checked` em overflow; forma dos VOs, incluindo `"00000000"` válido, dígito não-ASCII rejeitado, e case-sensitivity dos 11 finais do E2E; testes de arquitetura (whitelist de `ProjectReference`, `InternalsVisibleTo` só para `*.Testes`, nenhum membro público de domínio expondo `decimal`/`double`/`float`, toda exceção herdando de `MotorPixException`).

**Pronto quando.** `dotnet test --filter "gate=1"` verde, build sem warnings, e ADR-000 a ADR-005 escritas (idioma, IDs por fábrica, plano de contas, genesis, unidade de append, imposição mecânica dos invariantes).

**Depende de.** **D1**.

#### Estado: CONCLUÍDO

625 testes verdes (605 de domínio, 20 de arquitetura), build sem warnings, ADR-000 a ADR-006. Três correções de rota, todas vindas da revisão adversarial e todas registradas em ADR:

- **Descoberto foi eliminado do sistema.** `ABERTURA` passou a ser conta de ativo, o que a deixa positiva após o débito do genesis. O `bool permiteDescoberto` era um invariante desligável em uma linha (ADR-002).
- **A natureza da conta passou a ser derivada de `ClasseConta`**, embutida no `ContaId`. Registrar a conta PI como ativo inverteria a guarda exatamente onde o invariante 8 mora, sem quebrar nenhuma aritmética (ADR-002).
- **O genesis virou prefixo auto-selante do log.** Sem isso, um `D ABERTURA / C PI` posterior criaria dinheiro no pool sem violar partidas dobradas (ADR-003).
- **`ProjecaoSaldos` foi reescrita como implementação genuinamente independente**, acumulando débitos e créditos separados sem passar por `Conta.NaturalDe`. Antes, inverter o sinal naquele método produzia o mesmo erro dos dois lados e o replay ficava verde (ADR-006).

**Dívidas declaradas, a resolver no gate indicado:**

| Dívida | Onde | Gate |
|---|---|---|
| ~~`Ispb.Valor`, `EndToEndId.Valor`, `ChavePix.Valor` etc. usam o nome `Valor` para uma `string`~~ — **quitada no gate 2**: renomeado para `Texto` em 6 tipos e ~74 pontos nos testes. | — | ✔ |
| `SaldoNegativoException` cobre tanto "saldo insuficiente do cliente" (rejeição de negócio) quanto "PI negativa" (violação de invariante). Hoje distinguíveis por `Classe`; o gate 3 pode precisar de tipos separados para não deixar um `catch` engolir a violação como recusa comercial. | `ContabilidadeExcecoes.cs` | 3 |
| `MinimoNatural` é hoje identicamente zero — a guarda impede que negativo chegue a ser gravado. Continua valendo como testemunha independente da guarda, mas não registra excursões como a formulação original de P2 sugeria. Documentado em `IInspecaoLedger`. | `Ledger` | — (decidido: manter) |
| Profundidade de validação por tipo de `ChavePix` (CNPJ alfanumérico, verificação de posse, DNS de e-mail) declarada fora de escopo. | `ChavePix.cs` | 2 (ADR própria) |

### Gate 2 — Dict: chave → ISPB + conta

**Objetivo.** Diretório de chaves determinístico com normalização dentro do value object.

**Entregáveis.** `IDiretorioChaves` e `ResolucaoChave(Ispb, ContaId)` em `Contratos`; `MotorPix.Dict` com implementação in-memory e fixture de chaves; `ChavePix` completo com normalização e dígito verificador.

**Testes.** `Normalizar(Normalizar(x)) == Normalizar(x)`; `"123.456.789-09"` e `"12345678909"` resolvem para a mesma entrada; mutar um dígito de CPF/CNPJ invalida (propriedade); chave inexistente lança `ChaveNaoEncontradaNoDictException`; ISPB com zeros à esquerda sobrevive à concatenação no E2E.

**Pronto quando.** `--filter "gate=2"` verde e ADR-007 (profundidade de validação por tipo de chave; DNS, operadora e posse da chave declarados fora de escopo).

**Depende de.** Nada.

#### Estado: CONCLUÍDO

666 testes verdes no total (625 no gate 1, 41 no gate 2), build sem warnings. Entregue: `MotorPix.Contratos` (`IDiretorioChaves`, `ResolucaoChave`), `MotorPix.Dict` (`DiretorioDeChavesEmMemoria`), três exceções de domínio e ADR-007. A dívida do nome `Valor` → `Texto` nos identificadores foi quitada antes de escrever o módulo.

Decisões tomadas no gate:

- **A coerência do vínculo mora em `ResolucaoChave`, não no diretório.** Se morasse na implementação in-memory, outra implementação de `IDiretorioChaves` poderia devolver resolução incoerente sem passar por guarda nenhuma — e quem consome a interface não sabe de qual implementação veio o objeto. Agora a incoerência é irrepresentável.
- **A guarda de classe roda antes da de ledger.** Uma conta PI vive no ledger do SPI por construção, então a guarda de ledger dispararia primeiro e diria "ledger errado" quando o defeito real é "isto nem é conta de cliente".
- **`TryResolver` leva `[NotNullWhen(true)]`.** Sem isso, o consumidor do gate 3 recebe CS8602 — que vira *erro* sob `TreatWarningsAsErrors` — e o reflexo é silenciar com `!`, supressão que passaria a esconder também uma implementação devolvendo `true` com `null`.
- **A rede de hierarquia de exceções passou a varrer o fonte de `src/`.** A checagem por reflexão cobria só o assembly de domínio: sondei plantando uma exceção fora da hierarquia em `MotorPix.Dict` e ela passou verde. A varredura textual pega, e evita o problema de carregar dois `MotorPixException` de assemblies diferentes.
- **Teste novo de isolamento:** nenhum módulo referencia outro módulo. Hoje a lista tem um item; existe para que o gate 3 a alimente em vez de descobrir a regra por uma linha vermelha em outro teste.

**Entrada obrigatória do gate 3, levantada aqui:**

> **Ninguém valida que a conta resolvida existe no ledger do recebedor.** Vincular uma chave a uma conta que o PSP recebedor nunca abriu — ou que foi fechada — faz o fluxo feliz liquidar no SPI (seta 5) e só então estourar `ContaDesconhecidaException` na seta 7. Dinheiro liquidado sem contrapartida, e a soma de saldos **não pega**: cada ledger continua fechando em zero bruto. É a divergência órfã que a conciliação existe para achar.
>
> Que o DICT não conheça ledgers é separação correta — conhecê-los o acoplaria ao Spi e ao PspRecebedor. A correção é uma segunda porta em `Contratos` do lado recebedor (algo como `bool PodeCreditar(ContaId)`), consultada **antes** da seta 5, com a recusa virando `pacs.002 RJCT` em vez de exceção pós-liquidação. Não foi implementada agora para não antecipar gate com código sem consumidor.

### Gate 3 — Fluxo feliz (setas 1–7)

**Objetivo.** O caminho feliz completo, com a máquina de estados isolada e testada exaustivamente.

**Entregáveis.** `Psp.Nucleo`: `Transacao`, `EstadoTransacao`, `TipoEvento`, tabela de transições `IReadOnlyDictionary<(EstadoTransacao, TipoEvento), EstadoTransacao>` privada e estática, histórico append-only de transições, `LedgerPsp`. `MotorPix.Spi`: validação do `pacs.008` com dedup por E2E (`arquitetura:19`), liquidação atômica (`arquitetura:20`), `LedgerSpi`. `MotorPix.PspPagador`: API de pagamento, adapter, outbox. `MotorPix.PspRecebedor`: handler do `pacs.002`. `MotorPix.Mensagens`. `IBarramento` com `Drenar()`.

Duas regras estruturais deste gate, ambas em ADR:
- **Persistir a transição antes de produzir o efeito externo**, para todo par (transição, efeito). Isso torna `(VALIDADA, Pacs002*)` inalcançável por construção.
- **Despacho por outbox drenado explicitamente pelo host ou pelo teste**, não por chamada reentrante. Elimina o `pacs.002` chegando dentro da própria pilha da seta 4, torna expressável o gate 5 (`pacs.002` que nunca chega) e cria a costura de injeção de falha. A direção e a semântica da seta `SM_P --> VAL` ficam preservadas.

**Testes.** Caminho feliz ponta a ponta com asserção lançamento a lançamento nas setas 3, 5 e 7; **matriz exaustiva 7 x 9** montando cada estado por reidratação de snapshot (nunca percorrendo o caminho, senão o teste de `RECEBIDA` depende do de `VALIDADA`), assertando `(EndToEndId, EstadoAtual, TipoEvento)` e não só o tipo da exceção; teste que **parseia `motor-pix-maquina-estados.mermaid`** (embarcado como recurso) e compara as 9 arestas entre estados nomeados com a tabela escrita à mão — a tabela de produção nunca é gerada do diagrama nem o teste da tabela, senão vira tautologia; saldo insuficiente do cliente leva a `REJEITADA` pela aresta `estados:5`; saldo insuficiente na PI vira `pacs.002 RJCT` (`estados:10`) com estorno por lançamento novo condicionado à existência de débito sem estorno para aquele E2E; P3 em quiescência igual a zero.

**Pronto quando.** `--filter "gate=3"` verde e ADRs do gate escritas.

**Depende de.** **D2**.

#### Estado: CONCLUÍDO

722 testes verdes no total (625 gate 1, 41 gate 2, 56 gate 3), build sem warnings. Entregues: `MotorPix.Mensagens`, `MotorPix.Psp.Nucleo` (máquina de estados, `Transacao`, `LedgerPsp`), `MotorPix.Spi`, `MotorPix.PspPagador`, `MotorPix.PspRecebedor`, `MotorPix.Composicao` (barramento, relógio de sistema, composition root). ADR-008 (outbox) e ADR-009 (garantia da conta de destino).

A revisão adversarial achou **três defeitos críticos**, todos corrigidos com teste de regressão que falharia antes da correção:

- **A drenagem apagava mensagens.** `Drenar` tirava a rodada inteira da fila antes de entregar; uma entrega que lançasse levava junto as seguintes, em definitivo. O SPI liquidava, o crédito do recebedor nunca acontecia, `Pendentes` voltava a zero — e a soma por ledger continuava fechando, então nenhuma propriedade contábil acusava. Agora a entrega é uma por vez e o envelope só sai da fila depois de entregue.
- **Havia caminho com débito lançado e sem `pacs.008`.** O construtor do `Pacs008` era chamado depois do débito; se lançasse, o cliente ficava sem o dinheiro e a transação congelada em `ENVIADA_SPI`. A mensagem passou a ser montada antes da transição para `VALIDADA` — o que também respeita o fato de o diagrama **não** ter aresta `VALIDADA → REJEITADA`.
- **Exceção escapava de `ReceberPacs008`.** Só `SaldoNegativoException` era tratada; um pagamento intra-participante fazia `Lancamento.Criar` recusar e a exceção subia, deixando o E2E sem resposta registrada. Agora qualquer `MotorPixException` na liquidação vira `pacs.002 RJCT` com `OrdemInvalida` — o valor do enum que existia e nunca era produzido.

Mais dois de severidade alta:

- **`PspPagador.PodeCreditar` respondia `true` sem ter handler de crédito**, autorizando o SPI a liquidar contra um destino que ninguém honraria. Responde `false` até o gate 6 acrescentar o crédito de devolução (ADR-009, emenda).
- **O adapter não distinguia reentrega de contradição.** Um `pacs.002` reentregue — caso normal do gate 4 — subia `TransicaoInvalidaException` através do barramento. Agora `Receber` classifica antes de aplicar: reentrega concordante é no-op, contradição vira `DivergenciaPatrimonial` registrada para a conciliação.

### Gate 4 — Idempotência: reenvio do mesmo E2E

**Objetivo.** Duas camadas de dedup, com semânticas diferentes e índices independentes.

**Entregáveis.** Registro de idempotência no `PspPagador`, **reivindicado por insert atômico (`TryAdd`) antes da chamada ao DICT** — `if (repo.Existe(e2e))` seguido de insert é check-then-act e dois POSTs iguais fariam duas resoluções de chave antes de qualquer colisão. Snapshot da resposta gravado como evento do log (senão o replay do gate 8 não reproduz a resposta idempotente). Dedup do `pacs.008` no `Spi` por E2E de mensagem, respondendo com o mesmo `pacs.002` já emitido.

**Testes.** Reenvio idêntico devolve a resposta original byte a byte e zero lançamento novo, em **todos** os estados, inclusive com a original em voo; reenvio com payload divergente conforme D3; `pacs.008` duplicado no SPI não produz segunda liquidação; teste dedicado de concorrência disparando N POSTs paralelos com o mesmo E2E e assertando exatamente um agregado e um conjunto de lançamentos — isolado num assembly com paralelismo próprio, fora da suíte determinística.

**Pronto quando.** `--filter "gate=4"` verde e ADR-010 (snapshot congelado; impressão digital do pedido; dedup do SPI indexa E2E da mensagem, e por isso o `pacs.004` nunca colide com o `pacs.008` que referencia).

**Depende de.** **D3**.

#### Estado: CONCLUÍDO

746 testes verdes no total, build sem warnings. Entregues: `RegistroDeIdempotencia` com impressão digital canônica e log append-only das respostas congeladas, a decisão D3 implementada, e ADR-010.

A revisão adversarial achou dois defeitos, e o crítico foi **introduzido por uma correção preventiva minha**:

- **A compensação da reivindicação órfã era incondicional.** Eu havia adicionado um `catch` que soltava a reivindicação sempre que o processamento falhasse — sem perguntar se algo irreversível já tinha ido para o ledger. Com falha injetada no despacho da seta 4: cliente debitado, `TryTransacao` falso, registro de idempotência vazio, nenhuma divergência anotada. Só o ledger sabia, e a soma continuava constante. Agora a compensação só acontece quando não houve débito; com débito lançado, o E2E fica queimado de propósito e a transação permanece como registro para a conciliação.
- **O dedup do SPI era replay cego.** Comparava só o E2E, então um `pacs.008` com o mesmo identificador e outro creditor recebia de volta o ACSC da ordem original — o SPI afirmando "liquidado" com o `OrgnlTxRef` de um pagamento diferente. Exatamente a falha que a D3 proibiu na API do PSP, e não havia razão para a camada de baixo ser mais permissiva. O SPI passou a guardar a impressão da ordem e a responder `RJCT / OrdemInvalida` à reentrega divergente.

`E2eDuplicadoException`, criada no gate 3 como ponte até o replay existir, foi removida — virou código morto e seria uma armadilha para quem escrevesse um `catch` no futuro.

### Gate 5 — Timeout + consulta de status

**Objetivo.** Expiração determinística e saída de `EXPIRADA` exclusivamente por consulta.

**Entregáveis.** `PoliticaDeTimeout(TimeSpan Limite)` injetada (default 30 segundos de relógio lógico; nenhum teste depende da magnitude); `VarredorDeVencidos` como **porta explícita** chamada pelo host ou pelo teste — não há nó de scheduler em `arquitetura:1-71` e inventar um hosted service seria criar módulo ausente do diagrama; `ISpi.ConsultarStatus` retornando `ResultadoConsulta { Liquidada, Rejeitada, Indeterminada }`; regra do adapter para `pacs.002` atrasado.

**Decisões cravadas aqui (não são perguntas).** O prazo vencido não expira nada — a **varredura** expira; logo um ACSC que chegue antes da varredura é `ENVIADA_SPI --> LIQUIDADA` legítimo (`estados:9`) e o determinismo do teste é sobre ordem de eventos, não sobre relógio de parede. Contagem a partir do instante em que `ENVIADA_SPI` foi gravado, capturado do `IClock` naquela transição, nunca do timestamp embutido no E2E. Fronteira `decorrido >= limite`.

**Testes.** Fronteira nos três pontos (`limite-1`, `limite`, `limite+1`); consulta `Indeterminada` é no-op — permanece em `EXPIRADA`, zero lançamento, zero evento, **zero exceção**; `pacs.002` atrasado sobre `EXPIRADA` dispara consulta e fecha pela aresta `estados:13` ou `estados:14`; teste de arquitetura proibindo `Task.Delay` e `Thread.Sleep` nos assemblies de teste (`prompt:51`).

**Pronto quando.** `--filter "gate=5"` verde e ADR-011 (varredura empurrada; tradução de evidência no adapter; política injetada).

**Depende de.** Nada.

#### Estado: CONCLUÍDO

768 testes verdes no total, build sem warnings. Entregues: `PoliticaDeTimeout` injetada, `ExpirarVencidos` como porta explícita, `ISpi.ConsultarStatus` com `ResultadoConsulta`, a regra do `pacs.002` atrasado, e ADR-011.

A revisão não achou nenhum crítico — não há caminho em que o motor estorne ou confirme por suposição. Achou três problemas de **API**, todos com o mesmo tema: o que o gate 7 vai precisar:

- **`TryTransacao` entregava a entidade viva**, e `Transacao.Aplicar` é público. Uma linha fecharia um caso `EXPIRADA` sem consultar o SPI, sem estornar o débito e fora do lock — exatamente a opção que a decisão D5 proíbe, e a conciliação é o primeiro consumidor tentado a usá-la. Passou a devolver `VistaDaTransacao`, fotografia imutável: o invariante 6 é imposto pelo tipo, não por convenção.
- **`ExpirarVencidos` devolvia só a contagem.** Quem recebesse `3` não tinha como saber quem consultar, e o critério de aceite do gate 7 — "nenhuma transação permanece em `EXPIRADA`" — não era nem executável nem verificável pela API pública. Agora devolve os E2E, e `Expiradas` lista as que estão nesse estado.
- **`Pacs002Atrasados` nunca encolhia.** O laço óbvio do host — consultar enquanto houver atrasados — nunca terminaria, e um E2E já liquidado seguia respondendo "pendente". Virou worklist de verdade.

### Gate 6 — Devolução `pacs.004`

**Objetivo.** A devolução como transação própria, percorrendo a mesma máquina, sem tocar nos lançamentos da original.

**Entregáveis.** `Pacs004`; `IGeradorEndToEndId` sobre `IClock` e `IFonteAleatoria` (os 11 caracteres finais são a segunda fonte ambiental do sistema e `prompt:24` só cobre a primeira — sem injetá-la, o gate 6 não é reproduzível e o replay do gate 8 não reproduz identificadores); `TipoTransacao { Pagamento, Devolucao }`; comando de solicitação no `PspRecebedor`; `internal void MarcarDevolvida(EndToEndId e2eDevolucao, DateTimeOffset em)` na original, disparado pelo evento `DevolucaoLiquidada` roteado por ISPB.

**Testes.** A devolução nasce em `RECEBIDA` com E2E próprio e percorre até `LIQUIDADA`; a original só vai a `DEVOLVIDA` quando a devolução liquida; devolução rejeitada ou expirada deixa a original em `LIQUIDADA`; devolução de devolução rejeitada pelo discriminador de tipo; valor diferente do original rejeitado (`DevolucaoInvalidaException`) — sem essa checagem, uma devolução maior injeta dinheiro; P4 assertando efeito líquido zero na soma original + devolução; nenhum lançamento novo com correlação ao E2E original.

**Pronto quando.** `--filter "gate=6"` verde e ADR-012 (devolução total e única, parcial fora de escopo declarado; gerador de E2E injetado).

**Depende de.** **D4**.

#### Estado: CONCLUÍDO

788 testes verdes no total, build sem warnings. Entregues: `Pacs004`, `OrgnlTxRef.E2eDevolvido`, `IFonteAleatoria` + `GeradorDeE2e`, `PspRecebedor.SolicitarDevolucao`, handler de crédito no pagador, `Transacao.MarcarDevolucao`, e ADR-012.

**O caminho feliz não fechava**, e os dois agentes do fan-out convergiram no mesmo defeito de forma independente: `PspRecebedor.Receber` só sabia ser *creditor*, então o `pacs.002` da devolução que ele mesmo originou lançava dentro da drenagem — e como o barramento só tira o envelope da fila após entrega bem-sucedida, a mensagem ficava presa para sempre, bloqueando todas as outras. Mais cinco correções estão na ADR-012.

**Dívida declarada para o gate 7**, registrada em detalhe na ADR-012: a devolução não participa da varredura de vencidos nem da consulta de status (essas portas são só do pagador), e os dois PSPs divergem onde deveriam ser iguais. A correção é promover handler de crédito, classificador de `pacs.002`, varredor e consulta para `MotorPix.Psp.Nucleo` — o kernel de papel que já existe exatamente por essa razão.

### Gate 7 — Conciliação PSP × SPI

**Objetivo.** Elevar P3 a operação executável e fechar as `EXPIRADA` remanescentes.

**Entregáveis.** `MotorPix.Conciliacao` com `Conciliador.Conciliar()` consumindo os três ledgers por `IConsultaLedger`, produzindo `RelatorioDeConciliacao` com `DivergenciaPorE2E` classificada em: pendência de pagador explicada, pendência de recebedor explicada, e **órfã** (alarme). O canal de saída — emitir consulta de status por E2E divergente e reprocessamento idempotente do crédito pendente — fica atrás de flag até a aprovação de D5.

**Testes.** Após sequências aleatórias com falhas injetadas, toda divergência é classificada e nenhuma é órfã; "após consultar e conciliar até estabilizar, **nenhuma transação permanece em `EXPIRADA`**" — que é o critério de aceite real do gate e a leitura correta da ausência de `EXPIRADA --> [*]` em `estados:18-20`; reentrega dupla do crédito ao recebedor não credita duas vezes (guarda P5 no ledger).

**Pronto quando.** `--filter "gate=7"` verde e ADR-013 aprovada.

**Depende de.** **D5** — a única decisão que ficou aberta até aqui, **aprovada** na opção (A).

#### Estado: CONCLUÍDO

802 testes verdes no total, build sem warnings. Entregues: `MotorPix.Conciliacao` (`Conciliador`, `RelatorioDeConciliacao`), `IPspConciliavel`, `NucleoDePsp` unificando os dois PSPs, e ADR-013.

**D5 foi aprovada na opção (A)**, e este é o **único desvio de arquivo normativo** de todo o roteiro: `motor-pix-arquitetura.mermaid` ganhou duas arestas pontilhadas saindo de `MATCH`. A conciliação detecta e pergunta; quem decide continua sendo o SPI, pelas arestas de transição que já existiam.

**A dívida do gate 6 foi paga junto.** `NucleoDePsp` passou a concentrar o que os dois PSPs faziam igual — guardar transações, classificar `pacs.002`, expirar vencidos, fechar por consulta e creditar. A devolução agora participa de timeout e consulta, sem o que o critério de aceite deste gate seria vacuamente verdadeiro para ela. O refactor não quebrou nenhum dos 788 testes existentes.

A revisão achou **dois críticos e um alto**, todos corrigidos com teste (detalhes na ADR-013). O bug de sinal nas órfãs foi encontrado pelos dois agentes do fan-out de forma independente: toda pendência legítima do pagador virava uma órfã do dobro do valor.

### Gate 8 — Replay das projeções

**Objetivo.** Provar que o ledger é a verdade e as projeções são derivadas.

**Entregáveis.** `ProjecaoSaldos.Reconstruir(log)`, `ProjecaoRespostas.Reconstruir(log)`, `ProjecaoEstados.Reconstruir(historico)`; número de sequência atribuído pelo store em cada `Commit`; instante gravado no commit no momento do append. **Nenhuma projeção consulta `IClock` nem gera identificador** — se precisar de tempo, o tempo já está no lançamento.

**Testes.** O1 determinismo (`Replay(log) == Replay(log)`); O2 prefixo (`Replay(log.Take(n))` igual ao snapshot capturado após a n-ésima operação da sequência gerada) — é este que pega projeção que depende do futuro; O3 equivalência incremental versus fold; O4 sensibilidade à ordem (se embaralhar o log nunca muda o resultado, a projeção é comutativa demais e o gate não prova o que promete — o mínimo corrente por conta resolve); comparação estrutural incluindo o **conjunto de chaves**; replay a partir de log vazio reproduz o genesis.

**Pronto quando.** `--filter "gate=8"` verde e ADR-014 (projeções são funções puras do log; correlação cross-ledger por `EndToEndId`; ordem global só como metadado de teste).

**Depende de.** Nada.

#### Estado: CONCLUÍDO

**824 testes verdes no total** (625 + 41 + 54 + 25 + 22 + 21 + 14 + 22), build sem warnings. Entregues: `ProjecaoEstados`, `ProjecaoRespostas`, `ProjecaoDecisoes`, log de decisões no SPI, e ADR-014.

A revisão achou dois defeitos reais, ambos corrigidos com teste de regressão:

- **O índice vivo e o replay desempatavam ao contrário.** `Congelar` sobrescrevia (última vence) enquanto a projeção fazia `TryAdd` (primeira vence): os dois divergiam sobre o mesmo log — exatamente o que este gate existe para impedir — e o índice apagava a resposta que já tinha saído pela API.
- **A decisão do SPI não era reconstruível de log nenhum.** Uma ordem recusada por participante desconhecido nem chega a lançar no ledger, então um motor reconstruído dos três ledgers responderia `Indeterminada` onde o vivo responde `Rejeitada` — e a transação em `EXPIRADA` perderia sua única saída legítima. O SPI ganhou log append-only de decisões e uma quarta projeção. É o item que põe o invariante 6 fora da memória volátil.

Também corrigido: as duas arestas da ADR-013 estavam desenhadas nos nós errados (ver a ADR).

### O que fica em aberto, declarado

- **Invariante 3, parcial.** A propriedade de soma constante roda sobre `CenarioContabil` — três ledgers crus montados à mão —, e não sobre o `MotorMontado`. O catálogo de costuras de falha que este plano prometeu (`Costura`, `IInterruptor`, `LedgerComFalhas`) **não foi construído**: a injeção de falha existe de forma pontual, em regressões específicas, sem o catálogo de propriedades pendurado nela. `prompt:47` está cumprido em espírito, não em letra. **É a dívida mais importante que sobra.**
- **O diagrama de arquitetura não tem rede automatizada.** A máquina de estados é confrontada por um teste que parseia o `.mermaid`; o de arquitetura é fiel por revisão e por nada mais. A assimetria é real.
- **`MinimoNatural` testemunha pouco**, como já registrado no gate 1: é identicamente zero porque a guarda impede que negativo seja gravado.
- **Nem todo estado do motor tem log.** O vínculo devolução↔original e os dados do crédito recebido no recebedor vivem em dicionários sem log próprio. São reconstruíveis indiretamente, mas não por uma projeção declarada.

---

## Estratégia de testes

**Biblioteca.** `prompt:47` autoriza "FsCheck ou gerador próprio". Adoto **os dois, em papéis distintos**: FsCheck 3.x (`FsCheck.Xunit`, API C# de `FsCheck.Fluent`) para propriedades escalares — value objects, operadores de `Valor`, dígito verificador, balanceamento de lançamento — onde o shrinking pronto vale muito e a modelagem é trivial; e **gerador próprio dirigido pelo estado** para sequências de comandos, onde a API de sequências do FsCheck a partir de C# custa mais do que entrega. CsCheck seria tecnicamente superior para model-based, mas não está na lista autorizada e usá-lo exigiria ADR aprovado (`prompt:58`) — fica como alternativa registrada, não adotada.

**Modelo de comandos.**

```csharp
abstract record Comando;
sealed record IniciarPagamento(EndToEndId E2E, ContaId Origem, ChavePix Destino, Valor Valor) : Comando;
sealed record ReenviarPagamento(EndToEndId E2E, Valor Valor)                                  : Comando;
sealed record DrenarBarramento(int Quantidade)                                                : Comando;
sealed record ResponderSpi(EndToEndId E2E, StatusPacs002 Status)                              : Comando;
sealed record AvancarRelogio(TimeSpan Delta)                                                  : Comando;
sealed record VarrerVencidos()                                                                : Comando;
sealed record ConsultarStatus(EndToEndId E2E)                                                 : Comando;
sealed record Devolver(EndToEndId Original)                                                   : Comando;
sealed record Conciliar()                                                                     : Comando;
```

O gerador é **dirigido pelo estado do modelo**: só produz `Devolver` se existe transação `LIQUIDADA`, só produz `ResponderSpi` se existe `ENVIADA_SPI`. Gerar comandos cegos desperdiça a maior parte das execuções em rejeições triviais. O modelo de referência guarda saldos, **mínimo corrente por conta**, estados por E2E e respostas congeladas — é o oracle de P2, P3 e da idempotência. Shrinking próprio por deleção de comandos (remoção de sufixo, depois bisseção), suficiente para reduzir uma violação na operação 743 a um caso legível.

**Injeção de falhas.** Sendo tudo in-process (`arquitetura:5`) e in-memory, "falha" só pode significar exceção numa **costura nomeada**, apanhada pelo harness, com o estado preservado como ficou. As costuras são uma lista fechada, declarada na ADR do gate 1, mapeando nas fronteiras das setas 1–7:

```csharp
enum Costura { AntesDeGravarLancamento, EntreLancamentoEDespacho, AntesDeEntregarPacs002AoPagador,
               AntesDeEntregarPacs002AoRecebedor, AntesDeCreditarRecebedor, AntesDeCommitar }
interface IInterruptor { void Talvez(Costura c); }   // lança FalhaInjetadaException
```

A falha **não é um comando**: o gerador produz um conjunto de costuras armadas, ortogonal à sequência, senão a falha só cai entre operações e a propriedade fica quase vazia. A entrega do `pacs.002` ao pagador e ao recebedor tem de poder falhar **independentemente** — `arquitetura:46` e `arquitetura:47` são duas setas separadas, e é essa combinação que produz "recebedor adiantado com pagador em `EXPIRADA`", o cenário que justifica a existência da conciliação. Nenhuma costura fica **dentro** do append: o `Commit` é atômico por construção, e permitir falha entre as duas pernas tornaria o invariante 3 trivialmente falso. Decorators (`LedgerComFalhas`, `BarramentoComFalhas`) vivem em `MotorPix.Testes.Comum`; o domínio nunca sabe que existe falha.

Após **cada** comando, e novamente após cada `FalhaInjetadaException`, o harness verifica a conjunção P0–P5. Ao final da sequência roda `Conciliar()` e exige quiescência: divergência zero, toda transação em estado terminal, nenhuma pendência órfã.

**Verificação do replay.** Duas implementações genuinamente distintas — projeção incremental write-through no caminho de escrita e fold puro sobre o log — comparadas estruturalmente por O1–O4 do gate 8. Se as duas forem a mesma função, o teste prova apenas que `Aggregate` é determinístico.

**Controle do tempo.** `IClock` injetado é a única fonte (`prompt:24`), com `DateTimeOffset` e não `DateTime` (elimina a ambiguidade de `Kind`, fonte de bug silencioso). `RelogioFake` com `Avancar(TimeSpan)` é escrito à mão — 15 linhas, zero dependência, controle total do instante de avaliação. `RelogioSistema` em `Composicao` é o único tipo autorizado a tocar `DateTime`, e o `BannedSymbols.txt` transforma qualquer outro uso em erro de compilação. Nenhum `Task.Delay` ou `Thread.Sleep` em teste, verificado por teste de arquitetura.

**Reprodutibilidade.** A semente do gerador é impressa em toda falha; contraexemplos já encontrados viram testes de regressão com semente fixa. Nenhuma fonte ambiental (`Guid.NewGuid`, `DateTime.UtcNow`, `Random.Shared`) dentro do gerador ou do sistema sob teste — senão o replay por semente é ilusório.

---

## Riscos e armadilhas

**Declarar o gate 1 verde com prova vazia.** Se a propriedade de soma for medida como fold do log, ela é verdadeira por construção e passa com o cliente debitado duas vezes ou com dinheiro criado no recebedor. Prevenção: a soma é medida sobre as projeções materializadas, particionada por classe de conta (P1), e a propriedade que realmente pega bugs é a reconciliação cruzada por E2E (P3), exercitada desde o gate 3 e elevada a operação no gate 7.

**Tratar `LIQUIDADA` como terminal.** Derivar `EhTerminal` das arestas para `[*]` (`estados:18-20`) faz o motor rejeitar o `pacs.004` e mata o gate 6 inteiro. Prevenção: terminalidade é propriedade declarada do enum, e a célula `(LIQUIDADA, DevolucaoLiquidada)` é testada nominalmente.

**Estornar sempre que entrar em `REJEITADA`.** `REJEITADA` é alcançável por três arestas com consequências contábeis opostas: vindo de `RECEBIDA` (`estados:5`) não houve débito e estornar cria dinheiro do nada; vindo de `ENVIADA_SPI` ou `EXPIRADA` o estorno é obrigatório. Prevenção: a condição é uma propriedade do **ledger** ("existe débito para este E2E sem estorno correspondente?"), o que torna o handler uniforme e idempotente de quebra — e não uma inspeção do estado de origem.

**Marcar o lançamento original como estornado.** Seria UPDATE, proibido por `prompt:17` e por `estados:36-40`. Prevenção: "já estornado" é uma projeção construída do log, indexada por lançamento original; o estorno é lançamento novo com `ChaveIdempotencia` própria.

**Check-then-act de saldo.** `RECEBIDA --> VALIDADA` valida "saldo do cliente ok" (`estados:4`) e o débito só ocorre na transição seguinte (`estados:7`). Consultar a projeção e apender depois deixa uma janela em que dois débitos passam pela mesma validação e o saldo fica negativo **sem mover a soma** — invariante 8 violado com o invariante 3 intacto. Prevenção: guarda e append no mesmo ato atômico, sob o mesmo lock por ledger; a política mora na conta (`PermiteDescoberto`), não no chamador.

**`pacs.002` reentrante no caminho feliz.** Chamar de volta o `PspPagador` de dentro da pilha da seta 4 faz o ACSC chegar com a transação ainda em `VALIDADA`, e o par não existe no diagrama: `TransicaoInvalidaException` no cenário feliz. Prevenção: outbox drenado explicitamente mais a regra de persistir a transição antes do efeito externo.

**Engolir `TransicaoInvalidaException` no adapter.** As células contraditórias (`LIQUIDADA` recebendo RJCT, `REJEITADA` recebendo ACSC) representam divergência patrimonial real: o dinheiro moveu no Ledger SPI e o PSP acredita no contrário. Capturar e logar torna a divergência invisível. Prevenção: o adapter distingue a família contraditória e a encaminha à conciliação; o teste exaustivo continua assertando o tipo base em todas as células inválidas.

**Descartar o `pacs.002` atrasado por purismo.** A leitura literal manda lançar exceção sobre `EXPIRADA` e jogar fora a confirmação autoritativa do SPI. Prevenção: o `pacs.002` atrasado não transiciona — ele **dispara a consulta de status**, e a saída acontece pela aresta que já existe, com o invariante 6 satisfeito ao pé da letra.

**`default(EndToEndId)` como chave de idempotência.** Dois `default` são iguais entre si e nunca passaram por validação; um gerador que os produza fura o gate 4 em silêncio. Prevenção: D1, mais um gerador que nunca produz `default`, mais validação de identidade nos construtores de `Lancamento` e `Transacao`.

**`decimal` entrando pela borda da API.** O payload `{ "valor": 10.50 }` força desserialização para `decimal` e viola o invariante 1 antes de o domínio ser alcançado; `10.505` trunca em silêncio. Prevenção: o DTO carrega `valorEmCentavos` inteiro, valor fracionário é rejeitado com erro explícito, e um teste de arquitetura garante que nenhum tipo público de domínio expõe `decimal`, `double` ou `float`.

**Overflow silencioso.** Um gerador de propriedades produz extremos de propósito, e `long.MaxValue + 1` num acumulador quebra a soma **sem lançar nada** — a soma também dá wrap e o teste passa. Prevenção: `CheckForOverflowUnderflow` na solução, `checked` explícito nos operadores de `Valor`, `EstouroDeValorException` como exceção de domínio, e viés deliberado do gerador para valores próximos de `long.MaxValue / 2`.

**Teste exaustivo tautológico.** Gerar os casos a partir da mesma tabela que o dispatcher usa prova apenas que a tabela é igual a si mesma. Prevenção: as 10 arestas esperadas são escritas à mão com o número da linha do `.mermaid` em comentário, e um segundo teste parseia o diagrama e compara — se alguém editar o arquivo normativo, a suíte quebra até o código acompanhar.

**Confundir estorno com devolução.** Estorno é correção intra-ledger da mesma transação, sem E2E novo; devolução é transação nova com E2E próprio (`prompt:22`, `estados:42-47`). Lançar a devolução como reversão da original viola o invariante 7 e produz uma segunda contabilização do mesmo dinheiro. Prevenção: tipos distintos no domínio, para que o compilador impeça a confusão.

**Achar que o espelho divergir do SPI é bug.** A defasagem entre `ESPELHO_PI` e `PI` é a representação contábil do dinheiro em trânsito e é a razão de existir do nó `MATCH`. Se o espelho nunca divergisse, a conciliação seria estruturalmente vazia. Prevenção: P3 exige que **toda** divergência seja explicada por um E2E em causa conhecida — o defeito é a divergência **órfã**, não a divergência.
