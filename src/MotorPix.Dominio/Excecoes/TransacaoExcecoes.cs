using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Transicao que o diagrama normativo nao autoriza (invariante 4).
/// <para>
/// Carrega o par (estado, evento) como texto porque o dominio nao conhece os enums da maquina de
/// estados — eles vivem no nucleo de PSP. Guardar o nome evita que o assembly de dominio precise
/// referenciar um modulo, o que inverteria o grafo de dependencias.
/// </para>
/// </summary>
public sealed class TransicaoInvalidaException : MotorPixException
{
    public TransicaoInvalidaException(EndToEndId e2e, string estadoAtual, string evento)
        : base($"Transacao {e2e}: o evento '{evento}' nao e valido no estado '{estadoAtual}'.")
    {
        E2E = e2e;
        EstadoAtual = estadoAtual;
        Evento = evento;
    }

    public EndToEndId E2E { get; }

    public string EstadoAtual { get; }

    public string Evento { get; }
}

/// <summary>Evento dirigido a um E2E que o repositorio nao conhece.</summary>
public sealed class TransacaoDesconhecidaException : MotorPixException
{
    public TransacaoDesconhecidaException(EndToEndId e2e)
        : base($"Transacao desconhecida: {e2e}.")
    {
        E2E = e2e;
    }

    public EndToEndId E2E { get; }
}

/// <summary>Ordem de pagamento malformada, recusada antes de virar transacao.</summary>
public sealed class OrdemInvalidaException : MotorPixException
{
    public OrdemInvalidaException(EndToEndId e2e, string motivo)
        : base($"Ordem invalida para {e2e}: {motivo}.")
    {
        E2E = e2e;
        Motivo = motivo;
    }

    public EndToEndId E2E { get; }

    public string Motivo { get; }
}

/// <summary>
/// Saldo do cliente insuficiente para o pagamento.
/// <para>
/// Separada de <see cref="SaldoNegativoException"/> de proposito: aqui e recusa comercial, que
/// vira a aresta "validacao local falha" da maquina de estados. Saldo negativo numa conta PI e
/// violacao de invariante e tem de ser escalada, jamais tratada como recusa de negocio — um
/// <c>catch</c> unico para os dois casos confundiria as duas coisas.
/// </para>
/// </summary>
public sealed class SaldoInsuficienteException : MotorPixException
{
    public SaldoInsuficienteException(ContaId conta, long saldoEmCentavos, long valorEmCentavos)
        : base($"Saldo insuficiente em '{conta}': {saldoEmCentavos} centavos para um pagamento de {valorEmCentavos}.")
    {
        Conta = conta;
        SaldoEmCentavos = saldoEmCentavos;
        ValorEmCentavos = valorEmCentavos;
    }

    public ContaId Conta { get; }

    public long SaldoEmCentavos { get; }

    public long ValorEmCentavos { get; }
}
