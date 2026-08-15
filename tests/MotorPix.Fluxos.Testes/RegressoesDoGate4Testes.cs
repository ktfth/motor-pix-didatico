using MotorPix.Composicao;
using MotorPix.Contratos;
using MotorPix.Dict;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Spi;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Regressoes dos dois defeitos que a revisao adversarial do gate 4 encontrou.
/// <para>
/// O primeiro deles nasceu de uma correcao apressada: soltar a reivindicacao de idempotencia
/// sempre que o processamento falhasse, sem perguntar se algo ja tinha sido gravado no ledger.
/// </para>
/// </summary>
public sealed class RegressoesDoGate4Testes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // ---------------------------------------------------------------------------------------
    // C1 — compensacao nao pode apagar o registro de um debito ja commitado
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "4")]
    public void Pagar_QuandoODespachoFalhaDepoisDoDebito_MantemTransacaoEE2eQueimadosParaAConciliacao()
    {
        // O ledger e append-only e nao volta atras. Se a compensacao soltasse a reivindicacao aqui,
        // o cliente ficaria debitado com TryTransacao falso, registro de idempotencia vazio e
        // nenhuma divergencia anotada — so o ledger saberia, e a soma continuaria constante, entao
        // nenhuma propriedade contabil acusaria.
        RelogioFake relogio = new();
        BarramentoQueRecusaDespacho barramento = new();
        DiretorioDeChavesEmMemoria dict = new();
        PspPagador.PspPagador pagador = new(IspbPagador, relogio, dict, barramento);

        ContaId origem = pagador.AbrirCliente("0001", Valor.DeCentavos(Fundo));
        ContaId destino = ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0002");
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        dict.Vincular(chave, IspbRecebedor, destino);

        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = new(e2e, "0001", chave, Valor.DeCentavos(Pagamento));

        Assert.Throws<MensageriaInvalidaException>(() => pagador.Pagar(comando));

        // O debito aconteceu e permanece: e append-only.
        Assert.True(pagador.Ledger.HouveDebito(e2e));
        Assert.Equal(Fundo - Pagamento, pagador.Ledger.SaldoDoCliente(origem).Centavos);

        // E por isso a transacao e a reivindicacao PERMANECEM: o E2E fica queimado de proposito, e
        // a transacao sobra como registro para a conciliacao encontrar.
        Assert.True(pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.EnviadaSpi, transacao.Estado);
        Assert.True(pagador.Idempotencia.TryEntrada(e2e, out _));
    }

    [Fact]
    [Trait("gate", "4")]
    public void Pagar_QuandoFalhaAntesDeQualquerLancamento_LiberaOE2eParaQueOClientePossaReenviar()
    {
        // O outro lado da mesma moeda: sem debito lancado, nada e irreversivel, e queimar o
        // identificador impediria para sempre um pagamento que nunca aconteceu.
        RelogioFake relogio = new();
        BarramentoEmMemoria barramento = new();
        DiretorioQueLanca dict = new();
        PspPagador.PspPagador pagador = new(IspbPagador, relogio, dict, barramento);

        ContaId origem = pagador.AbrirCliente("0001", Valor.DeCentavos(Fundo));
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = new(e2e, "0001", chave, Valor.DeCentavos(Pagamento));

        Assert.Throws<ChaveNaoEncontradaNoDictException>(() => pagador.Pagar(comando));

        Assert.False(pagador.Ledger.HouveDebito(e2e));
        Assert.False(pagador.TryTransacao(e2e, out _));
        Assert.False(pagador.Idempotencia.TryEntrada(e2e, out _));
        Assert.Equal(0, pagador.Idempotencia.Quantidade);

        // E o reenvio do mesmo E2E funciona, agora que o DICT responde.
        dict.PararDeLancar(chave, IspbRecebedor, ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0002"));

        RespostaDePagamento resposta = pagador.Pagar(comando);

        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);
        Assert.True(pagador.Ledger.HouveDebito(e2e));
        Assert.Equal(Fundo - Pagamento, pagador.Ledger.SaldoDoCliente(origem).Centavos);
    }

    // ---------------------------------------------------------------------------------------
    // A1 — o dedup do SPI nao pode ser replay cego
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "4")]
    public void ReceberPacs008_ComOMesmoE2eEOrdemDiferente_RecusaEmVezDeReplicarOAcscDaOutraOrdem()
    {
        // Sem a impressao da ordem, a reentrega devolveria o ACSC original — cujo OrgnlTxRef
        // descreve OUTRA ordem. O SPI afirmaria "liquidado" para algo que nunca tocou o ledger, e
        // afirmaria isso com o bloco de referencia de um pagamento diferente. E a mesma falha que a
        // decisao D3 proibiu na API do PSP; nao havia motivo para a camada de baixo ser mais
        // permissiva que a de cima.
        RelogioFake relogio = new();
        BarramentoEmMemoria barramento = new();
        EspiaoDeParticipante pagador = new(IspbPagador);
        EspiaoDeParticipante recebedor = new(IspbRecebedor);
        barramento.RegistrarParticipante(pagador);
        barramento.RegistrarParticipante(recebedor);

        SpiEmMemoria spi = new(relogio, barramento, barramento.Participantes);
        barramento.RegistrarSpi(spi);
        spi.AbrirContaPi(IspbPagador);
        spi.AbrirContaPi(IspbRecebedor);
        spi.AportarNoGenesis(IspbPagador, Valor.DeCentavos(Fundo));
        spi.AportarNoGenesis(IspbRecebedor, Valor.DeCentavos(Fundo));

        ContaId contaPagador = ContaId.Cliente(LedgerId.Psp(IspbPagador), "0001");
        ContaId contaRecebedor = ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0002");
        ContaId outraContaRecebedor = ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0003");
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        EndToEndId e2e = E2eDe(1);

        spi.ReceberPacs008(new Pacs008(
            e2e, IspbPagador, contaPagador, IspbRecebedor, contaRecebedor,
            Valor.DeCentavos(Pagamento), chave));

        barramento.Drenar();

        Assert.Equal(StatusPacs002.Acsc, Assert.Single(recebedor.Recebidas).Status);
        long piRecebedorApos = spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;
        int commitsApos = spi.Consulta.Log().Count;

        // Mesmo E2E, creditor e valor diferentes.
        spi.ReceberPacs008(new Pacs008(
            e2e, IspbPagador, contaPagador, IspbRecebedor, outraContaRecebedor,
            Valor.DeCentavos(999_99), chave));

        barramento.Drenar();

        // A resposta guardada continua sendo a da ordem verdadeira.
        Assert.True(spi.TryRespostaDe(e2e, out Pacs002? guardada));
        Assert.NotNull(guardada);
        Assert.Equal(StatusPacs002.Acsc, guardada.Status);
        Assert.Equal(contaRecebedor, guardada.Referencia!.ContaCreditor);

        // Nada moveu por causa da impostora.
        Assert.Equal(piRecebedorApos, spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos);
        Assert.Equal(commitsApos, spi.Consulta.Log().Count);

        // E o que foi despachado pela impostora e um RJCT, nao um ACSC alheio. O recebedor nao
        // recebeu segunda mensagem nenhuma: RJCT so vai ao pagador.
        Pacs002 ultimaAoPagador = pagador.Recebidas[^1];
        Assert.Equal(StatusPacs002.Rjct, ultimaAoPagador.Status);
        Assert.Equal(MotivoRejeicao.OrdemInvalida, ultimaAoPagador.Motivo);
        Assert.Single(recebedor.Recebidas);
    }

    [Fact]
    [Trait("gate", "4")]
    public void ReceberPacs008_ComAMesmaOrdemExata_ContinuaDevolvendoAMesmaRespostaSemLiquidarDeNovo()
    {
        // A guarda acima nao pode ter quebrado a reentrega legitima, que e a razao de o dedup existir.
        MotorMontado motor = MontarMotor();
        ContaId contaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, "0002");
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, "0001");
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        motor.Recebedor.RegistrarChave(chave, contaRecebedor);

        EndToEndId e2e = E2eDe(1);
        Pacs008 ordem = new(
            e2e, IspbPagador, contaPagador, IspbRecebedor, contaRecebedor,
            Valor.DeCentavos(Pagamento), chave);

        motor.Spi.ReceberPacs008(ordem);
        Assert.True(motor.Spi.TryRespostaDe(e2e, out Pacs002? primeira));

        long piApos = motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;
        int commitsApos = motor.Spi.Consulta.Log().Count;

        motor.Spi.ReceberPacs008(ordem);

        Assert.True(motor.Spi.TryRespostaDe(e2e, out Pacs002? segunda));
        Assert.Same(primeira, segunda);
        Assert.Equal(piApos, motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos);
        Assert.Equal(commitsApos, motor.Spi.Consulta.Log().Count);
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    private static MotorMontado MontarMotor() =>
        MotorMontado.Montar(new RelogioFake(), IspbPagador, IspbRecebedor, Valor.DeCentavos(Fundo));

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);

    /// <summary>Participante que so anota o que recebeu, para o teste inspecionar o despachado.</summary>
    private sealed class EspiaoDeParticipante(Ispb ispb) : IParticipante
    {
        private readonly List<Pacs002> _recebidas = [];

        public Ispb Ispb { get; } = ispb;

        public IReadOnlyList<Pacs002> Recebidas => _recebidas;

        public void Receber(Pacs002 confirmacao)
        {
            ArgumentNullException.ThrowIfNull(confirmacao);
            _recebidas.Add(confirmacao);
        }

        public bool PodeCreditar(ContaId conta) => true;
    }

    /// <summary>Barramento que aceita enfileirar tudo, menos a ordem de pagamento da seta 4.</summary>
    private sealed class BarramentoQueRecusaDespacho : IBarramento
    {
        public int Pendentes => 0;

        public void Enfileirar(Envelope envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            if (envelope is EnvelopePacs008)
            {
                throw new MensageriaInvalidaException("falha injetada no despacho da seta 4");
            }
        }

        public int Drenar() => 0;
    }

    /// <summary>Diretorio que recusa a primeira consulta e depois passa a responder.</summary>
    private sealed class DiretorioQueLanca : IDiretorioChaves
    {
        private ResolucaoChave? _resolucao;

        public void PararDeLancar(ChavePix chave, Ispb ispb, ContaId conta) =>
            _resolucao = new ResolucaoChave(chave, ispb, conta);

        public ResolucaoChave Resolver(ChavePix chave) =>
            _resolucao ?? throw new ChaveNaoEncontradaNoDictException(chave);

        public bool TryResolver(
            ChavePix chave,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResolucaoChave? resolucao)
        {
            if (_resolucao is null)
            {
                throw new ChaveNaoEncontradaNoDictException(chave);
            }

            resolucao = _resolucao;
            return true;
        }
    }
}
