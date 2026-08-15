namespace MotorPix.Psp.Nucleo;

/// <summary>
/// Quanto tempo uma transacao pode ficar em <c>ENVIADA_SPI</c> antes de a varredura a considerar
/// vencida.
/// <para>
/// E parametro injetado, e nao constante: nenhum invariante nem criterio de aceite depende da
/// magnitude. O que o prompt exige e determinismo com <c>IClock</c> fake — e determinismo vem de o
/// teste escolher o limite, nao de o limite ser um numero em particular.
/// </para>
/// <para>
/// A fronteira e <c>decorrido &gt;= Limite</c>: exatamente no limite ja vence. Escolher o lado
/// aberto ou fechado e arbitrario; escolher e nao documentar e que produz o teste intermitente.
/// </para>
/// </summary>
public sealed record PoliticaDeTimeout
{
    public PoliticaDeTimeout(TimeSpan limite)
    {
        if (limite <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(limite), limite, "o limite de timeout tem de ser positivo");
        }

        Limite = limite;
    }

    /// <summary>Trinta segundos de relogio logico. Nenhum teste depende deste numero.</summary>
    public static PoliticaDeTimeout Padrao { get; } = new(TimeSpan.FromSeconds(30));

    public TimeSpan Limite { get; }

    /// <summary>
    /// Diz se uma transicao gravada em <paramref name="desde"/> ja venceu em
    /// <paramref name="agora"/>.
    /// <para>
    /// A contagem parte do instante em que <c>ENVIADA_SPI</c> foi gravado, lido do
    /// <c>IClock</c> naquela transicao — nunca do timestamp embutido no <c>EndToEndId</c>, que e
    /// metadado de quem emitiu a mensagem e poria o vencimento nas maos de fora.
    /// </para>
    /// </summary>
    public bool Venceu(DateTimeOffset desde, DateTimeOffset agora) => agora - desde >= Limite;
}
