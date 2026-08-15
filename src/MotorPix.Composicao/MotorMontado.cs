using MotorPix.Contratos;
using MotorPix.Dict;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.Spi;

namespace MotorPix.Composicao;

/// <summary>
/// O motor inteiro montado: os quatro modulos ligados pelas interfaces, mais o barramento.
/// <para>
/// Este e o unico lugar em que os modulos se encontram. O ciclo das setas 4 e 6
/// (<c>PspPagador -&gt; Spi -&gt; PspPagador</c>) e resolvido aqui por registro tardio no
/// barramento: nenhum projeto referencia outro modulo, e o grafo de <c>ProjectReference</c>
/// continua acíclico.
/// </para>
/// </summary>
public sealed class MotorMontado
{
    private MotorMontado(
        IClock relogio,
        BarramentoEmMemoria barramento,
        DiretorioDeChavesEmMemoria dict,
        SpiEmMemoria spi,
        PspPagador.PspPagador pagador,
        PspRecebedor.PspRecebedor recebedor)
    {
        Relogio = relogio;
        Barramento = barramento;
        Dict = dict;
        Spi = spi;
        Pagador = pagador;
        Recebedor = recebedor;
    }

    public IClock Relogio { get; }

    public BarramentoEmMemoria Barramento { get; }

    public DiretorioDeChavesEmMemoria Dict { get; }

    public SpiEmMemoria Spi { get; }

    public PspPagador.PspPagador Pagador { get; }

    public PspRecebedor.PspRecebedor Recebedor { get; }

    /// <summary>
    /// Monta o motor com dois participantes, contas abertas e genesis aplicado dos dois lados.
    /// Ao final, a diferenca entre a conta PI e o espelho de cada participante e zero.
    /// </summary>
    public static MotorMontado Montar(
        IClock relogio,
        Ispb ispbPagador,
        Ispb ispbRecebedor,
        Valor fundoInicial,
        string contaDoPagador = "0001",
        string contaDoRecebedor = "0002",
        PoliticaDeTimeout? politica = null,
        IFonteAleatoria? aleatoria = null)
    {
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(ispbPagador);
        ArgumentNullException.ThrowIfNull(ispbRecebedor);

        BarramentoEmMemoria barramento = new();
        DiretorioDeChavesEmMemoria dict = new();

        PspPagador.PspPagador pagador = new(ispbPagador, relogio, dict, barramento, politica);
        PspRecebedor.PspRecebedor recebedor = new(ispbRecebedor, relogio, dict, barramento, new GeradorDeE2e(relogio, aleatoria ?? new SufixoSequencial()), politica);

        // Fonte unica de participantes: o SPI le o mesmo mapa que o barramento usa para entregar.
        // Dois registros paralelos podiam divergir — um participante presente so no do SPI faria a
        // liquidacao acontecer e a entrega do pacs.002 falhar, com o dinheiro ja movido.
        barramento.RegistrarParticipante(pagador);
        barramento.RegistrarParticipante(recebedor);

        SpiEmMemoria spi = new(relogio, barramento, barramento.Participantes);

        barramento.RegistrarSpi(spi);

        // Porta de consulta de status (seta pontilhada): mesma tecnica de registro tardio, porque o
        // SPI precisa dos participantes para nascer e o pagador precisa do SPI para consultar.
        pagador.RegistrarSpi(spi);
        recebedor.RegistrarSpi(spi);

        pagador.AbrirCliente(contaDoPagador, fundoInicial);
        recebedor.AbrirCliente(contaDoRecebedor, fundoInicial);

        spi.AbrirContaPi(ispbPagador);
        spi.AbrirContaPi(ispbRecebedor);
        spi.AportarNoGenesis(ispbPagador, fundoInicial);
        spi.AportarNoGenesis(ispbRecebedor, fundoInicial);

        return new MotorMontado(relogio, barramento, dict, spi, pagador, recebedor);
    }
}
