using MotorPix.Composicao;
using MotorPix.Conciliacao;
using MotorPix.Contratos;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Especificacoes.Suporte;

/// <summary>
/// O estado de um cenario. O SpecFlow instancia um por cenario e injeta nas classes de passos,
/// entao nao ha estado estatico compartilhado entre cenarios — o mesmo isolamento que cada
/// <c>[Fact]</c> tem de graca.
/// <para>
/// A montagem e preguicosa porque os parametros (fundo inicial, limite de timeout) chegam do
/// texto do cenario, nao do construtor. Ler qualquer coisa antes de montar lanca, em vez de
/// devolver zero: um cenario mal escrito tem de falhar como erro, nao passar por acidente.
/// </para>
/// </summary>
public sealed class ContextoDoMotor
{
    public const long FundoPadrao = 1_000_00;

    public static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    public static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    private MotorMontado? _motor;
    private GeradorE2eDeterministico? _gerador;

    public RelogioFake Relogio { get; } = new();

    public MotorMontado Motor =>
        _motor ?? throw new InvalidOperationException(
            "o motor nao foi montado: o cenario precisa comecar por um passo 'Dado que o motor esta montado'");

    public ContaId ContaPagador { get; private set; } = null!;

    public ContaId ContaRecebedor { get; private set; } = null!;

    public ChavePix ChaveDoRecebedor { get; private set; } = null!;

    /// <summary>O E2E do pagamento corrente. Cenarios com mais de um pagamento usam <see cref="E2ePorApelido"/>.</summary>
    public EndToEndId E2E { get; private set; } = null!;

    /// <summary>Pagamentos nomeados no texto do cenario ("o pagamento A"), para cenarios com varios.</summary>
    public Dictionary<string, EndToEndId> E2ePorApelido { get; } = [];

    /// <summary>A ultima resposta da API, para os passos "Entao a resposta ...".</summary>
    public RespostaDePagamento? UltimaResposta { get; set; }

    /// <summary>A ultima excecao capturada por um passo que esperava falha.</summary>
    public Exception? UltimaExcecao { get; set; }

    /// <summary>O ultimo relatorio de conciliacao produzido.</summary>
    public RelatorioDeConciliacao? UltimoRelatorio { get; set; }

    public long SaldoDoClientePagador => Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos;

    public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

    public long SaldoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

    public long SaldoEspelhoRecebedor => Motor.Recebedor.Ledger.SaldoDoEspelho().Centavos;

    public long SaldoPiPagador => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

    public long SaldoPiRecebedor => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;

    /// <summary>O pool do SPI: a soma que o invariante 3 exige constante.</summary>
    public long PoolDoSpi => SaldoPiPagador + SaldoPiRecebedor;

    public void Montar(long fundoInicial, TimeSpan? limiteDeTimeout = null)
    {
        if (_motor is not null)
        {
            throw new InvalidOperationException("o motor ja foi montado neste cenario");
        }

        _motor = MotorMontado.Montar(
            Relogio,
            IspbPagador,
            IspbRecebedor,
            Valor.DeCentavos(fundoInicial),
            politica: limiteDeTimeout is null ? null : new PoliticaDeTimeout(limiteDeTimeout.Value));

        ContaPagador = ContaId.Cliente(_motor.Pagador.Ledger.Id, "0001");
        ContaRecebedor = ContaId.Cliente(_motor.Recebedor.Ledger.Id, "0002");

        ChaveDoRecebedor = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        _motor.Recebedor.RegistrarChave(ChaveDoRecebedor, ContaRecebedor);

        // Determinístico de proposito: o E2E entra na chave de idempotencia e nas mensagens, e um
        // cenario que falhe precisa ser reproduzivel a partir do texto, sem semente escondida.
        _gerador = new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca);
        E2E = _gerador.Proximo();
    }

    /// <summary>Reserva o proximo E2E e o guarda sob um apelido do texto do cenario.</summary>
    public EndToEndId ProximoE2e(string apelido)
    {
        if (_gerador is null)
        {
            throw new InvalidOperationException("o motor nao foi montado");
        }

        EndToEndId e2e = _gerador.Proximo();
        E2ePorApelido[apelido] = e2e;
        E2E = e2e;
        return e2e;
    }

    public EndToEndId E2eDe(string apelido) =>
        E2ePorApelido.TryGetValue(apelido, out EndToEndId? e2e)
            ? e2e
            : throw new InvalidOperationException($"nenhum pagamento chamado '{apelido}' foi iniciado neste cenario");

    public RespostaDePagamento Pagar(long centavos) => Pagar(E2E, centavos);

    public RespostaDePagamento Pagar(EndToEndId e2e, long centavos)
    {
        RespostaDePagamento resposta = Motor.Pagador.Pagar(
            new ComandoDePagamento(e2e, "0001", ChaveDoRecebedor, Valor.DeCentavos(centavos)));

        UltimaResposta = resposta;
        return resposta;
    }

    public VistaDaTransacao TransacaoDoPagador(EndToEndId e2e) =>
        Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao)
            ? transacao
            : throw new InvalidOperationException($"o pagador nao conhece o E2E {e2e}");

    /// <summary>O conciliador ligado aos dois participantes, como o host faria.</summary>
    public Conciliador Conciliador() =>
        new(
            Motor.Spi.Consulta,
            new Dictionary<Ispb, IPspConciliavel>
            {
                [IspbPagador] = Motor.Pagador,
                [IspbRecebedor] = Motor.Recebedor,
            });
}
