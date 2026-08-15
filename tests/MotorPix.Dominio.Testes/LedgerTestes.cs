using System.Reflection;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// O ato contabil atomico: guardas avaliadas antes de qualquer mutacao, append indivisivel,
/// projecao escrita junto e log que so cresce.
/// <para>
/// Os saldos esperados sao sempre escritos a mao no teste, nunca derivados da mesma funcao que o
/// ledger usa: um oraculo calculado com <c>NaturalDe</c> provaria apenas que a funcao e
/// deterministica.
/// </para>
/// <para>
/// Nao existe conta com descoberto, e por isso nao existe teste de descoberto: a guarda de saldo
/// negativo e unica e vale para as quatro classes de conta, inclusive a contra-conta de abertura.
/// </para>
/// </summary>
public sealed class LedgerTestes
{
    private const string PrimeiroCliente = "0001";
    private const string SegundoCliente = "0002";

    private static readonly Ispb IspbPadrao = Ispb.Criar("11111111");
    private static readonly Ispb IspbOutro = Ispb.Criar("22222222");
    private static readonly ContaId ContaAberturaSpi = ContaId.Abertura(LedgerId.Spi);
    private static readonly ContaId ContaPiPadrao = ContaId.Pi(IspbPadrao);

    // ---------------------------------------------------------------------------------------
    // Abrir
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Abrir_ContasNovas_CriaComSaldoEMinimoZeroEPassaAConterAsContas()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        ContaId cliente = ContaId.Cliente(ledger.Id, PrimeiroCliente);
        ContaId espelho = ContaId.EspelhoPi(ledger.Id);

        Assert.False(ledger.ContemConta(cliente));
        Assert.False(ledger.ContemConta(espelho));

        Commit commit = ledger.Abrir(
            PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente),
            PlanoDeContas.EspelhoPi(ledger.Id));

        Assert.True(ledger.ContemConta(cliente));
        Assert.True(ledger.ContemConta(espelho));
        Assert.Equal(Saldo.Zero, ledger.SaldoNatural(cliente));
        Assert.Equal(Saldo.Zero, ledger.SaldoNatural(espelho));
        Assert.Equal(Saldo.Zero, ledger.MinimoNatural(cliente));
        Assert.Equal(Saldo.Zero, ledger.MinimoNatural(espelho));

        // Abrir conta e evento do log, nao configuracao externa: o commit registra a abertura.
        Assert.Empty(commit.Lancamentos);
        Assert.Collection(
            commit.ContasAbertas,
            conta => Assert.Equal(cliente, conta.Id),
            conta => Assert.Equal(espelho, conta.Id));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Abrir_MesmaContaDuasVezesNoMesmoLote_LancaContaJaAbertaENaoAbreNada()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        ContaId cliente = ContaId.Cliente(ledger.Id, PrimeiroCliente);

        ContaJaAbertaException excecao = Assert.Throws<ContaJaAbertaException>(
            () => ledger.Abrir(
                PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente),
                PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente)));

        Assert.Equal(cliente, excecao.Conta);
        Assert.False(ledger.ContemConta(cliente));
        Assert.Empty(ledger.Log());
    }

    [Fact]
    [Trait("gate", "1")]
    public void Abrir_MesmaContaEmLotesDiferentes_LancaContaJaAberta()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        ContaId cliente = ContaId.Cliente(ledger.Id, PrimeiroCliente);
        ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente));

        ContaJaAbertaException excecao = Assert.Throws<ContaJaAbertaException>(
            () => ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente)));

        Assert.Equal(cliente, excecao.Conta);
        Assert.Single(ledger.Log());
    }

    [Fact]
    [Trait("gate", "1")]
    public void Abrir_ContaDeOutroLedger_LancaLedgerIncorreto()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        LedgerId outroLedger = LedgerId.Psp(IspbOutro);

        LedgerIncorretoException excecao = Assert.Throws<LedgerIncorretoException>(
            () => ledger.Abrir(PlanoDeContas.Cliente(outroLedger, PrimeiroCliente)));

        Assert.Equal(ledger.Id, excecao.Esperado);
        Assert.Equal(outroLedger, excecao.Recebido);
        Assert.Empty(ledger.Log());
    }

    [Fact]
    [Trait("gate", "1")]
    public void Abrir_SemContas_LancaAtoContabilInvalido()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        Conta[]? loteNulo = null;

        Assert.Throws<AtoContabilInvalidoException>(() => ledger.Abrir());
        Assert.Throws<AtoContabilInvalidoException>(() => ledger.Abrir(loteNulo!));
        Assert.Empty(ledger.Log());
    }

    [Fact]
    [Trait("gate", "1")]
    public void Abrir_ComContaNulaNoLote_LancaAtoContabilInvalidoENaoAbreAContaValida()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        ContaId cliente = ContaId.Cliente(ledger.Id, PrimeiroCliente);

        Assert.Throws<AtoContabilInvalidoException>(
            () => ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente), null!));

        Assert.False(ledger.ContemConta(cliente));
        Assert.Empty(ledger.Log());
    }

    // ---------------------------------------------------------------------------------------
    // Lancar: guardas de forma
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_SemLancamentos_LancaAtoContabilInvalido()
    {
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        Lancamento[]? atoNulo = null;
        int commitsAntes = ledger.Log().Count;

        Assert.Throws<AtoContabilInvalidoException>(() => ledger.Lancar());
        Assert.Throws<AtoContabilInvalidoException>(() => ledger.Lancar(atoNulo!));

        int commitsDepois = ledger.Log().Count;
        Assert.Equal(commitsAntes, commitsDepois);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_ComLancamentoNuloNoAto_LancaAtoContabilInvalidoENaoGravaNada()
    {
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);
        ChaveIdempotencia chave = ChaveIdempotencia.Genesis(cliente);
        Lancamento valido = Lancamento.Criar(espelho, cliente, Valor.DeCentavos(1_000_00), chave, "genesis");
        int commitsAntes = ledger.Log().Count;

        Assert.Throws<AtoContabilInvalidoException>(() => ledger.Lancar(valido, null!));

        int commitsDepois = ledger.Log().Count;
        Assert.Equal(commitsAntes, commitsDepois);
        Assert.False(ledger.ContemChave(chave));
        Assert.Equal(Saldo.Zero, ledger.SaldoNatural(cliente));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_EmContaNaoAberta_LancaContaDesconhecida()
    {
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId inexistente = ContaId.Cliente(ledger.Id, "9999");
        Lancamento lancamento = Lancamento.Criar(
            inexistente,
            ClienteDe(ledger),
            Valor.DeCentavos(10_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "debito em conta que nunca foi aberta");

        ContaDesconhecidaException excecao = Assert.Throws<ContaDesconhecidaException>(
            () => ledger.Lancar(lancamento));

        Assert.Equal(inexistente, excecao.Conta);
        Assert.False(ledger.ContemConta(inexistente));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_LancamentoDeOutroLedger_LancaLedgerIncorreto()
    {
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        LedgerId outroLedger = LedgerId.Psp(IspbOutro);
        Lancamento estrangeiro = Lancamento.Criar(
            ContaId.EspelhoPi(outroLedger),
            ContaId.Cliente(outroLedger, PrimeiroCliente),
            Valor.DeCentavos(10_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.CreditoRecebedor),
            "lancamento do ledger do vizinho");

        LedgerIncorretoException excecao = Assert.Throws<LedgerIncorretoException>(
            () => ledger.Lancar(estrangeiro));

        Assert.Equal(ledger.Id, excecao.Esperado);
        Assert.Equal(outroLedger, excecao.Recebido);
    }

    // ---------------------------------------------------------------------------------------
    // Saldo natural: o sinal depende da natureza da conta
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_ContaPassivo_CresceACreditoEDiminuiADebito()
    {
        // CLIENTE e Passivo: o PSP deve o saldo ao cliente, entao credito aumenta e debito reduz.
        // Inverter isto nao mexeria na soma do sistema e so apareceria no sinal desta conta.
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);

        ledger.Lancar(Lancamento.Criar(
            espelho,
            cliente,
            Valor.DeCentavos(1_000_00),
            ChaveIdempotencia.Genesis(cliente),
            "genesis: credito no cliente"));

        Assert.Equal(new Saldo(1_000_00), ledger.SaldoNatural(cliente));

        ledger.Lancar(Lancamento.Criar(
            cliente,
            espelho,
            Valor.DeCentavos(100_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "debito do cliente pagador"));

        Assert.Equal(new Saldo(900_00), ledger.SaldoNatural(cliente));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_ContaAtivo_CresceADebitoEDiminuiACredito()
    {
        // ESPELHO_PI e Ativo: e o que o PSP entende ter na conta PI, entao debito aumenta e credito
        // reduz — o oposto do CLIENTE, com exatamente os mesmos dois lancamentos.
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);

        ledger.Lancar(Lancamento.Criar(
            espelho,
            cliente,
            Valor.DeCentavos(1_000_00),
            ChaveIdempotencia.Genesis(cliente),
            "genesis: debito no espelho"));

        Assert.Equal(new Saldo(1_000_00), ledger.SaldoNatural(espelho));

        ledger.Lancar(Lancamento.Criar(
            cliente,
            espelho,
            Valor.DeCentavos(100_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "credito no espelho"));

        Assert.Equal(new Saldo(900_00), ledger.SaldoNatural(espelho));
    }

    // ---------------------------------------------------------------------------------------
    // Idempotencia por chave
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_ComChaveJaGravadaEmOutroAto_LancaChaveIdempotenciaDuplicada()
    {
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);
        Fundear(ledger, 1_000_00);

        ChaveIdempotencia chave = ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador);
        ledger.Lancar(Lancamento.Criar(cliente, espelho, Valor.DeCentavos(10_00), chave, "debito"));
        Assert.True(ledger.ContemChave(chave));

        int commitsAntes = ledger.Log().Count;
        Saldo saldoAntes = ledger.SaldoNatural(cliente);

        ChaveIdempotenciaDuplicadaException excecao = Assert.Throws<ChaveIdempotenciaDuplicadaException>(
            () => ledger.Lancar(Lancamento.Criar(cliente, espelho, Valor.DeCentavos(10_00), chave, "reentrega")));

        Assert.Equal(chave.Texto, excecao.Chave);

        int commitsDepois = ledger.Log().Count;
        Assert.Equal(commitsAntes, commitsDepois);
        Assert.Equal(saldoAntes, ledger.SaldoNatural(cliente));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_ComChaveRepetidaDentroDoMesmoAto_LancaChaveIdempotenciaDuplicada()
    {
        // O indice ja gravado nao basta: duas ocorrencias da mesma chave no mesmo lote nunca
        // chegariam a se ver se a checagem so olhasse o que ja esta no ledger.
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);
        Fundear(ledger, 1_000_00);

        ChaveIdempotencia chave = ChaveIdempotencia.De(E2eEmSequencia(7), EtapaLancamento.DebitoPagador);
        int commitsAntes = ledger.Log().Count;

        ChaveIdempotenciaDuplicadaException excecao = Assert.Throws<ChaveIdempotenciaDuplicadaException>(
            () => ledger.Lancar(
                Lancamento.Criar(cliente, espelho, Valor.DeCentavos(10_00), chave, "primeira ocorrencia"),
                Lancamento.Criar(cliente, espelho, Valor.DeCentavos(10_00), chave, "segunda ocorrencia")));

        Assert.Equal(chave.Texto, excecao.Chave);
        Assert.False(ledger.ContemChave(chave));

        int commitsDepois = ledger.Log().Count;
        Assert.Equal(commitsAntes, commitsDepois);
        Assert.Equal(new Saldo(1_000_00), ledger.SaldoNatural(cliente));
    }

    // ---------------------------------------------------------------------------------------
    // Guarda de saldo negativo: unica e universal, sem excecao para nenhuma classe de conta
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_DebitandoAlemDoSaldoDoCliente_LancaSaldoNegativoApontandoAContaQueCairia()
    {
        // O espelho tem 1_000_00 e o primeiro cliente so 600_00: o debito de 800_00 derruba o
        // cliente e deixa o espelho em 200_00. Num ledger de um cliente so, cliente e espelho valem
        // sempre o mesmo e caem juntos — a excecao apontaria qualquer um dos dois e a assercao
        // passaria por ordem de dicionario, nao por regra.
        Ledger ledger = PspComDoisClientes(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);

        SaldoNegativoException excecao = Assert.Throws<SaldoNegativoException>(
            () => ledger.Lancar(Lancamento.Criar(
                cliente,
                espelho,
                Valor.DeCentavos(800_00),
                ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
                "debito maior que o saldo do cliente")));

        Assert.Equal(cliente, excecao.Conta);
        Assert.Equal(new Saldo(600_00), excecao.SaldoAtual);
        Assert.Equal(new Saldo(-200_00), excecao.SaldoResultante);

        // A classe viaja na excecao para que o consumidor distinga as duas leituras sem depender do
        // texto da mensagem: saldo insuficiente de cliente e rejeicao de negocio, saldo negativo em
        // conta PI e violacao de invariante e vai para a conciliacao.
        Assert.Equal(ClasseConta.Cliente, excecao.Classe);

        Assert.Equal(new Saldo(600_00), ledger.SaldoNatural(cliente));
        Assert.Equal(new Saldo(1_000_00), ledger.SaldoNatural(espelho));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_LevandoQualquerClasseDeContaAbaixoDeZero_ERecusadoInclusiveNaContraContaDeAbertura()
    {
        // Nao existe conta com descoberto. A contra-conta de abertura nao e excecao: sendo ativo,
        // o debito do genesis a deixa POSITIVA, entao o que a mantinha fora da guarda no modelo
        // antigo era o sinal invertido, e nunca um privilegio. Este teste percorre as quatro
        // classes e exige a mesma recusa em todas.
        HashSet<ClasseConta> recusadas = [];

        // --- Cliente (passivo). Debito acima do saldo do segundo cliente; o espelho, com 1_000_00,
        // continua positivo, entao so uma conta cairia e a excecao aponta exatamente ela.
        Ledger psp = PspComDoisClientes(new RelogioFake());
        ContaId segundoCliente = ContaId.Cliente(psp.Id, SegundoCliente);

        SaldoNegativoException noCliente = Assert.Throws<SaldoNegativoException>(
            () => psp.Lancar(Lancamento.Criar(
                segundoCliente,
                EspelhoDe(psp),
                Valor.DeCentavos(500_00),
                ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
                "debito acima do saldo do segundo cliente")));

        Assert.Equal(segundoCliente, noCliente.Conta);
        Assert.Equal(ClasseConta.Cliente, noCliente.Classe);
        Assert.Equal(new Saldo(400_00), psp.SaldoNatural(segundoCliente));
        recusadas.Add(noCliente.Classe);

        // --- Pi (passivo). A conta PI pagadora tem 1_000_00 e a liquidacao pede 1_500_00: so ela
        // cairia, ja que a PI recebedora e creditada. E aqui que o invariante 8 mora.
        Ledger spi = SpiComContasAbertas(new RelogioFake());
        ContaId piOutro = ContaId.Pi(IspbOutro);

        spi.Lancar(Lancamento.Criar(
            ContaAberturaSpi,
            ContaPiPadrao,
            Valor.DeCentavos(1_000_00),
            ChaveIdempotencia.Genesis(ContaPiPadrao),
            "genesis do pool do SPI"));

        SaldoNegativoException naPi = Assert.Throws<SaldoNegativoException>(
            () => spi.Lancar(Lancamento.Criar(
                ContaPiPadrao,
                piOutro,
                Valor.DeCentavos(1_500_00),
                ChaveIdempotencia.De(E2eEmSequencia(2), EtapaLancamento.Liquidacao),
                "liquidacao acima do saldo da conta PI pagadora")));

        Assert.Equal(ContaPiPadrao, naPi.Conta);
        Assert.Equal(ClasseConta.Pi, naPi.Classe);
        Assert.Equal(new Saldo(1_000_00), spi.SaldoNatural(ContaPiPadrao));
        recusadas.Add(naPi.Classe);

        // --- EspelhoPi e Abertura, as duas contas de ativo, no mesmo par e em direcoes opostas.
        // Nesse par o natural de uma e o simetrico do da outra, entao cada direcao derruba
        // exatamente uma delas — e nenhuma das duas tem licenca para ficar negativa.
        Ledger ativos = new(LedgerId.Psp(IspbOutro), new RelogioFake());
        ContaId espelho = ContaId.EspelhoPi(ativos.Id);
        ContaId abertura = ContaId.Abertura(ativos.Id);
        ativos.Abrir(PlanoDeContas.EspelhoPi(ativos.Id), Conta.De(abertura));

        // D ABERTURA / C ESPELHO_PI: creditar um ativo o derruba. O espelho nao nasce credor.
        SaldoNegativoException noEspelho = Assert.Throws<SaldoNegativoException>(
            () => ativos.Lancar(Lancamento.Criar(
                abertura,
                espelho,
                Valor.DeCentavos(500_00),
                ChaveIdempotencia.Genesis(espelho),
                "genesis na direcao errada: credita o espelho")));

        Assert.Equal(espelho, noEspelho.Conta);
        Assert.Equal(ClasseConta.EspelhoPi, noEspelho.Classe);
        Assert.Equal(Saldo.Zero, ativos.SaldoNatural(espelho));
        recusadas.Add(noEspelho.Classe);

        // D ESPELHO_PI / C ABERTURA: a mesma contra-conta que o genesis correto deixa positiva
        // fica negativa se for creditada, e a guarda a recusa como recusaria qualquer outra.
        SaldoNegativoException naAbertura = Assert.Throws<SaldoNegativoException>(
            () => ativos.Lancar(Lancamento.Criar(
                espelho,
                abertura,
                Valor.DeCentavos(500_00),
                ChaveIdempotencia.Genesis(abertura),
                "genesis na direcao errada: credita a abertura")));

        Assert.Equal(abertura, naAbertura.Conta);
        Assert.Equal(ClasseConta.Abertura, naAbertura.Classe);
        Assert.Equal(Saldo.Zero, ativos.SaldoNatural(abertura));
        recusadas.Add(naAbertura.Classe);

        // Guarda contra cobertura parcial: se uma classe nova entrasse no enum, este teste passaria
        // a mentir sobre "todas as classes" sem que ninguem percebesse.
        Assert.Equal(Enum.GetValues<ClasseConta>().Length, recusadas.Count);
        Assert.Equal(4, recusadas.Count);

        // Nada foi gravado em nenhum dos ledgers: as quatro recusas sao anteriores a mutacao.
        Assert.Empty(ativos.Log(1));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_GenesisDoSpi_DeixaAContraContaDeAberturaPositivaEIgualAoPool()
    {
        // Controle positivo do teste acima: a contra-conta nao fica de fora da guarda porque nunca
        // precisa ficar. Sendo ativo, o debito do genesis a deixa com o pool inteiro do lado
        // natural, e nenhuma conta do sistema precisa de descoberto para o genesis fechar.
        Ledger spi = SpiComContasAbertas(new RelogioFake());

        spi.Lancar(
            Lancamento.Criar(
                ContaAberturaSpi,
                ContaPiPadrao,
                Valor.DeCentavos(1_000_00),
                ChaveIdempotencia.Genesis(ContaPiPadrao),
                "genesis da conta PI do participante padrao"),
            Lancamento.Criar(
                ContaAberturaSpi,
                ContaId.Pi(IspbOutro),
                Valor.DeCentavos(1_000_00),
                ChaveIdempotencia.Genesis(ContaId.Pi(IspbOutro)),
                "genesis da conta PI do outro participante"));

        Assert.Equal(new Saldo(2_000_00), spi.SaldoNatural(ContaAberturaSpi));
        Assert.False(spi.SaldoNatural(ContaAberturaSpi).EhNegativo);
        Assert.Equal(new Saldo(1_000_00), spi.SaldoNatural(ContaPiPadrao));
        Assert.Equal(new Saldo(1_000_00), spi.SaldoNatural(ContaId.Pi(IspbOutro)));

        // O bruto e que soma zero no ledger inteiro: o natural nao soma constante nenhuma, porque
        // ativo e passivo leem o mesmo bruto com sinais opostos.
        Assert.Equal(new Saldo(-2_000_00), spi.SaldoBruto(ContaAberturaSpi));
        Assert.Equal(new Saldo(1_000_00), spi.SaldoBruto(ContaPiPadrao));
        Assert.Equal(new Saldo(1_000_00), spi.SaldoBruto(ContaId.Pi(IspbOutro)));
    }

    // ---------------------------------------------------------------------------------------
    // Atomicidade
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_QuandoOSegundoLancamentoViolaAGuardaDeSaldo_DeixaOLedgerExatamenteComoEstava()
    {
        // O primeiro lancamento, isolado, passaria: 600 - 400 = 200. E o ato inteiro que e avaliado
        // antes de qualquer mutacao. Se a guarda rodasse durante a aplicacao, o primeiro ja estaria
        // gravado quando o segundo estourasse: saldo errado, log com commit parcial e a chave do
        // primeiro queimada sem que o ato tenha acontecido.
        Ledger ledger = PspComDoisClientes(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);

        ChaveIdempotencia chavePrimeiro = ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador);
        ChaveIdempotencia chaveSegundo = ChaveIdempotencia.De(E2eEmSequencia(2), EtapaLancamento.DebitoPagador);

        int commitsAntes = ledger.Log().Count;
        Saldo clienteAntes = ledger.SaldoNatural(cliente);
        Saldo espelhoAntes = ledger.SaldoNatural(espelho);
        Saldo minimoAntes = ledger.MinimoNatural(cliente);

        SaldoNegativoException excecao = Assert.Throws<SaldoNegativoException>(
            () => ledger.Lancar(
                Lancamento.Criar(cliente, espelho, Valor.DeCentavos(400_00), chavePrimeiro, "primeiro debito"),
                Lancamento.Criar(cliente, espelho, Valor.DeCentavos(300_00), chaveSegundo, "segundo debito")));

        // O ato inteiro pede 700_00 de uma conta com 600_00; o espelho, com 1_000_00, sobra positivo,
        // entao a excecao aponta a unica conta que cairia.
        Assert.Equal(cliente, excecao.Conta);
        Assert.Equal(ClasseConta.Cliente, excecao.Classe);
        Assert.Equal(new Saldo(600_00), excecao.SaldoAtual);
        Assert.Equal(new Saldo(-100_00), excecao.SaldoResultante);

        int commitsDepois = ledger.Log().Count;
        Assert.Equal(commitsAntes, commitsDepois);
        Assert.Equal(clienteAntes, ledger.SaldoNatural(cliente));
        Assert.Equal(espelhoAntes, ledger.SaldoNatural(espelho));
        Assert.Equal(minimoAntes, ledger.MinimoNatural(cliente));

        // A chave do PRIMEIRO lancamento nao pode ter sido queimada: a reentrega legitima desse
        // debito precisa continuar possivel.
        Assert.False(ledger.ContemChave(chavePrimeiro));
        Assert.False(ledger.ContemChave(chaveSegundo));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_QuandoOSaldoEstouraOLong_LancaEstouroDeValorENaoGravaNada()
    {
        // Wrap silencioso de long e o pior modo de falha do projeto: quebraria a soma de saldos sem
        // lancar nada, e a propria verificacao de soma daria wrap junto.
        // D ESPELHO_PI (ativo) / C CLIENTE (passivo) mantem as duas contas com saldo natural
        // positivo, entao a guarda de nao-negatividade nao dispara e o ato chega intacto ao
        // acumulo — que e exatamente onde o estouro precisa ser detectado.
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);

        Valor extremo = Valor.DeCentavos(long.MaxValue);
        ledger.Lancar(Lancamento.Criar(
            espelho,
            cliente,
            extremo,
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "primeiro extremo"));

        int commitsAntes = ledger.Log().Count;
        Saldo saldoAntes = ledger.SaldoNatural(cliente);

        EstouroDeValorException excecao = Assert.Throws<EstouroDeValorException>(
            () => ledger.Lancar(Lancamento.Criar(
                espelho,
                cliente,
                extremo,
                ChaveIdempotencia.De(E2eEmSequencia(2), EtapaLancamento.DebitoPagador),
                "segundo extremo")));

        Assert.Equal("saldo do ledger", excecao.Operacao);

        int commitsDepois = ledger.Log().Count;
        Assert.Equal(commitsAntes, commitsDepois);
        Assert.Equal(saldoAntes, ledger.SaldoNatural(cliente));
    }

    // ---------------------------------------------------------------------------------------
    // Sequencia, instante e log
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Commits_NumeramASequenciaAPartirDeUmContandoAberturasELancamentos()
    {
        Ledger ledger = NovoLedgerPsp(new RelogioFake());
        ContaId cliente = ContaId.Cliente(ledger.Id, PrimeiroCliente);
        ContaId espelho = ContaId.EspelhoPi(ledger.Id);
        ContaId segundo = ContaId.Cliente(ledger.Id, SegundoCliente);

        Commit primeiroCommit = ledger.Abrir(
            PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente),
            PlanoDeContas.EspelhoPi(ledger.Id));
        Commit segundoCommit = ledger.Lancar(Lancamento.Criar(
            espelho,
            cliente,
            Valor.DeCentavos(1_000_00),
            ChaveIdempotencia.Genesis(cliente),
            "genesis do PSP"));
        Commit terceiroCommit = ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, SegundoCliente));
        Commit quartoCommit = ledger.Lancar(Lancamento.Criar(
            cliente,
            segundo,
            Valor.DeCentavos(100_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "transferencia interna"));

        Assert.Equal(1L, primeiroCommit.Sequencia);
        Assert.Equal(2L, segundoCommit.Sequencia);
        Assert.Equal(3L, terceiroCommit.Sequencia);
        Assert.Equal(4L, quartoCommit.Sequencia);
        Assert.Equal(ledger.Id, quartoCommit.Ledger);

        Assert.Collection(
            ledger.Log(),
            commit => Assert.Equal(1L, commit.Sequencia),
            commit => Assert.Equal(2L, commit.Sequencia),
            commit => Assert.Equal(3L, commit.Sequencia),
            commit => Assert.Equal(4L, commit.Sequencia));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Commit_Em_VemDoRelogioInjetadoENaoDoRelogioDaMaquina()
    {
        DateTimeOffset inicio = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        TimeSpan intervalo = TimeSpan.FromMinutes(7);
        RelogioFake relogio = new(inicio);
        Ledger ledger = PspComContasAbertas(relogio);

        Commit abertura = Assert.Single(ledger.Log());
        Assert.Equal(inicio, abertura.Em);

        relogio.Avancar(intervalo);
        Commit lancamento = ledger.Lancar(Lancamento.Criar(
            EspelhoDe(ledger),
            ClienteDe(ledger),
            Valor.DeCentavos(1_000_00),
            ChaveIdempotencia.Genesis(ClienteDe(ledger)),
            "genesis do PSP"));

        Assert.Equal(inicio + intervalo, lancamento.Em);
        Assert.Equal(intervalo, lancamento.Em - abertura.Em);
    }

    [Theory]
    [InlineData(-1L, 4)]
    [InlineData(0L, 4)]
    [InlineData(1L, 3)]
    [InlineData(2L, 2)]
    [InlineData(3L, 1)]
    [InlineData(4L, 0)]
    [InlineData(999L, 0)]
    [Trait("gate", "1")]
    public void Log_DesdeSequencia_EhExclusivoNoLimiteInformado(long desdeSequencia, int commitsEsperados)
    {
        Ledger ledger = LedgerComQuatroCommits(new RelogioFake());

        int quantidade = ledger.Log(desdeSequencia).Count;

        Assert.Equal(commitsEsperados, quantidade);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Log_DesdeSequenciaUm_OmiteOCommitDeSequenciaUm()
    {
        Ledger ledger = LedgerComQuatroCommits(new RelogioFake());

        IReadOnlyList<Commit> recorte = ledger.Log(1);

        Assert.Equal(2L, recorte[0].Sequencia);
        Assert.DoesNotContain(recorte, commit => commit.Sequencia == 1L);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Log_NosDoisRamos_DevolveColecaoSomenteLeituraEDesligadaDoLedger()
    {
        Ledger ledger = LedgerComQuatroCommits(new RelogioFake());

        IReadOnlyList<Commit> inteiro = ledger.Log();
        IReadOnlyList<Commit> recorte = ledger.Log(1);

        // A versao anterior deste teste fazia `(leitura as List<Commit>)?.Clear()`: com o ramo
        // devolvendo List de verdade o Clear rodava e apagava o historico; com ele devolvendo outra
        // coisa o `as` dava nulo e o teste passava sem executar nada. Em nenhum dos dois casos ele
        // provava o que promete. Aqui a imutabilidade e exigida diretamente, e nos DOIS ramos: se
        // um deles devolvesse a lista sintetizada de uma expressao de colecao, a garantia passaria
        // a depender de qual sobrecarga o chamador usou.
        Assert.IsNotType<List<Commit>>(inteiro);
        Assert.IsNotType<List<Commit>>(recorte);

        Assert.Throws<NotSupportedException>(() => ((IList<Commit>)inteiro).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Commit>)recorte).Clear());

        long[] sequenciasRelidas = ledger.Log().Select(commit => commit.Sequencia).ToArray();
        Assert.Equal(new[] { 1L, 2L, 3L, 4L }, sequenciasRelidas);

        // E uma copia, e nao uma vista: um append posterior nao aparece na leitura ja entregue.
        ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, "0003"));

        Assert.Equal(4, inteiro.Count);
        Assert.Equal(5, ledger.Log().Count);
    }

    // ---------------------------------------------------------------------------------------
    // Minimo natural
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void MinimoNatural_QuandoAContaCaiEDepoisSeRecompoe_NaoAcompanhaARecuperacao()
    {
        // Esta e a razao de o campo existir: o saldo corrente sobe de volta e nao denuncia nada,
        // enquanto o minimo so pode descer. A excursao aqui e inteira em territorio nao negativo,
        // porque nenhuma conta do sistema chega a ficar negativa — o piso de toda conta e o valor
        // que ela tinha na abertura, que e zero, e por isso o minimo observavel e zero.
        // Um minimo negativo em qualquer conta seria, por si so, prova de que a guarda vazou.
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId cliente = ClienteDe(ledger);
        ContaId espelho = EspelhoDe(ledger);
        Fundear(ledger, 1_000_00);

        Assert.Equal(new Saldo(1_000_00), ledger.SaldoNatural(cliente));

        // Fundo da excursao: 1_000_00 -> 100_00.
        ledger.Lancar(Lancamento.Criar(
            cliente,
            espelho,
            Valor.DeCentavos(900_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "debito quase total"));

        Saldo saldoNoFundo = ledger.SaldoNatural(cliente);
        Saldo minimoNoFundo = ledger.MinimoNatural(cliente);

        Assert.Equal(new Saldo(100_00), saldoNoFundo);
        Assert.Equal(Saldo.Zero, minimoNoFundo);
        Assert.True(minimoNoFundo <= saldoNoFundo);

        // Recomposicao: 100_00 -> 900_00. O saldo volta ao alto...
        ledger.Lancar(Lancamento.Criar(
            espelho,
            cliente,
            Valor.DeCentavos(800_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.EstornoPagador),
            "estorno parcial do debito"));

        Assert.Equal(new Saldo(900_00), ledger.SaldoNatural(cliente));

        // ...e o minimo nao sobe junto: ele e monotono nao crescente, e continua sendo um limite
        // inferior valido de tudo o que a conta ja valeu.
        Assert.Equal(minimoNoFundo, ledger.MinimoNatural(cliente));
        Assert.True(ledger.MinimoNatural(cliente) <= saldoNoFundo);
        Assert.True(ledger.MinimoNatural(cliente) <= ledger.SaldoNatural(cliente));
        Assert.False(ledger.MinimoNatural(cliente).EhNegativo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void MinimoNatural_EmTodasAsContasDoLedger_EhZeroPorqueOPisoEOInstanteDaAbertura()
    {
        // O minimo e medido desde a abertura, quando a conta valia zero, e nenhuma conta pode
        // descer abaixo de zero. Segue que o minimo de toda conta e exatamente zero — o campo
        // deixou de distinguir contas entre si e passou a valer como detector: qualquer valor
        // diferente de zero aqui e evidencia direta de que a guarda de nao-negatividade vazou.
        Ledger ledger = PspComDoisClientes(new RelogioFake());

        foreach (Commit commit in ledger.Log())
        {
            foreach (Conta conta in commit.ContasAbertas)
            {
                Assert.Equal(Saldo.Zero, ledger.MinimoNatural(conta.Id));
                Assert.False(ledger.SaldoNatural(conta.Id).EhNegativo);
            }
        }

        Assert.Equal(new Saldo(600_00), ledger.SaldoNatural(ClienteDe(ledger)));
        Assert.Equal(new Saldo(1_000_00), ledger.SaldoNatural(EspelhoDe(ledger)));
    }

    // ---------------------------------------------------------------------------------------
    // Estorno e consultas
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Estorno_ProduzLancamentoNovoEDevolveOSaldoSemAlterarODebitoOriginal()
    {
        CenarioContabil cenario = CenarioContabil.Padrao();
        Ledger pagador = cenario.LedgerPagador;
        EndToEndId e2e = new GeradorE2eDeterministico(cenario.IspbPagador, RelogioFake.Epoca).Proximo();
        Valor valor = Valor.DeCentavos(150_00);

        Saldo saldoInicial = pagador.SaldoNatural(cenario.ClientePagador);
        int commitsAntes = pagador.Log().Count;

        cenario.DebitarPagador(e2e, valor);
        Saldo depoisDoDebito = pagador.SaldoNatural(cenario.ClientePagador);

        cenario.EstornarPagador(e2e, valor);

        Assert.Equal(saldoInicial - valor, depoisDoDebito);
        Assert.Equal(saldoInicial, pagador.SaldoNatural(cenario.ClientePagador));

        int commitsDepois = pagador.Log().Count;
        Assert.Equal(commitsAntes + 2, commitsDepois);

        // Dois lancamentos para o mesmo E2E, nunca um UPDATE do primeiro: o debito original
        // continua no log, intacto, com a sua etapa e o seu sentido.
        List<Lancamento> doE2e = pagador.Log()
            .SelectMany(commit => commit.Lancamentos)
            .Where(lancamento => e2e.Equals(lancamento.Chave.E2E))
            .ToList();

        Assert.Collection(
            doE2e,
            lancamento =>
            {
                Assert.Equal(EtapaLancamento.DebitoPagador, lancamento.Chave.Etapa);
                Assert.Equal(cenario.ClientePagador, lancamento.Debito);
                Assert.Equal(cenario.EspelhoPagador, lancamento.Credito);
                Assert.Equal(valor, lancamento.Valor);
            },
            lancamento =>
            {
                Assert.Equal(EtapaLancamento.EstornoPagador, lancamento.Chave.Etapa);
                Assert.Equal(cenario.EspelhoPagador, lancamento.Debito);
                Assert.Equal(cenario.ClientePagador, lancamento.Credito);
                Assert.Equal(valor, lancamento.Valor);
            });
    }

    [Fact]
    [Trait("gate", "1")]
    public void ConsultasDeSaldo_EmContaNaoAberta_LancamContaDesconhecida()
    {
        // Todas as leituras tem o mesmo contrato. Devolver Saldo.Zero em qualquer uma delas faria a
        // conta que nunca existiu ficar indistinguivel da conta que existe e esta zerada.
        Ledger ledger = PspComContasAbertas(new RelogioFake());
        ContaId inexistente = ContaId.Cliente(ledger.Id, "9999");

        ContaDesconhecidaException noSaldo = Assert.Throws<ContaDesconhecidaException>(
            () => ledger.SaldoNatural(inexistente));
        ContaDesconhecidaException noBruto = Assert.Throws<ContaDesconhecidaException>(
            () => ledger.SaldoBruto(inexistente));
        ContaDesconhecidaException noMinimo = Assert.Throws<ContaDesconhecidaException>(
            () => ledger.MinimoNatural(inexistente));
        ContaDesconhecidaException noSnapshot = Assert.Throws<ContaDesconhecidaException>(
            () => ledger.Snapshot().Natural(inexistente));
        ContaDesconhecidaException naLeituraConsistente = Assert.Throws<ContaDesconhecidaException>(
            () => ledger.LerConsistente().Projecao.MinimoNatural(inexistente));

        Assert.Equal(inexistente, noSaldo.Conta);
        Assert.Equal(inexistente, noBruto.Conta);
        Assert.Equal(inexistente, noMinimo.Conta);
        Assert.Equal(inexistente, noSnapshot.Conta);
        Assert.Equal(inexistente, naLeituraConsistente.Conta);

        Assert.False(ledger.ContemConta(inexistente));
        Assert.False(ledger.Snapshot().Contem(inexistente));
    }

    [Fact]
    [Trait("gate", "1")]
    public void LerConsistente_DevolveLogEProjecaoDoMesmoInstante()
    {
        // Log() seguido de Snapshot() nao compoe: um append entre as duas chamadas faz o gate de
        // replay acusar divergencia que nunca existiu. Aqui as duas leituras saem juntas, e o
        // replay do log entregue tem de reproduzir exatamente a projecao entregue com ele.
        Ledger ledger = LedgerComQuatroCommits(new RelogioFake());

        (IReadOnlyList<Commit> log, SnapshotSaldos projecao) = ledger.LerConsistente();

        Assert.Equal(ledger.Log().Count, log.Count);
        Assert.True(ProjecaoSaldos.Reconstruir(log).Snapshot().EhIgualA(projecao));

        // O par entregue nao envelhece junto com o ledger: continua descrevendo o instante da leitura.
        ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, "0004"));

        Assert.Equal(4, log.Count);
        Assert.Equal(5, ledger.Log().Count);
        Assert.False(projecao.Contem(ContaId.Cliente(ledger.Id, "0004")));
    }

    [Theory]
    [InlineData("Alterar")]
    [InlineData("Atualizar")]
    [InlineData("Remover")]
    [InlineData("Excluir")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [Trait("gate", "1")]
    public void PortasDoLedger_NaoExpoemMetodoDeAlteracaoOuRemocao(string proibido)
    {
        // A regra "correcao e estorno sao lancamentos novos" e imposta pela ausencia de API, e nao
        // pela disciplina de quem escreve o chamador. IInspecaoLedger entra na varredura: a porta
        // de inspecao nasceu depois e nao pode ser a fresta por onde uma API de alteracao entra.
        IEnumerable<string> nomes = typeof(ILedger).GetMethods()
            .Concat(typeof(IConsultaLedger).GetMethods())
            .Concat(typeof(IInspecaoLedger).GetMethods())
            .Concat(typeof(Ledger).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(metodo => metodo.Name);

        Assert.DoesNotContain(nomes, nome => nome.Contains(proibido, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("gate", "1")]
    public void PortasDoLedger_SeparamEscritaDeInspecao()
    {
        // A porta de escrita nao pode carregar as leituras de inspecao: quem so precisa conferir
        // saldo, minimo ou snapshot receberia junto o direito de lancar.
        IEnumerable<string> naPortaDeEscrita = typeof(ILedger).GetMethods().Select(metodo => metodo.Name);

        foreach (string soDeInspecao in new[] { "MinimoNatural", "SaldoBruto", "Snapshot", "LerConsistente" })
        {
            Assert.DoesNotContain(naPortaDeEscrita, nome => string.Equals(nome, soDeInspecao, StringComparison.Ordinal));
        }

        // E a porta de inspecao nao pode carregar as de escrita.
        IEnumerable<string> naPortaDeInspecao = typeof(IInspecaoLedger).GetMethods().Select(metodo => metodo.Name);

        foreach (string soDeEscrita in new[] { "Lancar", "Abrir" })
        {
            Assert.DoesNotContain(naPortaDeInspecao, nome => string.Equals(nome, soDeEscrita, StringComparison.Ordinal));
        }

        // As leituras de inspecao existem mesmo — sem isto, o teste passaria se a interface fosse vazia.
        foreach (string esperado in new[] { "MinimoNatural", "SaldoBruto", "Snapshot", "LerConsistente" })
        {
            Assert.Contains(naPortaDeInspecao, nome => string.Equals(nome, esperado, StringComparison.Ordinal));
        }

        // O Ledger concreto implementa as tres, que e o que permite ao teste segurar um so objeto.
        Assert.IsAssignableFrom<ILedger>(NovoLedgerPsp(new RelogioFake()));
        Assert.IsAssignableFrom<IConsultaLedger>(NovoLedgerPsp(new RelogioFake()));
        Assert.IsAssignableFrom<IInspecaoLedger>(NovoLedgerPsp(new RelogioFake()));
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    private static Ledger NovoLedgerPsp(RelogioFake relogio) => new(LedgerId.Psp(IspbPadrao), relogio);

    private static ContaId ClienteDe(Ledger ledger) => ContaId.Cliente(ledger.Id, PrimeiroCliente);

    private static ContaId EspelhoDe(Ledger ledger) => ContaId.EspelhoPi(ledger.Id);

    private static EndToEndId E2eEmSequencia(long indice) =>
        new GeradorE2eDeterministico(IspbPadrao, RelogioFake.Epoca).EmSequencia(indice);

    private static Ledger PspComContasAbertas(RelogioFake relogio)
    {
        Ledger ledger = NovoLedgerPsp(relogio);
        ledger.Abrir(
            PlanoDeContas.Cliente(ledger.Id, PrimeiroCliente),
            PlanoDeContas.EspelhoPi(ledger.Id));

        return ledger;
    }

    /// <summary>
    /// SPI com a contra-conta de abertura e as contas PI dos dois participantes. As duas PI existem
    /// para que a liquidacao tenha para onde ir: com uma so, todo debito derruba tambem a
    /// contrapartida e a guarda deixa de apontar uma conta so.
    /// </summary>
    private static Ledger SpiComContasAbertas(RelogioFake relogio)
    {
        Ledger spi = new(LedgerId.Spi, relogio);
        spi.Abrir(PlanoDeContas.AberturaSpi(), PlanoDeContas.Pi(IspbPadrao), PlanoDeContas.Pi(IspbOutro));

        return spi;
    }

    /// <summary>
    /// PSP com o espelho em 1_000_00, o primeiro cliente em 600_00 e o segundo em 400_00.
    /// <para>
    /// O espelho maior que qualquer cliente e o que permite exigir da guarda que ela aponte
    /// exatamente a conta que cairia: num ledger de um cliente so, o espelho vale sempre o mesmo
    /// que o cliente e os dois ficam negativos no mesmo ato, entao a excecao poderia apontar
    /// qualquer um dos dois e a assercao passaria por ordem de dicionario, nao por regra.
    /// </para>
    /// </summary>
    private static Ledger PspComDoisClientes(RelogioFake relogio)
    {
        Ledger ledger = PspComContasAbertas(relogio);
        Fundear(ledger, 1_000_00);
        ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, SegundoCliente));

        ledger.Lancar(Lancamento.Criar(
            ClienteDe(ledger),
            ContaId.Cliente(ledger.Id, SegundoCliente),
            Valor.DeCentavos(400_00),
            ChaveIdempotencia.De(E2eEmSequencia(900), EtapaLancamento.DebitoPagador),
            "reparticao entre clientes do mesmo PSP"));

        return ledger;
    }

    /// <summary>Genesis do PSP: D ESPELHO_PI / C CLIENTE, que deixa as duas contas positivas.</summary>
    private static void Fundear(Ledger ledger, long centavos) =>
        ledger.Lancar(Lancamento.Criar(
            EspelhoDe(ledger),
            ClienteDe(ledger),
            Valor.DeCentavos(centavos),
            ChaveIdempotencia.Genesis(ClienteDe(ledger)),
            "genesis do PSP"));

    private static Ledger LedgerComQuatroCommits(RelogioFake relogio)
    {
        Ledger ledger = PspComContasAbertas(relogio);
        Fundear(ledger, 1_000_00);
        ledger.Abrir(PlanoDeContas.Cliente(ledger.Id, SegundoCliente));
        ledger.Lancar(Lancamento.Criar(
            ClienteDe(ledger),
            ContaId.Cliente(ledger.Id, SegundoCliente),
            Valor.DeCentavos(100_00),
            ChaveIdempotencia.De(E2eEmSequencia(1), EtapaLancamento.DebitoPagador),
            "transferencia interna"));

        return ledger;
    }
}
