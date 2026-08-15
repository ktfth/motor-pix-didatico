using MotorPix.Composicao;
using MotorPix.Contratos;
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
/// Regressoes dos defeitos que a revisao adversarial do gate 3 encontrou.
/// <para>
/// Cada teste aqui falharia antes da correspondente correcao. Sem eles, a correcao e so uma
/// afirmacao — e o proximo refactor a desfaz sem que nada acuse.
/// </para>
/// </summary>
public sealed class RegressoesDaRevisaoTestes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // ---------------------------------------------------------------------------------------
    // C1 — uma entrega que lanca nao pode levar junto as mensagens seguintes
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "3")]
    public void Drenar_QuandoUmaEntregaLanca_PreservaAMensagemQueFalhouEAsSeguintes()
    {
        // A versao anterior tirava a rodada inteira da fila antes de entregar. Uma entrega que
        // lancasse apagava as seguintes em definitivo: o SPI liquidava, o credito do recebedor
        // nunca acontecia, Pendentes voltava a zero — e a soma por ledger continuava fechando,
        // entao nenhuma propriedade contabil acusava a perda.
        BarramentoEmMemoria barramento = new();
        ParticipanteHostil hostil = new(IspbPagador);
        ParticipanteContador saudavel = new(IspbRecebedor);

        barramento.RegistrarParticipante(hostil);
        barramento.RegistrarParticipante(saudavel);

        Pacs002 confirmacao = Pacs002.Rejeitado(E2eDe(1), MotivoRejeicao.OrdemInvalida);
        barramento.Enfileirar(new EnvelopePacs002(IspbPagador, confirmacao));
        barramento.Enfileirar(new EnvelopePacs002(IspbRecebedor, confirmacao));

        Assert.Throws<TransacaoDesconhecidaException>(() => barramento.Drenar());

        // Nada foi entregue e nada se perdeu: as duas mensagens continuam na fila.
        Assert.Equal(2, barramento.Pendentes);
        Assert.Equal(0, barramento.TotalEntregue);
        Assert.Equal(0, saudavel.Recebidas);

        // Removida a causa da falha, a redrenagem entrega as duas — inclusive a que tinha falhado.
        hostil.PararDeLancar();
        int entregues = barramento.Drenar();

        Assert.Equal(2, entregues);
        Assert.Equal(0, barramento.Pendentes);
        Assert.Equal(2, barramento.TotalEntregue);
        Assert.Equal(1, saudavel.Recebidas);
        Assert.Equal(1, hostil.Recebidas);
    }

    [Fact]
    [Trait("gate", "3")]
    public void Drenar_DeDentroDeUmaEntrega_EhRecusadoSemPerderMensagem()
    {
        BarramentoEmMemoria barramento = new();
        ParticipanteQueDrena reentrante = new(IspbPagador, barramento);
        barramento.RegistrarParticipante(reentrante);

        barramento.Enfileirar(new EnvelopePacs002(
            IspbPagador,
            Pacs002.Rejeitado(E2eDe(1), MotivoRejeicao.OrdemInvalida)));

        MensageriaInvalidaException excecao = Assert.Throws<MensageriaInvalidaException>(() => barramento.Drenar());

        Assert.Contains("reentrante", excecao.Motivo, StringComparison.Ordinal);
        Assert.Equal(1, barramento.Pendentes);
    }

    // ---------------------------------------------------------------------------------------
    // C2 — nao pode existir caminho com debito lancado e sem pacs.008 a caminho
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "3")]
    public void Pagar_ComChaveApontandoParaAPropriaContaDeOrigem_RejeitaSemDebitarNada()
    {
        // O pacs.008 e montado ANTES do debito. Se fosse montado depois, o construtor lancaria com
        // o cliente ja debitado: transacao congelada em ENVIADA_SPI, nenhuma mensagem a caminho, e
        // partida dobrada intacta — invisivel para toda propriedade contabil.
        MotorMontado motor = MontarMotor();
        ContaId contaDoPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, "0001");

        ChavePix chave = ChavePix.Criar(TipoChave.Email, "eu@exemplo.com");
        motor.Dict.Vincular(chave, IspbPagador, contaDoPagador);

        EndToEndId e2e = E2eDe(1);
        RespostaDePagamento resposta = motor.Pagador.Pagar(
            new ComandoDePagamento(e2e, "0001", chave, Valor.DeCentavos(Pagamento)));

        Assert.Equal(EstadoTransacao.Rejeitada, resposta.Estado);
        Assert.Equal(MotivoRejeicaoLocal.OrdemInvalida, resposta.Motivo);

        // O ponto central: nenhum debito, nenhuma mensagem, saldo intacto.
        Assert.False(motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.Equal(0, motor.Barramento.Pendentes);
        Assert.Equal(Fundo, motor.Pagador.Ledger.SaldoDoCliente(contaDoPagador).Centavos);
    }

    // ---------------------------------------------------------------------------------------
    // C3 — nenhuma excecao pode sair de ReceberPacs008
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "3")]
    public void Pagar_ComChaveApontandoParaOutraContaDoProprioPagador_ParaNaGuardaDeCredito()
    {
        // Defesa em profundidade: a guarda PodeCreditar do participante creditor recusa ANTES de o
        // SPI tentar liquidar. O pagador so honra credito de devolucao, e nao ha transacao liquidada
        // apontando para a conta de destino — entao ele responde false, e o lancamento
        // intra-participante que Lancamento.Criar recusaria nem chega a ser tentado.
        MotorMontado motor = MontarMotor();
        ContaId segundaContaDoPagador = motor.Pagador.AbrirCliente("0009", Valor.DeCentavos(Fundo));
        ContaId contaDeOrigem = ContaId.Cliente(motor.Pagador.Ledger.Id, "0001");

        ChavePix chave = ChavePix.Criar(TipoChave.Email, "vizinho@exemplo.com");
        motor.Dict.Vincular(chave, IspbPagador, segundaContaDoPagador);

        EndToEndId e2e = E2eDe(1);
        RespostaDePagamento resposta = motor.Pagador.Pagar(
            new ComandoDePagamento(e2e, "0001", chave, Valor.DeCentavos(Pagamento)));

        // A ordem e montavel (contas diferentes), entao o pagamento sai e o debito acontece.
        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);
        Assert.True(motor.Pagador.Ledger.HouveDebito(e2e));

        long piPagadorAntes = motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

        Assert.Equal(2, motor.Barramento.Drenar());

        Assert.True(motor.Spi.TryRespostaDe(e2e, out Pacs002? resultado));
        Assert.NotNull(resultado);
        Assert.Equal(StatusPacs002.Rjct, resultado.Status);
        Assert.Equal(MotivoRejeicao.ContaDeDestinoIndisponivel, resultado.Motivo);

        // Nada moveu no SPI, e o debito do cliente foi estornado por lancamento novo.
        Assert.Equal(piPagadorAntes, motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos);
        Assert.True(motor.Pagador.Ledger.HouveEstorno(e2e));
        Assert.Equal(Fundo, motor.Pagador.Ledger.SaldoDoCliente(contaDeOrigem).Centavos);
    }

    [Fact]
    [Trait("gate", "3")]
    public void ReceberPacs008_QuandoOLancamentoDeLiquidacaoERecusado_ViraRjctEmVezDeExcecao()
    {
        // Alcanca o catch amplo do SPI de proposito: um participante permissivo aceita creditar,
        // entao a guarda anterior nao intercepta e o lancamento PI(x) -> PI(x) e de fato tentado.
        // Lancamento.Criar recusa com razao; o que nao pode e a excecao escapar de ReceberPacs008 —
        // o E2E ficaria sem resposta registrada (nem a reentrega produziria pacs.002), o pagador
        // ficaria em ENVIADA_SPI com o cliente debitado, e a drenagem morreria no meio.
        RelogioFake relogio = new();
        BarramentoEmMemoria barramento = new();
        ParticipanteContador permissivo = new(IspbPagador);
        barramento.RegistrarParticipante(permissivo);

        SpiEmMemoria spi = new(relogio, barramento, barramento.Participantes);
        barramento.RegistrarSpi(spi);
        spi.AbrirContaPi(IspbPagador);
        spi.AportarNoGenesis(IspbPagador, Valor.DeCentavos(Fundo));

        LedgerId ledgerDoPagador = LedgerId.Psp(IspbPagador);
        EndToEndId e2e = E2eDe(7);

        Pacs008 intraParticipante = new(
            e2e,
            IspbPagador,
            ContaId.Cliente(ledgerDoPagador, "0001"),
            IspbPagador,
            ContaId.Cliente(ledgerDoPagador, "0009"),
            Valor.DeCentavos(Pagamento),
            ChavePix.Criar(TipoChave.Email, "vizinho@exemplo.com"));

        long piAntes = spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

        // Nao lanca.
        spi.ReceberPacs008(intraParticipante);

        Assert.True(spi.TryRespostaDe(e2e, out Pacs002? resultado));
        Assert.NotNull(resultado);
        Assert.Equal(StatusPacs002.Rjct, resultado.Status);
        Assert.Equal(MotivoRejeicao.OrdemInvalida, resultado.Motivo);
        Assert.Equal(piAntes, spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos);

        // E o RJCT segue para o pagador normalmente, sem interromper a entrega.
        Assert.Equal(1, barramento.Drenar());
        Assert.Equal(1, permissivo.Recebidas);
    }

    // ---------------------------------------------------------------------------------------
    // A2 — o adapter classifica antes de aplicar
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "3")]
    public void Receber_OMesmoAcscDepoisDeLiquidada_EhNoOpEmVezDeExcecaoAtravessandoOBarramento()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaRecebedor = PrepararDestino(motor);
        EndToEndId e2e = PagarELiquidar(motor);

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        int transicoesAntes = transacao.Historico.Count;
        long saldoAntes = motor.Recebedor.Ledger.SaldoDoCliente(contaRecebedor).Centavos;

        // A mensagem concorda com o estado: chegou duas vezes, so isso. Reentrega e o caso normal
        // do gate 4 e do pacs.002 atrasado do gate 5 — nao pode explodir atravessando o barramento.
        Pacs002 reentrega = Pacs002.Aceito(
            e2e,
            new OrgnlTxRef(
                IspbPagador,
                ContaId.Cliente(motor.Pagador.Ledger.Id, "0001"),
                IspbRecebedor,
                contaRecebedor,
                Valor.DeCentavos(Pagamento)));

        motor.Pagador.Receber(reentrega);

        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.Equal(transicoesAntes, transacao.Historico.Count);
        Assert.Empty(motor.Pagador.Divergencias);
        Assert.Equal(saldoAntes, motor.Recebedor.Ledger.SaldoDoCliente(contaRecebedor).Centavos);
    }

    [Fact]
    [Trait("gate", "3")]
    public void Receber_UmRjctDepoisDeLiquidada_RegistraDivergenciaEmVezDeEngolirOuExplodir()
    {
        // O SPI diz que rejeitou uma transacao que o PSP registrou como liquidada. O sintoma e
        // contabil, nao de protocolo: o dinheiro moveu no ledger do SPI e o PSP acredita no
        // contrario. Engolir esconderia; deixar subir derrubaria a entrega das outras mensagens.
        MotorMontado motor = MontarMotor();
        PrepararDestino(motor);
        EndToEndId e2e = PagarELiquidar(motor);

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);

        int transicoesAntes = transacao.Historico.Count;

        motor.Pagador.Receber(Pacs002.Rejeitado(e2e, MotivoRejeicao.SaldoInsuficienteNaContaPi));

        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.Equal(transicoesAntes, transacao.Historico.Count);

        DivergenciaPatrimonial divergencia = Assert.Single(motor.Pagador.Divergencias);
        Assert.Equal(e2e, divergencia.E2E);
        Assert.Equal(nameof(EstadoTransacao.Liquidada), divergencia.EstadoDoPsp);
        Assert.Equal(nameof(StatusPacs002.Rjct), divergencia.StatusDoSpi);

        // Divergencia nao e estorno: o dinheiro nao pode ser mexido por suposicao.
        Assert.False(motor.Pagador.Ledger.HouveEstorno(e2e));
    }

    // ---------------------------------------------------------------------------------------
    // M1 / A1 — tipos de excecao e a guarda de credito do pagador
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ComE2eJaConhecido_DevolveARespostaCongeladaEmVezDeExcecao()
    {
        // No gate 3 esta colisao lancava E2eDuplicadoException, um tipo-ponte que existia so para
        // a colisao nao sair como ArgumentException do dicionario interno. O gate 4 substituiu a
        // ponte pelo comportamento que o diagrama pede: replay da resposta original.
        MotorMontado motor = MontarMotor();
        PrepararDestino(motor);
        EndToEndId e2e = E2eDe(1);
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        ComandoDePagamento comando = new(e2e, "0001", chave, Valor.DeCentavos(Pagamento));

        RespostaDePagamento primeira = motor.Pagador.Pagar(comando);
        int commitsAposPrimeira = motor.Pagador.Ledger.Consulta.Log().Count;

        RespostaDePagamento reenvio = motor.Pagador.Pagar(comando);

        Assert.Equal(primeira, reenvio);
        Assert.Equal(commitsAposPrimeira, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(1, motor.Pagador.Idempotencia.Quantidade);
    }

    [Fact]
    [Trait("gate", "6")]
    public void PodeCreditar_NoPagador_SoEhVerdadeiroQuandoHaExpectativaRealDeCredito()
    {
        // O pagador so sabe honrar credito de DEVOLUCAO. Responder "true" pela mera existencia da
        // conta reintroduziria o modo de falha que a ADR-009 criou esta guarda para impedir: o SPI
        // liquidaria, o ACSC chegaria para um E2E desconhecido e a entrega lancaria — com o
        // dinheiro ja movido entre contas PI e a fila envenenada.
        MotorMontado motor = MontarMotor();
        ContaId contaDoPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, "0001");
        ContaId contaRecebedor = PrepararDestino(motor);

        // A conta existe, mas nao ha nenhuma transacao liquidada que possa vir a ser devolvida.
        Assert.True(motor.Pagador.Ledger.ContemConta(contaDoPagador));
        Assert.False(motor.Pagador.PodeCreditar(contaDoPagador));

        // Depois de um pagamento liquidar, a expectativa existe.
        PagarELiquidar(motor);
        Assert.True(motor.Pagador.PodeCreditar(contaDoPagador));

        // O recebedor credita por chave vinculada, entao basta a conta de cliente existir.
        Assert.True(motor.Recebedor.PodeCreditar(contaRecebedor));

        // Nos dois, nunca conta estrutural nem conta inexistente.
        Assert.False(motor.Pagador.PodeCreditar(motor.Pagador.Ledger.EspelhoPi));
        Assert.False(motor.Recebedor.PodeCreditar(motor.Recebedor.Ledger.EspelhoPi));
        Assert.False(motor.Pagador.PodeCreditar(ContaId.Cliente(motor.Pagador.Ledger.Id, "9999")));
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    private static MotorMontado MontarMotor() =>
        MotorMontado.Montar(new RelogioFake(), IspbPagador, IspbRecebedor, Valor.DeCentavos(Fundo));

    private static ContaId PrepararDestino(MotorMontado motor)
    {
        ContaId conta = ContaId.Cliente(motor.Recebedor.Ledger.Id, "0002");
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

        if (!motor.Dict.TryResolver(chave, out _))
        {
            motor.Recebedor.RegistrarChave(chave, conta);
        }

        return conta;
    }

    private static EndToEndId PagarELiquidar(MotorMontado motor)
    {
        EndToEndId e2e = E2eDe(1);
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

        motor.Pagador.Pagar(new ComandoDePagamento(e2e, "0001", chave, Valor.DeCentavos(Pagamento)));
        motor.Barramento.Drenar();

        return e2e;
    }

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);

    /// <summary>Participante que recusa a primeira entrega, para provar que a fila sobrevive.</summary>
    private sealed class ParticipanteHostil(Ispb ispb) : IParticipante
    {
        private bool _lancando = true;

        public Ispb Ispb { get; } = ispb;

        public int Recebidas { get; private set; }

        public void PararDeLancar() => _lancando = false;

        public void Receber(Pacs002 confirmacao)
        {
            if (_lancando)
            {
                throw new TransacaoDesconhecidaException(confirmacao.E2E);
            }

            Recebidas++;
        }

        public bool PodeCreditar(ContaId conta) => false;
    }

    private sealed class ParticipanteContador(Ispb ispb) : IParticipante
    {
        public Ispb Ispb { get; } = ispb;

        public int Recebidas { get; private set; }

        public void Receber(Pacs002 confirmacao)
        {
            ArgumentNullException.ThrowIfNull(confirmacao);
            Recebidas++;
        }

        public bool PodeCreditar(ContaId conta) => true;
    }

    private sealed class ParticipanteQueDrena(Ispb ispb, IBarramento barramento) : IParticipante
    {
        public Ispb Ispb { get; } = ispb;

        public void Receber(Pacs002 confirmacao) => barramento.Drenar();

        public bool PodeCreditar(ContaId conta) => false;
    }
}
