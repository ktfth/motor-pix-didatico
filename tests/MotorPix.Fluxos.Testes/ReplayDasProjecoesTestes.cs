using System.Reflection;
using MotorPix.Composicao;
using MotorPix.Conciliacao;
using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Gate 8: as tres projecoes reconstruidas do zero, a partir do log.
/// <para>
/// O criterio do prompt e uma frase so — "replay do ledger reproduz exatamente as projecoes
/// atuais" —, e a dificuldade inteira do gate esta em nao provar essa frase com um teste vazio. Se
/// os dois lados da comparacao sairem da mesma funcao, o teste demonstra apenas que a funcao e
/// deterministica: qualquer erro de sinal, qualquer conta perdida e qualquer dependencia do futuro
/// aparece identica nos dois lados e o gate fica verde por construcao.
/// </para>
/// <para>
/// Por isso os quatro oraculos sao desiguais de proposito. O1 (determinismo) e o mais fraco e esta
/// aqui so para nomear o que <em>nao</em> basta. O2 (prefixo) e o que carrega o gate: compara o
/// replay dos n primeiros commits com a projecao fotografada naquele exato ponto da sequencia, e e
/// o unico que pega uma projecao que depende de informacao que ainda nao existia. O3 compara duas
/// implementacoes genuinamente distintas — a incremental do <see cref="Ledger"/>, escrita durante o
/// append, e o fold puro de <see cref="ProjecaoSaldos"/>, que acumula debitos e creditos separados
/// e deriva a natureza sem passar por <c>Conta.NaturalDe</c> (ADR-006). O4 exige que embaralhar o
/// log <b>mude</b> o resultado: uma projecao indiferente a ordem nao esta reconstruindo historia
/// nenhuma.
/// </para>
/// </summary>
public sealed class ReplayDasProjecoesTestes
{
    // -----------------------------------------------------------------------------------------------
    // O1 — determinismo (o oraculo fraco)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Reconstruir duas vezes do mesmo log da o mesmo resultado, nas tres projecoes.
    /// <para>
    /// Sozinho, este oraculo nao prova o que o gate promete: ele e satisfeito por
    /// <c>Reconstruir(log) =&gt; new ProjecaoSaldos()</c>, que ignora o log inteiro e devolve vazio
    /// sempre. Tudo o que ele estabelece e que nenhuma das tres funcoes le fonte ambiental — nem
    /// relogio, nem identificador sorteado, nem ordem de enumeracao instavel. E uma pre-condicao dos
    /// outros tres oraculos, e nao um deles.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_DuasVezesDoMesmoLog_ProduzProjecoesIguaisNasTresProjecoes()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        foreach (LedgerSobReplay ledger in cenario.Ledgers)
        {
            IReadOnlyList<Commit> log = ledger.Consulta.Log();

            SnapshotSaldos primeira = ProjecaoSaldos.Reconstruir(log).Snapshot();
            SnapshotSaldos segunda = ProjecaoSaldos.Reconstruir(log).Snapshot();

            Assert.True(
                primeira.EhIgualA(segunda),
                $"duas reconstrucoes do log de {ledger.Nome} divergiram: {Junte(primeira.Diferencas(segunda))}");
        }

        IReadOnlyList<HistoricoDeTransacao> historicos = cenario.Motor.Pagador.HistoricosParaReplay();
        ProjecaoEstados estadosA = ProjecaoEstados.Reconstruir(historicos);
        ProjecaoEstados estadosB = ProjecaoEstados.Reconstruir(historicos);

        Assert.True(
            estadosA.EhIgualA(estadosB),
            $"duas reconstrucoes do mesmo historico divergiram: {Junte(estadosA.Diferencas(estadosB))}");

        IReadOnlyList<EntradaDeIdempotencia> respostas = cenario.Motor.Pagador.Idempotencia.Log();

        Assert.True(
            ProjecaoRespostas.Reconstruir(respostas).EhIgualA(ProjecaoRespostas.Reconstruir(respostas)),
            "duas reconstrucoes do mesmo log de idempotencia divergiram");

        // A prova de que o oraculo nao rodou no vazio: havia log para reconstruir nas tres.
        Assert.NotEmpty(historicos);
        Assert.NotEmpty(respostas);
    }

    // -----------------------------------------------------------------------------------------------
    // O2 — prefixo (o oraculo que carrega o gate)
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Para cada ponto da sequencia, o replay dos <c>n</c> primeiros commits reproduz a projecao
    /// fotografada naquele ponto.
    /// <para>
    /// Este e o teste mais importante do gate, e a razao e uma so: <b>ele e o unico que pega uma
    /// projecao que depende do futuro</b>. Comparar apenas o estado final aceita um fold que olhe o
    /// log inteiro antes de decidir cada valor — que percorra duas vezes, que corrija para tras, que
    /// use o ultimo commit para ajustar o primeiro. Um fold assim bate no fim e erra em todo ponto
    /// intermediario, e o gate ficaria verde com ele.
    /// </para>
    /// <para>
    /// A foto e tirada com <c>LerConsistente</c>, que devolve log e projecao sob a mesma aquisicao
    /// do lock: <c>Log()</c> seguido de <c>Snapshot()</c> sao duas leituras e nao compoem — um
    /// append entre elas faria o gate acusar divergencia que nunca existiu.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_DeCadaPrefixoDoLog_ReproduzOSnapshotFotografadoNaqueleMomento()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        // Um roteiro curto tornaria o oraculo quase vazio: sem pontos intermediarios nao ha prefixo
        // proprio para comparar, e o teste degeneraria em O1.
        Assert.True(
            cenario.Instantaneos.Count >= 12,
            $"o roteiro rico rendeu apenas {cenario.Instantaneos.Count} pontos de captura");

        foreach (InstantaneoDoMotor foto in cenario.Instantaneos)
        {
            for (int i = 0; i < cenario.Ledgers.Count; i++)
            {
                LedgerSobReplay ledger = cenario.Ledgers[i];
                InstantaneoDeLedger naquele = foto.Ledgers[i];

                SnapshotSaldos prefixo = ProjecaoSaldos
                    .Reconstruir(ledger.Consulta.Log().Take(naquele.Commits))
                    .Snapshot();

                Assert.True(
                    prefixo.EhIgualA(naquele.Snapshot),
                    $"apos '{foto.Operacao}', o replay dos {naquele.Commits} primeiros commits de "
                    + $"{ledger.Nome} divergiu do snapshot daquele momento: "
                    + Junte(prefixo.Diferencas(naquele.Snapshot)));
            }
        }
    }

    // -----------------------------------------------------------------------------------------------
    // O3 — incremental versus fold
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// A projecao mantida durante o append e o fold puro do log coincidem, nos tres ledgers.
    /// <para>
    /// A comparacao vale alguma coisa porque as duas implementacoes sao independentes <b>de
    /// proposito</b> (ADR-006). O <see cref="Ledger"/> guarda um saldo bruto por conta, atualizado
    /// dentro do mesmo ato atomico do lancamento, e converte para natural chamando
    /// <c>Conta.NaturalDe</c>. A <see cref="ProjecaoSaldos"/> acumula somas de debito e de credito
    /// separadas e escreve a regra "ativo cresce a debito, passivo cresce a credito" outra vez, com
    /// suas proprias maos. Antes dessa separacao as duas convergiam naquele metodo: inverter o sinal
    /// la dentro produzia o mesmo valor errado dos dois lados e o replay ficava verde.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Snapshot_DaProjecaoIncremental_EhIgualAoFoldPuroDoLogNosTresLedgers()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        foreach (LedgerSobReplay ledger in cenario.Ledgers)
        {
            (IReadOnlyList<Commit> log, SnapshotSaldos incremental) = ledger.Inspecao.LerConsistente();
            SnapshotSaldos fold = ProjecaoSaldos.Reconstruir(log).Snapshot();

            Assert.True(
                fold.EhIgualA(incremental),
                $"o fold do log de {ledger.Nome} divergiu da projecao mantida no append: "
                + Junte(fold.Diferencas(incremental)));

            // Contagem separada da igualdade: EhIgualA ja compara o conjunto de chaves, e esta
            // assercao existe para que a mensagem de falha diga "sumiu uma conta" em vez de listar
            // uma diferenca de saldo em conta que so existe de um lado.
            Assert.Equal(incremental.Contas.Count, fold.Contas.Count);
            Assert.NotEmpty(fold.Contas);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // O4 — sensibilidade a ordem
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Embaralhar o log de um ledger muda o resultado — aqui, recusando o replay.
    /// <para>
    /// O oraculo existe porque uma projecao comutativa demais nao prova nada: se a ordem dos commits
    /// fosse indiferente, "reconstruir a historia" seria apenas somar um conjunto, e um log com os
    /// mesmos lancamentos em ordem impossivel — credito antes do debito que o financia — passaria
    /// como se fosse a historia real.
    /// </para>
    /// <para>
    /// Em <see cref="ProjecaoSaldos"/> a sensibilidade e <b>total</b>: o replay exige o log completo
    /// comecando na sequencia 1 e sem buracos, entao toda permutacao nao trivial e recusada em vez
    /// de produzir numero diferente. Recusar tambem e mudar o resultado, e e a forma mais forte de
    /// mudar: as duas entradas tem exatamente os mesmos commits, e mesmo assim uma reconstroi e a
    /// outra nao. O embaralhamento e deterministico — inversao e troca de duas posicoes —, porque
    /// <c>System.Random</c> esta banido e um contraexemplo irreproduzivel nao serve para nada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_ComOLogDeSaldosInvertidoOuComDuasPosicoesTrocadas_RecusaPorSequenciaForaDeOrdem()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();
        IReadOnlyList<Commit> log = cenario.Motor.Pagador.Ledger.Consulta.Log();

        Assert.True(log.Count > 2, $"o log do pagador tem {log.Count} commits e nao da para embaralhar");

        // Em ordem, reconstroi.
        Assert.NotEmpty(ProjecaoSaldos.Reconstruir(log).Contas);

        List<Commit> invertido = [.. log.Reverse()];

        // Mesmos commits, mesmo tamanho: a unica diferenca entre as duas entradas e a ordem.
        Assert.Equal(log.Count, invertido.Count);
        Assert.All(invertido, commit => Assert.Contains(commit, log));

        AtoContabilInvalidoException aoInverter =
            Assert.Throws<AtoContabilInvalidoException>(() => ProjecaoSaldos.Reconstruir(invertido));

        Assert.StartsWith("log incompleto para replay", aoInverter.Motivo, StringComparison.Ordinal);

        // Troca das DUAS ULTIMAS posicoes: o replay processa quase todo o log corretamente e so
        // entao recusa. Trocar as duas primeiras tambem falharia, mas falharia no primeiro commit —
        // e um teste assim nao distinguiria "confere a ordem" de "confere so o comeco".
        List<Commit> trocado = [.. log];
        (trocado[^1], trocado[^2]) = (trocado[^2], trocado[^1]);

        AtoContabilInvalidoException aoTrocar =
            Assert.Throws<AtoContabilInvalidoException>(() => ProjecaoSaldos.Reconstruir(trocado));

        Assert.StartsWith("log incompleto para replay", aoTrocar.Motivo, StringComparison.Ordinal);
    }

    /// <summary>
    /// A projecao de respostas e o caso em que embaralhar <b>nao</b> lanca: ela aceita o log inteiro
    /// em qualquer ordem, e por isso a ordem entra na igualdade.
    /// <para>
    /// Invertido, o log produz o mesmo <em>conjunto</em> de E2E respondidos e uma <em>ordem</em>
    /// diferente. Se <c>EhIgualA</c> comparasse so o conjunto, as duas projecoes seriam iguais e o
    /// replay estaria dizendo que a ordem em que a API respondeu nao faz parte do estado — que e
    /// falso: a ordem de producao e o que distingue a primeira resposta congelada, a que o cliente
    /// recebeu, das seguintes.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_ComOLogDeRespostasInvertido_ProduzOMesmoConjuntoEmOrdemDiferente()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();
        IReadOnlyList<EntradaDeIdempotencia> log = cenario.Motor.Pagador.Idempotencia.Log();

        Assert.True(log.Count > 1, $"o log de idempotencia tem {log.Count} entrada(s)");

        ProjecaoRespostas naOrdem = ProjecaoRespostas.Reconstruir(log);
        ProjecaoRespostas invertida = ProjecaoRespostas.Reconstruir(log.Reverse());

        // Mesmo conjunto de respondidos.
        Assert.Equal(naOrdem.Respondidos.Count, invertida.Respondidos.Count);
        Assert.All(naOrdem.Respondidos, e2e => Assert.Contains(e2e, invertida.Respondidos));

        // Ordem diferente, e por isso projecoes diferentes.
        Assert.NotEqual(naOrdem.Ordem[0], invertida.Ordem[0]);
        Assert.False(
            naOrdem.EhIgualA(invertida),
            "a projecao de respostas ficou indiferente a ordem de producao");
    }

    /// <summary>
    /// Dentro de uma transacao, inverter as transicoes faz o replay recusar o historico.
    /// <para>
    /// Entre transacoes a ordem e genuinamente irrelevante — o estado de cada E2E depende so do
    /// proprio historico, e afirmar o contrario seria inventar acoplamento. Dentro de uma, nao: a
    /// sequencia de transicoes <em>e</em> o caminho percorrido na maquina normativa, e percorre-lo
    /// ao contrario nao produz outro estado, produz uma historia que nao existe.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_ComAsTransicoesDeUmaTransacaoInvertidas_RecusaOHistorico()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        HistoricoDeTransacao original = cenario.HistoricoDoPagador(cenario.E2e(Marco.Zerador));
        Assert.True(original.Transicoes.Count > 1, "o historico escolhido tem uma unica transicao");

        HistoricoDeTransacao aoContrario = new(original.E2E, [.. original.Transicoes.Reverse()]);

        Assert.Throws<TransicaoInvalidaException>(() => ProjecaoEstados.Reconstruir([aoContrario]));

        // A ordem ENTRE transacoes, essa sim, nao muda nada: sao historicos independentes.
        IReadOnlyList<HistoricoDeTransacao> historicos = cenario.Motor.Pagador.HistoricosParaReplay();

        Assert.True(
            ProjecaoEstados.Reconstruir(historicos).EhIgualA(ProjecaoEstados.Reconstruir(historicos.Reverse())),
            "o estado de uma transacao passou a depender da ordem das outras");
    }

    // -----------------------------------------------------------------------------------------------
    // Conjunto de chaves
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Conta que zerou continua na projecao reconstruida.
    /// <para>
    /// Comparar apenas valores deixaria passar exatamente o defeito mais provavel de um fold: a
    /// conta que zerou e desapareceu do dicionario reconstruido. Nenhum saldo divergiria — o lado
    /// que perdeu a conta simplesmente nao teria o que comparar —, e um <c>SaldoNatural</c> que
    /// devolvesse zero para conta ausente tornaria "sumiu" indistinguivel de "esta zerada".
    /// </para>
    /// <para>
    /// O roteiro produz o caso de proposito: o ultimo pagamento drena o cliente do pagador ate o
    /// centavo, e com ele zeram o espelho do PSP e a conta PI dele no SPI. Sao tres contas em dois
    /// ledgers diferentes, todas com saldo natural zero e todas obrigatorias na reconstrucao.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_ComContasZeradasPeloRoteiro_MantemAsContasNoConjuntoDeChaves()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        foreach (LedgerSobReplay ledger in cenario.Ledgers)
        {
            (IReadOnlyList<Commit> log, SnapshotSaldos materializado) = ledger.Inspecao.LerConsistente();
            ProjecaoSaldos reconstruida = ProjecaoSaldos.Reconstruir(log);

            Assert.Equal(materializado.Contas.Count, reconstruida.Contas.Count);

            foreach (ContaId conta in materializado.Contas)
            {
                Assert.Contains(conta, reconstruida.Contas);
                Assert.Equal(materializado.Natural(conta), reconstruida.SaldoNatural(conta));
            }
        }

        ProjecaoSaldos doPagador = ProjecaoSaldos.Reconstruir(cenario.Motor.Pagador.Ledger.Consulta.Log());
        ProjecaoSaldos doSpi = ProjecaoSaldos.Reconstruir(cenario.Motor.Spi.Consulta.Log());

        ContaId espelhoDoPagador = cenario.Motor.Pagador.Ledger.EspelhoPi;
        ContaId piDoPagador = ContaId.Pi(CenarioDeReplay.IspbPagador);

        // As tres zeradas: presentes, e presentes com zero.
        foreach ((ProjecaoSaldos projecao, ContaId conta) in new[]
        {
            (doPagador, cenario.ContaPagador),
            (doPagador, espelhoDoPagador),
            (doSpi, piDoPagador),
        })
        {
            Assert.Contains(conta, projecao.Contas);
            Assert.Equal(0L, projecao.SaldoNatural(conta).Centavos);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Log vazio e log recortado
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Sem insumo nao ha estado reconstruido — e nao um estado inventado por default.
    /// <para>
    /// A projecao de log vazio nao tem conta nenhuma, e nao um plano de contas "padrao" com saldos
    /// zerados: o plano de contas tambem e evento do log (<c>Commit.ContasAbertas</c>), e uma
    /// projecao que abrisse contas por conta propria estaria adicionando informacao que o log nao
    /// tem.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_DeLogVazio_ProduzProjecaoSemNenhumaConta()
    {
        ProjecaoSaldos projecao = ProjecaoSaldos.Reconstruir([]);

        Assert.Empty(projecao.Contas);
        Assert.Empty(projecao.Snapshot().Contas);

        // As outras duas concordam: sem insumo, nao ha estado reconstruido — e nao um estado
        // inventado por default.
        Assert.Empty(ProjecaoEstados.Reconstruir([]).Transacoes);
        Assert.Empty(ProjecaoRespostas.Reconstruir([]).Respondidos);
        Assert.Empty(ProjecaoRespostas.Reconstruir([]).Ordem);
    }

    /// <summary>
    /// Um recorte do log nao e um log: o replay recusa em vez de reconstruir saldo errado.
    /// <para>
    /// A armadilha nao e exotica — <c>IConsultaLedger.Log(desdeSequencia)</c> devolve exatamente o
    /// tipo que <c>Reconstruir</c> aceita, e recortar o comeco corta os commits de abertura de conta.
    /// Sem a exigencia de sequencia, o replay veria lancamentos em contas que nunca foram abertas e o
    /// erro chegaria como <c>KeyNotFoundException</c> vinda de um indexador, ou pior: como um saldo
    /// plausivel e errado.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_DeLogRecortadoPelaSequencia_LancaAtoContabilInvalido()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();
        IConsultaLedger consulta = cenario.Motor.Pagador.Ledger.Consulta;

        IReadOnlyList<Commit> recorte = consulta.Log(desdeSequencia: 1);

        Assert.NotEmpty(recorte);
        Assert.Equal(consulta.Log().Count - 1, recorte.Count);

        AtoContabilInvalidoException excecao =
            Assert.Throws<AtoContabilInvalidoException>(() => ProjecaoSaldos.Reconstruir(recorte));

        Assert.StartsWith("log incompleto para replay", excecao.Motivo, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------------
    // ProjecaoEstados reaplica a tabela, e nao copia o destino gravado
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Historico corrompido nao reconstroi: o replay reaplica a maquina normativa.
    /// <para>
    /// Se o replay copiasse o destino que o historico traz gravado, ele concordaria com
    /// <b>qualquer</b> historico — inclusive um em que <c>RECEBIDA</c> vira <c>LIQUIDADA</c> por
    /// validacao local, transicao que o diagrama nao tem. Um replay que concorda com tudo nao e
    /// verificacao, e transcricao: ele existe justamente para nao concordar.
    /// </para>
    /// <para>
    /// Tres formas de corromper, tres recusas: destino que discorda da tabela, par (estado, evento)
    /// que a maquina nao tem, e origem que discorda do estado ja alcancado pelo replay.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_ComHistoricoCorrompido_LancaTransicaoInvalidaEmCadaFormaDeCorrupcao()
    {
        EndToEndId e2e = new GeradorE2eDeterministico(CenarioDeReplay.IspbPagador, RelogioFake.Epoca).Proximo();
        DateTimeOffset em = RelogioFake.Epoca;

        // 1. O par existe na tabela, mas o destino gravado nao e o que ela determina.
        //    (RECEBIDA, ValidacaoLocalOk) leva a VALIDADA — nunca a LIQUIDADA.
        HistoricoDeTransacao destinoMentiroso = new(
            e2e,
            [new TransicaoAplicada(EstadoTransacao.Recebida, TipoEvento.ValidacaoLocalOk, EstadoTransacao.Liquidada, em)]);

        Assert.Throws<TransicaoInvalidaException>(() => ProjecaoEstados.Reconstruir([destinoMentiroso]));

        // 2. O par nao existe na maquina: nenhum pacs.002 chega a uma transacao em RECEBIDA.
        HistoricoDeTransacao parInexistente = new(
            e2e,
            [new TransicaoAplicada(EstadoTransacao.Recebida, TipoEvento.Pacs002Acsc, EstadoTransacao.Liquidada, em)]);

        Assert.Throws<TransicaoInvalidaException>(() => ProjecaoEstados.Reconstruir([parInexistente]));

        // 3. A origem gravada discorda de onde o replay esta. Toda transacao nasce em RECEBIDA:
        //    um historico que comece dizendo "eu estava em ENVIADA_SPI" pulou o caminho de entrada.
        HistoricoDeTransacao origemMentirosa = new(
            e2e,
            [new TransicaoAplicada(EstadoTransacao.EnviadaSpi, TipoEvento.Pacs002Acsc, EstadoTransacao.Liquidada, em)]);

        Assert.Throws<TransicaoInvalidaException>(() => ProjecaoEstados.Reconstruir([origemMentirosa]));

        // E o contraste: o mesmo caminho, agora fiel a tabela, reconstroi sem reclamar.
        HistoricoDeTransacao fiel = new(
            e2e,
            [
                new TransicaoAplicada(EstadoTransacao.Recebida, TipoEvento.ValidacaoLocalOk, EstadoTransacao.Validada, em),
                new TransicaoAplicada(
                    EstadoTransacao.Validada,
                    TipoEvento.DebitoLancadoEPacs008Despachado,
                    EstadoTransacao.EnviadaSpi,
                    em),
                new TransicaoAplicada(EstadoTransacao.EnviadaSpi, TipoEvento.Pacs002Acsc, EstadoTransacao.Liquidada, em),
            ]);

        ProjecaoEstados reconstruida = ProjecaoEstados.Reconstruir([fiel]);

        Assert.Equal(EstadoTransacao.Liquidada, reconstruida.Estado(e2e));
        Assert.Equal(3, reconstruida.QuantidadeDeTransicoes(e2e));
    }

    // -----------------------------------------------------------------------------------------------
    // As projecoes contra o motor vivo
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// O estado reconstruido de cada transacao, nos dois PSPs, e o que o motor devolve pela API.
    /// <para>
    /// A comparacao vale porque os dois lados vem de lugares diferentes: <c>TryTransacao</c> le o
    /// campo <c>Estado</c> da entidade viva, mantido transicao a transicao por <c>Aplicar</c>; a
    /// projecao ignora esse campo e recalcula tudo a partir do historico, reaplicando a tabela.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_DosHistoricosDosDoisPsps_ReproduzOEstadoQueOMotorMantemEmMemoria()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        ProjecaoEstados doPagador = ProjecaoEstados.Reconstruir(cenario.Motor.Pagador.HistoricosParaReplay());

        Assert.NotEmpty(doPagador.Transacoes);

        foreach (EndToEndId e2e in doPagador.Transacoes)
        {
            Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? viva));
            Assert.NotNull(viva);

            Assert.Equal(viva.Estado, doPagador.Estado(e2e));
            Assert.Equal(viva.Historico.Count, doPagador.QuantidadeDeTransicoes(e2e));
        }

        ProjecaoEstados doRecebedor = ProjecaoEstados.Reconstruir(cenario.Motor.Recebedor.HistoricosParaReplay());

        Assert.NotEmpty(doRecebedor.Transacoes);

        foreach (EndToEndId e2e in doRecebedor.Transacoes)
        {
            Assert.True(cenario.Motor.Recebedor.TryDevolucao(e2e, out VistaDaTransacao? viva));
            Assert.NotNull(viva);

            Assert.Equal(viva.Estado, doRecebedor.Estado(e2e));
            Assert.Equal(viva.Historico.Count, doRecebedor.QuantidadeDeTransicoes(e2e));
        }

        // Os desfechos que o roteiro plantou, nomeados: sem isto o laco acima poderia estar
        // comparando um punhado de transacoes todas no mesmo estado.
        Assert.Equal(EstadoTransacao.Devolvida, doPagador.Estado(cenario.E2e(Marco.PagamentoDevolvido)));
        Assert.Equal(EstadoTransacao.Rejeitada, doPagador.Estado(cenario.E2e(Marco.RejeitadoLocalmente)));
        Assert.Equal(EstadoTransacao.Rejeitada, doPagador.Estado(cenario.E2e(Marco.RejeitadoPeloSpi)));
        Assert.Equal(EstadoTransacao.Liquidada, doPagador.Estado(cenario.E2e(Marco.ExpiradoEFechadoPorConsulta)));
        Assert.Equal(EstadoTransacao.Liquidada, doPagador.Estado(cenario.E2e(Marco.Zerador)));
        Assert.Equal(EstadoTransacao.Liquidada, doRecebedor.Estado(cenario.E2e(Marco.Devolucao)));
    }

    /// <summary>
    /// O replay do log de idempotencia reproduz resposta por resposta, na ordem de producao.
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_DoLogDeIdempotencia_ReproduzCadaRespostaCongeladaNaOrdemDeProducao()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();
        IReadOnlyList<EntradaDeIdempotencia> log = cenario.Motor.Pagador.Idempotencia.Log();

        ProjecaoRespostas projecao = ProjecaoRespostas.Reconstruir(log);

        Assert.Equal(log.Count, projecao.Ordem.Count);
        Assert.Equal(cenario.Motor.Pagador.Idempotencia.Quantidade, projecao.Respondidos.Count);

        for (int i = 0; i < log.Count; i++)
        {
            Assert.Equal(log[i].E2E, projecao.Ordem[i]);
        }

        foreach (EndToEndId e2e in projecao.Respondidos)
        {
            Assert.True(cenario.Motor.Pagador.Idempotencia.TryEntrada(e2e, out EntradaDeIdempotencia? viva));
            Assert.NotNull(viva);

            Assert.Equal(viva, projecao.Resposta(e2e));
        }

        // A resposta congelada e um instantaneo, e nao uma projecao viva: a transacao devolvida
        // chegou a DEVOLVIDA muito depois de a API ter respondido ENVIADA_SPI, e o que o replay
        // reconstroi e o que foi respondido.
        EntradaDeIdempotencia devolvida = projecao.Resposta(cenario.E2e(Marco.PagamentoDevolvido));

        Assert.Equal(EstadoTransacao.EnviadaSpi, devolvida.EstadoRespondido);
        Assert.False(devolvida.EstaEmVoo);
    }

    /// <summary>
    /// Depois do replay, reenviar um E2E devolve a mesma resposta de antes.
    /// <para>
    /// Sem esta terceira projecao o replay reconstruiria saldos e estados e ainda assim o motor
    /// responderia diferente: a resposta idempotente e <b>estado</b>, e estado que nao esta em
    /// nenhum log nao sobrevive a uma reconstrucao. O teste compara o que a API devolve com o que a
    /// projecao reconstruiu — nao com o que a API devolveu antes —, senao provaria apenas que o
    /// registro vivo e estavel, que e o gate 4 e nao este.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Pagar_ReenviandoUmE2eDepoisDoReplay_DevolveAMesmaRespostaQueAProjecaoReconstruiu()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        ProjecaoRespostas projecao = ProjecaoRespostas.Reconstruir(cenario.Motor.Pagador.Idempotencia.Log());

        int commitsAntes = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;
        int respostasAntes = cenario.Motor.Pagador.Idempotencia.Log().Count;

        foreach ((Marco marco, long centavos) in new[]
        {
            (Marco.PagamentoDevolvido, CenarioDeReplay.ValorDoPagamento),
            (Marco.RejeitadoLocalmente, CenarioDeReplay.ValorDoPagamento),
            (Marco.Zerador, CenarioDeReplay.ValorZerador),
        })
        {
            EndToEndId e2e = cenario.E2e(marco);
            EntradaDeIdempotencia reconstruida = projecao.Resposta(e2e);

            RespostaDePagamento reenvio = cenario.Reenviar(marco, centavos);

            Assert.Equal(e2e, reenvio.E2E);
            Assert.Equal(reconstruida.EstadoRespondido, reenvio.Estado);
            Assert.Equal(reconstruida.MotivoRespondido, reenvio.Motivo);
        }

        // "Zero lancamento novo" e "zero resposta nova": o reenvio nao apendou em lugar nenhum.
        Assert.Equal(commitsAntes, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(respostasAntes, cenario.Motor.Pagador.Idempotencia.Log().Count);
    }

    /// <summary>
    /// Reivindicacao em voo nao e resposta, e o replay a ignora.
    /// <para>
    /// Ela nunca deveria estar no log — la so entram entradas congeladas —, mas reconstruir uma
    /// resposta que ninguem recebeu seria pior do que ignora-la: o motor reconstruido passaria a
    /// afirmar ao cliente um desfecho que a API nunca chegou a produzir.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Reconstruir_ComEntradaEmVoo_IgnoraAReivindicacaoQueNuncaVirouResposta()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();
        IReadOnlyList<EntradaDeIdempotencia> log = cenario.Motor.Pagador.Idempotencia.Log();

        GeradorE2eDeterministico gerador = new(CenarioDeReplay.IspbPagador, RelogioFake.Epoca, inicio: 900);
        EndToEndId semResposta = gerador.Proximo();

        EntradaDeIdempotencia emVoo = new(
            semResposta,
            ImpressaoDoPedido.De(
                cenario.ContaPagador,
                cenario.ChaveDoRecebedor,
                Valor.DeCentavos(CenarioDeReplay.ValorDoPagamento)),
            EstadoTransacao.Recebida,
            MotivoRespondido: null,
            Em: RelogioFake.Epoca)
        {
            EstaEmVoo = true,
        };

        ProjecaoRespostas comEmVoo = ProjecaoRespostas.Reconstruir([.. log, emVoo]);

        Assert.DoesNotContain(semResposta, comEmVoo.Respondidos);
        Assert.False(comEmVoo.TryResposta(semResposta, out EntradaDeIdempotencia? nenhuma));
        Assert.Null(nenhuma);

        Assert.True(
            comEmVoo.EhIgualA(ProjecaoRespostas.Reconstruir(log)),
            "a entrada em voo mudou a projecao reconstruida");
    }

    // -----------------------------------------------------------------------------------------------
    // Nenhuma projecao le o relogio nem gera identificador
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// As tres projecoes sao funcoes puras do log, verificado por reflexao.
    /// <para>
    /// A regra e do gate: <b>nenhuma projecao consulta <c>IClock</c> nem gera identificador</b>. Se
    /// uma delas precisasse de tempo, o tempo ja esta gravado — em <c>Commit.Em</c>, lido do relogio
    /// no instante do append, e em <c>TransicaoAplicada.Em</c>. Uma projecao que lesse o relogio
    /// produziria resultado diferente a cada execucao sobre o mesmo log, e o gate inteiro deixaria de
    /// significar coisa alguma.
    /// </para>
    /// <para>
    /// A verificacao e estrutural, e nao comportamental, de proposito: um teste que apenas rodasse o
    /// replay duas vezes seria satisfeito por uma projecao que le o relogio e por acaso obtem o mesmo
    /// instante nas duas.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Projecoes_PorReflexao_NaoDeclaramRelogioNemFonteAleatoriaEmNenhumaSuperficie()
    {
        Type[] projecoes = [typeof(ProjecaoSaldos), typeof(ProjecaoEstados), typeof(ProjecaoRespostas)];
        Type[] ambientais = [typeof(IClock), typeof(IFonteAleatoria)];

        const BindingFlags Membros = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        const BindingFlags Publicos = BindingFlags.Public
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type projecao in projecoes)
        {
            FieldInfo[] campos = projecao.GetFields(Membros);
            PropertyInfo[] propriedades = projecao.GetProperties(Membros);
            MethodInfo[] metodos = projecao.GetMethods(Publicos);

            // Guardas contra teste vazio: se a reflexao nao enxergar membro nenhum, os lacos abaixo
            // passam sem verificar coisa alguma.
            Assert.NotEmpty(campos);
            Assert.NotEmpty(metodos);

            foreach (Type ambiental in ambientais)
            {
                foreach (FieldInfo campo in campos)
                {
                    Assert.False(
                        ambiental.IsAssignableFrom(campo.FieldType),
                        $"{projecao.Name} guarda {ambiental.Name} no campo {campo.Name}");
                }

                foreach (PropertyInfo propriedade in propriedades)
                {
                    Assert.False(
                        ambiental.IsAssignableFrom(propriedade.PropertyType),
                        $"{projecao.Name} expoe {ambiental.Name} na propriedade {propriedade.Name}");
                }

                foreach (MethodInfo metodo in metodos)
                {
                    foreach (ParameterInfo parametro in metodo.GetParameters())
                    {
                        Assert.False(
                            ambiental.IsAssignableFrom(parametro.ParameterType),
                            $"{projecao.Name}.{metodo.Name} recebe {ambiental.Name} em '{parametro.Name}'");
                    }
                }

                foreach (ConstructorInfo construtor in projecao.GetConstructors(Publicos))
                {
                    foreach (ParameterInfo parametro in construtor.GetParameters())
                    {
                        Assert.False(
                            ambiental.IsAssignableFrom(parametro.ParameterType),
                            $"o construtor publico de {projecao.Name} recebe {ambiental.Name}");
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Apoio
    // -----------------------------------------------------------------------------------------------

    internal static string Junte(IReadOnlyList<string> diferencas) =>
        diferencas.Count == 0 ? "(nenhuma diferenca listada)" : string.Join(" | ", diferencas);
}

/// <summary>Os desfechos que os roteiros plantam, para que os testes se refiram a eles por nome.</summary>
internal enum Marco
{
    /// <summary>Roteiro rico: pagamento que liquidou e depois foi devolvido. Termina em DEVOLVIDA.</summary>
    PagamentoDevolvido = 1,

    /// <summary>Roteiro rico: chave que o DICT nao conhece. Termina em REJEITADA, sem tocar o ledger.</summary>
    RejeitadoLocalmente = 2,

    /// <summary>Roteiro rico: recusado pelo SPI depois do debito. Termina em REJEITADA, com estorno.</summary>
    RejeitadoPeloSpi = 3,

    /// <summary>Roteiro rico: expirou pela varredura e fechou por consulta de status.</summary>
    ExpiradoEFechadoPorConsulta = 4,

    /// <summary>Roteiro rico: a devolucao, transacao propria do recebedor. Termina em LIQUIDADA.</summary>
    Devolucao = 5,

    /// <summary>Roteiro rico: pagamento final, que zera cliente, espelho e conta PI do pagador.</summary>
    Zerador = 6,

    /// <summary>Roteiro interrompido: unico que completou as setas 1 a 7 antes da interrupcao.</summary>
    LiquidadoAntesDaInterrupcao = 7,

    /// <summary>Roteiro interrompido: debitado, com o <c>pacs.008</c> preso na fila.</summary>
    SemEntregaDoPacs008 = 8,

    /// <summary>Roteiro interrompido: liquidado no SPI, com os <c>pacs.002</c> presos na fila.</summary>
    SemEntregaDoPacs002 = 9,
}

/// <summary>Um dos tres ledgers, com as duas portas que o replay usa.</summary>
internal sealed record LedgerSobReplay(string Nome, IConsultaLedger Consulta, IInspecaoLedger Inspecao);

/// <summary>A projecao de um ledger num ponto da sequencia, com quantos commits existiam ali.</summary>
internal sealed record InstantaneoDeLedger(string Ledger, int Commits, SnapshotSaldos Snapshot);

/// <summary>O quadro dos tres ledgers logo apos uma operacao do roteiro.</summary>
internal sealed record InstantaneoDoMotor(string Operacao, IReadOnlyList<InstantaneoDeLedger> Ledgers);

/// <summary>
/// O motor montado e conduzido por um roteiro rico, com o quadro fotografado apos cada operacao.
/// <para>
/// As fotos existem para o oraculo do prefixo: capturar so o estado final tornaria o gate incapaz de
/// distinguir um fold honesto de um que olha o log inteiro antes de decidir cada valor. Elas sao
/// tiradas com <c>LerConsistente</c>, que devolve log e projecao sob a mesma aquisicao do lock.
/// </para>
/// <para>
/// O roteiro rico e desenhado para exercitar caminhos com consequencias contabeis <em>diferentes</em>:
/// um pagamento que completa, um que morre na validacao local sem tocar o ledger, um que morre no SPI
/// depois do debito e por isso estorna, um que expira e so fecha por consulta, uma devolucao completa,
/// e um ultimo que drena o cliente do pagador ate zerar tres contas em dois ledgers. Um roteiro de
/// pagamentos todos iguais faria todas as comparacoes deste gate passarem por acidente.
/// </para>
/// </summary>
internal sealed class CenarioDeReplay
{
    public const long FundoInicial = 1_000_00;
    public const long ValorDoPagamento = 250_00;
    public const long ValorExpirado = 300_00;

    /// <summary>
    /// O valor menor do roteiro. Serve ao pagamento que o SPI recusa (roteiro rico) e ao que liquida
    /// sem credito (roteiro interrompido) — valores diferentes entre si tornam legivel qual parcela
    /// da diferenca entre conta PI e espelho pertence a qual E2E.
    /// </summary>
    public const long ValorMenor = 100_00;

    /// <summary>
    /// O que sobra na conta do cliente do pagador depois de todo o resto:
    /// <c>1000 - 250 (pago) - 300 (expirado) + 250 (devolvido)</c>. Pagar exatamente isto zera
    /// cliente, espelho e conta PI do pagador de uma vez.
    /// </summary>
    public const long ValorZerador = FundoInicial - ValorDoPagamento - ValorExpirado + ValorDoPagamento;

    public const string NumeroDaContaPagador = "0001";
    public const string NumeroDaContaRecebedor = "0002";

    /// <summary>Conta valida em forma que o recebedor nunca abriu. E o destino que o SPI recusa.</summary>
    public const string NumeroDaContaFantasma = "9999";

    public static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    public static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    private static readonly TimeSpan Limite = new(0, 0, 20);

    private readonly GeradorE2eDeterministico _gerador;
    private readonly Dictionary<Marco, EndToEndId> _marcos = [];
    private readonly List<InstantaneoDoMotor> _instantaneos = [];

    private CenarioDeReplay()
    {
        Relogio = new RelogioFake();

        Motor = MotorMontado.Montar(
            Relogio,
            IspbPagador,
            IspbRecebedor,
            Valor.DeCentavos(FundoInicial),
            NumeroDaContaPagador,
            NumeroDaContaRecebedor,
            new PoliticaDeTimeout(Limite));

        ContaPagador = ContaId.Cliente(Motor.Pagador.Ledger.Id, NumeroDaContaPagador);
        ContaRecebedor = ContaId.Cliente(Motor.Recebedor.Ledger.Id, NumeroDaContaRecebedor);
        ContaFantasma = ContaId.Cliente(Motor.Recebedor.Ledger.Id, NumeroDaContaFantasma);

        ChaveDoRecebedor = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        Motor.Recebedor.RegistrarChave(ChaveDoRecebedor, ContaRecebedor);

        ChaveOrfa = ChavePix.Criar(TipoChave.Email, "ninguem@exemplo.com");

        // Vinculada direto no diretorio, e nao por RegistrarChave: aquele caminho confere que a
        // conta existe no ledger do recebedor e recusaria. O que este vinculo produz e a recusa do
        // SPI, que pergunta PodeCreditar antes de liquidar.
        ChaveFantasma = ChavePix.Criar(TipoChave.Email, "fantasma@exemplo.com");
        Motor.Dict.Vincular(ChaveFantasma, IspbRecebedor, ContaFantasma);

        _gerador = new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca);

        Ledgers =
        [
            new LedgerSobReplay("Psp pagador", Motor.Pagador.Ledger.Consulta, Motor.Pagador.Ledger.Inspecao),
            new LedgerSobReplay("Psp recebedor", Motor.Recebedor.Ledger.Consulta, Motor.Recebedor.Ledger.Inspecao),
            new LedgerSobReplay("Spi", Motor.Spi.Consulta, Motor.Spi.Inspecao),
        ];

        Conciliador = new Conciliador(
            Motor.Spi.Consulta,
            new Dictionary<Ispb, IPspConciliavel>
            {
                [IspbPagador] = Motor.Pagador,
                [IspbRecebedor] = Motor.Recebedor,
            });
    }

    public RelogioFake Relogio { get; }

    public MotorMontado Motor { get; }

    public Conciliador Conciliador { get; }

    public ChavePix ChaveDoRecebedor { get; }

    public ChavePix ChaveOrfa { get; }

    public ChavePix ChaveFantasma { get; }

    public ContaId ContaPagador { get; }

    public ContaId ContaRecebedor { get; }

    public ContaId ContaFantasma { get; }

    public IReadOnlyList<LedgerSobReplay> Ledgers { get; }

    public IReadOnlyList<InstantaneoDoMotor> Instantaneos => _instantaneos;

    /// <summary>
    /// O roteiro rico, com todos os desfechos representados e o motor em quiescencia ao final.
    /// </summary>
    public static CenarioDeReplay Rico()
    {
        CenarioDeReplay cenario = new();
        cenario.Fotografar("motor montado, genesis dos dois lados");

        // 1. Pagamento completo, setas 1 a 7. E ele que sera devolvido no passo 5.
        EndToEndId devolvido = cenario.Pagar(Marco.PagamentoDevolvido, ValorDoPagamento);
        cenario.Fotografar("pagamento debitado e pacs.008 despachado");
        cenario.Motor.Barramento.Drenar();
        cenario.Fotografar("pagamento liquidado no SPI e creditado ao recebedor");

        // 2. Chave que o DICT nao conhece: recusa de negocio, nenhum lancamento em ledger nenhum.
        cenario.Pagar(Marco.RejeitadoLocalmente, ValorDoPagamento, cenario.ChaveOrfa);
        cenario.Fotografar("pagamento recusado na validacao local");

        // 3. Destino que o recebedor nunca abriu: o debito acontece, o SPI recusa e o PSP estorna
        //    por lancamento novo. Dois lancamentos que se cancelam, e nao uma correcao do primeiro.
        cenario.Pagar(Marco.RejeitadoPeloSpi, ValorMenor, cenario.ChaveFantasma);
        cenario.Fotografar("pagamento debitado, a caminho de uma recusa do SPI");
        cenario.Motor.Barramento.Drenar();
        cenario.Fotografar("recusa do SPI aplicada e debito estornado");

        // 4. Expirado: o pacs.008 fica na fila, a varredura expira a transacao, e so entao a fila
        //    anda. O pacs.002 chega atrasado e nao transiciona nada — de EXPIRADA so se sai por
        //    consulta de status.
        cenario.Pagar(Marco.ExpiradoEFechadoPorConsulta, ValorExpirado);
        cenario.Fotografar("pagamento debitado com a mensagem retida na fila");

        cenario.Relogio.Avancar(Limite);
        IReadOnlyCollection<EndToEndId> expiradas = cenario.Motor.Pagador.ExpirarVencidos();
        Assert.Contains(cenario.E2e(Marco.ExpiradoEFechadoPorConsulta), expiradas);
        cenario.Fotografar("varredura de vencidos");

        cenario.Motor.Barramento.Drenar();
        cenario.Fotografar("fila liberada: SPI liquida e o pacs.002 chega atrasado");

        Assert.Equal(
            ResultadoConsulta.Liquidada,
            cenario.Motor.Pagador.ConsultarStatus(cenario.E2e(Marco.ExpiradoEFechadoPorConsulta)));
        cenario.Fotografar("consulta de status fecha a expirada");

        // 5. Devolucao completa: transacao nova, com E2E proprio, originada pelo recebedor.
        VistaDaTransacao devolucao = cenario.Motor.Recebedor.SolicitarDevolucao(
            devolvido,
            Valor.DeCentavos(ValorDoPagamento));

        cenario.Marcar(Marco.Devolucao, devolucao.E2E);
        cenario.Fotografar("devolucao solicitada e debitada no recebedor");

        cenario.Motor.Barramento.Drenar();
        cenario.Fotografar("devolucao liquidada e original marcada como devolvida");

        // 6. O ultimo pagamento leva o cliente do pagador a zero — e com ele o espelho do PSP e a
        //    conta PI dele no SPI. Sao as tres contas que o teste do conjunto de chaves cobra.
        Assert.Equal(ValorZerador, cenario.Motor.Pagador.Ledger.SaldoDoCliente(cenario.ContaPagador).Centavos);

        cenario.Pagar(Marco.Zerador, ValorZerador);
        cenario.Fotografar("pagamento final debitado, zerando o cliente do pagador");
        cenario.Motor.Barramento.Drenar();
        cenario.Fotografar("pagamento final liquidado");

        cenario.ExigirRoteiroRico();
        return cenario;
    }

    /// <summary>
    /// O mesmo motor, com a drenagem interrompida no meio: mensagens ficam pendentes, um pagamento
    /// fica debitado sem liquidacao e outro fica liquidado sem credito.
    /// <para>
    /// "Falha" aqui e o que ela pode ser num sistema in-process: uma entrega que nao aconteceu. Nao
    /// existe barramento paralelo nem espera real — o outbox e drenado explicitamente, entao deixar
    /// de drenar <em>e</em> a interrupcao.
    /// </para>
    /// </summary>
    public static CenarioDeReplay ComDrenagemInterrompida()
    {
        CenarioDeReplay cenario = new();
        cenario.Fotografar("motor montado, genesis dos dois lados");

        // Um que atravessa inteiro, para que o quadro tenha tambem o caso completo.
        cenario.Pagar(Marco.LiquidadoAntesDaInterrupcao, ValorDoPagamento);
        cenario.Motor.Barramento.Drenar();
        cenario.Fotografar("primeiro pagamento completo");

        // A partir daqui ninguem drena. O pacs.008 deste fica na fila: debitado no PSP, invisivel
        // para o SPI.
        cenario.Pagar(Marco.SemEntregaDoPacs008, ValorDoPagamento);
        cenario.Fotografar("segundo pagamento debitado, pacs.008 retido");

        // Deste, a ordem e entregue na mao do SPI e as respostas ficam na fila: liquidado no SPI,
        // sem credito ao cliente do recebedor.
        EndToEndId semResposta = cenario.Pagar(Marco.SemEntregaDoPacs002, ValorMenor);
        cenario.EntregarOrdemAoSpiSemEntregarAResposta(semResposta, ValorMenor);
        cenario.Fotografar("terceiro pagamento liquidado no SPI, pacs.002 retidos");

        cenario.Relogio.Avancar(Limite);
        cenario.Motor.Pagador.ExpirarVencidos();
        cenario.Fotografar("varredura de vencidos com a fila ainda parada");

        // A interrupcao e o ponto do cenario: se a fila estivesse vazia, nao haveria estado
        // intermediario nenhum para o replay ter de reproduzir.
        Assert.True(cenario.Motor.Barramento.Pendentes > 0, "a drenagem nao ficou interrompida");

        return cenario;
    }

    public EndToEndId E2e(Marco marco) =>
        _marcos.TryGetValue(marco, out EndToEndId? e2e)
            ? e2e
            : throw new InvalidOperationException($"o roteiro deste cenario nao produziu o marco {marco}");

    public HistoricoDeTransacao HistoricoDoPagador(EndToEndId e2e) =>
        Motor.Pagador.HistoricosParaReplay().Single(h => h.E2E.Equals(e2e));

    /// <summary>Reenvia um pedido ja respondido, com o payload identico ao original.</summary>
    public RespostaDePagamento Reenviar(Marco marco, long centavos)
    {
        ChavePix chave = marco switch
        {
            Marco.RejeitadoLocalmente => ChaveOrfa,
            Marco.RejeitadoPeloSpi => ChaveFantasma,
            _ => ChaveDoRecebedor,
        };

        return Motor.Pagador.Pagar(new ComandoDePagamento(
            E2e(marco),
            NumeroDaContaPagador,
            chave,
            Valor.DeCentavos(centavos)));
    }

    /// <summary>
    /// Entrega a ordem direto ao SPI, deixando o <c>pacs.002</c> na fila. A impressao coincide com a
    /// que o pagador despachou, entao o dedup do SPI reconhece a mensagem retida como reentrega da
    /// mesma ordem.
    /// </summary>
    public void EntregarOrdemAoSpiSemEntregarAResposta(EndToEndId e2e, long centavos) =>
        Motor.Spi.ReceberPacs008(new Pacs008(
            e2e,
            IspbPagador,
            ContaPagador,
            IspbRecebedor,
            ContaRecebedor,
            Valor.DeCentavos(centavos),
            ChaveDoRecebedor));

    private EndToEndId Pagar(Marco marco, long centavos, ChavePix? chave = null)
    {
        EndToEndId e2e = _gerador.Proximo();
        Marcar(marco, e2e);

        Motor.Pagador.Pagar(new ComandoDePagamento(
            e2e,
            NumeroDaContaPagador,
            chave ?? ChaveDoRecebedor,
            Valor.DeCentavos(centavos)));

        return e2e;
    }

    private void Marcar(Marco marco, EndToEndId e2e) => _marcos[marco] = e2e;

    private void Fotografar(string operacao)
    {
        List<InstantaneoDeLedger> fotos = [];

        foreach (LedgerSobReplay ledger in Ledgers)
        {
            (IReadOnlyList<Commit> log, SnapshotSaldos projecao) = ledger.Inspecao.LerConsistente();
            fotos.Add(new InstantaneoDeLedger(ledger.Nome, log.Count, projecao));
        }

        _instantaneos.Add(new InstantaneoDoMotor(operacao, fotos));
    }

    /// <summary>
    /// Confere que o roteiro entregou de fato os desfechos que promete.
    /// <para>
    /// Sem esta guarda, uma mudanca no motor que fizesse, digamos, a recusa do SPI deixar de
    /// acontecer tornaria todos os testes deste gate silenciosamente mais fracos: eles continuariam
    /// verdes, comparando um cenario empobrecido consigo mesmo.
    /// </para>
    /// </summary>
    private void ExigirRoteiroRico()
    {
        Assert.Equal(0, Motor.Barramento.Pendentes);

        Assert.Equal(EstadoTransacao.Devolvida, Vista(Marco.PagamentoDevolvido).Estado);
        Assert.Equal(EstadoTransacao.Rejeitada, Vista(Marco.RejeitadoLocalmente).Estado);
        Assert.Equal(EstadoTransacao.Rejeitada, Vista(Marco.RejeitadoPeloSpi).Estado);
        Assert.Equal(EstadoTransacao.Liquidada, Vista(Marco.ExpiradoEFechadoPorConsulta).Estado);
        Assert.Equal(EstadoTransacao.Liquidada, Vista(Marco.Zerador).Estado);

        // A rejeicao local nao tocou o ledger; a do SPI tocou duas vezes, e as duas continuam la.
        Assert.False(Motor.Pagador.Ledger.HouveDebito(E2e(Marco.RejeitadoLocalmente)));
        Assert.True(Motor.Pagador.Ledger.HouveDebito(E2e(Marco.RejeitadoPeloSpi)));
        Assert.True(Motor.Pagador.Ledger.HouveEstorno(E2e(Marco.RejeitadoPeloSpi)));

        // A expirada passou mesmo por EXPIRADA, e saiu de la pela aresta estados:13.
        IReadOnlyList<TransicaoAplicada> daExpirada = Vista(Marco.ExpiradoEFechadoPorConsulta).Historico;
        Assert.Contains(daExpirada, t => t.Destino == EstadoTransacao.Expirada);
        Assert.Equal(TipoEvento.ConsultaConfirmaLiquidacao, daExpirada[^1].Evento);

        Assert.True(Motor.Recebedor.TryDevolucao(E2e(Marco.Devolucao), out VistaDaTransacao? devolucao));
        Assert.NotNull(devolucao);
        Assert.Equal(EstadoTransacao.Liquidada, devolucao.Estado);
        Assert.Equal(TipoTransacao.Devolucao, devolucao.Tipo);

        // As tres contas zeradas de proposito.
        Assert.Equal(0L, Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos);
        Assert.Equal(0L, Motor.Pagador.Ledger.SaldoDoEspelho().Centavos);
        Assert.Equal(0L, Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos);

        // E o motor termina em quiescencia: e a linha de base contra a qual o cenario interrompido
        // e comparado.
        Assert.True(Conciliador.Conciliar().EmQuiescencia);
    }

    private VistaDaTransacao Vista(Marco marco)
    {
        Assert.True(Motor.Pagador.TryTransacao(E2e(marco), out VistaDaTransacao? vista));
        Assert.NotNull(vista);
        return vista;
    }
}
