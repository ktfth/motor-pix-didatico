using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Valores;

/// <summary>
/// Quantia monetaria estritamente positiva, em centavos (invariante 1).
/// <para>
/// E o tipo da <em>perna de lancamento</em>, nao o tipo do saldo — por isso exige
/// <c>Centavos &gt; 0</c>. Saldo, que tem sinal e pode ser zero, e <see cref="Saldo"/>.
/// Separar os dois e o que permite a esta struct ser estritamente positiva sem quebrar o fold
/// do replay.
/// </para>
/// <para>
/// Nao existe <c>operator *</c> nem <c>operator /</c>: nenhum dos tres documentos tem tarifa,
/// juros, cambio ou rateio, e divisao e a porta pela qual o arredondamento entra.
/// </para>
/// <para>
/// <c>default(Valor)</c> vale zero centavos e portanto <em>nao</em> e um valor valido.
/// A struct nao consegue impedir sua propria construcao default, entao quem consome — Partida e
/// Lancamento — recusa explicitamente via <see cref="EhPositivo"/>.
/// </para>
/// </summary>
public readonly record struct Valor : IComparable<Valor>
{
    public const long CentavosPorReal = 100;

    private readonly long _centavos;

    private Valor(long centavos) => _centavos = centavos;

    public long Centavos => _centavos;

    /// <summary>
    /// Falso para <c>default(Valor)</c>. Toda fronteira que aceita um <see cref="Valor"/> vindo
    /// de fora do dominio precisa consultar isto antes de usar.
    /// </summary>
    public bool EhPositivo => _centavos > 0;

    public static Valor DeCentavos(long centavos) =>
        centavos > 0
            ? new Valor(centavos)
            : throw new ValorInvalidoException(centavos, centavos == 0 ? "zero" : "negativo");

    public static Valor DeReais(long reais, long centavos = 0)
    {
        if (centavos is < 0 or > 99)
        {
            throw new ValorInvalidoException(centavos, "centavos fora de 0..99");
        }

        if (reais < 0)
        {
            throw new ValorInvalidoException(reais, "reais negativos");
        }

        try
        {
            return DeCentavos(checked((reais * CentavosPorReal) + centavos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("DeReais", reais, centavos);
        }
    }

    public static bool TryDeCentavos(long centavos, out Valor valor)
    {
        if (centavos > 0)
        {
            valor = new Valor(centavos);
            return true;
        }

        valor = default;
        return false;
    }

    /// <summary>
    /// Soma revalidando pela fabrica.
    /// <para>
    /// Construir direto deixaria <c>default(Valor) + default(Valor)</c> produzir um
    /// <see cref="Valor"/> de zero centavos sem lancar — um valor invalido nascido de dentro do
    /// proprio tipo, enquanto a subtracao equivalente ja recusava.
    /// </para>
    /// </summary>
    public static Valor operator +(Valor esquerda, Valor direita)
    {
        try
        {
            return DeCentavos(checked(esquerda._centavos + direita._centavos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("+", esquerda._centavos, direita._centavos);
        }
    }

    public static Valor operator -(Valor esquerda, Valor direita)
    {
        long resultado = esquerda._centavos - direita._centavos;
        return resultado > 0
            ? new Valor(resultado)
            : throw new ValorInvalidoException(resultado, "subtracao produziria valor nao positivo");
    }

    public static bool operator <(Valor esquerda, Valor direita) => esquerda._centavos < direita._centavos;

    public static bool operator >(Valor esquerda, Valor direita) => esquerda._centavos > direita._centavos;

    public static bool operator <=(Valor esquerda, Valor direita) => esquerda._centavos <= direita._centavos;

    public static bool operator >=(Valor esquerda, Valor direita) => esquerda._centavos >= direita._centavos;

    public int CompareTo(Valor outro) => _centavos.CompareTo(outro._centavos);

    /// <summary>
    /// Formatacao manual por divisao inteira. Passar por <c>decimal</c> para formatar violaria
    /// o invariante 1 no unico lugar em que ninguem procuraria.
    /// </summary>
    public override string ToString()
    {
        long reais = _centavos / CentavosPorReal;
        long resto = _centavos % CentavosPorReal;
        return $"R$ {reais},{resto:D2}";
    }
}
