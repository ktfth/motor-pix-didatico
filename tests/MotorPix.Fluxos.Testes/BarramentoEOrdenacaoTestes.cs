using MotorPix.Composicao;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// O barramento de entrega diferida: o que ele garante sobre ordem, sobre reentrega e sobre tempo.
/// <para>
/// Enfileirar nao entrega. Toda a ordenacao do motor descansa nisso — a transicao e persistida
/// antes de o efeito externo sair, e o efeito externo so acontece quando alguem drena. E isso que
/// torna inalcancavel o par <c>(VALIDADA, pacs.002)</c>, que torna expressavel o cenario do gate 5
/// (a resposta que nunca chega) e que da onde encaixar injecao de falha sem sujar o dominio.
/// </para>
/// </summary>
public sealed class BarramentoEOrdenacaoTestes
{
    private const long FundoInicial = 1_000_00;
    private const long ValorDoPagamento = 250_00;

    /// <summary>Tempo que a mensagem passa em voo. Escrito como TimeSpan literal, sem ponto flutuante.</summary>
    private static readonly TimeSpan TempoEmVoo = new(0, 1, 30);

    private static readonly TimeSpan UmaHora = new(1, 0, 0);

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    /// <summary>
    /// Depois de <c>Pagar</c> retornar, a transacao ja esta em ENVIADA_SPI e o <c>pacs.008</c>
    /// ainda nao saiu.
    /// <para>
    /// E exatamente esta ordem que torna o par <c>(VALIDADA, pacs.002)</c> inalcancavel: quando a
    /// resposta do SPI puder chegar, a transicao estados:7 ja esta gravada. Se a seta 4 fosse
    /// chamada de dentro de <c>Pagar</c>, o SPI responderia na mesma pilha, com a transacao ainda
    /// em VALIDADA — e o diagrama nao tem esse par, ou seja: excecao de transicao invalida no
    /// caminho feliz.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Pagar_AntesDeDrenar_JaGravouATransicaoComOPacs008AindaPendente()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        RespostaDePagamento resposta = cenario.Pagar(e2e);

        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);
        Assert.Equal(0, cenario.Motor.Barramento.TotalEntregue);

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.EnviadaSpi, transacao.Estado);

        TransicaoAplicada ultima = transacao.Historico[^1];
        Assert.Equal(EstadoTransacao.Validada, ultima.Origem);
        Assert.Equal(TipoEvento.DebitoLancadoEPacs008Despachado, ultima.Evento);
        Assert.Equal(EstadoTransacao.EnviadaSpi, ultima.Destino);

        // O par que a ordem torna inalcancavel nao existe na tabela normativa.
        Assert.False(MaquinaDeEstados.EhValida(EstadoTransacao.Validada, TipoEvento.Pacs002Acsc));
        Assert.False(MaquinaDeEstados.EhValida(EstadoTransacao.Validada, TipoEvento.Pacs002Rjct));

        // O debito interno, esse ja aconteceu: a transicao estados:7 significa "debito lancado
        // E pacs.008 enviado", e o enviado aqui quer dizer despachado, nao entregue.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoDoClientePagador);
    }

    /// <summary>
    /// Sem drenar, nada acontece — nem com o relogio andando.
    /// <para>
    /// Nao existe scheduler: o diagrama nao tem esse no, e inventar um servico de fundo seria criar
    /// um modulo ausente do desenho. A consequencia util e que "o <c>pacs.002</c> que nunca chega"
    /// se escreve simplesmente nao chamando <c>Drenar</c> — e esse e o cenario que o gate 5 usa
    /// para levar a transacao de ENVIADA_SPI a EXPIRADA por timeout do <c>IClock</c>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void SemDrenar_NemOSpiLiquidaNemORecebedorCredita_MesmoComORelogioAndando()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e);
        cenario.Relogio.Avancar(UmaHora);

        Assert.False(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? nenhuma));
        Assert.Null(nenhuma);

        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveCredito(e2e));
        Assert.Null(cenario.Motor.Recebedor.UltimoCreditoEm);

        // A mensagem continua parada, e a transacao continua esperando.
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);
        Assert.Equal(0, cenario.Motor.Barramento.TotalEntregue);

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.EnviadaSpi, transacao.Estado);

        // Daqui o gate 5 sai por estados:11, sem que nada no motor precise mudar.
        Assert.True(MaquinaDeEstados.EhValida(EstadoTransacao.EnviadaSpi, TipoEvento.TimeoutDetectado));
    }

    /// <summary>
    /// Uma unica drenagem percorre a cadeia inteira: a seta 4 produz a seta 6, e a drenagem
    /// processa em rodadas o que foi enfileirado durante ela mesma.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Drenar_UmaVezSo_EntregaOPacs008EOsDoisPacs002EmCadeia()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e);

        int entregues = cenario.Motor.Barramento.Drenar();

        // pacs.008 ao SPI; depois pacs.002 ACSC ao pagador e ao recebedor.
        Assert.Equal(3, entregues);
        Assert.Equal(3, cenario.Motor.Barramento.TotalEntregue);
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveCredito(e2e));
    }

    [Fact]
    [Trait("gate", "3")]
    public void Drenar_UmaSegundaVez_NaoReentregaNadaENaoMudaNenhumSaldo()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e);
        Assert.Equal(3, cenario.Motor.Barramento.Drenar());

        long clientePagador = cenario.SaldoDoClientePagador;
        long clienteRecebedor = cenario.SaldoDoClienteRecebedor;
        long piPagador = cenario.SaldoPiPagador;
        long piRecebedor = cenario.SaldoPiRecebedor;
        int commitsDoRecebedor = cenario.CommitsDoRecebedor;

        Assert.Equal(0, cenario.Motor.Barramento.Drenar());

        Assert.Equal(3, cenario.Motor.Barramento.TotalEntregue);
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);
        Assert.Equal(clientePagador, cenario.SaldoDoClientePagador);
        Assert.Equal(clienteRecebedor, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(piPagador, cenario.SaldoPiPagador);
        Assert.Equal(piRecebedor, cenario.SaldoPiRecebedor);
        Assert.Equal(commitsDoRecebedor, cenario.CommitsDoRecebedor);
    }

    /// <summary>
    /// Rejeicao no SPI: duas entregas, nao tres.
    /// <para>
    /// O RJCT vai so ao pagador. O recebedor nunca soube da ordem — ele nao viu o <c>pacs.008</c>,
    /// nao lancou nada e nao tem estado para reverter —, entao avisa-lo de algo que nao aconteceu
    /// nao teria efeito nenhum no ledger dele. Entregar mesmo assim custaria uma mensagem por
    /// rejeicao e criaria um caminho de codigo cujo unico comportamento correto e nao fazer nada:
    /// o lugar exato onde um defeito futuro se esconde.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Drenar_ComRejeicaoDoSpi_EntregaDuasMensagensPorqueORjctSoVaiAoPagador()
    {
        Cenario cenario = Cenario.Montar();
        ChavePix fantasma = cenario.VincularChaveEmContaNuncaAberta();
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsDoRecebedor = cenario.CommitsDoRecebedor;

        cenario.Pagar(e2e, fantasma);

        Assert.Equal(2, cenario.Motor.Barramento.Drenar());
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        Assert.True(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? confirmacao));
        Assert.NotNull(confirmacao);
        Assert.Equal(StatusPacs002.Rjct, confirmacao.Status);

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Rejeitada, transacao.Estado);

        // O recebedor nao foi tocado por mensagem nenhuma.
        Assert.Equal(commitsDoRecebedor, cenario.CommitsDoRecebedor);
        Assert.Null(cenario.Motor.Recebedor.UltimoCreditoEm);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);
    }

    /// <summary>
    /// O mesmo <c>pacs.008</c> entregue duas vezes ao SPI: a resposta e reemitida, a liquidacao nao.
    /// <para>
    /// A ordem e montada a mao e entregue direto porque o que se testa aqui e a guarda do SPI, e
    /// nao a do pagador; o <c>pacs.002</c> de volta fica pendente de proposito, ja que nenhuma
    /// transacao foi aberta no pagador para este E2E.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void ReceberPacs008_ReentregueAoSpi_DevolveAMesmaRespostaELiquidaUmaVezSo()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();
        Pacs008 ordem = cenario.MontarPacs008(e2e);

        cenario.Motor.Spi.ReceberPacs008(ordem);

        Assert.True(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? primeira));
        Assert.NotNull(primeira);
        Assert.Equal(StatusPacs002.Acsc, primeira.Status);

        int commitsDoSpi = cenario.CommitsDoSpi;

        cenario.Motor.Spi.ReceberPacs008(ordem);

        Assert.True(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? segunda));
        Assert.NotNull(segunda);

        // Mesma resposta, e nao uma resposta nova de conteudo parecido.
        Assert.Same(primeira, segunda);

        // As contas PI se moveram uma vez so, e o log do SPI nao cresceu na segunda entrega.
        Assert.Equal(commitsDoSpi, cenario.CommitsDoSpi);
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial * 2, cenario.SaldoPiPagador + cenario.SaldoPiRecebedor);

        // Responder de novo e barato e inofensivo — sao quatro pacs.002 enfileirados para duas
        // entregas de pacs.008. Liquidar de novo e que seria irreversivel.
        Assert.Equal(4, cenario.Motor.Barramento.Pendentes);
    }

    /// <summary>
    /// O mesmo ACSC entregue duas vezes ao recebedor credita uma vez so.
    /// <para>
    /// A guarda e a chave de idempotencia do ledger — "ja existe credito para este E2E?" —, e nao
    /// uma checagem de estado da transacao nem a memoria da mensagem que chegou. E por isso que ela
    /// resiste a reentrega vinda de qualquer caminho: a segunda copia deste teste e um objeto
    /// <c>Pacs002</c> distinto, montado do zero, como viria de um reprocessamento da conciliacao no
    /// gate 7 e nao do barramento.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Receber_OMesmoAcscDuasVezesNoRecebedor_CreditaUmaVezSo()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsAntes = cenario.CommitsDoRecebedor;

        cenario.Motor.Recebedor.Receber(cenario.MontarAcsc(e2e));

        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(commitsAntes + 1, cenario.CommitsDoRecebedor);
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveCredito(e2e));

        // Outra instancia, mesmo conteudo: a guarda nao pode depender de reconhecer o objeto.
        cenario.Motor.Recebedor.Receber(cenario.MontarAcsc(e2e));

        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(commitsAntes + 1, cenario.CommitsDoRecebedor);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoEspelhoRecebedor);
    }

    /// <summary>
    /// O instante gravado vem do <c>IClock</c> injetado, e nao do relogio de parede.
    /// <para>
    /// O relogio avanca entre o despacho e a drenagem: se qualquer ponto do motor lesse o tempo do
    /// ambiente, o instante da liquidacao nao bateria com <c>Epoca + TempoEmVoo</c> — e o teste
    /// falharia por uma diferenca que nenhuma outra assercao enxerga.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Tempo_DasTransicoesEDoCredito_VemDoRelogioInjetadoENaoDoAmbiente()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e);

        DateTimeOffset instanteDoDespacho = RelogioFake.Epoca;
        DateTimeOffset instanteDaLiquidacao = RelogioFake.Epoca + TempoEmVoo;

        cenario.Relogio.Avancar(TempoEmVoo);
        cenario.Motor.Barramento.Drenar();

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);

        Assert.Equal(instanteDoDespacho, transacao.CriadaEm);
        Assert.Equal(instanteDaLiquidacao, transacao.AtualizadaEm);

        Assert.Collection(
            transacao.Historico,
            t => Assert.Equal(instanteDoDespacho, t.Em),
            t => Assert.Equal(instanteDoDespacho, t.Em),
            t => Assert.Equal(instanteDaLiquidacao, t.Em));

        Assert.Equal(instanteDaLiquidacao, cenario.Motor.Recebedor.UltimoCreditoEm);
    }

    /// <summary>
    /// Em quiescencia, o espelho de cada PSP volta a coincidir com a conta PI correspondente.
    /// <para>
    /// A defasagem durante o voo nao e erro: <b>e</b> o dinheiro em transito. O pagador ja debitou o
    /// cliente e ja baixou o proprio espelho, mas a conta PI dele no SPI so cai quando a liquidacao
    /// acontece. Enquanto as duas leituras discordam, existe uma ordem em andamento; quando voltam a
    /// bater, nao existe mais. E dessa diferenca que a conciliacao do gate 7 vive.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Quiescencia_AposOFluxoCompleto_IgualaEspelhoEContaPiNosDoisParticipantes()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        cenario.Pagar(e2e);

        // Em voo: o pagador tem exatamente o valor da ordem a menos no espelho do que na conta PI.
        Assert.Equal(ValorDoPagamento, cenario.SaldoPiPagador - cenario.SaldoEspelhoPagador);
        Assert.Equal(0L, cenario.SaldoPiRecebedor - cenario.SaldoEspelhoRecebedor);

        cenario.Motor.Barramento.Drenar();

        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);
        Assert.Equal(cenario.SaldoPiPagador, cenario.SaldoEspelhoPagador);
        Assert.Equal(cenario.SaldoPiRecebedor, cenario.SaldoEspelhoRecebedor);

        // E o pool nao mudou de tamanho: o dinheiro trocou de dono, nao de quantidade.
        Assert.Equal(FundoInicial * 2, cenario.SaldoPiPagador + cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial * 2, cenario.SaldoDoClientePagador + cenario.SaldoDoClienteRecebedor);
    }

    /// <summary>Motor montado, com os atalhos de leitura e os construtores de mensagem a mao.</summary>
    private sealed class Cenario
    {
        public const string ContaDoPagador = "0001";
        public const string ContaDoRecebedor = "0002";
        public const string ContaNuncaAberta = "9999";

        private readonly GeradorE2eDeterministico _gerador;

        private Cenario(MotorMontado motor, RelogioFake relogio, GeradorE2eDeterministico gerador, ChavePix chave)
        {
            Motor = motor;
            Relogio = relogio;
            _gerador = gerador;
            ChaveDoRecebedor = chave;

            ContaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
            ContaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor);
            ContaFantasma = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaNuncaAberta);
        }

        public MotorMontado Motor { get; }

        public RelogioFake Relogio { get; }

        public ChavePix ChaveDoRecebedor { get; }

        public ContaId ContaPagador { get; }

        public ContaId ContaRecebedor { get; }

        public ContaId ContaFantasma { get; }

        public long SaldoDoClientePagador => Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos;

        public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

        public long SaldoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoEspelhoRecebedor => Motor.Recebedor.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoPiPagador => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

        public long SaldoPiRecebedor => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;

        public int CommitsDoRecebedor => Motor.Recebedor.Ledger.Consulta.Log().Count;

        public int CommitsDoSpi => Motor.Spi.Consulta.Log().Count;

        public static Cenario Montar()
        {
            RelogioFake relogio = new();

            MotorMontado motor = MotorMontado.Montar(
                relogio,
                IspbPagador,
                IspbRecebedor,
                Valor.DeCentavos(FundoInicial),
                ContaDoPagador,
                ContaDoRecebedor);

            ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
            motor.Recebedor.RegistrarChave(chave, ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor));

            return new Cenario(motor, relogio, new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca), chave);
        }

        public EndToEndId ProximoE2e() => _gerador.Proximo();

        public RespostaDePagamento Pagar(EndToEndId e2e, ChavePix? chave = null) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(
                e2e,
                ContaDoPagador,
                chave ?? ChaveDoRecebedor,
                Valor.DeCentavos(ValorDoPagamento)));

        /// <summary>A mesma ordem que o pagador produziria na seta 4, montada a mao para reentrega.</summary>
        public Pacs008 MontarPacs008(EndToEndId e2e) =>
            new(
                e2e,
                IspbPagador,
                ContaPagador,
                IspbRecebedor,
                ContaRecebedor,
                Valor.DeCentavos(ValorDoPagamento),
                ChaveDoRecebedor);

        /// <summary>Um <c>pacs.002</c> ACSC novo a cada chamada, com o mesmo conteudo.</summary>
        public Pacs002 MontarAcsc(EndToEndId e2e) =>
            Pacs002.Aceito(
                e2e,
                new OrgnlTxRef(
                    IspbPagador,
                    ContaPagador,
                    IspbRecebedor,
                    ContaRecebedor,
                    Valor.DeCentavos(ValorDoPagamento)));

        /// <summary>
        /// Vincula uma chave direto no DICT para uma conta que o recebedor nunca abriu, para
        /// produzir a rejeicao do SPI sem depender de saldo. O caminho de registro do recebedor
        /// recusaria o vinculo, que e o que faz dele a primeira linha de defesa.
        /// </summary>
        public ChavePix VincularChaveEmContaNuncaAberta()
        {
            ChavePix fantasma = ChavePix.Criar(TipoChave.Email, "fantasma@exemplo.com");
            Motor.Dict.Vincular(fantasma, IspbRecebedor, ContaFantasma);
            return fantasma;
        }
    }
}
