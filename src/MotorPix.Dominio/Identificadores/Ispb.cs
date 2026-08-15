using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Identificadores;

/// <summary>
/// Identificador de participante no SPI: exatamente 8 digitos ASCII.
/// <para>
/// Guardado como <c>string</c> e jamais como <c>int</c>. <c>00000000</c> e um ISPB valido, e o
/// <see cref="EndToEndId"/> e montado por concatenacao — um <c>int</c> perderia os zeros a
/// esquerda e produziria um E2E de 31 caracteres.
/// </para>
/// <para>
/// Sem digito verificador: ISPB nao tem. Validar existencia do participante e responsabilidade
/// dos modulos Spi e Dict; este value object valida forma.
/// </para>
/// </summary>
public sealed record Ispb
{
    public const int Comprimento = 8;

    private Ispb(string texto) => Texto = texto;

    public string Texto { get; }

    public static Ispb Criar(string? bruto)
    {
        if (bruto is null)
        {
            throw new IspbInvalidoException(bruto, "nulo");
        }

        if (bruto.Length != Comprimento)
        {
            throw new IspbInvalidoException(bruto, $"comprimento {bruto.Length}, esperado {Comprimento}");
        }

        for (int i = 0; i < bruto.Length; i++)
        {
            if (!TextoAscii.EhDigito(bruto[i]))
            {
                throw new IspbInvalidoException(bruto, $"caractere nao numerico ASCII na posicao {i}");
            }
        }

        return new Ispb(bruto);
    }

    public static bool TryCriar(string? bruto, out Ispb? ispb)
    {
        try
        {
            ispb = Criar(bruto);
            return true;
        }
        catch (IspbInvalidoException)
        {
            ispb = null;
            return false;
        }
    }

    public bool Equals(Ispb? outro) => outro is not null && string.Equals(Texto, outro.Texto, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Texto);

    public override string ToString() => Texto;
}
