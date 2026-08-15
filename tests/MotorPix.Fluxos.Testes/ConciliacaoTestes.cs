using MotorPix.Composicao;
using MotorPix.Conciliacao;
using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Gate 7: o no <c>MATCH</c> do diagrama de arquitetura — "Ledgers PSP x Ledger SPI, fecha os casos
/// EXPIRADA" — e as duas arestas pontilhadas que a ADR-013 acrescentou ao desenho.
/// <para>
/// A decisao D5 em uma frase: a conciliacao <b>detecta e pergunta</b>; quem decide continua sendo o
/// SPI. As duas arestas novas (<c>MATCH -.-&gt; VAL</c> e <c>MATCH -.-&gt; SM_R</c>) nao criam
/// transicao nenhuma: a transicao acontece pelas arestas que ja existiam, <c>estados:13</c> e
/// <c>estados:14</c>. Por isso <see cref="IPspConciliavel"/> nao oferece forma alguma de mover uma
/// transacao — decidir a partir de evidencia indireta e local e exatamente o que o invariante 6
/// proibe.
/// </para>
/// <para>
/// Toda a suite le o mesmo insumo: a defasagem entre a conta PI, no ledger do SPI, e o espelho dela
/// dentro do ledger do PSP. Essa defasagem <em>e</em> o dinheiro em transito enquanto a operacao nao
/// completa, e volta a zero quando ela completa. O que a conciliacao faz e dar nome a cada centavo
/// dessa diferenca; o que nao recebe nome vira alarme.
/// </para>
/// </summary>
public sealed class ConciliacaoTestes
{
    private const long FundoInicial = 1_000_00;
    private const long ValorDoPagamento = 250_00;

    /// <summary>Credito aplicado no recebedor sem que o SPI tenha movido nada. E a orfa deliberada.</summary>
    private const long CreditoSemLastro = 77_00;

    private static readonly TimeSpan Limite = new(0, 0, 20);

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // -------------------------------------------------------------------------------------------
    // Quiescencia: o estado em que nao ha nada a conciliar
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Depois do caminho feliz completo, os livros coincidem.
    /// <para>
    /// Em quiescencia o espelho e a conta PI de cada participante valem o mesmo, e a diferenca e
    /// zero dos dois lados. Durante o voo eles <em>nao</em> coincidem — e isso nao e defeito nem
    /// atraso de sincronizacao: a defasagem e a representacao contabil do dinheiro em transito. O
    /// pagador ja lancou a saida e o SPI ainda nao liquidou; enquanto isso o valor existe em
    /// exatamente um lugar, e a diferenca diz qual e ele.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Conciliar_DepoisDoCaminhoFeliz_NaoAcusaNadaEFicaEmQuiescencia()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.PagarELiquidar(ValorDoPagamento);

        // Pre-condicao do cenario, e nao objeto do teste: as setas 1 a 7 fecharam.
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Transacao(e2e).Estado);
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        Assert.Empty(relatorio.Divergencias);
        Assert.Empty(relatorio.Orfas);
        Assert.Empty(relatorio.Expiradas);
        Assert.Empty(relatorio.AConsultar);
        Assert.Empty(relatorio.ACreditar);

        // Espelho e conta PI coincidem nos dois participantes: nada em transito, diferenca zero.
        Assert.Equal(0L, relatorio.DiferencaPorParticipante[IspbPagador].Centavos);
        Assert.Equal(0L, relatorio.DiferencaPorParticipante[IspbRecebedor].Centavos);
        Assert.Equal(cenario.SaldoPiPagador, cenario.SaldoEspelhoPagador);
        Assert.Equal(cenario.SaldoPiRecebedor, cenario.SaldoEspelhoRecebedor);

        Assert.True(relatorio.EmQuiescencia);
    }

    // -------------------------------------------------------------------------------------------
    // Divergencia explicada: pendencia do pagador
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// O cliente foi debitado e o SPI nao liquidou: o dinheiro saiu e nao se sabe se chegou.
    /// <para>
    /// O ponto do teste nao e achar a divergencia — e nao acusar alarme por ela. Uma divergencia com
    /// E2E, valor e participante e dinheiro em transito <b>explicado</b>: ha um lancamento que diz
    /// onde ele esta e uma pergunta que pode ser feita ao SPI a respeito dele. Alarme e o que sobra
    /// depois de todas as explicacoes, e aqui nao sobra nada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Conciliar_ComPagamentoDebitadoENaoLiquidado_AcusaPendenciaDoPagadorSemNenhumaOrfa()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        // Pagar sem drenar: o pacs.008 fica na fila e o SPI nunca ve a ordem. "A mensagem nao
        // chegou" nao e uma espera que falhou, e uma drenagem que nao aconteceu.
        Assert.Equal(EstadoTransacao.EnviadaSpi, cenario.Pagar(e2e, ValorDoPagamento).Estado);
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        List<DivergenciaPorE2E> pendencias =
            [.. relatorio.Divergencias.Where(d => d.Classe == ClasseDeDivergencia.PendenciaDoPagador)];

        DivergenciaPorE2E pendencia = Assert.Single(pendencias);

        Assert.Equal(e2e, pendencia.E2E);
        Assert.Equal(IspbPagador, pendencia.Participante);
        Assert.Equal(ValorDoPagamento, pendencia.Valor.Centavos);

        // A diferenca do pagador e exatamente o valor em transito: o espelho ja caiu (seta 3) e a
        // conta PI ainda nao (seta 5).
        Assert.Equal(ValorDoPagamento, relatorio.DiferencaPorParticipante[IspbPagador].Centavos);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoEspelhoPagador);

        // O recebedor nao participa desta divergencia: para ele nao ha nada em voo.
        Assert.Equal(0L, relatorio.DiferencaPorParticipante[IspbRecebedor].Centavos);

        // A conta que separa "explicada" de "alarme": a diferenca do participante tem de ser
        // exatamente a soma das divergencias atribuidas a ele. O que sobrar dessa subtracao e orfao,
        // e aqui a subtracao fecha em zero.
        Assert.Empty(relatorio.Orfas);

        // Explicada e fechavel: e este o E2E que a aresta MATCH -.-> VAL vai reapresentar ao SPI.
        Assert.Contains(e2e, relatorio.AConsultar);
        Assert.False(relatorio.EmQuiescencia);
    }

    // -------------------------------------------------------------------------------------------
    // Divergencia explicada: pendencia do recebedor
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// O SPI liquidou e o credito ao cliente nao aconteceu: o dinheiro chegou e nao foi entregue.
    /// <para>
    /// O cenario e montado entregando o <c>pacs.008</c> na mao do SPI e deixando as respostas na
    /// fila. E o mesmo desenho do gate 5, e continua sendo a unica forma honesta de dizer "a
    /// mensagem se perdeu" sem inventar um barramento paralelo.
    /// </para>
    /// <para>
    /// Esta e a divergencia que a aresta <c>MATCH -.-&gt; SM_R</c> atende: o credito e reprocessavel,
    /// e a idempotencia dele e a chave do ledger — nao uma checagem de estado.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Conciliar_ComLiquidacaoSemCreditoAoCliente_AcusaPendenciaDoRecebedorSemNenhumaOrfa()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e, ValorDoPagamento);
        cenario.EntregarOrdemAoSpiSemEntregarAResposta(e2e, ValorDoPagamento);

        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Spi.ConsultarStatus(e2e));
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveCredito(e2e));

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        DivergenciaPorE2E pendencia = Assert.Single(relatorio.Divergencias);

        Assert.Equal(ClasseDeDivergencia.PendenciaDoRecebedor, pendencia.Classe);
        Assert.Equal(e2e, pendencia.E2E);
        Assert.Equal(IspbRecebedor, pendencia.Participante);
        Assert.Equal(ValorDoPagamento, pendencia.Valor.Centavos);

        // A conta PI do recebedor ja subiu e o espelho dele nao: a diferenca e o valor parado no
        // SPI, a caminho do cliente.
        Assert.Equal(ValorDoPagamento, relatorio.DiferencaPorParticipante[IspbRecebedor].Centavos);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoRecebedor);

        // Do lado do pagador tudo fechou: ele debitou e o SPI liquidou.
        Assert.Equal(0L, relatorio.DiferencaPorParticipante[IspbPagador].Centavos);

        Assert.Empty(relatorio.Orfas);
        Assert.Contains(e2e, relatorio.ACreditar);

        // Nao e caso de consulta: o SPI ja respondeu, e a resposta dele nao esta em duvida.
        Assert.Empty(relatorio.AConsultar);
        Assert.False(relatorio.EmQuiescencia);
    }

    // -------------------------------------------------------------------------------------------
    // Divergencia orfa: o alarme
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Uma diferenca que nenhum E2E explica.
    /// <para>
    /// Orfa e dinheiro criado ou destruido: existe saldo de um lado sem lancamento do outro que o
    /// justifique. Ela <b>nao</b> e fechavel por consulta — nao ha ao SPI o que perguntar, porque
    /// nao existe ordem correspondente —, e por isso nao aparece em <c>AConsultar</c>. Nenhuma
    /// automacao a resolve: ela existe para ser vista.
    /// </para>
    /// <para>
    /// O credito sem lastro e a forma mais curta de produzi-la, e nao um artificio de teste: e
    /// exatamente o que acontece quando um <c>pacs.002</c> chega descrevendo uma liquidacao que o
    /// ledger do SPI nunca gravou. Repare que a soma por ledger continua fechando em zero dos dois
    /// lados — as partidas dobradas nao acusam nada aqui, e e por isso que a conciliacao precisa
    /// existir.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Conciliar_ComCreditoSemLastroNoRecebedor_ClassificaComoOrfaEForaDeAConsultar()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId fantasma = cenario.ProximoE2e();

        cenario.CreditarSemLastroNoRecebedor(fantasma, CreditoSemLastro);

        Assert.True(cenario.Motor.Recebedor.Ledger.HouveCredito(fantasma));
        Assert.False(cenario.Motor.Spi.TryRespostaDe(fantasma, out Pacs002? nenhuma));
        Assert.Null(nenhuma);

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        DivergenciaPorE2E orfa = Assert.Single(relatorio.Orfas);

        Assert.Equal(ClasseDeDivergencia.Orfa, orfa.Classe);
        Assert.Equal(IspbRecebedor, orfa.Participante);
        Assert.Equal(CreditoSemLastro, orfa.Valor.Centavos);
        Assert.NotEmpty(relatorio.Orfas);

        // O espelho do recebedor subiu sem que a conta PI dele subisse: dinheiro que so existe nos
        // livros do participante.
        Assert.Equal(FundoInicial + CreditoSemLastro, cenario.SaldoEspelhoRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
        Assert.Equal(-CreditoSemLastro, relatorio.DiferencaPorParticipante[IspbRecebedor].Centavos);

        // O que o relatorio NAO faz com ela: nao pergunta ao SPI e nao manda reprocessar credito.
        Assert.DoesNotContain(orfa.E2E, relatorio.AConsultar);
        Assert.DoesNotContain(orfa.E2E, relatorio.ACreditar);
        Assert.DoesNotContain(fantasma, relatorio.AConsultar);
        Assert.Empty(relatorio.Expiradas);
        Assert.False(relatorio.EmQuiescencia);

        // E ela sobrevive ao ciclo automatico: estabilizar nao e resolver.
        RelatorioDeConciliacao depois = cenario.Conciliador.ConciliarAteEstabilizar();
        Assert.NotEmpty(depois.Orfas);
        Assert.False(depois.EmQuiescencia);
    }

    // -------------------------------------------------------------------------------------------
    // O criterio de aceite do gate
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// "Apos consultar e conciliar ate estabilizar, nenhuma transacao permanece em <c>EXPIRADA</c>."
    /// <para>
    /// Este e o criterio que a ADR-013 torna executavel, e o unico motivo pelo qual o desvio de
    /// diagrama foi proposto: com a alternativa "so reportar", a frase "fecha os casos EXPIRADA" nao
    /// seria verificavel pela API publica — alguem de fora do motor teria de agir sobre o relatorio
    /// para que qualquer coisa fechasse.
    /// </para>
    /// <para>
    /// Tres transacoes com desfechos diferentes: uma que fechou sozinha, uma cuja ordem chegou ao SPI
    /// e cuja resposta se perdeu, e uma cujo <c>pacs.002</c> so foi entregue depois de a varredura
    /// ja ter expirado a transacao. As duas ultimas ficam em <c>EXPIRADA</c>, e nenhuma delas sai de
    /// la por suposicao: saem porque o SPI foi perguntado.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void ConciliarAteEstabilizar_ComTransacoesDeixadasPeloCaminho_NaoDeixaNenhumaEmExpirada()
    {
        Cenario cenario = Cenario.Montar();

        // Uma que fecha sozinha, pelas setas 1 a 7.
        EndToEndId completa = cenario.PagarELiquidar(ValorDoPagamento);

        // Uma cuja ordem o SPI recebeu na mao e cuja resposta ficou na fila.
        EndToEndId semResposta = cenario.ProximoE2e();
        cenario.Pagar(semResposta, ValorDoPagamento);
        cenario.EntregarOrdemAoSpiSemEntregarAResposta(semResposta, ValorDoPagamento);

        // Uma cujo pacs.008 ainda nem saiu da fila.
        EndToEndId semEntrega = cenario.ProximoE2e();
        cenario.Pagar(semEntrega, ValorDoPagamento);

        IReadOnlyCollection<EndToEndId> expiradas = cenario.ExpirarPorVarredura();

        Assert.Contains(semResposta, expiradas);
        Assert.Contains(semEntrega, expiradas);
        Assert.DoesNotContain(completa, expiradas);

        // Agora a fila anda: o pacs.008 retido chega ao SPI e os pacs.002 chegam a transacoes que ja
        // estao em EXPIRADA. Nenhum deles transiciona coisa alguma — o invariante 6 diz que de
        // EXPIRADA so se sai por consulta —, mas todos registram que ha o que consultar.
        cenario.Motor.Barramento.Drenar();

        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);
        Assert.Contains(semResposta, cenario.Motor.Pagador.Pacs002Atrasados);
        Assert.Contains(semEntrega, cenario.Motor.Pagador.Pacs002Atrasados);
        Assert.Equal(EstadoTransacao.Expirada, cenario.Transacao(semResposta).Estado);
        Assert.Equal(EstadoTransacao.Expirada, cenario.Transacao(semEntrega).Estado);

        RelatorioDeConciliacao relatorio = cenario.Conciliador.ConciliarAteEstabilizar();

        // O criterio, medido nos dois PSPs e nao apenas no relatorio: quem responde por "esta em
        // EXPIRADA" e o proprio PSP.
        Assert.Empty(cenario.Motor.Pagador.Expiradas);
        Assert.Empty(cenario.Motor.Recebedor.Expiradas);
        Assert.Empty(relatorio.Expiradas);

        // E elas fecharam pela aresta estados:13, que ja existia — a conciliacao nao inventou saida.
        foreach (EndToEndId e2e in new[] { semResposta, semEntrega })
        {
            VistaDaTransacao fechada = cenario.Transacao(e2e);
            Assert.Equal(EstadoTransacao.Liquidada, fechada.Estado);

            TransicaoAplicada ultima = fechada.Historico[^1];
            Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
            Assert.Equal(TipoEvento.ConsultaConfirmaLiquidacao, ultima.Evento);
            Assert.Equal(EstadoTransacao.Liquidada, ultima.Destino);
        }

        Assert.Empty(cenario.Motor.Pagador.Pacs002Atrasados);
        Assert.Empty(cenario.Motor.Pagador.Divergencias);
        Assert.Empty(cenario.Motor.Recebedor.Divergencias);

        // Com tudo fechado, os livros voltam a coincidir e o relatorio final esta em quiescencia.
        Assert.Empty(relatorio.Divergencias);
        Assert.Equal(0L, relatorio.DiferencaPorParticipante[IspbPagador].Centavos);
        Assert.Equal(0L, relatorio.DiferencaPorParticipante[IspbRecebedor].Centavos);
        Assert.True(relatorio.EmQuiescencia);

        Assert.Equal(FundoInicial - (3 * ValorDoPagamento), cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial + (3 * ValorDoPagamento), cenario.SaldoDoClienteRecebedor);
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Transacao(completa).Estado);
    }

    // -------------------------------------------------------------------------------------------
    // O que a consulta nao fecha
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Consulta indeterminada deixa a divergencia aberta, e isso e correto.
    /// <para>
    /// O <c>pacs.008</c> nunca chegou ao SPI — que e justamente o caso que produz <c>EXPIRADA</c> —,
    /// entao a resposta honesta e "nao sei". A conciliacao fecha <b>reapresentando a pergunta</b>,
    /// nao respondendo por conta propria: e este o caso que a nota do diagrama de estados entrega a
    /// ela ao dizer "o que a consulta nao fechar, a conciliacao fecha".
    /// </para>
    /// <para>
    /// Se um relatorio nao-quiescente fosse considerado falha do gate, a saida seria justamente a
    /// que o invariante 6 proibe: concluir por evidencia local. Permanecer aberto e o desfecho
    /// certo.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Fechar_QuandoAConsultaRespondeIndeterminada_MantemAExpiradaEForaDeQuiescencia()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e, ValorDoPagamento);
        Assert.Equal(e2e, Assert.Single(cenario.ExpirarPorVarredura()));

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();

        Assert.Contains(e2e, relatorio.Expiradas);
        Assert.Contains(e2e, relatorio.AConsultar);

        int historicoAntes = cenario.Transacao(e2e).Historico.Count;

        // Uma pergunta emitida — e uma so, mesmo que o E2E apareca como expirado e como pendencia
        // do pagador ao mesmo tempo.
        Assert.Equal(1, cenario.Conciliador.Fechar(relatorio));

        // A resposta do SPI: nao ha o que confirmar nem o que negar.
        Assert.Equal(ResultadoConsulta.Indeterminada, cenario.Motor.Spi.ConsultarStatus(e2e));

        VistaDaTransacao depois = cenario.Transacao(e2e);
        Assert.Equal(EstadoTransacao.Expirada, depois.Estado);
        Assert.Equal(historicoAntes, depois.Historico.Count);
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(e2e));

        RelatorioDeConciliacao reconciliado = cenario.Conciliador.Conciliar();

        Assert.Contains(e2e, reconciliado.Expiradas);
        Assert.False(reconciliado.EmQuiescencia);
        Assert.Contains(e2e, cenario.Motor.Pagador.Expiradas);
    }

    /// <summary>
    /// <c>Fechar</c> nao move dinheiro por conta propria.
    /// <para>
    /// Num cenario indeterminado, a passada de fechamento inteira nao produz um unico lancamento em
    /// nenhum dos tres ledgers. E o teste que impede a tentacao mais plausivel do gate: olhar o
    /// espelho, olhar a conta PI, concluir que "nao liquidou" e estornar. Decidir por evidencia
    /// local e exatamente o que o invariante 6 proibe — o SPI e a autoridade, e esta a uma chamada
    /// de distancia.
    /// </para>
    /// <para>
    /// A comparacao e do conjunto de chaves de idempotencia, e nao da contagem de commits: um
    /// estorno indevido entraria como commit novo com chave nova, e a contagem sozinha nao diria
    /// qual apareceu.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Fechar_NumCenarioIndeterminado_NaoAcrescentaLancamentoEmNenhumDosTresLedgers()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e, ValorDoPagamento);
        Assert.Single(cenario.ExpirarPorVarredura());

        string pagadorAntes = RetratoDoLedger(cenario.Motor.Pagador.Ledger.Consulta);
        string recebedorAntes = RetratoDoLedger(cenario.Motor.Recebedor.Ledger.Consulta);
        string spiAntes = RetratoDoLedger(cenario.Motor.Spi.Consulta);

        RelatorioDeConciliacao relatorio = cenario.Conciliador.Conciliar();
        Assert.Equal(1, cenario.Conciliador.Fechar(relatorio));

        Assert.Equal(pagadorAntes, RetratoDoLedger(cenario.Motor.Pagador.Ledger.Consulta));
        Assert.Equal(recebedorAntes, RetratoDoLedger(cenario.Motor.Recebedor.Ledger.Consulta));
        Assert.Equal(spiAntes, RetratoDoLedger(cenario.Motor.Spi.Consulta));

        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(EstadoTransacao.Expirada, cenario.Transacao(e2e).Estado);
    }

    /// <summary>
    /// O laco de estabilizacao para, mesmo quando a consulta nao fecha nada.
    /// <para>
    /// O limite de rodadas nao e otimizacao: e a diferenca entre "estabilizou" e "esta oscilando".
    /// Uma divergencia que a consulta responde indeterminada continua sendo consultavel na rodada
    /// seguinte, entao sem o limite o laco giraria para sempre — reapresentando a mesma pergunta a
    /// quem ja respondeu que nao sabe.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void ConciliarAteEstabilizar_QuandoAConsultaNaoFechaNada_ParaNoLimiteDeRodadasSemMoverNada()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e, ValorDoPagamento);
        Assert.Single(cenario.ExpirarPorVarredura());

        string pagadorAntes = RetratoDoLedger(cenario.Motor.Pagador.Ledger.Consulta);
        string spiAntes = RetratoDoLedger(cenario.Motor.Spi.Consulta);
        int historicoAntes = cenario.Transacao(e2e).Historico.Count;

        RelatorioDeConciliacao comLimiteCurto = cenario.Conciliador.ConciliarAteEstabilizar(1);

        Assert.Contains(e2e, comLimiteCurto.Expiradas);
        Assert.False(comLimiteCurto.EmQuiescencia);

        // E com o limite padrao o desfecho e o mesmo: rodar mais vezes nao produz informacao nova.
        RelatorioDeConciliacao comLimitePadrao = cenario.Conciliador.ConciliarAteEstabilizar();

        Assert.Contains(e2e, comLimitePadrao.Expiradas);
        Assert.False(comLimitePadrao.EmQuiescencia);

        Assert.Equal(pagadorAntes, RetratoDoLedger(cenario.Motor.Pagador.Ledger.Consulta));
        Assert.Equal(spiAntes, RetratoDoLedger(cenario.Motor.Spi.Consulta));
        Assert.Equal(historicoAntes, cenario.Transacao(e2e).Historico.Count);
        Assert.Equal(EstadoTransacao.Expirada, cenario.Transacao(e2e).Estado);

        // Zero rodada nao e "nao fazer nada": e pedido sem sentido, e recusado como tal.
        Assert.Throws<ArgumentOutOfRangeException>(() => cenario.Conciliador.ConciliarAteEstabilizar(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cenario.Conciliador.ConciliarAteEstabilizar(-1));
    }

    // -------------------------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Todas as chaves de idempotencia gravadas num ledger, na ordem do log, ja concatenadas.
    /// Uma unica string para que a falha do teste mostre o que apareceu, e nao apenas que dois
    /// numeros diferem.
    /// </summary>
    private static string RetratoDoLedger(IConsultaLedger consulta)
    {
        List<string> chaves = [];

        foreach (Commit commit in consulta.Log())
        {
            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                chaves.Add(lancamento.Chave.Texto);
            }
        }

        return string.Join(" | ", chaves);
    }

    /// <summary>
    /// Motor montado com os dois PSPs registrados na conciliacao, exatamente como um host faria:
    /// pela porta de leitura do ledger do SPI e por <see cref="IPspConciliavel"/>.
    /// </summary>
    private sealed class Cenario
    {
        private const string NumeroDaContaPagador = "0001";
        private const string NumeroDaContaRecebedor = "0002";

        private readonly GeradorE2eDeterministico _gerador;

        private Cenario(RelogioFake relogio, MotorMontado motor, ChavePix chave)
        {
            Relogio = relogio;
            Motor = motor;
            Chave = chave;

            ContaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, NumeroDaContaPagador);
            ContaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, NumeroDaContaRecebedor);

            _gerador = new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca);

            // A conciliacao recebe o que o desenho lhe da: leitura dos ledgers e a porta de
            // consulta dos PSPs. Nenhum direito de escrever, nenhuma forma de transicionar.
            Conciliador = new Conciliador(
                motor.Spi.Consulta,
                new Dictionary<Ispb, IPspConciliavel>
                {
                    [IspbPagador] = motor.Pagador,
                    [IspbRecebedor] = motor.Recebedor,
                });
        }

        public RelogioFake Relogio { get; }

        public MotorMontado Motor { get; }

        public ChavePix Chave { get; }

        public ContaId ContaPagador { get; }

        public ContaId ContaRecebedor { get; }

        public Conciliador Conciliador { get; }

        public long SaldoDoClientePagador => Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos;

        public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

        public long SaldoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoEspelhoRecebedor => Motor.Recebedor.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoPiPagador => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

        public long SaldoPiRecebedor => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;

        public static Cenario Montar()
        {
            RelogioFake relogio = new();

            MotorMontado motor = MotorMontado.Montar(
                relogio,
                IspbPagador,
                IspbRecebedor,
                Valor.DeCentavos(FundoInicial),
                NumeroDaContaPagador,
                NumeroDaContaRecebedor,
                new PoliticaDeTimeout(Limite));

            ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
            motor.Recebedor.RegistrarChave(chave, ContaId.Cliente(motor.Recebedor.Ledger.Id, NumeroDaContaRecebedor));

            return new Cenario(relogio, motor, chave);
        }

        public EndToEndId ProximoE2e() => _gerador.Proximo();

        public RespostaDePagamento Pagar(EndToEndId e2e, long centavos) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(e2e, NumeroDaContaPagador, Chave, Valor.DeCentavos(centavos)));

        /// <summary>Pagamento completo, das setas 1 a 7.</summary>
        public EndToEndId PagarELiquidar(long centavos)
        {
            EndToEndId e2e = ProximoE2e();
            Pagar(e2e, centavos);
            Motor.Barramento.Drenar();
            return e2e;
        }

        /// <summary>
        /// "O SPI decidiu e a resposta ficou pelo caminho", sem inventar um barramento paralelo: a
        /// ordem e entregue na mao e o <c>pacs.002</c> que ela gera fica na fila, porque ninguem
        /// drena. A impressao da ordem coincide com a que o pagador despachou, entao o dedup do SPI
        /// reconhece o <c>pacs.008</c> pendente como reentrega da mesma ordem.
        /// </summary>
        public void EntregarOrdemAoSpiSemEntregarAResposta(EndToEndId e2e, long centavos) =>
            Motor.Spi.ReceberPacs008(new Pacs008(
                e2e,
                IspbPagador,
                ContaPagador,
                IspbRecebedor,
                ContaRecebedor,
                Valor.DeCentavos(centavos),
                Chave));

        /// <summary>
        /// Aplica no recebedor um credito que o SPI nunca movera, montando um <c>pacs.002 ACSC</c> a
        /// mao. E a orfa deliberada: saldo de um lado sem lancamento do outro que o explique.
        /// </summary>
        public void CreditarSemLastroNoRecebedor(EndToEndId e2e, long centavos) =>
            Motor.Recebedor.Receber(Pacs002.Aceito(
                e2e,
                new OrgnlTxRef(
                    IspbPagador,
                    ContaPagador,
                    IspbRecebedor,
                    ContaRecebedor,
                    Valor.DeCentavos(centavos))));

        /// <summary>
        /// O prazo vencido nao expira nada; a varredura expira. Devolve <b>quais</b> E2E foram
        /// alcancados.
        /// </summary>
        public IReadOnlyCollection<EndToEndId> ExpirarPorVarredura()
        {
            Relogio.Avancar(Limite);
            return Motor.Pagador.ExpirarVencidos();
        }

        public VistaDaTransacao Transacao(EndToEndId e2e)
        {
            Assert.True(Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? vista));
            Assert.NotNull(vista);
            return vista;
        }
    }
}
