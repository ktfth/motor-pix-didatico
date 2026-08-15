using MotorPix.Conciliacao;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Psp.Nucleo;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// O fecho do gate 8: o motor inteiro reconstruido do log, e o log conferindo com o motor vivo.
/// <para>
/// O criterio do prompt fala em "projecoes" no plural, e este arquivo cobra o plural. Reconstruir so
/// os saldos deixaria de fora duas coisas que tambem sao estado: em que ponto da maquina normativa
/// cada transacao esta, e o que a API respondeu a cada <c>EndToEndId</c>. Um motor que reconstruisse
/// apenas o dinheiro e voltasse a responder diferente ao mesmo reenvio nao teria reconstruido nada —
/// teria recalculado saldos.
/// </para>
/// <para>
/// Os tres testes daqui olham o mesmo quadro de tres angulos: o quadro completo em quiescencia, o
/// quadro visto pela conciliacao (que ja consome o mesmo fold), e o quadro em estado intermediario,
/// com mensagens presas na fila. O ultimo e o que importa de verdade: replay que so vale quando esta
/// tudo em ordem nao vale quando seria preciso.
/// </para>
/// </summary>
public sealed class ReplayDoMotorInteiroTestes
{
    // -----------------------------------------------------------------------------------------------
    // O motor inteiro reconstruido
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Depois do roteiro rico, o quadro reconstruido do log e igual ao quadro vivo, peca por peca.
    /// <para>
    /// Sao seis pecas: os tres ledgers, os estados dos dois PSPs e as respostas congeladas do
    /// pagador. Cada uma tem no motor uma estrutura mantida <em>durante</em> a operacao — saldo bruto
    /// atualizado no append, campo <c>Estado</c> movido por <c>Aplicar</c>, dicionario de respostas
    /// escrito por <c>Congelar</c> — e cada uma e refeita aqui do zero, a partir do log
    /// correspondente, sem olhar para a estrutura viva.
    /// </para>
    /// <para>
    /// E este o sentido de "o ledger e a verdade, o resto e derivado": se o quadro vivo fosse perdido
    /// inteiro, o log o reconstroi; se os dois discordam, o log e quem esta certo.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Replay_DoMotorInteiroAposORoteiroRico_ReproduzOQuadroVivoPecaPorPeca()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        ExigirQuadroReconstruidoIgualAoVivo(cenario);

        // As pecas nomeadas, para que a falha diga qual desfecho se perdeu — e para que o laco
        // acima nao possa passar comparando um quadro empobrecido consigo mesmo.
        ProjecaoSaldos doPagador = ProjecaoSaldos.Reconstruir(cenario.Motor.Pagador.Ledger.Consulta.Log());
        ProjecaoSaldos doRecebedor = ProjecaoSaldos.Reconstruir(cenario.Motor.Recebedor.Ledger.Consulta.Log());
        ProjecaoSaldos doSpi = ProjecaoSaldos.Reconstruir(cenario.Motor.Spi.Consulta.Log());

        Assert.Equal(0L, doPagador.SaldoNatural(cenario.ContaPagador).Centavos);
        Assert.Equal(
            2 * CenarioDeReplay.FundoInicial,
            doRecebedor.SaldoNatural(cenario.ContaRecebedor).Centavos);

        // O pool do SPI e o mesmo do genesis: o replay nao criou nem destruiu dinheiro.
        Assert.Equal(
            2 * CenarioDeReplay.FundoInicial,
            doSpi.SaldoNatural(ContaId.Pi(CenarioDeReplay.IspbPagador)).Centavos
            + doSpi.SaldoNatural(ContaId.Pi(CenarioDeReplay.IspbRecebedor)).Centavos);

        ProjecaoEstados estados = ProjecaoEstados.Reconstruir(cenario.Motor.Pagador.HistoricosParaReplay());

        Assert.Equal(EstadoTransacao.Devolvida, estados.Estado(cenario.E2e(Marco.PagamentoDevolvido)));
        Assert.Equal(EstadoTransacao.Liquidada, estados.Estado(cenario.E2e(Marco.ExpiradoEFechadoPorConsulta)));

        ProjecaoRespostas respostas = ProjecaoRespostas.Reconstruir(cenario.Motor.Pagador.Idempotencia.Log());

        Assert.Equal(
            MotivoRejeicaoLocal.ChaveNaoEncontrada,
            respostas.Resposta(cenario.E2e(Marco.RejeitadoLocalmente)).MotivoRespondido);
    }

    // -----------------------------------------------------------------------------------------------
    // A conciliacao concorda com o replay
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Em quiescencia, a conciliacao e o replay contam a mesma historia: nada em transito.
    /// <para>
    /// O teste fecha o circulo entre os gates 7 e 8. A <see cref="Conciliador"/> ja consome
    /// <see cref="ProjecaoSaldos"/> — o mesmo fold puro que este gate verifica —, entao o que o gate
    /// 8 prova sobre o replay e exatamente o que o gate 7 usa para decidir se ha divergencia. Se o
    /// fold estivesse errado, a conciliacao estaria decidindo sobre numeros errados, e nenhum teste
    /// do gate 7 acusaria: ele compara o relatorio com o que o proprio fold disse.
    /// </para>
    /// <para>
    /// A quiescencia e verificada por duas rotas independentes. A do relatorio vem de uma analise
    /// lancamento a lancamento, por etapa e por <c>EndToEndId</c>. A do replay vem dos saldos
    /// reconstruidos e dos estados reconstruidos, que nao sabem o que e uma "etapa". Duas rotas, um
    /// veredito.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Conciliar_ComOMotorEmQuiescencia_ConcordaComOQueAsProjecoesReconstruidasMostram()
    {
        CenarioDeReplay cenario = CenarioDeReplay.Rico();

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        Assert.True(relatorio.EmQuiescencia);
        Assert.Empty(relatorio.Divergencias);
        Assert.Empty(relatorio.Expiradas);

        ExigirRelatorioCoerenteComAsProjecoes(cenario, relatorio);

        // A leitura pelo replay, escrita aqui sem passar pelo relatorio: conta PI menos espelho e
        // zero para os dois participantes, que e a definicao de "nada em transito".
        Assert.Equal(0L, DiferencaReconstruida(cenario, CenarioDeReplay.IspbPagador));
        Assert.Equal(0L, DiferencaReconstruida(cenario, CenarioDeReplay.IspbRecebedor));

        Assert.Empty(ExpiradasReconstruidas(cenario));
    }

    /// <summary>
    /// Com a drenagem interrompida, o relatorio acusa exatamente o que as projecoes reconstruidas
    /// mostram: duas divergencias explicadas, nenhuma orfa, duas transacoes em <c>EXPIRADA</c>.
    /// <para>
    /// A defasagem entre conta PI e espelho nao e defeito — e a representacao contabil do dinheiro em
    /// transito, e e por isso que o no <c>MATCH</c> existe. O que o teste cobra e que cada centavo
    /// dessa defasagem tenha nome: a soma das divergencias atribuidas a um participante tem de ser
    /// igual a diferenca <em>reconstruida do log</em> dele. O que sobrar dessa subtracao e orfao, e
    /// aqui nao sobra nada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Conciliar_ComADrenagemInterrompida_AcusaAsMesmasDivergenciasQueOReplayExplica()
    {
        CenarioDeReplay cenario = CenarioDeReplay.ComDrenagemInterrompida();

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        Assert.False(relatorio.EmQuiescencia);
        Assert.Empty(relatorio.Orfas);

        ExigirRelatorioCoerenteComAsProjecoes(cenario, relatorio);

        // O que ficou pelo caminho, participante a participante, medido no replay:
        // do pagador, o pagamento debitado cujo pacs.008 nunca saiu da fila;
        // do recebedor, o pagamento que o SPI liquidou e cujo pacs.002 nunca chegou.
        Assert.Equal(
            CenarioDeReplay.ValorDoPagamento,
            DiferencaReconstruida(cenario, CenarioDeReplay.IspbPagador));

        Assert.Equal(
            CenarioDeReplay.ValorMenor,
            DiferencaReconstruida(cenario, CenarioDeReplay.IspbRecebedor));

        DivergenciaPorE2E doPagador = Assert.Single(
            relatorio.Divergencias,
            d => d.Classe == ClasseDeDivergencia.PendenciaDoPagador);

        DivergenciaPorE2E doRecebedor = Assert.Single(
            relatorio.Divergencias,
            d => d.Classe == ClasseDeDivergencia.PendenciaDoRecebedor);

        Assert.Equal(cenario.E2e(Marco.SemEntregaDoPacs008), doPagador.E2E);
        Assert.Equal(cenario.E2e(Marco.SemEntregaDoPacs002), doRecebedor.E2E);

        // E as duas transacoes que a varredura expirou aparecem nas duas leituras.
        Assert.Contains(cenario.E2e(Marco.SemEntregaDoPacs008), relatorio.Expiradas);
        Assert.Contains(cenario.E2e(Marco.SemEntregaDoPacs002), relatorio.Expiradas);
    }

    // -----------------------------------------------------------------------------------------------
    // Replay com o sistema no meio do caminho
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Com mensagens presas na fila, o replay continua exato.
    /// <para>
    /// O replay tem de valer <b>inclusive</b> com o sistema em estado intermediario, e e justamente
    /// nesse estado que ele importa: um motor que so se reconstroi quando tudo esta em ordem nao
    /// serve para nada, porque quando tudo esta em ordem nao ha o que reconstruir. Aqui ha um
    /// pagamento debitado sem liquidacao, um liquidado sem credito, duas transacoes em
    /// <c>EXPIRADA</c> e tres mensagens paradas no barramento — e o quadro reconstruido continua
    /// batendo peca por peca com o vivo.
    /// </para>
    /// <para>
    /// "Falha injetada" aqui e o que ela pode ser num motor in-process e in-memory: uma entrega que
    /// nao aconteceu. O outbox e drenado explicitamente pelo host ou pelo teste, entao deixar de
    /// drenar e a interrupcao — nao ha barramento paralelo nem espera real para quebrar.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "8")]
    public void Replay_ComADrenagemInterrompidaNoMeio_ReproduzOQuadroIntermediarioExatamente()
    {
        CenarioDeReplay cenario = CenarioDeReplay.ComDrenagemInterrompida();

        // O estado intermediario, afirmado antes de qualquer replay: e ele que o teste diz reproduzir.
        Assert.True(cenario.Motor.Barramento.Pendentes > 0);
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(cenario.E2e(Marco.SemEntregaDoPacs008)));
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveCredito(cenario.E2e(Marco.SemEntregaDoPacs002)));

        ExigirQuadroReconstruidoIgualAoVivo(cenario);

        ProjecaoEstados estados = ProjecaoEstados.Reconstruir(cenario.Motor.Pagador.HistoricosParaReplay());

        Assert.Equal(EstadoTransacao.Liquidada, estados.Estado(cenario.E2e(Marco.LiquidadoAntesDaInterrupcao)));
        Assert.Equal(EstadoTransacao.Expirada, estados.Estado(cenario.E2e(Marco.SemEntregaDoPacs008)));
        Assert.Equal(EstadoTransacao.Expirada, estados.Estado(cenario.E2e(Marco.SemEntregaDoPacs002)));

        // O replay reproduz tambem o que a API respondeu no meio do caminho: as tres respostas
        // congeladas continuam sendo ENVIADA_SPI, e nao o estado que as transacoes alcancaram depois.
        ProjecaoRespostas respostas = ProjecaoRespostas.Reconstruir(cenario.Motor.Pagador.Idempotencia.Log());

        foreach (Marco marco in new[]
        {
            Marco.LiquidadoAntesDaInterrupcao,
            Marco.SemEntregaDoPacs008,
            Marco.SemEntregaDoPacs002,
        })
        {
            Assert.Equal(EstadoTransacao.EnviadaSpi, respostas.Resposta(cenario.E2e(marco)).EstadoRespondido);
        }

        // E o prefixo continua valendo com a fila parada: cada foto do roteiro interrompido bate com
        // o replay dos commits que existiam naquele ponto.
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
                    + $"{ledger.Nome} divergiu: "
                    + ReplayDasProjecoesTestes.Junte(prefixo.Diferencas(naquele.Snapshot)));
            }
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Apoio
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Reconstroi as seis pecas do quadro e compara cada uma com a estrutura viva correspondente.
    /// </summary>
    private static void ExigirQuadroReconstruidoIgualAoVivo(CenarioDeReplay cenario)
    {
        // 1 a 3 — os tres ledgers.
        foreach (LedgerSobReplay ledger in cenario.Ledgers)
        {
            (IReadOnlyList<Commit> log, SnapshotSaldos vivo) = ledger.Inspecao.LerConsistente();
            ProjecaoSaldos reconstruida = ProjecaoSaldos.Reconstruir(log);
            SnapshotSaldos fold = reconstruida.Snapshot();

            Assert.True(
                fold.EhIgualA(vivo),
                $"o replay de {ledger.Nome} divergiu do vivo: {ReplayDasProjecoesTestes.Junte(fold.Diferencas(vivo))}");

            Assert.Equal(vivo.Contas.Count, reconstruida.Contas.Count);

            foreach (ContaId conta in vivo.Contas)
            {
                // Terceira leitura, por outro caminho ainda: a porta de inspecao do ledger, que
                // consulta o saldo bruto e converte por Conta.NaturalDe. Comparar so os dois
                // snapshots deixaria de fora o acessor que o resto do motor de fato usa.
                Assert.Equal(ledger.Inspecao.SaldoNatural(conta), reconstruida.SaldoNatural(conta));
                Assert.Equal(vivo.MinimoNatural(conta), fold.MinimoNatural(conta));
            }
        }

        // 4 e 5 — os estados das transacoes dos dois PSPs.
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

        foreach (EndToEndId e2e in doRecebedor.Transacoes)
        {
            Assert.True(cenario.Motor.Recebedor.TryDevolucao(e2e, out VistaDaTransacao? viva));
            Assert.NotNull(viva);
            Assert.Equal(viva.Estado, doRecebedor.Estado(e2e));
            Assert.Equal(viva.Historico.Count, doRecebedor.QuantidadeDeTransicoes(e2e));
        }

        // 6 — as respostas congeladas da API.
        ProjecaoRespostas respostas = ProjecaoRespostas.Reconstruir(cenario.Motor.Pagador.Idempotencia.Log());

        Assert.Equal(cenario.Motor.Pagador.Idempotencia.Quantidade, respostas.Respondidos.Count);

        foreach (EndToEndId e2e in respostas.Respondidos)
        {
            Assert.True(cenario.Motor.Pagador.Idempotencia.TryEntrada(e2e, out EntradaDeIdempotencia? viva));
            Assert.NotNull(viva);
            Assert.Equal(viva, respostas.Resposta(e2e));
        }
    }

    /// <summary>
    /// Confere o relatorio contra as projecoes reconstruidas, sem reimplementar a classificacao.
    /// <para>
    /// Sao tres identidades: a diferenca por participante do relatorio bate com a diferenca lida do
    /// motor vivo <em>e</em> com a reconstruida do log; a soma das divergencias explicadas de cada
    /// participante bate com essa diferenca (o que sobra e orfao, por definicao); e o conjunto de
    /// <c>EXPIRADA</c> do relatorio bate com o que a projecao de estados reconstroi.
    /// </para>
    /// </summary>
    private static void ExigirRelatorioCoerenteComAsProjecoes(
        CenarioDeReplay cenario,
        RelatorioDeConciliacao relatorio)
    {
        foreach (Ispb ispb in new[] { CenarioDeReplay.IspbPagador, CenarioDeReplay.IspbRecebedor })
        {
            long doRelatorio = relatorio.DiferencaPorParticipante[ispb].Centavos;

            // Leitura viva: nao passa por ProjecaoSaldos em ponto nenhum, e por isso e a metade
            // nao circular da comparacao — o Conciliador consome o fold, entao confronta-lo apenas
            // com o fold provaria somente que ele sabe somar duas vezes igual.
            Assert.Equal(doRelatorio, DiferencaViva(cenario, ispb));
            Assert.Equal(doRelatorio, DiferencaReconstruida(cenario, ispb));

            long explicado = relatorio.Divergencias
                .Where(d => d.Participante.Equals(ispb) && d.Classe != ClasseDeDivergencia.Orfa)
                .Sum(d => d.Valor.Centavos);

            long orfao = relatorio.Divergencias
                .Where(d => d.Participante.Equals(ispb) && d.Classe == ClasseDeDivergencia.Orfa)
                .Sum(d => d.Valor.Centavos);

            Assert.Equal(doRelatorio, explicado);
            Assert.Equal(0L, orfao);
        }

        IReadOnlyCollection<EndToEndId> reconstruidas = ExpiradasReconstruidas(cenario);

        Assert.Equal(reconstruidas.Count, relatorio.Expiradas.Count);
        Assert.All(reconstruidas, e2e => Assert.Contains(e2e, relatorio.Expiradas));
    }

    /// <summary>Conta PI menos espelho, lido do motor vivo.</summary>
    private static long DiferencaViva(CenarioDeReplay cenario, Ispb ispb)
    {
        long pi = cenario.Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(ispb)).Centavos;

        long espelho = ispb.Equals(CenarioDeReplay.IspbPagador)
            ? cenario.Motor.Pagador.Ledger.SaldoDoEspelho().Centavos
            : cenario.Motor.Recebedor.Ledger.SaldoDoEspelho().Centavos;

        return pi - espelho;
    }

    /// <summary>Conta PI menos espelho, reconstruido do log dos dois ledgers envolvidos.</summary>
    private static long DiferencaReconstruida(CenarioDeReplay cenario, Ispb ispb)
    {
        IConsultaLedger doPsp = ispb.Equals(CenarioDeReplay.IspbPagador)
            ? cenario.Motor.Pagador.Ledger.Consulta
            : cenario.Motor.Recebedor.Ledger.Consulta;

        long pi = ProjecaoSaldos
            .Reconstruir(cenario.Motor.Spi.Consulta.Log())
            .SaldoNatural(ContaId.Pi(ispb))
            .Centavos;

        long espelho = ProjecaoSaldos
            .Reconstruir(doPsp.Log())
            .SaldoNatural(ContaId.EspelhoPi(LedgerId.Psp(ispb)))
            .Centavos;

        return pi - espelho;
    }

    /// <summary>E2E que a projecao de estados reconstroi em <c>EXPIRADA</c>, nos dois PSPs.</summary>
    private static IReadOnlyCollection<EndToEndId> ExpiradasReconstruidas(CenarioDeReplay cenario)
    {
        List<EndToEndId> expiradas = [];

        foreach (IReadOnlyList<HistoricoDeTransacao> historicos in new[]
        {
            cenario.Motor.Pagador.HistoricosParaReplay(),
            cenario.Motor.Recebedor.HistoricosParaReplay(),
        })
        {
            ProjecaoEstados projecao = ProjecaoEstados.Reconstruir(historicos);

            expiradas.AddRange(projecao.Transacoes.Where(e2e => projecao.Estado(e2e) == EstadoTransacao.Expirada));
        }

        return expiradas;
    }
}
