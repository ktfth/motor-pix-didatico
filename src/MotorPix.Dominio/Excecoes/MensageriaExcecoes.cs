using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Falha de roteamento ou de transporte in-process.
/// <para>
/// Separada das excecoes contabeis de proposito: usar <c>AtoContabilInvalidoException</c> para uma
/// falha de mensageria confundiria o <c>catch</c> de quem monitora invariante de ledger — a
/// mensagem diria "ato contabil invalido" quando nenhum lancamento esteve envolvido.
/// </para>
/// </summary>
public sealed class MensageriaInvalidaException : MotorPixException
{
    public MensageriaInvalidaException(string motivo)
        : base($"Falha de mensageria: {motivo}.")
    {
        Motivo = motivo;
    }

    public string Motivo { get; }
}

// Aqui existia E2eDuplicadoException, uma ponte do gate 3: ela impedia que a colisao de E2E
// saisse como ArgumentException do dicionario interno, enquanto o replay da resposta nao existia.
// O gate 4 trouxe o replay, entao a ponte virou codigo morto e foi removida — um tipo de excecao
// que ninguem mais lanca e uma armadilha para quem for escrever um catch no futuro.

/// <summary>
/// O SPI e o PSP discordam sobre o desfecho de uma transacao.
/// <para>
/// Nao e erro de protocolo, e sintoma contabil: o dinheiro moveu no ledger do SPI e o PSP acredita
/// no contrario, ou vice-versa. Por isso e registrada e encaminhada a conciliacao em vez de virar
/// excecao atravessando o barramento — engoli-la esconderia a divergencia, e deixa-la subir
/// derrubaria a entrega das outras mensagens.
/// </para>
/// </summary>
public sealed record DivergenciaPatrimonial(EndToEndId E2E, string EstadoDoPsp, string StatusDoSpi);
