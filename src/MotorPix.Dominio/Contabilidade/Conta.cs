using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Contabilidade;

public enum Natureza
{
    /// <summary>Saldo natural cresce a debito. Ex.: o espelho da conta PI dentro do PSP.</summary>
    Ativo = 1,

    /// <summary>Saldo natural cresce a credito. Ex.: a conta do cliente, que o PSP deve a ele.</summary>
    Passivo = 2,
}

/// <summary>
/// Conta do plano de contas de um ledger.
/// <para>
/// A natureza e <b>derivada</b> da <see cref="ClasseConta"/>, nunca recebida do chamador. Se fosse
/// parametro, registrar a conta PI como ativo — erro plausivel, ja que "PI" e intuitivamente o que
/// o participante tem — inverteria a guarda de nao-negatividade exatamente na conta em que o
/// invariante 8 mora, e nenhum teste de aritmetica pegaria.
/// </para>
/// <para>
/// Nao existe permissao de descoberto. Toda conta do sistema mantem saldo natural maior ou igual a
/// zero, inclusive a contra-conta de abertura: sendo ativo, o debito do genesis a deixa positiva.
/// Um <c>bool permiteDescoberto</c> seria um invariante desligavel em uma linha.
/// </para>
/// </summary>
public sealed record Conta
{
    private Conta(ContaId id, Natureza natureza)
    {
        Id = id;
        Natureza = natureza;
    }

    public ContaId Id { get; }

    public Natureza Natureza { get; }

    /// <summary>
    /// Contra-conta de abertura: so pode ser tocada por um ato de genesis, e o genesis so existe
    /// enquanto o ledger nao tiver nenhum lancamento operacional.
    /// </summary>
    public bool EhContraContaDeAbertura => Id.Classe == ClasseConta.Abertura;

    public static Conta De(ContaId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new Conta(id, NaturezaDe(id.Classe));
    }

    /// <summary>
    /// Converte o saldo bruto armazenado (<c>soma dos creditos - soma dos debitos</c>) para o saldo
    /// natural da conta.
    /// <para>
    /// Toda regra de negocio le o saldo <em>natural</em>. Sem esta distincao, a guarda de
    /// nao-negatividade fica invertida em metade das contas e a comparacao espelho x PI compara
    /// sinais trocados.
    /// </para>
    /// </summary>
    public Saldo NaturalDe(long bruto)
    {
        if (Natureza == Natureza.Passivo)
        {
            return new Saldo(bruto);
        }

        // Negar long.MinValue estoura sob CheckForOverflowUnderflow e vazaria uma OverflowException
        // crua, fora da hierarquia de dominio, no meio da guarda de descoberto.
        if (bruto == long.MinValue)
        {
            throw new EstouroDeValorException("saldo natural de conta de ativo", bruto, -1);
        }

        return new Saldo(-bruto);
    }

    private static Natureza NaturezaDe(ClasseConta classe) => classe switch
    {
        ClasseConta.Cliente => Natureza.Passivo,
        ClasseConta.Pi => Natureza.Passivo,
        ClasseConta.EspelhoPi => Natureza.Ativo,
        ClasseConta.Abertura => Natureza.Ativo,
        _ => throw new ContaIdInvalidoException(classe.ToString(), "classe de conta desconhecida"),
    };
}
