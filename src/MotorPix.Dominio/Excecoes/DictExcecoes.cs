using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Chave que nao existe no diretorio.
/// <para>
/// Mora aqui, e nao no modulo Dict, porque quem precisa captura-la e o PSP pagador: se o tipo
/// vivesse dentro do Dict, o pagador teria de referenciar o modulo para escrever um <c>catch</c>,
/// e a comunicacao deixaria de ser exclusivamente por interface.
/// </para>
/// </summary>
public sealed class ChaveNaoEncontradaNoDictException : MotorPixException
{
    public ChaveNaoEncontradaNoDictException(ChavePix chave)
        : base($"Chave nao encontrada no diretorio: {chave}.")
    {
        Chave = chave;
    }

    public ChavePix Chave { get; }
}

/// <summary>
/// Tentativa de vincular uma chave que ja pertence a alguem.
/// Uma chave enderecada a dois titulares faria a mesma chave resolver para contas diferentes
/// conforme a ordem de registro — e o pagamento cairia em quem chegou por ultimo.
/// </summary>
public sealed class ChaveJaVinculadaException : MotorPixException
{
    public ChaveJaVinculadaException(ChavePix chave, Ispb titularAtual)
        : base($"Chave {chave} ja esta vinculada ao participante {titularAtual}.")
    {
        Chave = chave;
        TitularAtual = titularAtual;
    }

    public ChavePix Chave { get; }

    public Ispb TitularAtual { get; }
}

/// <summary>
/// Vinculo cuja conta nao pertence ao ledger do participante declarado.
/// <para>
/// Sem esta guarda, o diretorio poderia devolver o ISPB de um participante junto com uma conta que
/// vive no ledger de outro. O credito da seta 7 seria lancado no ledger errado, e o erro so
/// apareceria na conciliacao, como divergencia orfa.
/// </para>
/// </summary>
public sealed class VinculoInconsistenteException : MotorPixException
{
    public VinculoInconsistenteException(ChavePix chave, Ispb ispb, ContaId conta, string motivo)
        : base($"Vinculo invalido para a chave {chave} no participante {ispb} ({motivo}): conta '{conta}'.")
    {
        Chave = chave;
        Ispb = ispb;
        Conta = conta;
        Motivo = motivo;
    }

    public ChavePix Chave { get; }

    public Ispb Ispb { get; }

    public ContaId Conta { get; }

    public string Motivo { get; }
}
