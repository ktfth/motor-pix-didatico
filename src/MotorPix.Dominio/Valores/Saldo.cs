using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Valores;

/// <summary>
/// Resultado de projecao: quantia <em>com sinal</em>, em centavos.
/// Diferente de <see cref="Valor"/>, admite zero e negativo — um saldo natural negativo e
/// justamente o que a guarda de descoberto precisa poder calcular antes de recusar.
/// <c>default(Saldo)</c> vale zero, que e semanticamente correto para uma conta sem movimento.
/// </summary>
public readonly record struct Saldo(long Centavos) : IComparable<Saldo>
{
    public static readonly Saldo Zero = new(0);

    public bool EhNegativo => Centavos < 0;

    public bool EhZero => Centavos == 0;

    public static Saldo operator +(Saldo saldo, Valor valor)
    {
        try
        {
            return new Saldo(checked(saldo.Centavos + valor.Centavos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("Saldo +", saldo.Centavos, valor.Centavos);
        }
    }

    public static Saldo operator -(Saldo saldo, Valor valor)
    {
        try
        {
            return new Saldo(checked(saldo.Centavos - valor.Centavos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("Saldo -", saldo.Centavos, valor.Centavos);
        }
    }

    public static Saldo operator +(Saldo esquerda, Saldo direita)
    {
        try
        {
            return new Saldo(checked(esquerda.Centavos + direita.Centavos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("Saldo + Saldo", esquerda.Centavos, direita.Centavos);
        }
    }

    public static Saldo operator -(Saldo esquerda, Saldo direita)
    {
        try
        {
            return new Saldo(checked(esquerda.Centavos - direita.Centavos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("Saldo - Saldo", esquerda.Centavos, direita.Centavos);
        }
    }

    public static bool operator <(Saldo esquerda, Saldo direita) => esquerda.Centavos < direita.Centavos;

    public static bool operator >(Saldo esquerda, Saldo direita) => esquerda.Centavos > direita.Centavos;

    public static bool operator <=(Saldo esquerda, Saldo direita) => esquerda.Centavos <= direita.Centavos;

    public static bool operator >=(Saldo esquerda, Saldo direita) => esquerda.Centavos >= direita.Centavos;

    public int CompareTo(Saldo outro) => Centavos.CompareTo(outro.Centavos);

    /// <summary>
    /// Formata pela magnitude sem sinal.
    /// <para>
    /// Trocar <c>long.MinValue</c> por <c>long.MaxValue</c> evitaria a excecao de
    /// <see cref="Math.Abs(long)"/>, mas imprimiria um centavo a menos — e esta string aparece em
    /// <see cref="Excecoes.SaldoNegativoException"/> e nas mensagens de falha de teste, onde um
    /// valor errado faz o diagnostico mentir.
    /// </para>
    /// </summary>
    public override string ToString()
    {
        string sinal = Centavos < 0 ? "-" : string.Empty;
        ulong absoluto = Centavos < 0 ? (ulong)(-(Centavos + 1)) + 1 : (ulong)Centavos;
        ulong reais = absoluto / (ulong)Valor.CentavosPorReal;
        ulong resto = absoluto % (ulong)Valor.CentavosPorReal;
        return $"{sinal}R$ {reais},{resto:D2}";
    }
}
