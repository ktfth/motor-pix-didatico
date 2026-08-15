using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Mensagens;

public enum StatusPacs002
{
    /// <summary>AcceptedSettlementCompleted — liquidado.</summary>
    Acsc = 1,

    /// <summary>Rejected.</summary>
    Rjct = 2,
}

/// <summary>
/// Por que o SPI recusou a ordem. Estruturado, e nao texto livre: o PSP pagador precisa distinguir
/// os motivos para decidir se estorna, se reprocessa ou se escala.
/// </summary>
public enum MotivoRejeicao
{
    SaldoInsuficienteNaContaPi = 1,
    ParticipanteDesconhecido = 2,
    ContaDeDestinoIndisponivel = 3,
    OrdemInvalida = 4,
}

/// <summary>
/// Bloco de referencia da ordem original (<c>OrgnlTxRef</c> no ISO 20022).
/// <para>
/// Existe por necessidade concreta: a seta 6 entrega o <c>pacs.002 ACSC</c> ao PSP recebedor, e a
/// seta 7 exige que ele credite o cliente — mas o recebedor nunca viu o <c>pacs.008</c>. Sem este
/// bloco ele nao tem valor, nem conta de destino. A alternativa seria mandar as duas mensagens
/// atraves da fronteira, o que nao corresponde ao rotulo da seta.
/// </para>
/// </summary>
public sealed record OrgnlTxRef(
    Ispb IspbDebtor,
    ContaId ContaDebtor,
    Ispb IspbCreditor,
    ContaId ContaCreditor,
    Valor Valor)
{
    /// <summary>
    /// Preenchido apenas quando a ordem liquidada foi um <c>pacs.004</c>: e o E2E da transacao que
    /// esta sendo devolvida.
    /// <para>
    /// Sem ele, o participante que recebe o ACSC da devolucao credita o cliente mas nao tem como
    /// saber qual transacao original marcar como DEVOLVIDA — teria de adivinhar por valor e conta,
    /// que e ambiguo assim que existirem dois pagamentos iguais entre as mesmas contas.
    /// </para>
    /// </summary>
    public EndToEndId? E2eDevolvido { get; init; }
}

/// <summary>
/// Resultado da liquidacao (seta 6). Vai para o pagador e, quando ACSC, tambem para o recebedor.
/// </summary>
public sealed record Pacs002
{
    private Pacs002(EndToEndId e2e, StatusPacs002 status, OrgnlTxRef? referencia, MotivoRejeicao? motivo)
    {
        E2E = e2e;
        Status = status;
        Referencia = referencia;
        Motivo = motivo;
    }

    public EndToEndId E2E { get; }

    public StatusPacs002 Status { get; }

    /// <summary>Presente em ACSC; ausente em RJCT, porque nao ha liquidacao a referenciar.</summary>
    public OrgnlTxRef? Referencia { get; }

    /// <summary>Presente em RJCT; ausente em ACSC.</summary>
    public MotivoRejeicao? Motivo { get; }

    public static Pacs002 Aceito(EndToEndId e2e, OrgnlTxRef referencia)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        ArgumentNullException.ThrowIfNull(referencia);

        return new Pacs002(e2e, StatusPacs002.Acsc, referencia, motivo: null);
    }

    public static Pacs002 Rejeitado(EndToEndId e2e, MotivoRejeicao motivo)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        if (!Enum.IsDefined(motivo))
        {
            throw new ArgumentOutOfRangeException(nameof(motivo), motivo, "motivo de rejeicao desconhecido");
        }

        return new Pacs002(e2e, StatusPacs002.Rjct, referencia: null, motivo);
    }

    public override string ToString() =>
        Status == StatusPacs002.Acsc ? $"pacs.002 ACSC {E2E}" : $"pacs.002 RJCT {E2E} ({Motivo})";
}
