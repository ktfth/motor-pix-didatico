namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Familia das falhas de forma de identificador. Existe para que o chamador possa distinguir
/// "o texto que chegou nao e um identificador" de "o identificador e valido mas a regra recusou".
/// </summary>
public abstract class IdentificadorInvalidoException : MotorPixException
{
    protected IdentificadorInvalidoException(string tipo, string? bruto, string motivo)
        : base($"{tipo} invalido ({motivo}): '{bruto ?? "<nulo>"}'.")
    {
        Tipo = tipo;
        Bruto = bruto;
        Motivo = motivo;
    }

    public string Tipo { get; }

    public string? Bruto { get; }

    public string Motivo { get; }
}

public sealed class EndToEndIdInvalidoException : IdentificadorInvalidoException
{
    public EndToEndIdInvalidoException(string? bruto, string motivo)
        : base("EndToEndId", bruto, motivo)
    {
    }
}

public sealed class IspbInvalidoException : IdentificadorInvalidoException
{
    public IspbInvalidoException(string? bruto, string motivo)
        : base("Ispb", bruto, motivo)
    {
    }
}

public sealed class ContaIdInvalidoException : IdentificadorInvalidoException
{
    public ContaIdInvalidoException(string? bruto, string motivo)
        : base("ContaId", bruto, motivo)
    {
    }
}

public sealed class ChavePixInvalidaException : IdentificadorInvalidoException
{
    public ChavePixInvalidaException(string? bruto, string motivo)
        : base("ChavePix", bruto, motivo)
    {
    }
}

public sealed class LedgerIdInvalidoException : IdentificadorInvalidoException
{
    public LedgerIdInvalidoException(string? bruto, string motivo)
        : base("LedgerId", bruto, motivo)
    {
    }
}
