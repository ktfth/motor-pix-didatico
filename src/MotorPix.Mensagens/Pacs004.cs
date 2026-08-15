using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Mensagens;

/// <summary>
/// Devolucao (<c>pacs.004</c>).
/// <para>
/// E uma ordem <b>nova</b>, com <c>EndToEndId</c> proprio, que apenas <em>referencia</em> a
/// liquidada por <see cref="E2eOriginal"/>. Nao reabre nem altera a original: os papeis vem
/// invertidos — quem era creditor vira debtor — porque o dinheiro volta pelo mesmo caminho, em
/// sentido contrario.
/// </para>
/// <para>
/// Escopo declarado (decisao D4): devolucao total e unica, iniciada pelo PSP recebedor. O
/// <see cref="Valor"/> e campo explicito, e nao copia implicita do original, justamente para que
/// devolucao parcial seja aditiva no futuro sem mudar a forma da mensagem.
/// </para>
/// </summary>
public sealed record Pacs004
{
    public Pacs004(
        EndToEndId e2e,
        EndToEndId e2eOriginal,
        Ispb ispbDebtor,
        ContaId contaDebtor,
        Ispb ispbCreditor,
        ContaId contaCreditor,
        Valor valor)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        ArgumentNullException.ThrowIfNull(e2eOriginal);
        ArgumentNullException.ThrowIfNull(ispbDebtor);
        ArgumentNullException.ThrowIfNull(contaDebtor);
        ArgumentNullException.ThrowIfNull(ispbCreditor);
        ArgumentNullException.ThrowIfNull(contaCreditor);

        if (!valor.EhPositivo)
        {
            throw new ValorInvalidoException(valor.Centavos, "pacs.004 exige valor estritamente positivo");
        }

        if (e2e.Equals(e2eOriginal))
        {
            throw new DevolucaoInvalidaException(e2eOriginal, "a devolucao tem de ter EndToEndId proprio");
        }

        if (ispbDebtor.Equals(ispbCreditor) && contaDebtor.Equals(contaCreditor))
        {
            throw new OrdemInvalidaException(e2e, "debtor e creditor sao a mesma conta");
        }

        E2E = e2e;
        E2eOriginal = e2eOriginal;
        IspbDebtor = ispbDebtor;
        ContaDebtor = contaDebtor;
        IspbCreditor = ispbCreditor;
        ContaCreditor = contaCreditor;
        Valor = valor;
    }

    /// <summary>Identificador proprio da devolucao.</summary>
    public EndToEndId E2E { get; }

    /// <summary>A transacao liquidada que esta sendo devolvida.</summary>
    public EndToEndId E2eOriginal { get; }

    /// <summary>Quem devolve: era o creditor da original.</summary>
    public Ispb IspbDebtor { get; }

    public ContaId ContaDebtor { get; }

    /// <summary>Quem recebe de volta: era o debtor da original.</summary>
    public Ispb IspbCreditor { get; }

    public ContaId ContaCreditor { get; }

    public Valor Valor { get; }

    public override string ToString() => $"pacs.004 {E2E} ref {E2eOriginal} {Valor}";
}
