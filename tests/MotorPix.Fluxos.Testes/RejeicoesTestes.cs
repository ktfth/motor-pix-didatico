using MotorPix.Composicao;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Os caminhos que nao liquidam: as duas arestas de rejeicao do diagrama (estados:5, vinda de
/// RECEBIDA, e estados:10, vinda de ENVIADA_SPI) e as recusas do SPI que produzem a segunda.
/// <para>
/// Cada cenario asserta quatro coisas: o estado final da transacao, o motivo, os saldos das contas
/// envolvidas nos tres ledgers, e que a quantidade de dinheiro do sistema nao mudou. As tres
/// primeiras sao escritas a mao; a quarta e uma propriedade, e esta em <c>ExigirConservacao</c>.
/// </para>
/// <para>
/// REJEITADA e alcancavel por arestas com consequencias contabeis <em>opostas</em>: vindo de
/// RECEBIDA nao houve debito e nao ha o que estornar; vindo de ENVIADA_SPI houve, e o estorno e
/// obrigatorio. Um teste que so olhasse o estado final nao distinguiria os dois casos.
/// </para>
/// </summary>
public sealed class RejeicoesTestes
{
    private const long FundoInicial = 1_000_00;
    private const long ValorDoPagamento = 250_00;

    /// <summary>
    /// Cliente do pagador com muito mais dinheiro do que o PSP tem na propria conta PI. E um PSP
    /// que prometeu aos clientes mais do que mantem no SPI — a situacao que a aresta estados:10
    /// existe para tratar.
    /// </summary>
    private const string ContaDoClienteRico = "0003";
    private const long FundoDoClienteRico = 5_000_00;
    private const long ValorAcimaDaContaPi = 3_000_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    /// <summary>ISPB que nunca foi registrado como participante nem tem conta PI aberta.</summary>
    private static readonly Ispb IspbForaDoSpi = Ispb.Criar("33333333");

    [Fact]
    [Trait("gate", "3")]
    public void Pagar_ComChaveNaoVinculadaNoDict_RejeitaSemTocarNenhumDosTresLedgers()
    {
        Cenario cenario = Cenario.Montar();
        ChavePix orfa = ChavePix.Criar(TipoChave.Email, "ninguem@exemplo.com");
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsDoPagador = cenario.CommitsDoPagador;
        int commitsDoSpi = cenario.CommitsDoSpi;
        int commitsDoRecebedor = cenario.CommitsDoRecebedor;

        RespostaDePagamento resposta = cenario.Pagar(e2e, ValorDoPagamento, orfa);

        // Aresta estados:5. Chave ausente e recusa de negocio, nao defeito: chega a API como motivo
        // estruturado, e nao como excecao.
        Assert.Equal(EstadoTransacao.Rejeitada, resposta.Estado);
        Assert.Equal(MotivoRejeicaoLocal.ChaveNaoEncontrada, resposta.Motivo);

        // A seta 4 nunca saiu: nada foi enfileirado, entao nao ha o que drenar.
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);
        Assert.Equal(0, cenario.Motor.Barramento.TotalEntregue);

        // Nenhum lancamento em nenhum ledger — nem debito, nem estorno.
        Assert.Equal(commitsDoPagador, cenario.CommitsDoPagador);
        Assert.Equal(commitsDoSpi, cenario.CommitsDoSpi);
        Assert.Equal(commitsDoRecebedor, cenario.CommitsDoRecebedor);
        Assert.False(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(e2e));

        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoPagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);

        cenario.ExigirConservacao();
    }

    [Fact]
    [Trait("gate", "3")]
    public void Pagar_DeContaQueNaoExisteNoLedger_RejeitaComContaDeOrigemDesconhecidaSemConsultarODict()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsDoPagador = cenario.CommitsDoPagador;

        RespostaDePagamento resposta = cenario.Pagar(e2e, ValorDoPagamento, conta: Cenario.ContaInexistente);

        Assert.Equal(EstadoTransacao.Rejeitada, resposta.Estado);
        Assert.Equal(MotivoRejeicaoLocal.ContaDeOrigemDesconhecida, resposta.Motivo);

        Assert.False(cenario.Motor.Pagador.Ledger.ContemConta(
            ContaId.Cliente(cenario.Motor.Pagador.Ledger.Id, Cenario.ContaInexistente)));

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);

        // O destino ficou em branco: a guarda de origem roda ANTES da seta 2. Um pagamento que nao
        // tem de onde sair nao precisa saber para onde iria, e resolver a chave primeiro exporia o
        // DICT a consultas de quem nem e correntista.
        Assert.Null(transacao.IspbDestino);
        Assert.Null(transacao.ContaDestino);

        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);
        Assert.Equal(commitsDoPagador, cenario.CommitsDoPagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);

        cenario.ExigirConservacao();
    }

    [Fact]
    [Trait("gate", "3")]
    public void Pagar_AcimaDoSaldoDoCliente_RejeitaSemDebitarESemDespacharPacs008()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsDoPagador = cenario.CommitsDoPagador;

        RespostaDePagamento resposta = cenario.Pagar(e2e, FundoInicial + 1);

        Assert.Equal(EstadoTransacao.Rejeitada, resposta.Estado);
        Assert.Equal(MotivoRejeicaoLocal.SaldoInsuficiente, resposta.Motivo);

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);

        // Aqui o DICT ja tinha resolvido: a recusa e do saldo, e nao da chave. Sem esta assercao,
        // um defeito que fizesse a resolucao falhar silenciosamente passaria por "saldo insuficiente".
        Assert.Equal(IspbRecebedor, transacao.IspbDestino);
        Assert.Equal(cenario.ContaRecebedor, transacao.ContaDestino);

        Assert.False(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(e2e));
        Assert.Equal(commitsDoPagador, cenario.CommitsDoPagador);
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);

        cenario.ExigirConservacao();
    }

    /// <summary>
    /// O limite exato do lado de dentro. A guarda de saldo compara com <c>&lt;</c>, nao com
    /// <c>&lt;=</c>: zerar a conta e uma operacao legitima, e um off-by-one aqui recusaria o
    /// pagamento que consome todo o saldo — falha que so aparece no valor de fronteira.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Pagar_ExatamenteOSaldoDisponivel_EhAceitoELiquidaZerandoAConta()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        RespostaDePagamento resposta = cenario.Pagar(e2e, FundoInicial);

        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);
        Assert.Null(resposta.Motivo);

        Assert.Equal(3, cenario.Motor.Barramento.Drenar());

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.Null(transacao.MotivoRejeicao);

        Assert.Equal(0L, cenario.SaldoDoClientePagador);
        Assert.Equal(0L, cenario.SaldoEspelhoPagador);
        Assert.Equal(0L, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial * 2, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(FundoInicial * 2, cenario.SaldoEspelhoRecebedor);
        Assert.Equal(FundoInicial * 2, cenario.SaldoPiRecebedor);

        cenario.ExigirConservacao();
    }

    /// <summary>
    /// Aresta estados:10 com estorno. O cliente tem saldo, o PSP nao tem lastro na conta PI — e so
    /// o SPI, que e quem detem a conta PI, pode descobrir isso.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Liquidacao_ComContaPiSemSaldo_RejeitaEEstornaODebitoPorLancamentoNovo()
    {
        Cenario cenario = Cenario.Montar();
        ContaId clienteRico = cenario.AbrirClienteNoPagador(ContaDoClienteRico, FundoDoClienteRico);
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsDoSpi = cenario.CommitsDoSpi;

        RespostaDePagamento resposta = cenario.Pagar(e2e, ValorAcimaDaContaPi, conta: ContaDoClienteRico);

        // A validacao local passa: o cliente tem os 3.000,00. O debito e a seta 4 acontecem.
        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.Equal(FundoDoClienteRico - ValorAcimaDaContaPi, cenario.SaldoDoCliente(clienteRico));

        int commitsAposODebito = cenario.CommitsDoPagador;

        // Duas entregas, nao tres: o RJCT so vai ao pagador.
        Assert.Equal(2, cenario.Motor.Barramento.Drenar());

        Assert.True(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? confirmacao));
        Assert.NotNull(confirmacao);
        Assert.Equal(StatusPacs002.Rjct, confirmacao.Status);
        Assert.Equal(MotivoRejeicao.SaldoInsuficienteNaContaPi, confirmacao.Motivo);

        Assert.True(cenario.Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Rejeitada, transacao.Estado);
        Assert.Equal(MotivoRejeicaoLocal.RejeitadaPeloSpi, transacao.MotivoRejeicao);

        // O ponto central: o debito nao foi corrigido, foi COMPENSADO. Os dois lancamentos coexistem
        // no log, e o log so cresce.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.True(cenario.Motor.Pagador.Ledger.HouveEstorno(e2e));
        Assert.Equal(commitsAposODebito + 1, cenario.CommitsDoPagador);

        // O SPI nao gravou nada: a guarda de saldo negativo e avaliada antes do append.
        Assert.Equal(commitsDoSpi, cenario.CommitsDoSpi);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);

        // O cliente voltou ao saldo inicial, pelo caminho longo.
        Assert.Equal(FundoDoClienteRico, cenario.SaldoDoCliente(clienteRico));
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial + FundoDoClienteRico, cenario.SaldoEspelhoPagador);

        // Note que o espelho do pagador (6.000,00) e maior que a conta PI dele (1.000,00) mesmo em
        // repouso: e essa divergencia deliberada que produz o cenario. Por isso a conservacao aqui
        // nao pode ser "espelho igual a conta PI" — ver ExigirConservacao.
        cenario.ExigirConservacao();
    }

    /// <summary>
    /// Duas transacoes terminam em REJEITADA no mesmo motor, e o ledger fica diferente em cada uma.
    /// <para>
    /// Estornar a que morreu em RECEBIDA criaria dinheiro do nada: creditar de volta um debito que
    /// nunca foi lancado e emissao pura. Por isso a condicao do estorno e uma propriedade do
    /// <em>ledger</em> — "existe debito para este E2E sem estorno correspondente?" —, e nao do
    /// estado de origem da transicao. Se fosse do estado, esta assimetria seria invisivel: os dois
    /// casos exibem exatamente o mesmo <c>EstadoTransacao.Rejeitada</c>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Estorno_ExisteSoQuandoHouveDebito_DuasRejeitadasComConsequenciasContabeisOpostas()
    {
        Cenario cenario = Cenario.Montar();
        ContaId clienteRico = cenario.AbrirClienteNoPagador(ContaDoClienteRico, FundoDoClienteRico);

        EndToEndId comDebito = cenario.ProximoE2e();
        EndToEndId semDebito = cenario.ProximoE2e();

        // Vai ate ENVIADA_SPI e volta como RJCT (estados:10).
        cenario.Pagar(comDebito, ValorAcimaDaContaPi, conta: ContaDoClienteRico);

        // Morre na validacao local (estados:5), sem nunca tocar o ledger.
        ChavePix orfa = ChavePix.Criar(TipoChave.Email, "ninguem@exemplo.com");
        cenario.Pagar(semDebito, ValorDoPagamento, orfa);

        cenario.Motor.Barramento.Drenar();

        Assert.True(cenario.Motor.Pagador.TryTransacao(comDebito, out VistaDaTransacao? primeira));
        Assert.True(cenario.Motor.Pagador.TryTransacao(semDebito, out VistaDaTransacao? segunda));
        Assert.NotNull(primeira);
        Assert.NotNull(segunda);

        // Mesmo estado final nas duas.
        Assert.Equal(EstadoTransacao.Rejeitada, primeira.Estado);
        Assert.Equal(EstadoTransacao.Rejeitada, segunda.Estado);

        // Motivos e caminhos diferentes.
        Assert.Equal(MotivoRejeicaoLocal.RejeitadaPeloSpi, primeira.MotivoRejeicao);
        Assert.Equal(MotivoRejeicaoLocal.ChaveNaoEncontrada, segunda.MotivoRejeicao);
        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Historico[^1].Origem);
        Assert.Equal(EstadoTransacao.Recebida, segunda.Historico[^1].Origem);

        // E consequencias contabeis opostas.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(comDebito));
        Assert.True(cenario.Motor.Pagador.Ledger.HouveEstorno(comDebito));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveDebito(semDebito));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(semDebito));

        Assert.Equal(FundoDoClienteRico, cenario.SaldoDoCliente(clienteRico));
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial + FundoDoClienteRico, cenario.SaldoEspelhoPagador);

        cenario.ExigirConservacao();
    }

    /// <summary>
    /// A chave resolve, mas a conta apontada nunca foi aberta no ledger do recebedor.
    /// <para>
    /// O SPI pergunta <c>PodeCreditar</c> <b>antes</b> de liquidar. Sem essa guarda, a seta 5
    /// aconteceria e so a seta 7 estouraria: haveria dinheiro movido entre as contas PI sem
    /// contrapartida no ledger do recebedor. A soma por ledger continuaria fechando em zero nos
    /// tres — partidas dobradas nao sabem que a operacao ficou pela metade — e a divergencia
    /// ficaria orfa, visivel so na conciliacao do gate 7.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Liquidacao_ComContaDeDestinoNuncaAberta_EhRecusadaAntesDeMoverAsContasPi()
    {
        Cenario cenario = Cenario.Montar();
        ChavePix fantasma = cenario.VincularChaveEmContaNuncaAberta();
        EndToEndId e2e = cenario.ProximoE2e();

        int commitsDoSpi = cenario.CommitsDoSpi;
        int commitsDoRecebedor = cenario.CommitsDoRecebedor;

        RespostaDePagamento resposta = cenario.Pagar(e2e, ValorDoPagamento, fantasma);
        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);

        Assert.Equal(2, cenario.Motor.Barramento.Drenar());

        Assert.True(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? confirmacao));
        Assert.NotNull(confirmacao);
        Assert.Equal(StatusPacs002.Rjct, confirmacao.Status);
        Assert.Equal(MotivoRejeicao.ContaDeDestinoIndisponivel, confirmacao.Motivo);
        Assert.Null(confirmacao.Referencia);

        // O ponto: as contas PI nao se moveram, e o ledger do SPI nao ganhou commit nenhum.
        Assert.Equal(commitsDoSpi, cenario.CommitsDoSpi);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);

        // E o recebedor continua intocado: nem conta nova, nem credito.
        Assert.Equal(commitsDoRecebedor, cenario.CommitsDoRecebedor);
        Assert.False(cenario.Motor.Recebedor.PodeCreditar(cenario.ContaFantasma));
        Assert.False(cenario.Motor.Recebedor.Ledger.HouveCredito(e2e));
        Assert.Null(cenario.Motor.Recebedor.UltimoCreditoEm);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);

        // No pagador, debito e estorno.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.True(cenario.Motor.Pagador.Ledger.HouveEstorno(e2e));
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoPagador);

        cenario.ExigirConservacao();
    }

    /// <summary>
    /// <c>pacs.008</c> enderecado a um ISPB que o SPI nao conhece.
    /// <para>
    /// A ordem e entregue direto ao SPI, e nao pelo barramento: nenhum PSP montado consegue
    /// produzi-la, ja que o destino sai do DICT e o DICT so conhece participantes reais. O
    /// <c>pacs.002</c> de volta fica pendente de proposito — o pagador nao tem transacao para este
    /// E2E, entao drenar seria entregar resultado a quem nao pediu nada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void ReceberPacs008_ComParticipanteForaDoSpi_EhRejeitadoSemLiquidar()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId e2e = cenario.ProximoE2e();

        Pacs008 ordem = new(
            e2e,
            IspbPagador,
            cenario.ContaPagador,
            IspbForaDoSpi,
            ContaId.Cliente(LedgerId.Psp(IspbForaDoSpi), "0004"),
            Valor.DeCentavos(ValorDoPagamento),
            ChavePix.Criar(TipoChave.Email, "terceiro@exemplo.com"));

        int commitsDoSpi = cenario.CommitsDoSpi;

        cenario.Motor.Spi.ReceberPacs008(ordem);

        Assert.True(cenario.Motor.Spi.TryRespostaDe(e2e, out Pacs002? confirmacao));
        Assert.NotNull(confirmacao);
        Assert.Equal(StatusPacs002.Rjct, confirmacao.Status);
        Assert.Equal(MotivoRejeicao.ParticipanteDesconhecido, confirmacao.Motivo);
        Assert.Null(confirmacao.Referencia);

        // Uma unica mensagem de volta, e so ao debtor.
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);

        Assert.Equal(commitsDoSpi, cenario.CommitsDoSpi);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);

        cenario.ExigirConservacao();
    }

    /// <summary>Motor montado, com os atalhos de leitura e os enxertos que cada rejeicao precisa.</summary>
    private sealed class Cenario
    {
        public const string ContaDoPagador = "0001";
        public const string ContaDoRecebedor = "0002";

        /// <summary>Numero de conta valido em forma, jamais aberto em ledger nenhum.</summary>
        public const string ContaInexistente = "9999";

        private readonly GeradorE2eDeterministico _gerador;
        private readonly List<ContaId> _clientesDoPagador;
        private readonly List<ContaId> _clientesDoRecebedor;

        private Cenario(MotorMontado motor, GeradorE2eDeterministico gerador, ChavePix chaveDoRecebedor)
        {
            Motor = motor;
            _gerador = gerador;
            ChaveDoRecebedor = chaveDoRecebedor;

            ContaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
            ContaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor);
            ContaFantasma = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaInexistente);

            _clientesDoPagador = [ContaPagador];
            _clientesDoRecebedor = [ContaRecebedor];
        }

        public MotorMontado Motor { get; }

        public ChavePix ChaveDoRecebedor { get; }

        public ContaId ContaPagador { get; }

        public ContaId ContaRecebedor { get; }

        /// <summary>Conta do recebedor que nunca foi aberta; usada pela guarda <c>PodeCreditar</c>.</summary>
        public ContaId ContaFantasma { get; }

        public long SaldoDoClientePagador => SaldoDoCliente(ContaPagador);

        public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

        public long SaldoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoEspelhoRecebedor => Motor.Recebedor.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoPiPagador => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

        public long SaldoPiRecebedor => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;

        public int CommitsDoPagador => Motor.Pagador.Ledger.Consulta.Log().Count;

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

            return new Cenario(motor, new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca), chave);
        }

        public EndToEndId ProximoE2e() => _gerador.Proximo();

        public long SaldoDoCliente(ContaId conta) => Motor.Pagador.Ledger.SaldoDoCliente(conta).Centavos;

        public RespostaDePagamento Pagar(
            EndToEndId e2e,
            long centavos,
            ChavePix? chave = null,
            string conta = ContaDoPagador) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(
                e2e,
                conta,
                chave ?? ChaveDoRecebedor,
                Valor.DeCentavos(centavos)));

        /// <summary>
        /// Abre um segundo cliente no pagador com genesis proprio. O aporte entra no espelho do PSP
        /// mas <em>nao</em> na conta PI do SPI: e assim que o cenario da aresta estados:10 nasce.
        /// </summary>
        public ContaId AbrirClienteNoPagador(string numeroDaConta, long centavos)
        {
            ContaId conta = Motor.Pagador.AbrirCliente(numeroDaConta, Valor.DeCentavos(centavos));
            _clientesDoPagador.Add(conta);
            return conta;
        }

        /// <summary>
        /// Vincula uma chave direto no DICT, apontando para conta que o recebedor nunca abriu.
        /// <para>
        /// Vai pelo diretorio, e nao por <c>PspRecebedor.RegistrarChave</c>, porque aquele caminho
        /// confere a existencia da conta e recusaria — a guarda do recebedor e justamente o que
        /// impede este estado de nascer pela porta da frente. O que este teste exercita e a segunda
        /// linha de defesa: o SPI perguntando antes de liquidar.
        /// </para>
        /// </summary>
        public ChavePix VincularChaveEmContaNuncaAberta()
        {
            ChavePix fantasma = ChavePix.Criar(TipoChave.Email, "fantasma@exemplo.com");
            Motor.Dict.Vincular(fantasma, IspbRecebedor, ContaFantasma);
            return fantasma;
        }

        /// <summary>
        /// Nenhum dinheiro criado nem destruido, em duas leituras independentes.
        /// <para>
        /// A primeira e o pool do SPI: o genesis foi a unica emissao, e a liquidacao so troca o dono
        /// do saldo entre contas PI. A segunda e, dentro de cada PSP, o espelho valer exatamente o
        /// que o PSP deve aos proprios clientes — debito, credito e estorno mexem nos dois lados na
        /// mesma medida, entao a igualdade so quebra se um lancamento perder uma perna.
        /// </para>
        /// <para>
        /// Deliberadamente <b>nao</b> se afirma aqui que o espelho iguala a conta PI: essa igualdade
        /// vale apenas em quiescencia e num motor sem aporte assimetrico, e o cenario da conta PI
        /// sem saldo depende de quebra-la.
        /// </para>
        /// </summary>
        public void ExigirConservacao()
        {
            Assert.Equal(FundoInicial * 2, SaldoPiPagador + SaldoPiRecebedor);
            Assert.Equal(SaldoEspelhoPagador, Somar(Motor.Pagador.Ledger, _clientesDoPagador));
            Assert.Equal(SaldoEspelhoRecebedor, Somar(Motor.Recebedor.Ledger, _clientesDoRecebedor));
        }

        private static long Somar(LedgerPsp ledger, IReadOnlyList<ContaId> contas)
        {
            long total = 0;

            foreach (ContaId conta in contas)
            {
                total += ledger.SaldoDoCliente(conta).Centavos;
            }

            return total;
        }
    }
}
