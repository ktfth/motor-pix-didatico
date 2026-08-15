using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Testes.Comum;

/// <summary>
/// Os tres ledgers do desenho, ja com plano de contas e genesis aplicados.
/// <para>
/// O genesis fixa a constante do pool do SPI e deixa cada PSP com espelho igual ao que tem na PI.
/// A partir daqui, qualquer divergencia entre espelho e PI e dinheiro em transito — ou defeito.
/// </para>
/// </summary>
public sealed class CenarioContabil
{
    private CenarioContabil(
        RelogioFake relogio,
        Ispb ispbPagador,
        Ispb ispbRecebedor,
        Ledger ledgerPagador,
        Ledger ledgerSpi,
        Ledger ledgerRecebedor,
        Valor fundoInicial,
        string contaClientePagador,
        string contaClienteRecebedor)
    {
        Relogio = relogio;
        IspbPagador = ispbPagador;
        IspbRecebedor = ispbRecebedor;
        LedgerPagador = ledgerPagador;
        LedgerSpi = ledgerSpi;
        LedgerRecebedor = ledgerRecebedor;
        FundoInicial = fundoInicial;

        ClientePagador = ContaId.Cliente(ledgerPagador.Id, contaClientePagador);
        EspelhoPagador = ContaId.EspelhoPi(ledgerPagador.Id);
        ClienteRecebedor = ContaId.Cliente(ledgerRecebedor.Id, contaClienteRecebedor);
        EspelhoRecebedor = ContaId.EspelhoPi(ledgerRecebedor.Id);
        PiPagador = ContaId.Pi(ispbPagador);
        PiRecebedor = ContaId.Pi(ispbRecebedor);
        AberturaSpi = ContaId.Abertura(LedgerId.Spi);
    }

    public RelogioFake Relogio { get; }

    public Ispb IspbPagador { get; }

    public Ispb IspbRecebedor { get; }

    public Ledger LedgerPagador { get; }

    public Ledger LedgerSpi { get; }

    public Ledger LedgerRecebedor { get; }

    public Valor FundoInicial { get; }

    public ContaId ClientePagador { get; }

    public ContaId EspelhoPagador { get; }

    public ContaId ClienteRecebedor { get; }

    public ContaId EspelhoRecebedor { get; }

    public ContaId PiPagador { get; }

    public ContaId PiRecebedor { get; }

    public ContaId AberturaSpi { get; }

    public IReadOnlyList<Ledger> Ledgers => [LedgerPagador, LedgerSpi, LedgerRecebedor];

    public static CenarioContabil Padrao(
        RelogioFake? relogio = null,
        long fundoInicialEmCentavos = 1_000_00,
        string ispbPagador = "11111111",
        string ispbRecebedor = "22222222",
        string contaClientePagador = "0001",
        string contaClienteRecebedor = "0002")
    {
        relogio ??= new RelogioFake();

        Ispb pagador = Ispb.Criar(ispbPagador);
        Ispb recebedor = Ispb.Criar(ispbRecebedor);
        Valor fundo = Valor.DeCentavos(fundoInicialEmCentavos);

        LedgerId idPagador = LedgerId.Psp(pagador);
        LedgerId idRecebedor = LedgerId.Psp(recebedor);

        Ledger ledgerPagador = new(idPagador, relogio);
        Ledger ledgerRecebedor = new(idRecebedor, relogio);
        Ledger ledgerSpi = new(LedgerId.Spi, relogio);

        ContaId clientePagador = ContaId.Cliente(idPagador, contaClientePagador);
        ContaId clienteRecebedor = ContaId.Cliente(idRecebedor, contaClienteRecebedor);

        ledgerPagador.Abrir(
            PlanoDeContas.Cliente(idPagador, contaClientePagador),
            PlanoDeContas.EspelhoPi(idPagador));

        ledgerRecebedor.Abrir(
            PlanoDeContas.Cliente(idRecebedor, contaClienteRecebedor),
            PlanoDeContas.EspelhoPi(idRecebedor));

        ledgerSpi.Abrir(
            PlanoDeContas.AberturaSpi(),
            PlanoDeContas.Pi(pagador),
            PlanoDeContas.Pi(recebedor));

        // Genesis do PSP: D ESPELHO_PI / C CLIENTE. O PSP tem no SPI exatamente o que deve ao cliente.
        ledgerPagador.Lancar(Lancamento.Criar(
            ContaId.EspelhoPi(idPagador),
            clientePagador,
            fundo,
            ChaveIdempotencia.Genesis(clientePagador),
            "genesis do PSP pagador"));

        ledgerRecebedor.Lancar(Lancamento.Criar(
            ContaId.EspelhoPi(idRecebedor),
            clienteRecebedor,
            fundo,
            ChaveIdempotencia.Genesis(clienteRecebedor),
            "genesis do PSP recebedor"));

        // Genesis do SPI: D ABERTURA / C PI. Fixa a constante do pool.
        ledgerSpi.Lancar(
            Lancamento.Criar(
                ContaId.Abertura(LedgerId.Spi),
                ContaId.Pi(pagador),
                fundo,
                ChaveIdempotencia.Genesis(ContaId.Pi(pagador)),
                "genesis da conta PI do pagador"),
            Lancamento.Criar(
                ContaId.Abertura(LedgerId.Spi),
                ContaId.Pi(recebedor),
                fundo,
                ChaveIdempotencia.Genesis(ContaId.Pi(recebedor)),
                "genesis da conta PI do recebedor"));

        return new CenarioContabil(
            relogio,
            pagador,
            recebedor,
            ledgerPagador,
            ledgerSpi,
            ledgerRecebedor,
            fundo,
            contaClientePagador,
            contaClienteRecebedor);
    }

    /// <summary>Seta 3: debito do cliente no ledger do pagador.</summary>
    public Commit DebitarPagador(EndToEndId e2e, Valor valor) =>
        LedgerPagador.Lancar(Lancamento.Criar(
            ClientePagador,
            EspelhoPagador,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.DebitoPagador),
            $"debito do cliente pagador {e2e}"));

    /// <summary>Seta 5: liquidacao atomica no SPI.</summary>
    public Commit Liquidar(EndToEndId e2e, Valor valor) =>
        LedgerSpi.Lancar(Lancamento.Criar(
            PiPagador,
            PiRecebedor,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.Liquidacao),
            $"liquidacao {e2e}"));

    /// <summary>Seta 7: credito do cliente no ledger do recebedor.</summary>
    public Commit CreditarRecebedor(EndToEndId e2e, Valor valor) =>
        LedgerRecebedor.Lancar(Lancamento.Criar(
            EspelhoRecebedor,
            ClienteRecebedor,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.CreditoRecebedor),
            $"credito do cliente recebedor {e2e}"));

    /// <summary>Estorno do debito do pagador: lancamento novo, nunca UPDATE do original.</summary>
    public Commit EstornarPagador(EndToEndId e2e, Valor valor) =>
        LedgerPagador.Lancar(Lancamento.Criar(
            EspelhoPagador,
            ClientePagador,
            valor,
            ChaveIdempotencia.De(e2e, EtapaLancamento.EstornoPagador),
            $"estorno do debito {e2e}"));

    /// <summary>Caminho feliz completo, setas 3, 5 e 7.</summary>
    public void CaminhoFeliz(EndToEndId e2e, Valor valor)
    {
        DebitarPagador(e2e, valor);
        Liquidar(e2e, valor);
        CreditarRecebedor(e2e, valor);
    }
}
