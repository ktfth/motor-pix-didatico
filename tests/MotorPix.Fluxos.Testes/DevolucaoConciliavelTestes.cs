using MotorPix.Composicao;
using MotorPix.Conciliacao;
using MotorPix.Contratos;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// A divida que o gate 6 deixou e o gate 7 pagou: a devolucao tambem expira, tambem fecha por
/// consulta e tambem e enxergada pela conciliacao.
/// <para>
/// Ate o gate 6, varredura de vencidos e consulta de status eram porta do PSP <b>pagador</b>. A
/// devolucao nasce no recebedor e percorre a mesma maquina normativa, entao ela chegava a
/// <c>ENVIADA_SPI</c> e ficava — sem timeout, sem consulta, sem saida. O efeito colateral era pior
/// que a lacuna: o criterio de aceite da conciliacao, "nenhuma transacao permanece em
/// <c>EXPIRADA</c>", era <em>vacuamente verdadeiro</em> para devolucoes, porque nenhuma delas
/// conseguia sequer chegar a <c>EXPIRADA</c>. Um criterio que nao pode falhar nao prova nada.
/// </para>
/// <para>
/// O gate 7 moveu esse comportamento para <c>NucleoDePsp</c>, o kernel de papel compartilhado. Esta
/// suite existe para provar que a promocao aconteceu de fato — e que os dois PSPs se comportam
/// igual onde o diagrama nao distingue papeis.
/// </para>
/// </summary>
public sealed class DevolucaoConciliavelTestes
{
    private const long FundoInicial = 1_000_00;
    private const long ValorDoPagamento = 250_00;

    /// <summary>
    /// Credito que o recebedor registra <b>sem</b> lastro na conta PI. Existe so no cenario da
    /// devolucao rejeitada: num fluxo bem formado o SPI nao tem por que recusar uma devolucao total.
    /// </summary>
    private const long CreditoSemLastro = 5_000_00;

    private static readonly TimeSpan Limite = new(0, 0, 20);

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // -------------------------------------------------------------------------------------------
    // A devolucao alcanca EXPIRADA (estados:11)
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Devolucao despachada e sem resposta: passado o prazo, a varredura do <b>recebedor</b> a leva
    /// a <c>EXPIRADA</c>.
    /// <para>
    /// Ate o gate 6 este teste era inescrevivel: <c>ExpirarVencidos</c> so existia no pagador, e a
    /// varredura dele percorre as transacoes que ele originou. A devolucao vive no recebedor, entao
    /// ela simplesmente nao era alcancada — e por isso o criterio "nenhuma transacao permanece em
    /// EXPIRADA" passava sem exercitar devolucao nenhuma.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void ExpirarVencidos_NoRecebedorComDevolucaoSemResposta_LevaADevolucaoParaExpirada()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        VistaDaTransacao pedido = cenario.SolicitarDevolucao(original, ValorDoPagamento);

        Assert.Equal(EstadoTransacao.EnviadaSpi, pedido.Estado);
        Assert.Equal(TipoTransacao.Devolucao, pedido.Tipo);
        Assert.Equal(original, pedido.E2eOriginal);
        Assert.Empty(cenario.Motor.Recebedor.Expiradas);

        // O pacs.004 fica na fila: ninguem drena, entao o SPI nunca ve a devolucao.
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);

        cenario.Relogio.Avancar(Limite);

        // A varredura do pagador continua nao alcancando a devolucao — e nao deveria mesmo: ela nao
        // e transacao dele. O que mudou e que agora existe a varredura do outro lado.
        Assert.Empty(cenario.Motor.Pagador.ExpirarVencidos());

        Assert.Equal(pedido.E2E, Assert.Single(cenario.Motor.Recebedor.ExpirarVencidos()));

        VistaDaTransacao expirada = cenario.Devolucao(pedido.E2E);
        Assert.Equal(EstadoTransacao.Expirada, expirada.Estado);
        Assert.Contains(pedido.E2E, cenario.Motor.Recebedor.Expiradas);

        TransicaoAplicada ultima = expirada.Historico[^1];
        Assert.Equal(EstadoTransacao.EnviadaSpi, ultima.Origem);
        Assert.Equal(TipoEvento.TimeoutDetectado, ultima.Evento);
        Assert.Equal(EstadoTransacao.Expirada, ultima.Destino);

        // Timeout nao e falha: nada foi estornado e a original nao se mexeu.
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveEstorno(pedido.E2E));
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
    }

    // -------------------------------------------------------------------------------------------
    // A devolucao sai de EXPIRADA pela aresta estados:13
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Devolucao <c>EXPIRADA</c> que o SPI liquidou fecha em <c>LIQUIDADA</c> pela consulta.
    /// <para>
    /// A aresta e a mesma do pagamento, <c>estados:13</c>: a maquina normativa nao tem transicao
    /// propria de devolucao, e nao teria por que ter. O que faltava era o recebedor poder <em>fazer
    /// a pergunta</em>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void ConsultarStatus_NoRecebedorComDevolucaoExpiradaJaLiquidada_FechaEmLiquidadaPelaAresta13()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        VistaDaTransacao pedido = cenario.SolicitarDevolucao(original, ValorDoPagamento);
        cenario.EntregarDevolucaoAoSpiSemEntregarAResposta(pedido.E2E, original, ValorDoPagamento);

        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Spi.ConsultarStatus(pedido.E2E));
        Assert.Equal(pedido.E2E, Assert.Single(cenario.ExpirarNoRecebedor()));

        int commitsAntes = cenario.CommitsDoRecebedor;

        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Recebedor.ConsultarStatus(pedido.E2E));

        VistaDaTransacao fechada = cenario.Devolucao(pedido.E2E);
        Assert.Equal(EstadoTransacao.Liquidada, fechada.Estado);

        TransicaoAplicada ultima = fechada.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaLiquidacao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Liquidada, ultima.Destino);

        // Confirmar liquidacao nao lanca nada: o debito de quem devolveu ja esta de pe e continua
        // correto. Devolve-lo aqui seria criar um credito sem contrapartida.
        Assert.Equal(commitsAntes, cenario.CommitsDoRecebedor);
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveEstorno(pedido.E2E));
        Assert.Empty(cenario.Motor.Recebedor.Expiradas);

        // A consulta fecha a transacao de quem perguntou; ela nao entrega a mensagem que falta. O
        // credito de volta ao cliente pagador so acontece quando o ACSC chegar la, e por isso a
        // original ainda nao foi para DEVOLVIDA.
        Assert.False(cenario.Motor.Pagador.Ledger.HouveCredito(pedido.E2E));
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
    }

    // -------------------------------------------------------------------------------------------
    // A devolucao sai de EXPIRADA pela aresta estados:14
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Devolucao <c>EXPIRADA</c> que o SPI recusou fecha em <c>REJEITADA</c> pela consulta, e o
    /// debito de quem devolveu volta por <b>lancamento novo</b>.
    /// <para>
    /// O cenario precisa nascer de uma divergencia, e isso e propriedade do modelo e nao defeito do
    /// teste: num fluxo bem formado o SPI nao recusa uma devolucao total, porque a conta PI de quem
    /// devolve recebeu exatamente aquele valor na liquidacao original. Injetamos entao um credito
    /// sem lastro no recebedor — a mesma classe de divergencia que a conciliacao existe para achar —
    /// para que a devolucao peca de volta mais do que a original moveu.
    /// </para>
    /// <para>
    /// O estorno e lancamento novo ao lado do debito, jamais UPDATE: o log cresce em um commit e os
    /// dois convivem nele.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void ConsultarStatus_NoRecebedorComDevolucaoExpiradaERejeitada_FechaEmRejeitadaEEstorna()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.ProximoE2e();

        cenario.Pagar(original, ValorDoPagamento);
        cenario.CreditarSemLastroNoRecebedor(original, CreditoSemLastro);
        cenario.Motor.Barramento.Drenar();

        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
        Assert.Equal(FundoInicial + CreditoSemLastro, cenario.SaldoDoClienteRecebedor);

        VistaDaTransacao pedido = cenario.SolicitarDevolucao(original, CreditoSemLastro);

        Assert.True(cenario.Motor.Recebedor.Ledger.HouveDebito(pedido.E2E));
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);

        cenario.EntregarDevolucaoAoSpiSemEntregarAResposta(pedido.E2E, original, CreditoSemLastro);

        Assert.True(cenario.Motor.Spi.TryRespostaDe(pedido.E2E, out Pacs002? resposta));
        Assert.NotNull(resposta);
        Assert.Equal(StatusPacs002.Rjct, resposta.Status);
        Assert.Equal(MotivoRejeicao.OrdemInvalida, resposta.Motivo);

        Assert.Equal(pedido.E2E, Assert.Single(cenario.ExpirarNoRecebedor()));

        int commitsAntes = cenario.CommitsDoRecebedor;

        Assert.Equal(ResultadoConsulta.Rejeitada, cenario.Motor.Recebedor.ConsultarStatus(pedido.E2E));

        VistaDaTransacao fechada = cenario.Devolucao(pedido.E2E);
        Assert.Equal(EstadoTransacao.Rejeitada, fechada.Estado);
        Assert.Equal(MotivoRejeicaoLocal.RejeitadaPeloSpi, fechada.MotivoRejeicao);

        TransicaoAplicada ultima = fechada.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaRejeicao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Rejeitada, ultima.Destino);

        Assert.True(cenario.Motor.Recebedor.Ledger.HouveDebito(pedido.E2E));
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveEstorno(pedido.E2E));
        Assert.Equal(commitsAntes + 1, cenario.CommitsDoRecebedor);
        Assert.Equal(FundoInicial + CreditoSemLastro, cenario.SaldoDoClienteRecebedor);
        Assert.Empty(cenario.Motor.Recebedor.Expiradas);

        // Devolucao que nao liquida nao devolve nada, tambem quando quem fecha e a consulta: a
        // original permanece em LIQUIDADA e nao ganhou entrada nenhuma de historico.
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
        Assert.NotEqual(EstadoTransacao.Devolvida, cenario.Original(original).Estado);
    }

    // -------------------------------------------------------------------------------------------
    // A conciliacao enxerga a devolucao
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Uma devolucao expirada aparece em <c>Expiradas</c> do relatorio, e o ciclo de estabilizacao a
    /// fecha.
    /// <para>
    /// E aqui que a divida do gate 6 se paga por inteiro: o criterio de aceite do gate 7 deixa de
    /// ser vacuo para devolucoes. A conciliacao nao precisou saber que aquilo era uma devolucao —
    /// ela pergunta ao PSP quem esta em <c>EXPIRADA</c> e reapresenta a pergunta ao SPI, e o kernel
    /// compartilhado responde igual para os dois papeis.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "7")]
    public void Conciliar_ComDevolucaoExpirada_ListaAExpiradaEConciliarAteEstabilizarAFecha()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        VistaDaTransacao pedido = cenario.SolicitarDevolucao(original, ValorDoPagamento);
        cenario.EntregarDevolucaoAoSpiSemEntregarAResposta(pedido.E2E, original, ValorDoPagamento);

        Assert.Equal(pedido.E2E, Assert.Single(cenario.ExpirarNoRecebedor()));

        RelatorioDeConciliacao antes = cenario.Conciliador.Conciliar();

        Assert.Contains(pedido.E2E, antes.Expiradas);
        Assert.Contains(pedido.E2E, antes.AConsultar);
        Assert.Empty(antes.Orfas);
        Assert.False(antes.EmQuiescencia);

        RelatorioDeConciliacao depois = cenario.Conciliador.ConciliarAteEstabilizar();

        Assert.Empty(depois.Expiradas);
        Assert.Empty(cenario.Motor.Recebedor.Expiradas);
        Assert.Empty(cenario.Motor.Pagador.Expiradas);
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Devolucao(pedido.E2E).Estado);

        // A conciliacao fecha as DUAS pontas: a consulta (MATCH -.-> VAL) traz a devolucao para
        // LIQUIDADA, e o reprocessamento (MATCH -.-> SM_R) aplica o credito que o pacs.002 perdido
        // nunca entregou. Sem esta segunda aresta, o dinheiro pararia na conta PI do pagador e a
        // quiescencia seria inalcancavel — o relatorio ficaria para sempre com a pendencia aberta.
        Assert.Empty(depois.ACreditar);
        Assert.Empty(depois.Orfas);
        Assert.True(
            depois.EmQuiescencia,
            "apos conciliar ate estabilizar nao deveria sobrar divergencia: " +
            string.Join(" | ", depois.Divergencias.Select(d => $"{d.Classe} {d.Valor} {d.Explicacao}")));
    }

    // -------------------------------------------------------------------------------------------
    // Simetria entre os dois PSPs
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Os dois PSPs expiram, registram o <c>pacs.002</c> atrasado e fecham por consulta do mesmo
    /// jeito.
    /// <para>
    /// O mesmo roteiro roda duas vezes, mudando apenas quem origina a transacao: o pagador com um
    /// <c>pacs.008</c> e o recebedor com um <c>pacs.004</c>. Nenhuma assercao e escrita duas vezes,
    /// porque o ponto e exatamente que nao ha duas historias — <c>Expiradas</c>,
    /// <c>Pacs002Atrasados</c> e <c>Divergencias</c> existem e funcionam nos dois desde que o
    /// comportamento subiu para o kernel de papel.
    /// </para>
    /// <para>
    /// Repare no meio do roteiro: a mensagem chega depois de a transacao ja estar em
    /// <c>EXPIRADA</c>, e nao transiciona nada. Descarta-la jogaria fora a evidencia autoritativa do
    /// SPI; aplica-la violaria o invariante 6. Registrar que ha o que consultar, e consultar,
    /// satisfaz os dois — nos dois PSPs.
    /// </para>
    /// </summary>
    [Theory]
    [Trait("gate", "7")]
    [InlineData(false)]
    [InlineData(true)]
    public void PspsPagadorERecebedor_ExpiramRegistramOAtrasadoEFechamPorConsulta_DoMesmoJeito(bool peloRecebedor)
    {
        Cenario cenario = Cenario.Montar();
        Papel papel = peloRecebedor ? cenario.PapelDoRecebedor() : cenario.PapelDoPagador();

        EndToEndId e2e = papel.Originar();

        Assert.Empty(papel.Expiradas());
        Assert.Empty(papel.Pacs002Atrasados());
        Assert.Empty(papel.Divergencias());
        Assert.Equal(EstadoTransacao.EnviadaSpi, papel.Vista(e2e).Estado);

        cenario.Relogio.Avancar(Limite);

        Assert.Equal(e2e, Assert.Single(papel.ExpirarVencidos()));
        Assert.Contains(e2e, papel.Expiradas());

        // A fila anda: a ordem chega ao SPI, o SPI decide e o pacs.002 volta para uma transacao que
        // ja esta em EXPIRADA.
        cenario.Motor.Barramento.Drenar();

        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);
        Assert.Contains(e2e, papel.Pacs002Atrasados());
        Assert.Empty(papel.Divergencias());
        Assert.Equal(EstadoTransacao.Expirada, papel.Vista(e2e).Estado);

        Assert.Equal(ResultadoConsulta.Liquidada, papel.ConsultarStatus(e2e));

        VistaDaTransacao fechada = papel.Vista(e2e);

        Assert.True(
            fechada.Estado == EstadoTransacao.Liquidada,
            $"{papel.Nome}: esperava LIQUIDADA depois da consulta, veio {fechada.Estado}");

        TransicaoAplicada ultima = fechada.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaLiquidacao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Liquidada, ultima.Destino);

        Assert.Empty(papel.Expiradas());
        Assert.Empty(papel.Pacs002Atrasados());
        Assert.Empty(papel.Divergencias());
    }

    // -------------------------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Um PSP visto pelo que o kernel de papel lhe da, sem citar qual dos dois e. E a abstracao que
    /// permite escrever o roteiro uma vez so — se ela nao fosse possivel de montar, o gate 7 nao
    /// teria promovido comportamento nenhum.
    /// </summary>
    private sealed record Papel(
        string Nome,
        Func<EndToEndId> Originar,
        Func<IReadOnlyCollection<EndToEndId>> ExpirarVencidos,
        Func<IReadOnlyCollection<EndToEndId>> Expiradas,
        Func<IReadOnlyCollection<EndToEndId>> Pacs002Atrasados,
        Func<IReadOnlyList<DivergenciaPatrimonial>> Divergencias,
        Func<EndToEndId, ResultadoConsulta> ConsultarStatus,
        Func<EndToEndId, VistaDaTransacao> Vista);

    /// <summary>Motor montado, com a conciliacao ligada aos dois PSPs.</summary>
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

        public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

        public int CommitsDoRecebedor => Motor.Recebedor.Ledger.Consulta.Log().Count;

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

        public void Pagar(EndToEndId e2e, long centavos) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(e2e, NumeroDaContaPagador, Chave, Valor.DeCentavos(centavos)));

        /// <summary>Pagamento completo, das setas 1 a 7. E o insumo de qualquer devolucao.</summary>
        public EndToEndId PagarELiquidar(long centavos)
        {
            EndToEndId e2e = ProximoE2e();
            Pagar(e2e, centavos);
            Motor.Barramento.Drenar();
            return e2e;
        }

        public VistaDaTransacao SolicitarDevolucao(EndToEndId original, long centavos) =>
            Motor.Recebedor.SolicitarDevolucao(original, Valor.DeCentavos(centavos));

        /// <summary>
        /// A devolucao exatamente como o recebedor a despachou, entregue na mao do SPI. O
        /// <c>pacs.002</c> que ela gera fica na fila, porque ninguem drena — e assim "a resposta se
        /// perdeu" continua sendo uma drenagem que nao aconteceu, e nao uma espera que falhou.
        /// </summary>
        public void EntregarDevolucaoAoSpiSemEntregarAResposta(EndToEndId devolucao, EndToEndId original, long centavos) =>
            Motor.Spi.ReceberPacs004(new Pacs004(
                devolucao,
                original,
                IspbRecebedor,
                ContaRecebedor,
                IspbPagador,
                ContaPagador,
                Valor.DeCentavos(centavos)));

        /// <summary>
        /// Aplica no recebedor um credito que o SPI nao movera, montando um <c>pacs.002 ACSC</c> a
        /// mao. E a divergencia deliberada de que o cenario da devolucao rejeitada depende.
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

        public IReadOnlyCollection<EndToEndId> ExpirarNoRecebedor()
        {
            Relogio.Avancar(Limite);
            return Motor.Recebedor.ExpirarVencidos();
        }

        public VistaDaTransacao Original(EndToEndId e2e)
        {
            Assert.True(Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? vista));
            Assert.NotNull(vista);
            return vista;
        }

        public VistaDaTransacao Devolucao(EndToEndId e2e)
        {
            Assert.True(Motor.Recebedor.TryDevolucao(e2e, out VistaDaTransacao? vista));
            Assert.NotNull(vista);
            return vista;
        }

        /// <summary>O pagador originando um pagamento cujo <c>pacs.008</c> fica na fila.</summary>
        public Papel PapelDoPagador() => new(
            "pagador",
            () =>
            {
                EndToEndId e2e = ProximoE2e();
                Pagar(e2e, ValorDoPagamento);
                return e2e;
            },
            () => Motor.Pagador.ExpirarVencidos(),
            () => Motor.Pagador.Expiradas,
            () => Motor.Pagador.Pacs002Atrasados,
            () => Motor.Pagador.Divergencias,
            Motor.Pagador.ConsultarStatus,
            Original);

        /// <summary>
        /// O recebedor originando uma devolucao cujo <c>pacs.004</c> fica na fila. O pagamento que
        /// serve de insumo e liquidado antes, e por isso nao aparece em nenhuma varredura.
        /// </summary>
        public Papel PapelDoRecebedor() => new(
            "recebedor",
            () => SolicitarDevolucao(PagarELiquidar(ValorDoPagamento), ValorDoPagamento).E2E,
            () => Motor.Recebedor.ExpirarVencidos(),
            () => Motor.Recebedor.Expiradas,
            () => Motor.Recebedor.Pacs002Atrasados,
            () => Motor.Recebedor.Divergencias,
            Motor.Recebedor.ConsultarStatus,
            Devolucao);
    }
}
