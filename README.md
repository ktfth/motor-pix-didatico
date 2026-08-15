# Motor Pix

Motor Pix didático em .NET 8. O objetivo é estudo: **correção e clareza do domínio valem mais que performance, UI ou infraestrutura**.

Três documentos são a fonte da verdade, nesta ordem de precedência:

1. [`motor-pix-maquina-estados.mermaid`](motor-pix-maquina-estados.mermaid) — estados e transições da transação. **Normativo:** nenhuma transição fora dele existe.
2. [`motor-pix-arquitetura.mermaid`](motor-pix-arquitetura.mermaid) — módulos, ledgers e o fluxo numerado (setas 1–7 = caminho feliz).
3. [`prompt-motor-pix.md`](prompt-motor-pix.md) — invariantes e convenções.

O código serve os diagramas, não o contrário. Quando um deles muda, a suíte quebra — e isso é o mecanismo, não um acidente.

## Como rodar

**Não há executável.** Todos os 17 projetos são biblioteca ou suíte de teste: comunicação HTTP e host estão declarados fora de escopo em [`prompt-motor-pix.md`](prompt-motor-pix.md). Os módulos conversam in-process, por interface. Rodar este projeto significa **executar a verificação** — é a suíte que exercita o motor de ponta a ponta, do POST ao crédito do recebedor.

**Pré-requisito:** SDK .NET 8 ou superior. O alvo é `net8.0`; SDKs mais novos compilam via targeting pack, sem ajuste. Nada além disso — sem banco, sem Docker, sem variável de ambiente, sem serviço externo.

```bash
git clone https://github.com/ktfth/motor-pix-didatico.git
cd motor-pix-didatico
dotnet test MotorPix.slnx
```

Numa máquina limpa isso leva cerca de um minuto: ~9s de restore, ~28s de build, ~30s de testes. O `dotnet test` faz restore e build sozinho — os passos separados abaixo servem para isolar onde algo falhou:

```bash
dotnet restore MotorPix.slnx
dotnet build   MotorPix.slnx --no-restore
dotnet test    MotorPix.slnx --no-build
```

Saída esperada, por suíte:

| Suíte | Testes |
|---|---|
| `MotorPix.Dominio.Testes` | 605 |
| `MotorPix.Fluxos.Testes` | 130 |
| `MotorPix.Especificacoes` | 104 |
| `MotorPix.Dict.Testes` | 39 |
| `MotorPix.Psp.Nucleo.Testes` | 27 |
| `MotorPix.Arquitetura.Testes` | 23 |
| **Total** | **928** |

> O primeiro `dotnet build` num clone novo **gera** os `*.feature.cs` a partir dos `.feature` — eles não são versionados de propósito. Sem esse build, os cenários não existem para o runner.

928 testes, build sem warnings: **852 em C# e 76 cenários Gherkin executáveis**.

> Os 76 cenários vêm de 45 blocos escritos — 9 deles são `Esquema do Cenário`, que a expansão dos `Exemplos` transforma em 31 casos. O projeto `MotorPix.Especificacoes` reporta 104 testes porque inclui, além dos cenários, 28 `[Fact]` que testam as próprias ferramentas de tradução (`Dinheiro`, `Vocabulario`) — esses contam como C#.

Para rodar um gate específico do roteiro:

```bash
dotnet test MotorPix.slnx --filter "gate=3"            # testes em C#
dotnet test MotorPix.slnx --filter "Category=gate3"    # cenários Gherkin
```

> **São dois traits diferentes, e isso importa.** Os testes em C# usam `Trait("gate", "N")`; o SpecFlow converte a tag `@gateN` da funcionalidade em `Trait("Category", "gateN")`. Quem filtra só por `gate=N` roda os `[Fact]` e **nenhum cenário**, sem que nada avise.

> O filtro do VSTest aceita `=`, `!=`, `~` e os operadores `|` e `&` — **não** aceita comparação numérica, então `gate<=3` não funciona.

Requer SDK .NET 8 ou superior. O alvo é `net8.0`; o SDK 10 compila via targeting pack.

## Por onde começar a ler

| Se você quer… | Comece por |
|---|---|
| entender o desenho antes do código | os dois `.mermaid` e depois [`PLANO.md`](PLANO.md) |
| ler o comportamento sem ler C# | `tests/MotorPix.Especificacoes/Funcionalidades/*.feature` |
| ver o caminho feliz funcionando | `tests/MotorPix.Fluxos.Testes/CaminhoFelizTestes.cs` |
| entender o núcleo contábil | `src/MotorPix.Dominio/Contabilidade/Ledger.cs` |
| saber *por que* algo é assim | [`docs/adr/`](docs/adr/) — 16 decisões, cada uma com o defeito concreto que ela evita |
| conhecer o estado e as dívidas | a seção final de [`PLANO.md`](PLANO.md) |

## Mapa dos projetos

O grafo de dependências é imposto por teste: `MotorPix.Arquitetura.Testes` lê os `.csproj` e compara com uma whitelist escrita à mão. Acrescentar uma aresta exige editar aquele mapa, e editar aquele mapa aparece no diff.

### Núcleo — não conhece módulo nenhum

- **`MotorPix.Dominio`** — `Valor` e `Saldo` (`long` em centavos), IDs tipados, o `Ledger` append-only de partidas dobradas, `ProjecaoSaldos`, `IClock`, e a hierarquia de exceções.
- **`MotorPix.Mensagens`** — `Pacs008`, `Pacs002`, `Pacs004`. Vocabulário ISO 20022, sem XML e sem assinatura.
- **`MotorPix.Contratos`** — o assembly neutro onde as setas do diagrama viram interfaces. Só interface, record e enum.

### Kernel de papel

- **`MotorPix.Psp.Nucleo`** — a máquina de estados, a `Transacao`, o `LedgerPsp` e o `NucleoDePsp` com o que os dois PSPs fazem igual: expirar vencidos, consultar status, classificar `pacs.002`, creditar. **Não é um quinto módulo** — é o que impede que o mesmo bug exista em dois lugares.

### Módulos — dependem do núcleo e dos contratos, **nunca uns dos outros**

- **`MotorPix.Dict`** — diretório de chaves (seta 2).
- **`MotorPix.Spi`** — validação, dedup por E2E e liquidação atômica entre contas PI (seta 5).
- **`MotorPix.PspPagador`** — API de pagamento, origem das setas 1–4.
- **`MotorPix.PspRecebedor`** — crédito do cliente (seta 7) e origem da devolução.
- **`MotorPix.Conciliacao`** — ledgers PSP × ledger SPI; detecta divergências e manda consultar.

### Composição

- **`MotorPix.Composicao`** — o único projeto que conhece todos. É onde o ciclo das setas 4 e 6 se resolve, por registro tardio no barramento, e o único autorizado a ler o relógio do sistema.

### Especificações executáveis

- **`MotorPix.Especificacoes`** — o mesmo comportamento em Gherkin (SpecFlow), legível por quem não lê C#: 6 funcionalidades, 76 cenários. Entra pelo composition root, como os testes de fluxo — um cenário em linguagem de negócio não ganha permissão que um `[Fact]` não tem. Veja [ADR-015](docs/adr/ADR-015-especificacoes-executaveis.md) para por que o dinheiro do cenário nunca vira `decimal` e por que os nomes do diagrama são traduzidos por nome, nunca por posição.

## Os nove invariantes, e o que impõe cada um

O ponto do projeto é que invariante em prosa não vale nada: cada um tem um mecanismo.

| # | Invariante | Como é imposto |
|---|---|---|
| 1 | Dinheiro é `long` em centavos | `Valor` é `readonly record struct` sobre `long`, sem `*` nem `/`. `BannedSymbols.txt` bane `decimal`/`double`/`float`, e um teste de arquitetura varre reflexão **e** fonte — porque o analyzer não cobre declarações (ADR-005). |
| 2 | Ledgers append-only, partidas dobradas | `ILedger` não tem update nem delete: imposto por **ausência de API**. As duas pernas compartilham um único `Valor`, tornando "desbalanceado" irrepresentável. |
| 3 | Σ saldos constante, inclusive com falhas | Propriedades P0–P6 sobre sequências de semente fixa. **Parcial** — ver dívidas. |
| 4 | Transição fora do diagrama lança | Tabela escrita à mão + matriz exaustiva 7×9 + um teste que **parseia o `.mermaid`** e o confronta. `Estado` sem setter público; `VistaDaTransacao` impede a entidade viva de escapar. |
| 5 | Idempotência por `EndToEndId` | Reivindicação por insert atômico antes de qualquer consulta externa; resposta congelada; impressão canônica do pedido. Abaixo disso, a `ChaveIdempotencia` do ledger garante "zero lançamento novo" venha a reentrega por onde vier. |
| 6 | Timeout ≠ falha; de `EXPIRADA` só por consulta | `pacs.002` atrasado **registra e não transiciona**; `Indeterminada` é no-op legítimo; a conciliação não tem porta que aplique evento. |
| 7 | Devolução é transação nova | `pacs.004` com E2E próprio; o crédito de volta usa a chave da **devolução**; da original só muda o campo `Estado`. |
| 8 | Conta PI não fica negativa | A natureza da conta é **derivada** da classe, nunca recebida; guarda dentro do mesmo ato atômico do append. Não existe `permiteDescoberto`. |
| 9 | Tempo só via `IClock` | `BannedSymbols` cobre `UtcNow`, `Now`, `Today`, `TimeProvider.System`, `Stopwatch.GetTimestamp`, `TickCount64` — em `src/` **e** em `tests/`. `RelogioSistema`, no composition root, é a única exceção, e um teste garante que ela é a única. |

## Três decisões que explicam o resto

- **Outbox drenado explicitamente** ([ADR-008](docs/adr/ADR-008-outbox-e-ordem-dos-efeitos.md)). Nada é entregue na hora; `Drenar()` é chamado pelo teste ou pelo host. Sem isso, o `pacs.002` chegaria dentro da própria pilha do envio, com a transação ainda em `VALIDADA` — par que o diagrama não tem. De quebra, "o `pacs.002` que nunca chega" passa a ser expressável sem mecanismo nenhum: basta não drenar.
- **O replay é uma segunda implementação, não um segundo laço** ([ADR-006](docs/adr/ADR-006-independencia-do-replay.md)). `ProjecaoSaldos` deriva o saldo natural com expressão própria, sem chamar `Conta.NaturalDe`. Se as duas convergissem naquele método, inverter o sinal produziria o mesmo erro dos dois lados e o gate de replay ficaria verde. A duplicação é o mecanismo.
- **A conciliação pergunta, não decide** ([ADR-013](docs/adr/ADR-013-canal-de-saida-da-conciliacao.md)). É o **único desvio de arquivo normativo** do projeto, aprovado e registrado: `MATCH` ganhou duas arestas de saída. A transição continua acontecendo pelas arestas que já existiam.

## Estado e dívidas

Os oito gates do roteiro estão concluídos. O que **não** está pronto, declarado:

- **O invariante 3 é parcial.** A propriedade de soma constante roda sobre `CenarioContabil` — ledgers crus montados à mão — e não sobre o motor montado. O catálogo de costuras de falha (`Costura`, `IInterruptor`, `LedgerComFalhas`) que o plano previu **não foi construído**; a injeção de falha existe de forma pontual. É a dívida mais importante.
- **O diagrama de arquitetura não tem rede automatizada.** A máquina de estados é confrontada por teste; o de arquitetura é fiel por revisão e nada mais.
- **`MinimoNatural` testemunha pouco** — é identicamente zero, porque a guarda impede que negativo chegue a ser gravado.

A lista completa está no fim de [`PLANO.md`](PLANO.md).

## Fora de escopo

Certificados ICP-Brasil, XML real das mensagens, QR Code, Pix Cobrança, MED completo, antifraude e comunicação HTTP entre módulos. Os módulos conversam in-process, por interface.
