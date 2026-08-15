using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Lancamento que nao respeita a forma de partidas dobradas: mesma conta nos dois lados,
/// ou historico ausente. O caso "pernas de valores diferentes" e irrepresentavel por construcao,
/// ja que um lancamento carrega um unico <see cref="Valor"/> para as duas pernas.
/// </summary>
public sealed class LancamentoDesbalanceadoException : MotorPixException
{
    public LancamentoDesbalanceadoException(string motivo)
        : base($"Lancamento desbalanceado: {motivo}.")
    {
        Motivo = motivo;
    }

    public string Motivo { get; }
}

/// <summary>
/// Lancamento cujas pernas caem em ledgers diferentes.
/// Sem esta guarda o lancamento "funcionaria": debito e credito batem e a soma global continua
/// constante. Seria dinheiro atravessando a fronteira de um participante sem passar pelo SPI,
/// exatamente o que a propriedade de soma sozinha nao pega.
/// </summary>
public sealed class LancamentoEntreLedgersException : MotorPixException
{
    public LancamentoEntreLedgersException(ContaId debito, ContaId credito)
        : base($"Lancamento entre ledgers distintos: debito em '{debito}' e credito em '{credito}'.")
    {
        Debito = debito;
        Credito = credito;
    }

    public ContaId Debito { get; }

    public ContaId Credito { get; }
}

/// <summary>
/// Segunda tentativa de gravar a mesma <see cref="Contabilidade.ChaveIdempotencia"/> no ledger.
/// E esta guarda, e nao a idempotencia de API, que garante "zero lancamento novo" quando uma
/// mensagem interna e reentregue.
/// </summary>
public sealed class ChaveIdempotenciaDuplicadaException : MotorPixException
{
    public ChaveIdempotenciaDuplicadaException(string chave)
        : base($"Chave de idempotencia ja gravada neste ledger: '{chave}'.")
    {
        Chave = chave;
    }

    public string Chave { get; }
}

/// <summary>
/// Conta cujo saldo natural ficaria negativo. Nenhuma conta do sistema admite saldo natural
/// negativo, entao esta e a guarda unica: cobre o invariante 8 (conta PI) e a validacao de saldo
/// do cliente.
/// <para>
/// E avaliada dentro do mesmo ato atomico do lancamento: consultar a projecao e apender depois
/// deixaria uma janela em que dois debitos passam pela mesma validacao e o saldo fica negativo
/// sem mover a soma do sistema.
/// </para>
/// <para>
/// <see cref="Classe"/> existe para que o consumidor distinga as duas leituras sem depender do
/// texto da mensagem: saldo insuficiente de <see cref="ClasseConta.Cliente"/> e rejeicao de
/// negocio, enquanto saldo negativo em <see cref="ClasseConta.Pi"/> e violacao de invariante e
/// tem de ser encaminhada a conciliacao, jamais tratada como recusa comercial.
/// </para>
/// </summary>
public sealed class SaldoNegativoException : MotorPixException
{
    public SaldoNegativoException(ContaId conta, Saldo saldoAtual, Saldo saldoResultante)
        : base($"Conta '{conta}' nao admite saldo negativo: saldo {saldoAtual.Centavos} ficaria {saldoResultante.Centavos} centavos.")
    {
        Conta = conta;
        SaldoAtual = saldoAtual;
        SaldoResultante = saldoResultante;
    }

    public ContaId Conta { get; }

    public ClasseConta Classe => Conta.Classe;

    public Saldo SaldoAtual { get; }

    public Saldo SaldoResultante { get; }
}

/// <summary>
/// Tentativa de tocar a contra-conta de abertura fora do genesis.
/// <para>
/// A contra-conta e a unica porta de emissao de dinheiro do sistema. Sem esta guarda, um
/// lancamento <c>D ABERTURA / C PI</c> qualquer criaria saldo no pool sem quebrar partidas
/// dobradas: debito e credito batem, e so a conservacao do pool denunciaria — tarde.
/// </para>
/// </summary>
public sealed class AberturaAposGenesisException : MotorPixException
{
    public AberturaAposGenesisException(ContaId conta)
        : base($"Conta de abertura '{conta}' so pode ser tocada por um ato de genesis.")
    {
        Conta = conta;
    }

    public ContaId Conta { get; }
}

/// <summary>
/// Ato de genesis apresentado depois que o ledger ja recebeu lancamento operacional.
/// O genesis e um prefixo do log: e isso que o torna auto-selante, sem precisar de uma chamada
/// explicita de "fechar abertura" que alguem pode esquecer.
/// </summary>
public sealed class GenesisAposOperacaoException : MotorPixException
{
    public GenesisAposOperacaoException(LedgerId ledger)
        : base($"Ledger '{ledger}' ja tem lancamento operacional; genesis nao e mais aceito.")
    {
        Ledger = ledger;
    }

    public LedgerId Ledger { get; }
}

/// <summary>
/// Referencia a uma conta que nunca foi aberta neste ledger.
/// </summary>
public sealed class ContaDesconhecidaException : MotorPixException
{
    public ContaDesconhecidaException(ContaId conta)
        : base($"Conta desconhecida neste ledger: '{conta}'.")
    {
        Conta = conta;
    }

    public ContaId Conta { get; }
}

/// <summary>
/// Reabertura de uma conta ja existente. Abrir conta e evento do log, nao configuracao externa;
/// reabrir seria reescrever o plano de contas no meio do historico.
/// </summary>
public sealed class ContaJaAbertaException : MotorPixException
{
    public ContaJaAbertaException(ContaId conta)
        : base($"Conta ja aberta neste ledger: '{conta}'.")
    {
        Conta = conta;
    }

    public ContaId Conta { get; }
}

/// <summary>
/// Ato contabil sem conteudo, ou com item nulo. Um commit vazio poluiria o log com sequencia
/// sem efeito e faria o teste de prefixo do replay comparar estados identicos.
/// </summary>
public sealed class AtoContabilInvalidoException : MotorPixException
{
    public AtoContabilInvalidoException(string motivo)
        : base($"Ato contabil invalido: {motivo}.")
    {
        Motivo = motivo;
    }

    public string Motivo { get; }
}

/// <summary>
/// Lancamento enderecado a um ledger que nao e este.
/// </summary>
public sealed class LedgerIncorretoException : MotorPixException
{
    public LedgerIncorretoException(LedgerId esperado, LedgerId recebido)
        : base($"Ledger incorreto: este ledger e '{esperado}', o lancamento e de '{recebido}'.")
    {
        Esperado = esperado;
        Recebido = recebido;
    }

    public LedgerId Esperado { get; }

    public LedgerId Recebido { get; }
}
