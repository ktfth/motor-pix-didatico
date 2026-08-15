using MotorPix.Composicao;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// As setas 1 a 7 do diagrama de arquitetura, ponta a ponta, com assercao lancamento a lancamento.
/// <para>
/// Os saldos esperados sao escritos a mao. Deriva-los das mesmas funcoes que o motor usa provaria
/// apenas que as funcoes sao deterministicas.
/// </para>
/// </summary>
public sealed class CaminhoFelizTestes
{
    private const long FundoInicial = 1_000_00;
    private const long ValorDoPagamento = 250_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    [Fact]
    [Trait("gate", "3")]
    public void CaminhoFeliz_DoPostAoCreditoDoRecebedor_MoveExatamenteOValorEmTodosOsTresLedgers()
    {
        Cenario cenario = Cenario.Montar();

        RespostaDePagamento resposta = cenario.Pagar(ValorDoPagamento);

        // Depois da seta 4 a transacao ja esta em ENVIADA_SPI: a transicao e gravada ANTES do
        // efeito externo, e o pacs.008 esta apenas enfileirado.
        Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado);
        Assert.Null(resposta.Motivo);
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);

        // Seta 3 ja aconteceu; as setas 5 e 7 ainda nao.
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);

        // O espelho do pagador ja caiu, mas a conta PI real ainda nao: esta defasagem E o dinheiro
        // em transito, e e o insumo da conciliacao do gate 7.
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoEspelhoPagador);

        int entregues = cenario.Motor.Barramento.Drenar();

        // pacs.008 ao SPI, depois pacs.002 ACSC ao pagador e ao recebedor.
        Assert.Equal(3, entregues);
        Assert.Equal(0, cenario.Motor.Barramento.Pendentes);

        Assert.True(cenario.Motor.Pagador.TryTransacao(cenario.E2E, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        // Efeito liquido: V sai do cliente pagador e chega ao cliente recebedor; V sai da PI
        // pagadora e chega a PI recebedora.
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoPiRecebedor);

        // Em quiescencia, espelho e conta PI voltam a coincidir nos dois participantes: nao ha
        // mais nada em transito.
        Assert.Equal(cenario.SaldoPiPagador, cenario.SaldoEspelhoPagador);
        Assert.Equal(cenario.SaldoPiRecebedor, cenario.SaldoEspelhoRecebedor);

        // O pool do SPI e constante: o dinheiro mudou de dono, nao de quantidade.
        Assert.Equal(FundoInicial * 2, cenario.SaldoPiPagador + cenario.SaldoPiRecebedor);
    }

    [Fact]
    [Trait("gate", "3")]
    public void Historico_DaTransacaoLiquidada_RegistraExatamenteAsTresTransicoesDoDiagrama()
    {
        Cenario cenario = Cenario.Montar();
        cenario.Pagar(ValorDoPagamento);
        cenario.Motor.Barramento.Drenar();

        Assert.True(cenario.Motor.Pagador.TryTransacao(cenario.E2E, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);

        Assert.Collection(
            transacao.Historico,
            t =>
            {
                Assert.Equal(EstadoTransacao.Recebida, t.Origem);
                Assert.Equal(TipoEvento.ValidacaoLocalOk, t.Evento);
                Assert.Equal(EstadoTransacao.Validada, t.Destino);
            },
            t =>
            {
                Assert.Equal(EstadoTransacao.Validada, t.Origem);
                Assert.Equal(TipoEvento.DebitoLancadoEPacs008Despachado, t.Evento);
                Assert.Equal(EstadoTransacao.EnviadaSpi, t.Destino);
            },
            t =>
            {
                Assert.Equal(EstadoTransacao.EnviadaSpi, t.Origem);
                Assert.Equal(TipoEvento.Pacs002Acsc, t.Evento);
                Assert.Equal(EstadoTransacao.Liquidada, t.Destino);
            });
    }

    /// <summary>Monta o motor e guarda os atalhos de leitura que os testes usam.</summary>
    private sealed class Cenario
    {
        private Cenario(MotorMontado motor, ContaId contaPagador, ContaId contaRecebedor, ChavePix chave, EndToEndId e2e)
        {
            Motor = motor;
            ContaPagador = contaPagador;
            ContaRecebedor = contaRecebedor;
            Chave = chave;
            E2E = e2e;
        }

        public MotorMontado Motor { get; }

        public ContaId ContaPagador { get; }

        public ContaId ContaRecebedor { get; }

        public ChavePix Chave { get; }

        public EndToEndId E2E { get; }

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
                Valor.DeCentavos(FundoInicial));

            ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, "0001");
            ContaId contaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, "0002");

            ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
            motor.Recebedor.RegistrarChave(chave, contaRecebedor);

            EndToEndId e2e = new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).Proximo();

            return new Cenario(motor, contaPagador, contaRecebedor, chave, e2e);
        }

        public RespostaDePagamento Pagar(long centavos) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(E2E, "0001", Chave, Valor.DeCentavos(centavos)));
    }
}
