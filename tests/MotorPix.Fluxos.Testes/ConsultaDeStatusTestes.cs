using MotorPix.Composicao;
using MotorPix.Contratos;
using MotorPix.Dict;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Gate 5, segunda metade: a seta pontilhada <c>SM_P -.-&gt; VAL</c> do diagrama de arquitetura, que
/// e a unica saida de <c>EXPIRADA</c> (arestas estados:13 e estados:14).
/// <para>
/// Determinismo: nenhum teste deste arquivo usa <c>Task.Delay</c> ou <c>Thread.Sleep</c>. Todo
/// instante vem do <see cref="RelogioFake"/>, e toda entrega de mensagem vem de uma chamada
/// explicita a <c>Drenar</c>. "O pacs.002 nao chegou" nao e uma espera que falhou: e uma drenagem
/// que nao aconteceu.
/// </para>
/// </summary>
public sealed class ConsultaDeStatusTestes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;

    private static readonly TimeSpan Limite = new(0, 0, 20);

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // ---------------------------------------------------------------------------------------
    // estados:13 — a consulta confirma liquidacao
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_QuandoOSpiLiquidouEOPacs002NaoFoiEntregue_FechaEmLiquidadaSemEstornar()
    {
        Cenario cenario = Cenario.ComDestinoQueLiquida();
        cenario.Pagar();
        cenario.EntregarOrdemAoSpiSemEntregarAResposta();
        cenario.ExpirarPorVarredura();

        int commitsAntes = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;

        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        TransicaoAplicada ultima = transacao.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaLiquidacao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Liquidada, ultima.Destino);

        // O ponto contabil: confirmar liquidacao nao lanca nada. O debito ja esta de pe desde a
        // seta 3 e continua correto — devolver o dinheiro ao cliente aqui seria criar um credito
        // sem contrapartida, com o valor tambem creditado no ledger do SPI.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(cenario.E2E));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
        Assert.Equal(commitsAntes, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(Fundo - Pagamento, cenario.SaldoDoClientePagador);

        // A consulta fecha a transacao do PAGADOR; ela nao entrega a seta 7. O recebedor so e
        // creditado quando o pacs.002 finalmente for entregue.
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveCredito(cenario.E2E));
        Assert.Equal(Fundo, cenario.SaldoDoClienteRecebedor);

        int commitsDoRecebedorAntes = cenario.Motor.Recebedor.Ledger.Consulta.Log().Count;

        // A entrega atrasada, enfim. Sao cinco: o pacs.008 que estava na fila desde o pagamento,
        // o par de pacs.002 que o SPI emitiu quando recebeu a ordem na mao, e o segundo par que o
        // dedup do SPI reemite ao reconhecer o pacs.008 reentregue.
        Assert.Equal(5, cenario.Motor.Barramento.Drenar());
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        // O ACSC chegou duas vezes ao recebedor nesta mesma drenagem e o credito aconteceu uma so
        // vez: a guarda e a chave de idempotencia do ledger, nao uma checagem de estado.
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveCredito(cenario.E2E));
        Assert.Equal(Fundo + Pagamento, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(commitsDoRecebedorAntes + 1, cenario.Motor.Recebedor.Ledger.Consulta.Log().Count);

        // E o ACSC que chegou ao pagador sobre uma transacao ja LIQUIDADA concorda com o que ele
        // sabe: nao e divergencia, e nao transiciona nada.
        Assert.Empty(cenario.Motor.Pagador.Divergencias);
        Assert.Equal(EstadoTransacao.Liquidada, cenario.TransacaoDe(cenario.E2E).Estado);
    }

    // ---------------------------------------------------------------------------------------
    // estados:14 — a consulta confirma rejeicao
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_QuandoOSpiRejeitouEOPacs002NaoFoiEntregue_FechaEmRejeitadaEEstorna()
    {
        Cenario cenario = Cenario.ComDestinoQueOSpiRecusa();
        cenario.Pagar();
        cenario.EntregarOrdemAoSpiSemEntregarAResposta();

        Assert.True(cenario.Motor.Spi.TryRespostaDe(cenario.E2E, out Pacs002? doSpi));
        Assert.NotNull(doSpi);
        Assert.Equal(StatusPacs002.Rjct, doSpi.Status);
        Assert.Equal(MotivoRejeicao.ContaDeDestinoIndisponivel, doSpi.Motivo);

        cenario.ExpirarPorVarredura();

        int commitsAntes = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;

        Assert.Equal(ResultadoConsulta.Rejeitada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Rejeitada, transacao.Estado);
        Assert.Equal(MotivoRejeicaoLocal.RejeitadaPeloSpi, transacao.MotivoRejeicao);

        TransicaoAplicada ultima = transacao.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaRejeicao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Rejeitada, ultima.Destino);

        // O estorno e lancamento NOVO, jamais desfazimento do original: o debito continua no log e
        // o estorno entra ao lado dele. Por isso o log cresce em exatamente um commit — e cresce,
        // nunca encolhe, que e o que um UPDATE ou um DELETE produziria.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(cenario.E2E));
        Assert.True(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
        Assert.Equal(commitsAntes + 1, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);

        Assert.Equal(Fundo, cenario.SaldoDoClientePagador);
        Assert.Equal(Fundo, cenario.SaldoDoEspelhoPagador);
    }

    // ---------------------------------------------------------------------------------------
    // Indeterminada: o no-op legitimo
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_QuandoOSpiNuncaViuOE2e_ResponderIndeterminadaEDeixaTudoComoEstava()
    {
        // O pacs.008 nunca chegou ao SPI — que e justamente o caso que produz EXPIRADA. Responder
        // "rejeitada" para um E2E desconhecido convidaria o pagador a estornar um pagamento que
        // ainda pode liquidar; responder "liquidada" seria pior ainda. Indeterminada e a resposta
        // honesta, e nao transicionar nao e transicao: e este o caso que a nota do diagrama entrega
        // a conciliacao quando diz "o que a consulta nao fechar, a conciliacao fecha".
        Cenario cenario = Cenario.ComDestinoQueLiquida();
        cenario.Pagar();
        cenario.ExpirarPorVarredura();

        int commitsAntes = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;
        int historicoAntes = cenario.TransacaoDe(cenario.E2E).Historico.Count;

        // Nenhuma excecao atravessa esta chamada: indeterminada e resultado, nao erro.
        Assert.Equal(ResultadoConsulta.Indeterminada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Expirada, transacao.Estado);
        Assert.Equal(historicoAntes, transacao.Historico.Count);

        Assert.Equal(commitsAntes, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
        Assert.Equal(Fundo - Pagamento, cenario.SaldoDoClientePagador);
    }

    // ---------------------------------------------------------------------------------------
    // Consultar de novo
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_ChamadoDeNovoDepoisDeConfirmarRejeicao_NaoEstornaUmaSegundaVez()
    {
        // A consulta e uma porta que o host pode chamar em lote, e um estorno duplicado criaria
        // dinheiro. A guarda que impede isso e dupla: a transacao ja nao esta em EXPIRADA, e o
        // ledger recusaria a segunda chave de estorno de qualquer forma.
        Cenario cenario = Cenario.ComDestinoQueOSpiRecusa();
        cenario.Pagar();
        cenario.EntregarOrdemAoSpiSemEntregarAResposta();
        cenario.ExpirarPorVarredura();

        Assert.Equal(ResultadoConsulta.Rejeitada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        int commitsDepoisDaPrimeira = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;
        int historicoDepoisDaPrimeira = cenario.TransacaoDe(cenario.E2E).Historico.Count;

        Assert.Equal(ResultadoConsulta.Rejeitada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Rejeitada, transacao.Estado);
        Assert.Equal(historicoDepoisDaPrimeira, transacao.Historico.Count);
        Assert.Equal(commitsDepoisDaPrimeira, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(Fundo, cenario.SaldoDoClientePagador);
    }

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_ChamadoDeNovoDepoisDeConfirmarLiquidacao_NaoTransicionaNada()
    {
        Cenario cenario = Cenario.ComDestinoQueLiquida();
        cenario.Pagar();
        cenario.EntregarOrdemAoSpiSemEntregarAResposta();
        cenario.ExpirarPorVarredura();

        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        int historicoDepoisDaPrimeira = cenario.TransacaoDe(cenario.E2E).Historico.Count;
        int commitsDepoisDaPrimeira = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;

        // A segunda consulta continua devolvendo a verdade do SPI — o que muda e que ela nao tem
        // mais o que aplicar, porque LIQUIDADA nao aceita ConsultaConfirmaLiquidacao.
        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.Equal(historicoDepoisDaPrimeira, transacao.Historico.Count);
        Assert.Equal(commitsDepoisDaPrimeira, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
    }

    // ---------------------------------------------------------------------------------------
    // pacs.002 atrasado, chegando sobre uma transacao ja EXPIRADA
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void Receber_Pacs002AcscSobreTransacaoExpirada_RegistraOAtrasoEDeixaAConsultaFechar()
    {
        // Duas tentacoes, as duas erradas. Descartar a mensagem por purismo jogaria fora a
        // evidencia autoritativa do SPI e obrigaria a conciliacao a redescobrir o que ja se sabia.
        // Transicionar por ela violaria o invariante 6, que diz que de EXPIRADA so se sai por
        // consulta. Registrar que ha o que consultar, e consultar, satisfaz os dois.
        Cenario cenario = Cenario.ComDestinoQueLiquida();
        cenario.Pagar();
        cenario.ExpirarPorVarredura();

        // pacs.008 ao SPI, e o par de pacs.002 ACSC ao pagador e ao recebedor.
        Assert.Equal(3, cenario.Motor.Barramento.Drenar());
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        // Nenhuma excecao atravessou o barramento: se a entrega ao pagador tivesse lancado
        // TransicaoInvalidaException, o envelope voltaria para a fila e o credito do recebedor —
        // que vem depois dele — nunca aconteceria.
        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Expirada, transacao.Estado);
        Assert.Equal(cenario.E2E, Assert.Single(cenario.Motor.Pagador.Pacs002Atrasados));
        Assert.Empty(cenario.Motor.Pagador.Divergencias);

        int historicoAntesDaConsulta = transacao.Historico.Count;

        // O dinheiro chegou ao destino antes de o pagador saber disso — e exatamente por isso que
        // estornar no timeout seria criar dinheiro.
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveCredito(cenario.E2E));

        Assert.Equal(ResultadoConsulta.Liquidada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao fechada = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Liquidada, fechada.Estado);
        Assert.Equal(historicoAntesDaConsulta + 1, fechada.Historico.Count);

        TransicaoAplicada ultima = fechada.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaLiquidacao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Liquidada, ultima.Destino);

        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
        Assert.Equal(Fundo - Pagamento, cenario.SaldoDoClientePagador);
    }

    [Fact]
    [Trait("gate", "5")]
    public void Receber_Pacs002RjctSobreTransacaoExpirada_NaoEstornaEDeixaAConsultaFechar()
    {
        Cenario cenario = Cenario.ComDestinoQueOSpiRecusa();
        cenario.Pagar();
        cenario.ExpirarPorVarredura();

        // pacs.008 ao SPI e o RJCT ao pagador. O recebedor nao recebe RJCT: ele nunca soube da
        // ordem, e avisa-lo de algo que nao aconteceu nao move nada no ledger dele.
        Assert.Equal(2, cenario.Motor.Barramento.Drenar());

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Expirada, transacao.Estado);
        Assert.Equal(cenario.E2E, Assert.Single(cenario.Motor.Pagador.Pacs002Atrasados));

        // O RJCT atrasado nao estorna. Estornar aqui seria transicionar sem consultar por uma via
        // travessa: o efeito contabil da aresta estados:14 aconteceria sem a aresta.
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
        Assert.Equal(Fundo - Pagamento, cenario.SaldoDoClientePagador);

        Assert.Equal(ResultadoConsulta.Rejeitada, cenario.Motor.Pagador.ConsultarStatus(cenario.E2E));

        VistaDaTransacao fechada = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Rejeitada, fechada.Estado);
        Assert.Equal(MotivoRejeicaoLocal.RejeitadaPeloSpi, fechada.MotivoRejeicao);

        TransicaoAplicada ultima = fechada.Historico[^1];
        Assert.Equal(EstadoTransacao.Expirada, ultima.Origem);
        Assert.Equal(TipoEvento.ConsultaConfirmaRejeicao, ultima.Evento);
        Assert.Equal(EstadoTransacao.Rejeitada, ultima.Destino);

        Assert.True(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));
        Assert.Equal(Fundo, cenario.SaldoDoClientePagador);
    }

    // ---------------------------------------------------------------------------------------
    // Recusas da porta de consulta
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_DeE2eDesconhecido_LancaTransacaoDesconhecida()
    {
        // A guarda roda antes de o SPI ser consultado: perguntar ao SPI sobre um E2E que este PSP
        // nunca originou e pergunta sem dono — o resultado nao teria a que ser aplicado.
        Cenario cenario = Cenario.ComDestinoQueLiquida();
        cenario.Pagar();

        EndToEndId nuncaVisto = E2eDe(99);

        TransacaoDesconhecidaException erro = Assert.Throws<TransacaoDesconhecidaException>(
            () => cenario.Motor.Pagador.ConsultarStatus(nuncaVisto));

        Assert.Equal(nuncaVisto, erro.E2E);
    }

    [Fact]
    [Trait("gate", "5")]
    public void ConsultarStatus_SemSpiRegistrado_LancaMensageriaInvalida()
    {
        // O pagador montado a mao, sem passar pelo MotorMontado: e ele quem faz o registro tardio
        // da porta de consulta. Sem essa porta, a unica saida de EXPIRADA nao existe — e ficar
        // calado devolvendo Indeterminada esconderia um defeito de composicao atras de um
        // resultado que o dominio considera normal.
        RelogioFake relogio = new();
        BarramentoEmMemoria barramento = new();
        DiretorioDeChavesEmMemoria dict = new();
        PspPagador.PspPagador pagador = new(
            IspbPagador,
            relogio,
            dict,
            barramento,
            new PoliticaDeTimeout(Limite));

        pagador.AbrirCliente("0001", Valor.DeCentavos(Fundo));

        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        dict.Vincular(chave, IspbRecebedor, ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0002"));

        EndToEndId e2e = E2eDe(1);
        pagador.Pagar(new ComandoDePagamento(e2e, "0001", chave, Valor.DeCentavos(Pagamento)));

        relogio.Avancar(Limite);
        Assert.Single(pagador.ExpirarVencidos());

        Assert.Throws<MensageriaInvalidaException>(() => pagador.ConsultarStatus(e2e));

        // E a transacao continua em EXPIRADA: a falha de composicao nao inventou desfecho nenhum.
        Assert.True(pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Expirada, transacao.Estado);
        Assert.False(pagador.Ledger.HouveEstorno(e2e));
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);

    /// <summary>
    /// Motor montado em duas variantes: uma cujo destino o SPI aceita, outra cujo destino o SPI
    /// recusa. As duas produzem EXPIRADA do mesmo jeito — a diferenca aparece so na consulta.
    /// </summary>
    private sealed class Cenario
    {
        private const string NumeroDaContaPagador = "0001";
        private const string NumeroDaContaRecebedor = "0002";

        private Cenario(RelogioFake relogio, MotorMontado motor, ChavePix chave, ContaId contaDeDestino)
        {
            Relogio = relogio;
            Motor = motor;
            Chave = chave;
            ContaDeDestino = contaDeDestino;
            ContaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, NumeroDaContaPagador);
            ContaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, NumeroDaContaRecebedor);
            E2E = E2eDe(1);
        }

        public RelogioFake Relogio { get; }

        public MotorMontado Motor { get; }

        public ChavePix Chave { get; }

        /// <summary>O que o DICT resolve para <see cref="Chave"/>.</summary>
        public ContaId ContaDeDestino { get; }

        public ContaId ContaPagador { get; }

        public ContaId ContaRecebedor { get; }

        public EndToEndId E2E { get; }

        public long SaldoDoClientePagador => Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos;

        public long SaldoDoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

        public static Cenario ComDestinoQueLiquida()
        {
            (RelogioFake relogio, MotorMontado motor) = Montar();
            ContaId destino = ContaId.Cliente(motor.Recebedor.Ledger.Id, NumeroDaContaRecebedor);
            ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

            motor.Recebedor.RegistrarChave(chave, destino);

            return new Cenario(relogio, motor, chave, destino);
        }

        /// <summary>
        /// Vinculo bem formado — conta de cliente, no ledger do recebedor — apontando para uma
        /// conta que o recebedor nunca abriu. O DICT resolve, o pagamento e debitado e despachado,
        /// e a recusa acontece no SPI, que pergunta ao recebedor antes de liquidar. E o jeito de
        /// produzir um RJCT sem tocar em nenhuma guarda local do pagador.
        /// </summary>
        public static Cenario ComDestinoQueOSpiRecusa()
        {
            (RelogioFake relogio, MotorMontado motor) = Montar();
            ContaId fantasma = ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0009");
            ChavePix chave = ChavePix.Criar(TipoChave.Email, "fantasma@exemplo.com");

            motor.Dict.Vincular(chave, IspbRecebedor, fantasma);

            return new Cenario(relogio, motor, chave, fantasma);
        }

        public RespostaDePagamento Pagar() =>
            Motor.Pagador.Pagar(new ComandoDePagamento(
                E2E,
                NumeroDaContaPagador,
                Chave,
                Valor.DeCentavos(Pagamento)));

        /// <summary>
        /// A ordem exatamente como o pagador a despachou. Reconstrui-la aqui e o que permite
        /// entrega-la ao SPI sem passar pelo barramento: a impressao da ordem coincide, entao o
        /// dedup do SPI reconhece o pacs.008 pendente como reentrega da mesma ordem.
        /// </summary>
        public Pacs008 OrdemEquivalente() =>
            new(E2E, IspbPagador, ContaPagador, IspbRecebedor, ContaDeDestino, Valor.DeCentavos(Pagamento), Chave);

        /// <summary>
        /// "O SPI decidiu e a resposta ficou pelo caminho", escrito sem inventar um barramento
        /// paralelo: a ordem e entregue na mao, e o pacs.002 que ela gera fica na fila, nao
        /// entregue, porque ninguem drena.
        /// </summary>
        public void EntregarOrdemAoSpiSemEntregarAResposta() => Motor.Spi.ReceberPacs008(OrdemEquivalente());

        public void ExpirarPorVarredura()
        {
            Relogio.Avancar(Limite);
            Assert.Single(Motor.Pagador.ExpirarVencidos());
            Assert.Equal(EstadoTransacao.Expirada, TransacaoDe(E2E).Estado);
        }

        public VistaDaTransacao TransacaoDe(EndToEndId e2e)
        {
            Assert.True(Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
            Assert.NotNull(transacao);
            return transacao;
        }

        private static (RelogioFake Relogio, MotorMontado Motor) Montar()
        {
            RelogioFake relogio = new();

            MotorMontado motor = MotorMontado.Montar(
                relogio,
                IspbPagador,
                IspbRecebedor,
                Valor.DeCentavos(Fundo),
                NumeroDaContaPagador,
                NumeroDaContaRecebedor,
                new PoliticaDeTimeout(Limite));

            return (relogio, motor);
        }
    }
}
