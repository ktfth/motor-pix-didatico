using System.Reflection;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// A unica porta de emissao de dinheiro do sistema: a contra-conta de abertura e o ato de genesis
/// que a toca.
/// <para>
/// O genesis e um prefixo auto-selante do log. Nenhuma chamada de "fechar abertura" existe, e e
/// justamente por nao existir que ninguem pode esquecer de faze-la: assim que o primeiro ato
/// operacional entra, o ledger deixa de aceitar genesis para sempre, e a contra-conta deixa de ser
/// enderecavel.
/// </para>
/// <para>
/// Os saldos esperados sao escritos a mao aqui, nunca derivados da mesma funcao que o ledger usa
/// para calcula-los: um oraculo montado com <c>Conta.NaturalDe</c> provaria apenas que a funcao e
/// deterministica.
/// </para>
/// </summary>
public sealed class GuardasDeEmissaoTestes
{
    private const long FundoEmCentavos = 1_000_00;

    /// <summary>Dois participantes de R$ 1.000,00 cada: o pool nasce valendo R$ 2.000,00.</summary>
    private const long PoolEmCentavos = 2_000_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    private static readonly ContaId ContaAberturaSpi = ContaId.Abertura(LedgerId.Spi);
    private static readonly ContaId ContaPiPagador = ContaId.Pi(IspbPagador);
    private static readonly ContaId ContaPiRecebedor = ContaId.Pi(IspbRecebedor);

    // -------------------------------------------------------------------------------------------
    // O ato de genesis
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_AtoDeGenesisEmLedgerVirgem_DeixaAAberturaComSaldoNaturalPositivo()
    {
        // D ABERTURA / C PI, duas vezes. ABERTURA e ATIVO: o debito do genesis a deixa POSITIVA,
        // e por isso nenhuma conta do sistema precisa de descoberto. Enquanto ela era passivo, o
        // mesmo lancamento a jogava para -R$ 2.000,00 e obrigava a existir um invariante desligavel
        // ("esta conta pode ficar negativa") bem ao lado da guarda que protege o invariante 8.
        Ledger spi = SpiVirgemComContasAbertas(new RelogioFake());

        Commit genesis = spi.Lancar(
            LancamentoDeGenesis(ContaPiPagador, FundoEmCentavos),
            LancamentoDeGenesis(ContaPiRecebedor, FundoEmCentavos));

        Assert.Equal(new Saldo(PoolEmCentavos), spi.SaldoNatural(ContaAberturaSpi));
        Assert.False(spi.SaldoNatural(ContaAberturaSpi).EhNegativo);
        Assert.Equal(new Saldo(FundoEmCentavos), spi.SaldoNatural(ContaPiPagador));
        Assert.Equal(new Saldo(FundoEmCentavos), spi.SaldoNatural(ContaPiRecebedor));

        // O saldo natural nunca desceu abaixo de zero em nenhuma das tres contas: o minimo, que
        // registra o pior momento e nao so o estado final, continua zerado.
        Assert.Equal(Saldo.Zero, spi.MinimoNatural(ContaAberturaSpi));
        Assert.Equal(Saldo.Zero, spi.MinimoNatural(ContaPiPagador));
        Assert.Equal(Saldo.Zero, spi.MinimoNatural(ContaPiRecebedor));

        // O bruto (creditos - debitos) e o negativo do natural, porque a conta e de ativo.
        Assert.Equal(new Saldo(-PoolEmCentavos), spi.SaldoBruto(ContaAberturaSpi));
        Assert.Equal(2L, genesis.Sequencia);
        Assert.Equal(2, genesis.Lancamentos.Count);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_GenesisDepoisDeAtoOperacional_LancaGenesisAposOperacaoComOLedgerCorreto()
    {
        // Este e o teste que sela a emissao. Sem ele, um ato de genesis apresentado a qualquer
        // momento da vida do ledger continuaria criando dinheiro, e nenhuma outra guarda perceberia:
        // debito e credito batem, e a soma dos saldos brutos permanece zero.
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);
        Ledger spi = cenario.LedgerSpi;

        cenario.DebitarPagador(E2e(1), Valor.DeCentavos(10_00));
        cenario.Liquidar(E2e(1), Valor.DeCentavos(10_00));

        int commitsAntes = spi.Log().Count;
        SnapshotSaldos antes = spi.Snapshot();
        ChaveIdempotencia chave = ChaveIdempotencia.Genesis(cenario.AberturaSpi);

        GenesisAposOperacaoException excecao = Assert.Throws<GenesisAposOperacaoException>(
            () => spi.Lancar(Lancamento.Criar(
                cenario.AberturaSpi,
                cenario.PiPagador,
                Valor.DeCentavos(500_00),
                chave,
                "genesis tardio: emissao fora da abertura")));

        Assert.Equal(LedgerId.Spi, excecao.Ledger);
        Assert.Equal(spi.Id, excecao.Ledger);

        Assert.Equal(commitsAntes, spi.Log().Count);
        Assert.False(spi.ContemChave(chave));
        Assert.True(
            antes.EhIgualA(spi.Snapshot()),
            $"o genesis tardio mexeu no ledger {spi.Id}: {Junte(antes.Diferencas(spi.Snapshot()))}");
    }

    // -------------------------------------------------------------------------------------------
    // A contra-conta de abertura fora do genesis
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_AtoOperacionalComAAberturaNoDebito_LancaAberturaAposGenesis()
    {
        // Por que este caso importa: D ABERTURA / C PI criaria saldo no pool sem quebrar partidas
        // dobradas. Debito e credito batem, a soma dos brutos continua zero, e so a conservacao do
        // pool denunciaria — depois, e por diferenca, quando ninguem mais sabe qual ato mentiu.
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);
        Ledger spi = cenario.LedgerSpi;

        int commitsAntes = spi.Log().Count;
        ChaveIdempotencia chave = ChaveIdempotencia.De(E2e(2), EtapaLancamento.Liquidacao);

        AberturaAposGenesisException excecao = Assert.Throws<AberturaAposGenesisException>(
            () => spi.Lancar(Lancamento.Criar(
                cenario.AberturaSpi,
                cenario.PiPagador,
                Valor.DeCentavos(500_00),
                chave,
                "emissao disfarcada de liquidacao")));

        Assert.Equal(cenario.AberturaSpi, excecao.Conta);
        Assert.Equal(ClasseConta.Abertura, excecao.Conta.Classe);

        Assert.Equal(commitsAntes, spi.Log().Count);
        Assert.False(spi.ContemChave(chave));
        Assert.Equal(new Saldo(PoolEmCentavos), spi.SaldoNatural(cenario.AberturaSpi));
        Assert.Equal(new Saldo(FundoEmCentavos), spi.SaldoNatural(cenario.PiPagador));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_AtoOperacionalComAAberturaNoCredito_LancaAberturaAposGenesis()
    {
        // O outro sentido: D PI / C ABERTURA destroi saldo do pool. A guarda tem de olhar as duas
        // pernas, e nao so a que o genesis costuma usar — verificar apenas o debito deixaria a
        // metade destrutiva da mesma porta escancarada.
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);
        Ledger spi = cenario.LedgerSpi;

        int commitsAntes = spi.Log().Count;
        ChaveIdempotencia chave = ChaveIdempotencia.De(E2e(3), EtapaLancamento.Liquidacao);

        AberturaAposGenesisException excecao = Assert.Throws<AberturaAposGenesisException>(
            () => spi.Lancar(Lancamento.Criar(
                cenario.PiPagador,
                cenario.AberturaSpi,
                Valor.DeCentavos(500_00),
                chave,
                "recolhimento disfarcado de liquidacao")));

        Assert.Equal(cenario.AberturaSpi, excecao.Conta);

        Assert.Equal(commitsAntes, spi.Log().Count);
        Assert.False(spi.ContemChave(chave));
        Assert.Equal(new Saldo(PoolEmCentavos), spi.SaldoNatural(cenario.AberturaSpi));
        Assert.Equal(new Saldo(FundoEmCentavos), spi.SaldoNatural(cenario.PiPagador));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_AtoQueMisturaGenesisEOperacional_LancaAtoContabilInvalido()
    {
        // Ato misto e a fuga obvia das duas guardas acima: bastaria pendurar um lancamento de
        // genesis em qualquer ato operacional para que a classificacao do ato inteiro virasse
        // "genesis" e a contra-conta voltasse a ser enderecavel. Por isso a classificacao e
        // tudo-ou-nada.
        Ledger spi = SpiVirgemComContasAbertas(new RelogioFake());
        spi.Lancar(
            LancamentoDeGenesis(ContaPiPagador, FundoEmCentavos),
            LancamentoDeGenesis(ContaPiRecebedor, FundoEmCentavos));

        int commitsAntes = spi.Log().Count;
        ChaveIdempotencia chaveGenesis = ChaveIdempotencia.Genesis(ContaAberturaSpi);
        ChaveIdempotencia chaveOperacional = ChaveIdempotencia.De(E2e(4), EtapaLancamento.Liquidacao);

        AtoContabilInvalidoException excecao = Assert.Throws<AtoContabilInvalidoException>(
            () => spi.Lancar(
                Lancamento.Criar(
                    ContaAberturaSpi,
                    ContaPiPagador,
                    Valor.DeCentavos(500_00),
                    chaveGenesis,
                    "perna de genesis pendurada no ato"),
                Lancamento.Criar(
                    ContaPiPagador,
                    ContaPiRecebedor,
                    Valor.DeCentavos(10_00),
                    chaveOperacional,
                    "perna operacional do mesmo ato")));

        Assert.Equal("ato mistura lancamentos de genesis e operacionais", excecao.Motivo);

        Assert.Equal(commitsAntes, spi.Log().Count);
        Assert.False(spi.ContemChave(chaveGenesis));
        Assert.False(spi.ContemChave(chaveOperacional));
        Assert.Equal(new Saldo(PoolEmCentavos), spi.SaldoNatural(ContaAberturaSpi));
        Assert.Equal(new Saldo(FundoEmCentavos), spi.SaldoNatural(ContaPiPagador));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_RecusasDeEmissao_NaoGravamCommitChaveNemSaldo()
    {
        // As tres recusas contra o MESMO ledger, em sequencia. Uma guarda que recusa mas grava
        // metade do ato e pior do que guarda nenhuma: o log fica com um commit que ninguem
        // autorizou e a chave de idempotencia do lancamento legitimo fica queimada.
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);
        Ledger spi = cenario.LedgerSpi;

        cenario.DebitarPagador(E2e(5), Valor.DeCentavos(40_00));
        cenario.Liquidar(E2e(5), Valor.DeCentavos(40_00));

        int commitsAntes = spi.Log().Count;
        SnapshotSaldos antes = spi.Snapshot();

        ChaveIdempotencia chaveGenesisTardio = ChaveIdempotencia.Genesis(cenario.AberturaSpi);
        ChaveIdempotencia chaveAbertura = ChaveIdempotencia.De(E2e(6), EtapaLancamento.Liquidacao);
        ChaveIdempotencia chaveMistura = ChaveIdempotencia.De(E2e(7), EtapaLancamento.Liquidacao);

        Assert.Throws<GenesisAposOperacaoException>(
            () => spi.Lancar(Lancamento.Criar(
                cenario.AberturaSpi,
                cenario.PiPagador,
                Valor.DeCentavos(500_00),
                chaveGenesisTardio,
                "genesis tardio")));

        Assert.Throws<AberturaAposGenesisException>(
            () => spi.Lancar(Lancamento.Criar(
                cenario.AberturaSpi,
                cenario.PiRecebedor,
                Valor.DeCentavos(500_00),
                chaveAbertura,
                "abertura em ato operacional")));

        // A chave do ato recusado la em cima continua livre — e reusa-la aqui e a prova disso.
        Assert.Throws<AtoContabilInvalidoException>(
            () => spi.Lancar(
                Lancamento.Criar(
                    cenario.AberturaSpi,
                    cenario.PiPagador,
                    Valor.DeCentavos(500_00),
                    chaveGenesisTardio,
                    "perna de genesis"),
                Lancamento.Criar(
                    cenario.PiPagador,
                    cenario.PiRecebedor,
                    Valor.DeCentavos(10_00),
                    chaveMistura,
                    "perna operacional")));

        Assert.Equal(commitsAntes, spi.Log().Count);
        Assert.False(spi.ContemChave(chaveGenesisTardio));
        Assert.False(spi.ContemChave(chaveAbertura));
        Assert.False(spi.ContemChave(chaveMistura));
        Assert.True(
            antes.EhIgualA(spi.Snapshot()),
            $"as recusas de emissao mexeram no ledger {spi.Id}: {Junte(antes.Diferencas(spi.Snapshot()))}");
    }

    // -------------------------------------------------------------------------------------------
    // Conservacao do pool e soma bruta
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Emissao_SeladaAposOGenesis_ConservaOPoolEDeixaAContraContaIntocada()
    {
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);
        Ledger spi = cenario.LedgerSpi;

        long sequenciaDoGenesis = SequenciaDoAtoDeGenesis(spi);
        Assert.Equal(PoolEmCentavos, PoolDoSpi(spi));

        cenario.CaminhoFeliz(E2e(20), Valor.DeCentavos(250_00));
        Assert.Equal(PoolEmCentavos, PoolDoSpi(spi));

        cenario.DebitarPagador(E2e(21), Valor.DeCentavos(100_00));
        cenario.EstornarPagador(E2e(21), Valor.DeCentavos(100_00));
        Assert.Equal(PoolEmCentavos, PoolDoSpi(spi));

        cenario.DebitarPagador(E2e(22), Valor.DeCentavos(30_00));
        cenario.Liquidar(E2e(22), Valor.DeCentavos(30_00));
        Assert.Equal(PoolEmCentavos, PoolDoSpi(spi));

        // Varredura do log: depois do genesis, nenhum commit toca a contra-conta em nenhuma perna.
        int commitsPosteriores = 0;

        foreach (Commit commit in spi.Log())
        {
            if (commit.Sequencia <= sequenciaDoGenesis)
            {
                continue;
            }

            commitsPosteriores++;

            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                Assert.True(
                    !lancamento.Debito.Equals(cenario.AberturaSpi),
                    $"o commit #{commit.Sequencia} debita a contra-conta de abertura: {lancamento}");

                Assert.True(
                    !lancamento.Credito.Equals(cenario.AberturaSpi),
                    $"o commit #{commit.Sequencia} credita a contra-conta de abertura: {lancamento}");
            }
        }

        // Guarda contra vacuidade: se nenhum commit tivesse entrado depois do genesis, a varredura
        // acima passaria sem olhar nada.
        Assert.True(commitsPosteriores > 0, "nenhum commit operacional entrou depois do genesis");
        Assert.Equal(new Saldo(PoolEmCentavos), spi.SaldoNatural(cenario.AberturaSpi));
    }

    [Fact]
    [Trait("gate", "1")]
    public void SaldoBruto_SomadoEmCadaLedger_EhZeroAntesEDepoisDasOperacoes()
    {
        // Esta e a UNICA formulacao literal de "a soma e constante". A soma dos saldos BRUTOS
        // (creditos menos debitos) de um ledger e zero por partidas dobradas, sempre, em qualquer
        // instante. A soma dos saldos NATURAIS nao e constante: na seta 3 o cliente (passivo) e o
        // espelho (ativo) caem JUNTOS, e quem escrever a propriedade sobre naturais colhe uma falha
        // falsa — o assert final deste teste demonstra exatamente essa queda.
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);

        foreach (Ledger ledger in cenario.Ledgers)
        {
            long bruta = SomaBruta(ledger);

            Assert.True(
                bruta == 0L,
                $"antes das operacoes, a soma bruta do ledger {ledger.Id} vale {bruta} centavos");
        }

        long naturaisAntes = SomaNatural(cenario.LedgerPagador);

        cenario.CaminhoFeliz(E2e(30), Valor.DeCentavos(250_00));
        cenario.DebitarPagador(E2e(31), Valor.DeCentavos(100_00));
        cenario.EstornarPagador(E2e(31), Valor.DeCentavos(100_00));
        cenario.DebitarPagador(E2e(32), Valor.DeCentavos(30_00));
        cenario.Liquidar(E2e(32), Valor.DeCentavos(30_00));

        foreach (Ledger ledger in cenario.Ledgers)
        {
            long bruta = SomaBruta(ledger);

            Assert.True(
                bruta == 0L,
                $"depois das operacoes, a soma bruta do ledger {ledger.Id} vale {bruta} centavos");
        }

        long naturaisDepois = SomaNatural(cenario.LedgerPagador);

        Assert.True(
            naturaisAntes != naturaisDepois,
            $"a soma dos naturais do ledger {cenario.LedgerPagador.Id} nao deveria ser constante, "
            + $"e vale {naturaisAntes} centavos antes e {naturaisDepois} centavos depois");
    }

    // -------------------------------------------------------------------------------------------
    // Reentrancia pelo relogio
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Lancar_ComClockQueReentraNoMesmoLedger_LancaAtoContabilInvalidoENaoCorrompeOLog()
    {
        // O lock do ledger e reentrante na mesma thread: sem a guarda explicita, um IClock que
        // voltasse a chamar Lancar executaria um append aninhado no meio da fase 3, com o mesmo
        // numero de sequencia ja reservado pelo ato externo — dois commits de sequencia 3, um deles
        // descartado em silencio, e o replay divergindo sem que nenhuma excecao tivesse sido vista.
        RelogioHostil relogio = new(RelogioFake.Epoca);
        Ledger spi = SpiVirgemComContasAbertas(relogio);
        spi.Lancar(
            LancamentoDeGenesis(ContaPiPagador, FundoEmCentavos),
            LancamentoDeGenesis(ContaPiRecebedor, FundoEmCentavos));

        int commitsAntes = spi.Log().Count;
        ChaveIdempotencia chaveReentrante = ChaveIdempotencia.De(E2e(40), EtapaLancamento.Liquidacao);
        ChaveIdempotencia chaveExterna = ChaveIdempotencia.De(E2e(41), EtapaLancamento.Liquidacao);

        relogio.Armar(spi, Lancamento.Criar(
            ContaPiPagador,
            ContaPiRecebedor,
            Valor.DeCentavos(1_00),
            chaveReentrante,
            "ato disparado de dentro do proprio relogio"));

        AtoContabilInvalidoException excecao = Assert.Throws<AtoContabilInvalidoException>(
            () => spi.Lancar(Lancamento.Criar(
                ContaPiPagador,
                ContaPiRecebedor,
                Valor.DeCentavos(50_00),
                chaveExterna,
                "ato externo, que le o relogio hostil")));

        Assert.Equal("append reentrante no mesmo ledger", excecao.Motivo);
        Assert.Equal(1, relogio.Reentradas);

        // O ledger nao ficou corrompido: nada gravado, nenhuma chave queimada, nenhuma sequencia
        // duplicada, e o proximo ato legitimo continua o log de onde ele parou.
        IReadOnlyList<Commit> log = spi.Log();
        Assert.Equal(commitsAntes, log.Count);
        Assert.Equal(log.Count, log.Select(commit => commit.Sequencia).Distinct().Count());
        Assert.False(spi.ContemChave(chaveExterna));
        Assert.False(spi.ContemChave(chaveReentrante));
        Assert.Equal(new Saldo(FundoEmCentavos), spi.SaldoNatural(ContaPiPagador));

        Commit seguinte = spi.Lancar(Lancamento.Criar(
            ContaPiPagador,
            ContaPiRecebedor,
            Valor.DeCentavos(50_00),
            chaveExterna,
            "ato externo reapresentado"));

        Assert.Equal((long)commitsAntes + 1L, seguinte.Sequencia);
        Assert.Equal(new Saldo(FundoEmCentavos - 50_00), spi.SaldoNatural(ContaPiPagador));
    }

    // -------------------------------------------------------------------------------------------
    // Natureza derivada da classe
    // -------------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(ClasseConta.Cliente, Natureza.Passivo)]
    [InlineData(ClasseConta.Pi, Natureza.Passivo)]
    [InlineData(ClasseConta.EspelhoPi, Natureza.Ativo)]
    [InlineData(ClasseConta.Abertura, Natureza.Ativo)]
    public void Conta_De_DerivaANaturezaDaClasseDaConta(ClasseConta classe, Natureza esperada)
    {
        // A natureza ser DERIVADA, e nao parametro, e o que impede registrar a conta PI como ativo.
        // "PI" e intuitivamente o que o participante tem, entao registra-la como ativo e o erro
        // plausivel — e ele inverteria a guarda de nao-negatividade exatamente na conta em que o
        // invariante 8 mora, sem que nenhum teste de aritmetica percebesse: as contas continuariam
        // batendo, so o sinal do saldo estaria trocado.
        ContaId id = ContaDaClasse(classe);
        Conta conta = Conta.De(id);

        Assert.Equal(classe, id.Classe);
        Assert.Equal(esperada, conta.Natureza);
        Assert.Equal(classe == ClasseConta.Abertura, conta.EhContraContaDeAbertura);

        // E a conversao do bruto para o natural segue a natureza: ativo cresce a debito (bruto
        // negativo vira natural positivo), passivo cresce a credito.
        Saldo natural = conta.NaturalDe(-100_00);
        Assert.Equal(esperada == Natureza.Ativo ? new Saldo(100_00) : new Saldo(-100_00), natural);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Conta_NenhumMembroPublico_RecebeNaturezaComoParametro()
    {
        // Guarda estrutural do teorema acima: assim que Natureza voltar a ser parametro de alguma
        // fabrica, a derivacao vira sugestao e o chamador reassume o poder de mentir sobre a conta.
        MethodInfo[] comNatureza = typeof(Conta)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(metodo => metodo.GetParameters().Any(p => p.ParameterType == typeof(Natureza)))
            .ToArray();

        Assert.True(
            comNatureza.Length == 0,
            "Conta expoe membro que recebe Natureza como parametro: "
                + string.Join(", ", comNatureza.Select(m => m.Name)));

        foreach (ConstructorInfo construtor in typeof(Conta).GetConstructors())
        {
            Assert.DoesNotContain(construtor.GetParameters(), p => p.ParameterType == typeof(Natureza));
        }
    }

    // -------------------------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------------------------

    private static Ledger SpiVirgemComContasAbertas(IClock relogio)
    {
        Ledger spi = new(LedgerId.Spi, relogio);
        spi.Abrir(
            PlanoDeContas.AberturaSpi(),
            PlanoDeContas.Pi(IspbPagador),
            PlanoDeContas.Pi(IspbRecebedor));

        return spi;
    }

    private static Lancamento LancamentoDeGenesis(ContaId contaPi, long centavos) =>
        Lancamento.Criar(
            ContaAberturaSpi,
            contaPi,
            Valor.DeCentavos(centavos),
            ChaveIdempotencia.Genesis(contaPi),
            $"genesis da conta {contaPi}");

    private static EndToEndId E2e(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);

    /// <summary>Contas do ledger lidas do proprio log: abrir conta e evento do log, nao configuracao.</summary>
    private static IReadOnlyList<ContaId> ContasDe(Ledger ledger)
    {
        List<ContaId> contas = [];

        foreach (Commit commit in ledger.Log())
        {
            foreach (Conta conta in commit.ContasAbertas)
            {
                contas.Add(conta.Id);
            }
        }

        return contas;
    }

    /// <summary>Soma dos saldos naturais das contas PI. A contra-conta fica de fora de proposito.</summary>
    private static long PoolDoSpi(Ledger spi)
    {
        long soma = 0;

        foreach (ContaId conta in ContasDe(spi))
        {
            if (conta.Classe == ClasseConta.Pi)
            {
                soma = checked(soma + spi.SaldoNatural(conta).Centavos);
            }
        }

        return soma;
    }

    private static long SomaBruta(Ledger ledger)
    {
        long soma = 0;

        foreach (ContaId conta in ContasDe(ledger))
        {
            soma = checked(soma + ledger.SaldoBruto(conta).Centavos);
        }

        return soma;
    }

    private static long SomaNatural(Ledger ledger)
    {
        long soma = 0;

        foreach (ContaId conta in ContasDe(ledger))
        {
            soma = checked(soma + ledger.SaldoNatural(conta).Centavos);
        }

        return soma;
    }

    /// <summary>Sequencia do commit cujos lancamentos sao TODOS de genesis.</summary>
    private static long SequenciaDoAtoDeGenesis(Ledger ledger)
    {
        foreach (Commit commit in ledger.Log())
        {
            if (commit.Lancamentos.Count > 0
                && commit.Lancamentos.All(l => l.Chave.Etapa == EtapaLancamento.Genesis))
            {
                return commit.Sequencia;
            }
        }

        throw new Xunit.Sdk.XunitException($"o ledger {ledger.Id} nao tem ato de genesis no log");
    }

    private static string Junte(IReadOnlyList<string> diferencas) =>
        diferencas.Count == 0 ? "(sem diferencas listadas)" : string.Join("; ", diferencas);

    private static ContaId ContaDaClasse(ClasseConta classe) => classe switch
    {
        ClasseConta.Cliente => ContaId.Cliente(LedgerId.Psp(IspbPagador), "0001"),
        ClasseConta.EspelhoPi => ContaId.EspelhoPi(LedgerId.Psp(IspbPagador)),
        ClasseConta.Pi => ContaPiPagador,
        ClasseConta.Abertura => ContaAberturaSpi,
        _ => throw new Xunit.Sdk.XunitException($"classe de conta sem representante no teste: {classe}"),
    };

    /// <summary>
    /// <see cref="IClock"/> hostil: ao ser lido pela primeira vez depois de armado, reentra no
    /// proprio ledger que o esta consultando.
    /// <para>
    /// Escrito assim, e nao com uma thread, de proposito: a reentrancia perigosa e a da MESMA
    /// thread, que o <c>lock</c> deixa passar sem bloquear. Uma segunda thread ficaria esperando
    /// o lock e nunca reproduziria o defeito.
    /// </para>
    /// </summary>
    private sealed class RelogioHostil : IClock
    {
        private readonly DateTimeOffset _instante;
        private Ledger? _alvo;
        private Lancamento? _ataque;
        private bool _jaReentrou;

        internal RelogioHostil(DateTimeOffset instante) => _instante = instante;

        /// <summary>Quantas vezes o relogio conseguiu reentrar. Zero significaria teste vazio.</summary>
        internal int Reentradas { get; private set; }

        public DateTimeOffset Agora
        {
            get
            {
                if (_alvo is not null && _ataque is not null && !_jaReentrou)
                {
                    // Marcado antes da chamada: se o dominio deixasse a reentrada passar, o ataque
                    // pararia aqui em vez de recursar sem fim e derrubar a suite por pilha.
                    _jaReentrou = true;
                    Reentradas++;
                    _alvo.Lancar(_ataque);
                }

                return _instante;
            }
        }

        /// <summary>Arma o ataque depois da montagem do ledger, que precisa de um relogio manso.</summary>
        internal void Armar(Ledger alvo, Lancamento ataque)
        {
            _alvo = alvo;
            _ataque = ataque;
        }
    }
}
