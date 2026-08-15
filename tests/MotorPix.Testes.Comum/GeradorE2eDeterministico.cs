using MotorPix.Dominio.Identificadores;

namespace MotorPix.Testes.Comum;

/// <summary>
/// Produz EndToEndIds validos e reproduziveis a partir de um contador.
/// <para>
/// Nenhuma fonte ambiental: sem <c>Guid.NewGuid</c> e sem relogio de parede. Um contraexemplo
/// encontrado por semente tem de reproduzir identificador por identificador, senao o replay por
/// semente e ilusorio.
/// </para>
/// </summary>
public sealed class GeradorE2eDeterministico
{
    private const string Alfabeto = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int ComprimentoSufixo = 11;

    private readonly Ispb _ispb;
    private readonly DateTimeOffset _instante;
    private long _proximo;

    public GeradorE2eDeterministico(Ispb ispb, DateTimeOffset instante, long inicio = 1)
    {
        ArgumentNullException.ThrowIfNull(ispb);

        _ispb = ispb;
        _instante = instante;
        _proximo = inicio;
    }

    public EndToEndId Proximo() => EmSequencia(_proximo++);

    /// <summary>Mesmo indice, mesmo identificador — inclusive entre execucoes.</summary>
    public EndToEndId EmSequencia(long indice)
    {
        // Indice negativo faria restante % Alfabeto.Length devolver negativo e indexar fora da string.
        ArgumentOutOfRangeException.ThrowIfNegative(indice);

        Span<char> sufixo = stackalloc char[ComprimentoSufixo];
        long restante = indice;

        for (int i = ComprimentoSufixo - 1; i >= 0; i--)
        {
            sufixo[i] = Alfabeto[(int)(restante % Alfabeto.Length)];
            restante /= Alfabeto.Length;
        }

        return EndToEndId.Compor(_ispb, _instante, new string(sufixo));
    }
}
