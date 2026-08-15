namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Valor monetario fora do dominio permitido: zero, negativo, ou centavos fora de 0..99.
/// </summary>
public sealed class ValorInvalidoException : MotorPixException
{
    public ValorInvalidoException(long centavos, string motivo)
        : base($"Valor invalido ({motivo}): {centavos} centavos.")
    {
        Centavos = centavos;
        Motivo = motivo;
    }

    public long Centavos { get; }

    public string Motivo { get; }
}

/// <summary>
/// Estouro de <see cref="long"/> em aritmetica monetaria.
/// Existe porque <c>OverflowException</c> e generica: o prompt exige excecao de dominio, e um
/// gerador de propriedades produz extremos de proposito. Sem isto, o wrap silencioso quebraria
/// a soma de saldos e o teste ainda passaria, ja que a soma tambem daria wrap.
/// </summary>
public sealed class EstouroDeValorException : MotorPixException
{
    public EstouroDeValorException(string operacao, long esquerda, long direita)
        : base($"Estouro de valor em '{operacao}': {esquerda} e {direita} centavos.")
    {
        Operacao = operacao;
        Esquerda = esquerda;
        Direita = direita;
    }

    public string Operacao { get; }

    public long Esquerda { get; }

    public long Direita { get; }
}
