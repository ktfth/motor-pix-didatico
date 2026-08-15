using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Pedido de devolucao que o motor recusa.
/// <para>
/// A guarda mais importante e a de valor: sem ela, uma devolucao maior que a original injetaria
/// dinheiro no sistema — as partidas dobradas continuariam batendo, e a soma por ledger tambem,
/// porque o valor extra sairia da conta PI de quem devolve. Quem pagaria a diferenca seria o
/// participante, sem que nada acusasse.
/// </para>
/// </summary>
public sealed class DevolucaoInvalidaException : MotorPixException
{
    public DevolucaoInvalidaException(EndToEndId e2eOriginal, string motivo)
        : base($"Devolucao invalida para {e2eOriginal}: {motivo}.")
    {
        E2eOriginal = e2eOriginal;
        Motivo = motivo;
    }

    public EndToEndId E2eOriginal { get; }

    public string Motivo { get; }
}
