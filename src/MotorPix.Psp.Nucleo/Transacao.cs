using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Psp.Nucleo;

public enum TipoTransacao
{
    Pagamento = 1,

    /// <summary>Devolucao: transacao propria, com E2E proprio, referenciando a liquidada.</summary>
    Devolucao = 2,
}

/// <summary>Uma transicao ja aplicada. O historico e append-only, como o ledger.</summary>
public sealed record TransicaoAplicada(
    EstadoTransacao Origem,
    TipoEvento Evento,
    EstadoTransacao Destino,
    DateTimeOffset Em);

/// <summary>
/// A transacao, com estado governado pela maquina normativa.
/// <para>
/// E <c>sealed class</c>, e nao <c>record</c>: num record, <c>transacao with { Estado = ... }</c>
/// e um setter publico disfarcado, e o invariante 4 exige que estado nao tenha setter publico. O
/// unico mutador e <see cref="Aplicar"/>, e ele consulta a tabela antes de mover qualquer coisa.
/// </para>
/// </summary>
public sealed class Transacao
{
    private readonly List<TransicaoAplicada> _historico = [];

    private Transacao(
        EndToEndId e2e,
        TipoTransacao tipo,
        ContaId contaOrigem,
        ChavePix chaveDestino,
        Valor valor,
        DateTimeOffset criadaEm,
        EndToEndId? e2eOriginal)
    {
        E2E = e2e;
        Tipo = tipo;
        ContaOrigem = contaOrigem;
        ChaveDestino = chaveDestino;
        Valor = valor;
        CriadaEm = criadaEm;
        E2eOriginal = e2eOriginal;
        Estado = EstadoTransacao.Recebida;
    }

    public EndToEndId E2E { get; }

    public TipoTransacao Tipo { get; }

    public ContaId ContaOrigem { get; }

    public ChavePix ChaveDestino { get; }

    public Valor Valor { get; }

    public DateTimeOffset CriadaEm { get; }

    /// <summary>Preenchido apenas em transacoes de devolucao.</summary>
    public EndToEndId? E2eOriginal { get; }

    public EstadoTransacao Estado { get; private set; }

    /// <summary>Instante da ultima transicao. Base de contagem do timeout no gate 5.</summary>
    public DateTimeOffset AtualizadaEm { get; private set; }

    /// <summary>Resolucao do DICT, preenchida na validacao local.</summary>
    public Ispb? IspbDestino { get; private set; }

    public ContaId? ContaDestino { get; private set; }

    public MotivoRejeicaoLocal? MotivoRejeicao { get; private set; }

    /// <summary>
    /// O E2E da devolucao que referencia esta transacao, quando existe.
    /// <para>
    /// Guardado na ORIGINAL para que o vinculo entre as duas exista em algum lugar consultavel: a
    /// devolucao mora no modulo do outro participante, e sem isto a conciliacao veria uma original
    /// DEVOLVIDA e um credito sob um E2E que este PSP nunca originou, sem como parear os dois.
    /// </para>
    /// </summary>
    public EndToEndId? E2eDevolucao { get; private set; }

    /// <summary>
    /// Copia, e nao vista sobre a lista viva: enumerar o historico enquanto outra thread aplica um
    /// evento lancaria InvalidOperationException no meio de uma leitura inocente.
    /// </summary>
    public IReadOnlyList<TransicaoAplicada> Historico => [.. _historico];

    /// <summary>
    /// A unica porta de entrada: <c>[*] --&gt; RECEBIDA</c>. Nascer nao e um evento aplicavel a uma
    /// transacao existente, entao nao ha <c>TipoEvento</c> correspondente.
    /// </summary>
    public static Transacao Iniciar(
        EndToEndId e2e,
        ContaId contaOrigem,
        ChavePix chaveDestino,
        Valor valor,
        DateTimeOffset em,
        TipoTransacao tipo = TipoTransacao.Pagamento,
        EndToEndId? e2eOriginal = null)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        ArgumentNullException.ThrowIfNull(contaOrigem);
        ArgumentNullException.ThrowIfNull(chaveDestino);

        if (!valor.EhPositivo)
        {
            throw new ValorInvalidoException(valor.Centavos, "transacao exige valor estritamente positivo");
        }

        if (tipo == TipoTransacao.Devolucao && e2eOriginal is null)
        {
            throw new OrdemInvalidaException(e2e, "devolucao exige o E2E da transacao original");
        }

        if (tipo == TipoTransacao.Pagamento && e2eOriginal is not null)
        {
            throw new OrdemInvalidaException(e2e, "pagamento nao referencia transacao original");
        }

        return new Transacao(e2e, tipo, contaOrigem, chaveDestino, valor, em, e2eOriginal)
        {
            AtualizadaEm = em,
        };
    }

    /// <summary>
    /// Aplica um evento. Se o par (estado, evento) nao estiver na tabela, lanca e <b>nada muda</b>.
    /// </summary>
    public EstadoTransacao Aplicar(TipoEvento evento, DateTimeOffset em)
    {
        if (!MaquinaDeEstados.TryDestino(Estado, evento, out EstadoTransacao destino))
        {
            throw new TransicaoInvalidaException(E2E, Estado.ToString(), evento.ToString());
        }

        _historico.Add(new TransicaoAplicada(Estado, evento, destino, em));
        Estado = destino;
        AtualizadaEm = em;
        return destino;
    }

    /// <summary>
    /// Grava a resolucao do DICT. So aceita em RECEBIDA: depois disso o destino ja foi usado para
    /// montar o pacs.008, e sobrescreve-lo faria a transacao contar uma historia diferente da que
    /// o SPI recebeu — inclusive numa transacao ja liquidada.
    /// </summary>
    public void RegistrarDestino(Ispb ispb, ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(conta);

        if (Estado != EstadoTransacao.Recebida)
        {
            throw new TransicaoInvalidaException(E2E, Estado.ToString(), "RegistrarDestino");
        }

        IspbDestino = ispb;
        ContaDestino = conta;
    }

    /// <summary>
    /// Anota que esta transacao foi devolvida por <paramref name="e2eDevolucao"/>.
    /// Anotar nao e transicionar: a aresta estados:16 continua sendo aplicada por
    /// <see cref="Aplicar"/>, e so a partir de LIQUIDADA.
    /// </summary>
    public void MarcarDevolucao(EndToEndId e2eDevolucao)
    {
        ArgumentNullException.ThrowIfNull(e2eDevolucao);

        if (Tipo == TipoTransacao.Devolucao)
        {
            throw new DevolucaoInvalidaException(E2E, "uma devolucao nao pode ser devolvida");
        }

        E2eDevolucao = e2eDevolucao;
    }

    public void RegistrarMotivoDeRejeicao(MotivoRejeicaoLocal motivo)
    {
        if (!Enum.IsDefined(motivo))
        {
            throw new ArgumentOutOfRangeException(nameof(motivo), motivo, "motivo de rejeicao desconhecido");
        }

        MotivoRejeicao = motivo;
    }
}

/// <summary>
/// Por que a validacao local recusou (aresta estados:5). Estruturado: rejeicao de negocio nao e
/// excecao, e o motivo precisa chegar a resposta da API sem virar texto solto.
/// </summary>
public enum MotivoRejeicaoLocal
{
    ChaveNaoEncontrada = 1,
    SaldoInsuficiente = 2,
    ContaDeOrigemDesconhecida = 3,
    RejeitadaPeloSpi = 4,

    /// <summary>A ordem nao pode sequer ser montada: pagar para a propria conta, por exemplo.</summary>
    OrdemInvalida = 5,
}
