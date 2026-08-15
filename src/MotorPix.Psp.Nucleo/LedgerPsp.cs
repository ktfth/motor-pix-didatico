using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;

namespace MotorPix.Psp.Nucleo;

/// <summary>
/// O ledger interno de um PSP, com os lancamentos nomeados pelas setas do diagrama.
/// <para>
/// Um unico tipo serve pagador e recebedor. Os rotulos "cliente para espelho PI" e "espelho PI
/// para cliente" nao descrevem estruturas diferentes: sao os dois polos do mesmo ledger em ordem
/// invertida, porque o dinheiro anda em sentidos opostos nos dois papeis. Duplicar isso em dois
/// tipos seria dois lugares para o mesmo bug — e a devolucao, no gate 6, faz o mesmo PSP exercer
/// os dois papeis.
/// </para>
/// </summary>
public sealed class LedgerPsp
{
    private readonly Ledger _ledger;

    public LedgerPsp(Ispb ispb, IClock relogio)
    {
        ArgumentNullException.ThrowIfNull(ispb);

        Ispb = ispb;
        Id = LedgerId.Psp(ispb);
        _ledger = new Ledger(Id, relogio);
        EspelhoPi = ContaId.EspelhoPi(Id);

        _ledger.Abrir(PlanoDeContas.EspelhoPi(Id));
    }

    public Ispb Ispb { get; }

    public LedgerId Id { get; }

    public ContaId EspelhoPi { get; }

    public IConsultaLedger Consulta => _ledger;

    public IInspecaoLedger Inspecao => _ledger;

    public ContaId AbrirCliente(string numeroDaConta)
    {
        ContaId conta = ContaId.Cliente(Id, numeroDaConta);
        _ledger.Abrir(PlanoDeContas.Cliente(Id, numeroDaConta));
        return conta;
    }

    /// <summary>
    /// Genesis: <c>D ESPELHO_PI / C CLIENTE</c>. O PSP passa a ter no SPI exatamente o que deve ao
    /// cliente, o que zera a diferenca entre espelho e conta PI real.
    /// </summary>
    public Commit AportarNoGenesis(ContaId cliente, Valor valor)
    {
        ArgumentNullException.ThrowIfNull(cliente);

        return _ledger.Lancar(Lancamento.Criar(
            EspelhoPi,
            cliente,
            valor,
            ChaveIdempotencia.Genesis(cliente),
            $"genesis do cliente {cliente.Chave}"));
    }

    /// <summary>Seta 3: <c>D CLIENTE / C ESPELHO_PI</c>.</summary>
    public Commit DebitarCliente(EndToEndId e2e, ContaId cliente, Valor valor) =>
        _ledger.Lancar(Lancamento.Criar(
            cliente,
            EspelhoPi,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.DebitoPagador),
            $"debito do cliente pagador {e2e}"));

    /// <summary>Seta 7: <c>D ESPELHO_PI / C CLIENTE</c>.</summary>
    public Commit CreditarCliente(EndToEndId e2e, ContaId cliente, Valor valor) =>
        _ledger.Lancar(Lancamento.Criar(
            EspelhoPi,
            cliente,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.CreditoRecebedor),
            $"credito do cliente recebedor {e2e}"));

    /// <summary>
    /// Estorno do debito: lancamento novo, jamais UPDATE do original.
    /// <para>
    /// A chave de idempotencia propria e o que torna o estorno seguro contra reentrega: tentar
    /// estornar duas vezes o mesmo E2E e recusado pelo ledger, nao por uma checagem do chamador.
    /// </para>
    /// </summary>
    public Commit EstornarDebito(EndToEndId e2e, ContaId cliente, Valor valor) =>
        _ledger.Lancar(Lancamento.Criar(
            EspelhoPi,
            cliente,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.EstornoPagador),
            $"estorno do debito {e2e}"));

    public Saldo SaldoDoCliente(ContaId cliente) => _ledger.SaldoNatural(cliente);

    public Saldo SaldoDoEspelho() => _ledger.SaldoNatural(EspelhoPi);

    public bool ContemConta(ContaId conta) => _ledger.ContemConta(conta);

    /// <summary>
    /// Diz se o debito daquele E2E ja foi estornado.
    /// <para>
    /// A condicao do estorno e uma propriedade do <em>ledger</em> — "existe debito para este E2E
    /// sem estorno correspondente?" —, nao do estado da transacao. REJEITADA e alcancavel por tres
    /// arestas com consequencias contabeis opostas: vindo de RECEBIDA nao houve debito, e estornar
    /// criaria dinheiro do nada.
    /// </para>
    /// </summary>
    public bool HouveDebito(EndToEndId e2e) =>
        _ledger.ContemChave(ChaveIdempotencia.De(e2e, EtapaLancamento.DebitoPagador));

    public bool HouveEstorno(EndToEndId e2e) =>
        _ledger.ContemChave(ChaveIdempotencia.De(e2e, EtapaLancamento.EstornoPagador));

    public bool HouveCredito(EndToEndId e2e) =>
        _ledger.ContemChave(ChaveIdempotencia.De(e2e, EtapaLancamento.CreditoRecebedor));
}
