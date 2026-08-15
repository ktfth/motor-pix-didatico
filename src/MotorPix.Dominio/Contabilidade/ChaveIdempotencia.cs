using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Etapa contabil de um lancamento dentro do ciclo de vida de uma transacao.
/// Corresponde as setas numeradas do diagrama de arquitetura.
/// </summary>
public enum EtapaLancamento
{
    /// <summary>Abertura do sistema. Nao pertence a nenhuma transacao.</summary>
    Genesis = 0,

    /// <summary>Seta 3: debito do cliente no ledger do PSP pagador.</summary>
    DebitoPagador = 3,

    /// <summary>Seta 5: liquidacao atomica no ledger do SPI.</summary>
    Liquidacao = 5,

    /// <summary>Seta 7: credito do cliente no ledger do PSP recebedor.</summary>
    CreditoRecebedor = 7,

    /// <summary>Estorno do debito do pagador. Lancamento novo, jamais UPDATE do original.</summary>
    EstornoPagador = 30,
}

/// <summary>
/// Identidade de um lancamento para efeito de deduplicacao dentro de um ledger.
/// <para>
/// E esta chave, e nao a idempotencia da API, que garante "zero lancamento novo" quando uma
/// mensagem interna e reentregue: o par (transacao, etapa) so pode ser gravado uma vez por ledger.
/// </para>
/// </summary>
public sealed record ChaveIdempotencia
{
    private ChaveIdempotencia(string texto, EtapaLancamento etapa, EndToEndId? e2e)
    {
        Texto = texto;
        Etapa = etapa;
        E2E = e2e;
    }

    public string Texto { get; }

    public EtapaLancamento Etapa { get; }

    /// <summary>Nulo apenas para lancamentos de genesis, que nao pertencem a transacao alguma.</summary>
    public EndToEndId? E2E { get; }

    public static ChaveIdempotencia De(EndToEndId e2e, EtapaLancamento etapa)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        if (etapa == EtapaLancamento.Genesis)
        {
            throw new ArgumentOutOfRangeException(
                nameof(etapa),
                "Genesis nao pertence a uma transacao; use ChaveIdempotencia.Genesis.");
        }

        // Sem isto, De(e2e, (EtapaLancamento)99) gera uma chave gravavel para uma etapa que nenhum
        // outro modulo reconhece — e a chave, uma vez no log append-only, nao sai mais de la.
        if (!Enum.IsDefined(etapa))
        {
            throw new ArgumentOutOfRangeException(nameof(etapa), etapa, "etapa de lancamento desconhecida");
        }

        return new ChaveIdempotencia($"{e2e.Texto}#{etapa}", etapa, e2e);
    }

    public static ChaveIdempotencia Genesis(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        return new ChaveIdempotencia($"GENESIS#{conta}", EtapaLancamento.Genesis, e2e: null);
    }

    public bool Equals(ChaveIdempotencia? outra) =>
        outra is not null && string.Equals(Texto, outra.Texto, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Texto);

    public override string ToString() => Texto;
}
