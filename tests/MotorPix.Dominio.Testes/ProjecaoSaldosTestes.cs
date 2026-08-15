using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// O replay como oraculo independente do ledger.
/// <para>
/// <see cref="ProjecaoSaldos"/> so tem valor por ser uma SEGUNDA implementacao: ela acumula debitos
/// e creditos separados e deriva o saldo natural com expressao propria, sem passar por
/// <see cref="Conta.NaturalDe"/>. Se as duas convergissem naquele metodo, inverter o sinal la dentro
/// produziria o mesmo valor errado dos dois lados e o gate ficaria verde.
/// </para>
/// <para>
/// Mesmo assim, duas implementacoes nao bastam para toda classe de defeito: uma inversao de sinal
/// COMUM as duas passaria em qualquer comparacao entre elas. Por isso existe aqui um terceiro
/// oraculo, escrito a mao, com os valores calculados fora do codigo.
/// </para>
/// </summary>
public sealed class ProjecaoSaldosTestes
{
    private const long FundoEmCentavos = 1_000_00;

    // -------------------------------------------------------------------------------------------
    // Replay contra a projecao materializada
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Reconstruir_SobreOLogCompletoDeCadaLedger_ReproduzOSnapshotMaterializado()
    {
        CenarioContabil cenario = CenarioExercitado();

        foreach (Ledger ledger in cenario.Ledgers)
        {
            SnapshotSaldos materializado = ledger.Snapshot();
            ProjecaoSaldos projecao = ProjecaoSaldos.Reconstruir(ledger.Log());
            SnapshotSaldos reconstruido = projecao.Snapshot();

            // EhIgualA compara tambem o CONJUNTO DE CHAVES: uma conta que zerou e sumiu do
            // dicionario reconstruido passaria batido numa comparacao so por valores, e e
            // exatamente esse o defeito que o replay existe para pegar.
            Assert.True(
                reconstruido.EhIgualA(materializado),
                $"o replay do ledger {ledger.Id} divergiu da projecao materializada: "
                + $"{Junte(reconstruido.Diferencas(materializado))}");

            Assert.Equal(materializado.Contas.Count, reconstruido.Contas.Count);
            Assert.Equal(materializado.Contas.Count, projecao.Contas.Count);

            foreach (ContaId conta in materializado.Contas)
            {
                Assert.True(reconstruido.Contem(conta), $"a conta {conta} sumiu do snapshot reconstruido");
                Assert.Contains(conta, projecao.Contas);
                Assert.Equal(ledger.SaldoNatural(conta), projecao.SaldoNatural(conta));
                Assert.Equal(ledger.MinimoNatural(conta), projecao.MinimoNatural(conta));
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Log incompleto
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Reconstruir_DeLogComBuraco_LancaAtoContabilInvalido()
    {
        CenarioContabil cenario = CenarioExercitado();
        IReadOnlyList<Commit> completo = cenario.LedgerSpi.Log();

        Assert.True(completo.Count >= 3, $"o log do SPI tem so {completo.Count} commits, poucos para abrir buraco");

        List<Commit> comBuraco = completo.Where(commit => commit.Sequencia != 2L).ToList();

        AtoContabilInvalidoException excecao = Assert.Throws<AtoContabilInvalidoException>(
            () => ProjecaoSaldos.Reconstruir(comBuraco));

        // Erro de dominio, e nao KeyNotFoundException vinda de um indexador: o replay que aplica um
        // lancamento sobre uma conta que o commit de abertura faltante nunca abriu precisa dizer
        // qual e o problema, nao estourar no meio do dicionario.
        Assert.StartsWith("log incompleto para replay", excecao.Motivo, StringComparison.Ordinal);
        Assert.Contains("esperava sequencia 2", excecao.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Reconstruir_DeRecorteQueNaoComecaNaSequenciaUm_LancaAtoContabilInvalido()
    {
        // Este e o caso realista, e e uma armadilha de API: IConsultaLedger.Log(desdeSequencia)
        // devolve exatamente o tipo que Reconstruir aceita, entao passar um recorte compila sem
        // reclamacao nenhuma. Se Reconstruir aceitasse, produziria uma projecao com contas faltando
        // e saldos parciais — que e pior do que nao ter replay, porque parece uma segunda opiniao.
        CenarioContabil cenario = CenarioExercitado();
        IReadOnlyList<Commit> recorte = cenario.LedgerSpi.Log(desdeSequencia: 1);

        Assert.Equal(2L, recorte[0].Sequencia);

        AtoContabilInvalidoException excecao = Assert.Throws<AtoContabilInvalidoException>(
            () => ProjecaoSaldos.Reconstruir(recorte));

        Assert.StartsWith("log incompleto para replay", excecao.Motivo, StringComparison.Ordinal);
        Assert.Contains("esperava sequencia 1", excecao.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Reconstruir_DeLogVazio_ProduzProjecaoSemContasEConsultaLancaContaDesconhecida()
    {
        ProjecaoSaldos projecao = ProjecaoSaldos.Reconstruir([]);
        SnapshotSaldos snapshot = projecao.Snapshot();

        Assert.Empty(projecao.Contas);
        Assert.Empty(snapshot.Contas);

        ContaId ausente = ContaId.Pi(Ispb.Criar("11111111"));

        Assert.False(snapshot.Contem(ausente));

        // Devolver Saldo.Zero aqui faria a conta que "zerou e sumiu da projecao" ficar
        // indistinguivel da conta que existe e esta zerada — e a primeira e justamente o defeito
        // que o gate de replay precisa alcancar.
        Assert.Equal(ausente, Assert.Throws<ContaDesconhecidaException>(() => projecao.SaldoNatural(ausente)).Conta);
        Assert.Equal(ausente, Assert.Throws<ContaDesconhecidaException>(() => projecao.MinimoNatural(ausente)).Conta);
        Assert.Equal(ausente, Assert.Throws<ContaDesconhecidaException>(() => snapshot.Natural(ausente)).Conta);
        Assert.Equal(ausente, Assert.Throws<ContaDesconhecidaException>(() => snapshot.MinimoNatural(ausente)).Conta);
    }

    // -------------------------------------------------------------------------------------------
    // O terceiro oraculo, escrito a mao
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Replay_ComparadoAUmOraculoEscritoAMao_ConcordaComOLedgerEComAProjecao()
    {
        // Este e o UNICO teste do arquivo que pegaria uma inversao de sinal COMUM as duas
        // implementacoes. Ledger e ProjecaoSaldos podem concordar perfeitamente e estar os dois
        // errados: bastaria que "ativo cresce a debito" estivesse trocado nas duas cabecas na hora
        // de escrever. O dicionario abaixo foi calculado fora do codigo, a partir do genesis e das
        // operacoes aplicadas, e nao consulta ledger nenhum.
        CenarioContabil cenario = CenarioExercitado();

        Dictionary<ContaId, long> oraculo = new()
        {
            // Tudo em centavos. PSP pagador: 100000 do genesis, menos 25000 (caminho feliz),
            // menos 10000 e mais 10000 (debito estornado), menos 3000 (debito em transito).
            // Cliente e passivo e espelho e ativo, e mesmo assim os dois caem juntos na seta 3 —
            // e por isso que a soma dos NATURAIS de um ledger nao e constante.
            [cenario.ClientePagador] = 720_00,
            [cenario.EspelhoPagador] = 720_00,

            // SPI: a contra-conta e ativo e nasceu com os 200000 do genesis, e nunca mais foi
            // tocada. As duas contas PI somam esses mesmos 200000 em qualquer instante.
            [cenario.AberturaSpi] = 2_000_00,
            [cenario.PiPagador] = 720_00,
            [cenario.PiRecebedor] = 1_280_00,

            // PSP recebedor: so os 25000 do caminho feliz chegaram. Os 3000 liquidados no SPI e
            // ainda nao creditados sao a defasagem PI x espelho, isto e, dinheiro em transito.
            [cenario.EspelhoRecebedor] = 1_250_00,
            [cenario.ClienteRecebedor] = 1_250_00,
        };

        int contasVistas = 0;

        foreach (Ledger ledger in cenario.Ledgers)
        {
            SnapshotSaldos materializado = ledger.Snapshot();
            SnapshotSaldos reconstruido = ProjecaoSaldos.Reconstruir(ledger.Log()).Snapshot();

            foreach (ContaId conta in materializado.Contas)
            {
                Assert.True(oraculo.ContainsKey(conta), $"a conta {conta} nao esta prevista no oraculo escrito a mao");

                long esperado = oraculo[conta];
                contasVistas++;

                Assert.True(
                    materializado.Natural(conta).Centavos == esperado,
                    $"a projecao materializada diz {materializado.Natural(conta)} em {conta}, "
                    + $"o oraculo escrito a mao diz {new Saldo(esperado)}");

                Assert.True(
                    reconstruido.Natural(conta).Centavos == esperado,
                    $"o replay diz {reconstruido.Natural(conta)} em {conta}, "
                    + $"o oraculo escrito a mao diz {new Saldo(esperado)}");

                // Nenhuma conta do sistema passa por saldo natural negativo, entao os minimos
                // continuam exatamente onde a abertura os deixou.
                Assert.Equal(Saldo.Zero, materializado.MinimoNatural(conta));
                Assert.Equal(Saldo.Zero, reconstruido.MinimoNatural(conta));
            }
        }

        // O oraculo nao pode ter conta a mais nem a menos: se uma conta desaparecesse das duas
        // projecoes ao mesmo tempo, a comparacao acima passaria por vacuidade.
        Assert.Equal(oraculo.Count, contasVistas);
    }

    // -------------------------------------------------------------------------------------------
    // Determinismo e leitura consistente
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Reconstruir_DuasVezesDoMesmoLog_ProduzSnapshotsIguais()
    {
        CenarioContabil cenario = CenarioExercitado();

        foreach (Ledger ledger in cenario.Ledgers)
        {
            IReadOnlyList<Commit> log = ledger.Log();
            SnapshotSaldos primeira = ProjecaoSaldos.Reconstruir(log).Snapshot();
            SnapshotSaldos segunda = ProjecaoSaldos.Reconstruir(log).Snapshot();

            // Assert.Equal so serve porque SnapshotSaldos implementa IEquatable delegando a
            // EhIgualA. Sem isso, compararia referencia e passaria com qualquer conteudo.
            Assert.Equal(primeira, segunda);
            Assert.True(primeira.Equals(segunda));
            Assert.Equal(primeira.GetHashCode(), segunda.GetHashCode());
        }
    }

    [Fact]
    [Trait("gate", "1")]
    public void LerConsistente_LogEProjecaoDoMesmoInstante_BatemEntreSi()
    {
        // Log() seguido de Snapshot() NAO compoe: um append entre as duas chamadas faz o replay do
        // log antigo ser comparado contra a projecao nova, e o gate acusa uma divergencia que nunca
        // existiu. Como o append vem de outra thread, a falha e intermitente — o pior formato
        // possivel para um teste que deveria denunciar corrupcao contabil.
        CenarioContabil cenario = CenarioExercitado();
        Ledger spi = cenario.LedgerSpi;

        (IReadOnlyList<Commit> log, SnapshotSaldos projecao) = spi.LerConsistente();

        Assert.True(
            ProjecaoSaldos.Reconstruir(log).Snapshot().EhIgualA(projecao),
            "o par devolvido por LerConsistente nao bate consigo mesmo: "
            + $"{Junte(ProjecaoSaldos.Reconstruir(log).Snapshot().Diferencas(projecao))}");

        // O append que simula a corrida entre as duas leituras.
        EndToEndId depois = E2e(90);
        cenario.DebitarPagador(depois, Valor.DeCentavos(50_00));
        cenario.Liquidar(depois, Valor.DeCentavos(50_00));

        // O par capturado continua coerente, porque foi lido sob a mesma aquisicao do lock...
        Assert.True(
            ProjecaoSaldos.Reconstruir(log).Snapshot().EhIgualA(projecao),
            "o par capturado por LerConsistente deixou de bater depois de um append posterior");

        // ...enquanto o log velho comparado ao snapshot novo — que e exatamente o que Log() seguido
        // de Snapshot() produziria na corrida — acusa divergencia.
        Assert.False(
            ProjecaoSaldos.Reconstruir(log).Snapshot().EhIgualA(spi.Snapshot()),
            "o log anterior ao append deveria divergir do snapshot posterior a ele");

        (IReadOnlyList<Commit> logNovo, SnapshotSaldos projecaoNova) = spi.LerConsistente();

        Assert.True(
            ProjecaoSaldos.Reconstruir(logNovo).Snapshot().EhIgualA(projecaoNova),
            "a leitura consistente refeita nao bate consigo mesma: "
            + $"{Junte(ProjecaoSaldos.Reconstruir(logNovo).Snapshot().Diferencas(projecaoNova))}");

        Assert.Equal(log.Count + 1, logNovo.Count);
    }

    // -------------------------------------------------------------------------------------------
    // O log devolvido e imutavel nos dois ramos
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Log_NosDoisRamos_DevolveColecaoSomenteLeitura()
    {
        // Antes, um dos ramos devolvia o tipo sintetizado de uma expressao de colecao — um List
        // castavel e mutavel — e o outro devolvia ReadOnlyCollection. A imutabilidade do log
        // dependia de qual ramo o chamador tinha escolhido, e o ramo mutavel era justamente o
        // default (Log() sem argumento), isto e, o que todo mundo chama.
        CenarioContabil cenario = CenarioExercitado();
        Ledger spi = cenario.LedgerSpi;

        IReadOnlyList<Commit> completo = spi.Log();
        IReadOnlyList<Commit> recorte = spi.Log(desdeSequencia: 1);

        Assert.NotEmpty(completo);
        Assert.NotEmpty(recorte);
        Assert.IsNotType<List<Commit>>(completo);
        Assert.IsNotType<List<Commit>>(recorte);

        Commit intruso = new(999L, LedgerId.Spi, RelogioFake.Epoca, [], []);

        Assert.Throws<NotSupportedException>(() => ((IList<Commit>)completo).Add(intruso));
        Assert.Throws<NotSupportedException>(() => ((IList<Commit>)recorte).Add(intruso));

        // E a recusa nao foi so na copia: o log do ledger continua do mesmo tamanho.
        Assert.Equal(completo.Count, spi.Log().Count);
    }

    // -------------------------------------------------------------------------------------------
    // Igualdade de Commit
    // -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Commit_ComparadoPelaChaveNatural_IgnoraOConteudoEColideNoHashSet()
    {
        // A igualdade sintetizada pelo record compararia ContasAbertas e Lancamentos por
        // REFERENCIA: dois commits de conteudo identico nunca seriam iguais, e um Assert.Equal
        // sobre commits no gate de replay falharia sem que nada estivesse errado. (Sequencia,
        // Ledger) e a chave natural do log e identifica o commit univocamente.
        Ispb ispb = Ispb.Criar("11111111");
        Conta abertura = PlanoDeContas.AberturaSpi();
        Lancamento lancamento = Lancamento.Criar(
            ContaId.Abertura(LedgerId.Spi),
            ContaId.Pi(ispb),
            Valor.DeCentavos(FundoEmCentavos),
            ChaveIdempotencia.Genesis(ContaId.Pi(ispb)),
            "genesis da conta PI");

        // Instante escrito por extenso, e nao derivado com AddHours: o parametro daquele metodo e
        // de ponto flutuante, que nao entra nem de raspao num projeto deste repositorio.
        DateTimeOffset outroInstante = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        Commit original = new(1L, LedgerId.Spi, RelogioFake.Epoca, [abertura], []);
        Commit mesmoLugar = new(1L, LedgerId.Spi, outroInstante, [], [lancamento]);
        Commit outraSequencia = new(2L, LedgerId.Spi, RelogioFake.Epoca, [abertura], []);
        Commit outroLedger = new(1L, LedgerId.Psp(ispb), RelogioFake.Epoca, [abertura], []);

        Assert.Equal(original, mesmoLugar);
        Assert.Equal(original.GetHashCode(), mesmoLugar.GetHashCode());
        Assert.NotEqual(original, outraSequencia);
        Assert.NotEqual(original, outroLedger);

        HashSet<Commit> conjunto = [original, mesmoLugar, outraSequencia, outroLedger];

        Assert.Equal(3, conjunto.Count);
        Assert.Contains(mesmoLugar, conjunto);
    }

    // -------------------------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Cenario padrao com uma sequencia fixa de operacoes: um caminho feliz completo, um debito
    /// estornado e um debito liquidado mas ainda nao creditado. O terceiro caso existe para que os
    /// tres ledgers fiquem em estados diferentes entre si — comparar tres copias do mesmo estado
    /// nao prova replay nenhum.
    /// </summary>
    private static CenarioContabil CenarioExercitado()
    {
        CenarioContabil cenario = CenarioContabil.Padrao(fundoInicialEmCentavos: FundoEmCentavos);

        cenario.CaminhoFeliz(E2e(1), Valor.DeCentavos(250_00));

        cenario.DebitarPagador(E2e(2), Valor.DeCentavos(100_00));
        cenario.EstornarPagador(E2e(2), Valor.DeCentavos(100_00));

        cenario.DebitarPagador(E2e(3), Valor.DeCentavos(30_00));
        cenario.Liquidar(E2e(3), Valor.DeCentavos(30_00));

        return cenario;
    }

    private static EndToEndId E2e(long indice) =>
        new GeradorE2eDeterministico(Ispb.Criar("11111111"), RelogioFake.Epoca).EmSequencia(indice);

    private static string Junte(IReadOnlyList<string> diferencas) =>
        diferencas.Count == 0 ? "(sem diferencas listadas)" : string.Join("; ", diferencas);
}
