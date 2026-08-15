using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Mensagens;

/// <summary>
/// Ordem de pagamento (seta 4 do diagrama).
/// <para>
/// Vocabulario ISO 20022, sem XML e sem assinatura: a forma do record e nossa, os nomes sao de la.
/// "Debtor" e o pagador, "Creditor" e o recebedor.
/// </para>
/// </summary>
public sealed record Pacs008
{
    public Pacs008(
        EndToEndId e2e,
        Ispb ispbDebtor,
        ContaId contaDebtor,
        Ispb ispbCreditor,
        ContaId contaCreditor,
        Valor valor,
        ChavePix chaveDestino)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        ArgumentNullException.ThrowIfNull(ispbDebtor);
        ArgumentNullException.ThrowIfNull(contaDebtor);
        ArgumentNullException.ThrowIfNull(ispbCreditor);
        ArgumentNullException.ThrowIfNull(contaCreditor);
        ArgumentNullException.ThrowIfNull(chaveDestino);

        if (!valor.EhPositivo)
        {
            throw new ValorInvalidoException(valor.Centavos, "pacs.008 exige valor estritamente positivo");
        }

        // Pagar para si mesmo nao e transferencia: os dois lados do lancamento de liquidacao
        // cairiam na mesma conta PI, o que o proprio ledger ja recusaria — mas recusar aqui diz
        // ao chamador o que esta errado, em vez de falhar la dentro por um motivo estrutural.
        if (ispbDebtor.Equals(ispbCreditor) && contaDebtor.Equals(contaCreditor))
        {
            throw new OrdemInvalidaException(e2e, "debtor e creditor sao a mesma conta");
        }

        E2E = e2e;
        IspbDebtor = ispbDebtor;
        ContaDebtor = contaDebtor;
        IspbCreditor = ispbCreditor;
        ContaCreditor = contaCreditor;
        Valor = valor;
        ChaveDestino = chaveDestino;
    }

    public EndToEndId E2E { get; }

    public Ispb IspbDebtor { get; }

    public ContaId ContaDebtor { get; }

    public Ispb IspbCreditor { get; }

    public ContaId ContaCreditor { get; }

    public Valor Valor { get; }

    public ChavePix ChaveDestino { get; }

    public override string ToString() => $"pacs.008 {E2E} {Valor} {IspbDebtor}->{IspbCreditor}";
}
