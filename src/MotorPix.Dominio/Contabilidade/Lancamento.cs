using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Contabilidade;

public enum Lado
{
    Debito = 1,
    Credito = 2,
}

/// <summary>Uma perna de um lancamento. Derivada, nunca construida solta.</summary>
public readonly record struct Partida(ContaId Conta, Lado Lado, Valor Valor);

/// <summary>
/// Lancamento de partidas dobradas: debita uma conta e credita outra pelo mesmo valor.
/// <para>
/// As duas pernas compartilham um unico <see cref="Valor"/> por construcao. Isso torna
/// "pernas de valores diferentes" irrepresentavel, em vez de detectavel — a classe de bug mais
/// perigosa do sistema simplesmente nao tem como ser escrita.
/// </para>
/// <para>
/// Nao existe operacao de alteracao nem de remocao. Correcao e estorno sao lancamentos novos.
/// </para>
/// </summary>
public sealed record Lancamento
{
    private Lancamento(ContaId debito, ContaId credito, Valor valor, ChaveIdempotencia chave, string historico)
    {
        Debito = debito;
        Credito = credito;
        Valor = valor;
        Chave = chave;
        Historico = historico;
    }

    public ContaId Debito { get; }

    public ContaId Credito { get; }

    public Valor Valor { get; }

    public ChaveIdempotencia Chave { get; }

    public string Historico { get; }

    public LedgerId Ledger => Debito.Ledger;

    public IReadOnlyList<Partida> Partidas =>
    [
        new Partida(Debito, Lado.Debito, Valor),
        new Partida(Credito, Lado.Credito, Valor),
    ];

    public static Lancamento Criar(
        ContaId debito,
        ContaId credito,
        Valor valor,
        ChaveIdempotencia chave,
        string historico)
    {
        ArgumentNullException.ThrowIfNull(debito);
        ArgumentNullException.ThrowIfNull(credito);
        ArgumentNullException.ThrowIfNull(chave);

        // Recusa default(Valor): a struct nao consegue impedir a propria construcao default, entao
        // a fronteira que a consome e quem barra. Um lancamento de zero centavos seria "balanceado".
        if (!valor.EhPositivo)
        {
            throw new ValorInvalidoException(valor.Centavos, "lancamento exige valor estritamente positivo");
        }

        if (!debito.Ledger.Equals(credito.Ledger))
        {
            throw new LancamentoEntreLedgersException(debito, credito);
        }

        if (debito.Equals(credito))
        {
            throw new LancamentoDesbalanceadoException($"debito e credito na mesma conta '{debito}'");
        }

        if (string.IsNullOrWhiteSpace(historico))
        {
            throw new LancamentoDesbalanceadoException("historico vazio");
        }

        return new Lancamento(debito, credito, valor, chave, historico);
    }

    public override string ToString() => $"D {Debito} / C {Credito} {Valor} [{Chave}]";
}
