using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Identificadores;

/// <summary>
/// Identifica um dos tres ledgers do desenho: o do PSP pagador, o do SPI e o do PSP recebedor.
/// <para>
/// Existe para que <see cref="ContaId"/> carregue o ledger a que pertence. Sem isso, um lancamento
/// entre ledgers distintos "funciona" — debito e credito batem e a soma global continua constante —
/// e e lixo semantico que a propriedade de soma sozinha nao pega.
/// </para>
/// </summary>
public sealed record LedgerId
{
    private const string PrefixoPsp = "Psp:";

    private LedgerId(string texto) => Texto = texto;

    /// <summary>Ledger do SPI: contas PI dos participantes.</summary>
    public static LedgerId Spi { get; } = new("Spi");

    public string Texto { get; }

    /// <summary>Ledger interno de um PSP, um por participante.</summary>
    public static LedgerId Psp(Ispb ispb)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        return new LedgerId(PrefixoPsp + ispb.Texto);
    }

    public static LedgerId Criar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto))
        {
            throw new LedgerIdInvalidoException(bruto, "vazio");
        }

        if (string.Equals(bruto, Spi.Texto, StringComparison.Ordinal))
        {
            return Spi;
        }

        if (!bruto.StartsWith(PrefixoPsp, StringComparison.Ordinal))
        {
            throw new LedgerIdInvalidoException(bruto, $"esperado '{Spi.Texto}' ou '{PrefixoPsp}<ispb>'");
        }

        // Traduz a falha do ISPB embutido: quem pediu um LedgerId tem de receber
        // LedgerIdInvalidoException, e nao uma IspbInvalidoException cujo Bruto e so um pedaco
        // do texto que o chamador passou.
        if (!Ispb.TryCriar(bruto[PrefixoPsp.Length..], out Ispb? ispb) || ispb is null)
        {
            throw new LedgerIdInvalidoException(bruto, "ISPB embutido invalido");
        }

        return Psp(ispb);
    }

    public bool EhSpi => string.Equals(Texto, Spi.Texto, StringComparison.Ordinal);

    public bool Equals(LedgerId? outro) =>
        outro is not null && string.Equals(Texto, outro.Texto, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Texto);

    public override string ToString() => Texto;
}
