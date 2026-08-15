using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// Catalogo de propriedades P1..P6 do gate 1, medido sobre sequencias geradas por semente fixa.
/// <para>
/// Toda propriedade roda sobre as <em>projecoes materializadas</em> dos ledgers, capturadas apos
/// cada operacao da sequencia — inclusive apos as que falharam. Medir a soma como fold do log
/// provaria apenas que o fold e deterministico; o fold entra aqui como segunda opiniao (P6), nunca
/// como fonte da propriedade.
/// </para>
/// <para>
/// Sementes sao literais em <c>[InlineData]</c>, jamais sorteadas: um teste de propriedade com
/// semente ambiental e um teste intermitente disfarcado. A semente aparece na mensagem de toda
/// falha para que o contraexemplo reproduza com uma linha de comando.
/// </para>
/// </summary>
public sealed class PropriedadesContabeisTestes
{
    private const int Operacoes = 60;

    /// <summary>Semente unica dos testes que nao variam por semente. Fixa, pelo mesmo motivo das outras.</summary>
    private const long SementeUnica = 20_260_101L;

    /// <summary>
    /// Espelha, uma a uma, as sementes dos <c>[InlineData]</c> abaixo. Serve ao fato de cobertura,
    /// que precisa somar as sequencias das doze.
    /// </summary>
    private static readonly long[] Sementes =
    [
        1L, 2L, 7L, 13L, 42L, 99L, 1234L, 20260101L, 777777L, 8675309L, -31337L, 3074457345618258602L,
    ];

    // ---------------------------------------------------------------------------------------
    // P1 — conservacao do pool do SPI
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void SomaDosSaldosDasContasPi_EmTodoPontoDaSequencia_PermaneceIgualAoTotalDoGenesis(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        // O genesis do SPI credita o fundo inicial em cada uma das duas contas PI. ABERTURA e a
        // contrapartida e fica fora da soma: sendo ativo, o debito do genesis a deixa valendo o
        // pool inteiro, e inclui-la contaria o mesmo dinheiro duas vezes. (A grandeza que soma
        // zero por partidas dobradas e a dos saldos BRUTOS, nao a dos naturais: o natural le o
        // mesmo bruto com sinais opostos conforme a natureza da conta.)
        long esperado = checked(cenario.FundoInicial.Centavos * 2);

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            long pool = PoolDoSpi(registro.SnapshotDe(cenario.LedgerSpi.Id), cenario.AberturaSpi);

            Assert.True(
                pool == esperado,
                $"semente={semente}: apos {registro} o pool do SPI vale {pool} centavos, esperado {esperado}");
        }

        long poolFinal = PoolDoSpi(cenario.LedgerSpi.Snapshot(), cenario.AberturaSpi);

        Assert.True(
            poolFinal == esperado,
            $"semente={semente}: pool do SPI ao final vale {poolFinal} centavos, esperado {esperado}");
    }

    // ---------------------------------------------------------------------------------------
    // P2 — nao-negatividade em todo ponto, em toda conta
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void TodasAsContas_EmTodoPontoDaSequencia_TemSaldoEMinimoNaturalNaoNegativos(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        // Nenhuma conta sai da propriedade: "sem descoberto" deixou de ser uma categoria de conta e
        // virou regra do sistema. A lista vem do proprio log — a abertura de conta e evento do log,
        // entao nenhuma conta escapa da varredura por ter nascido fora dele.
        Dictionary<LedgerId, HashSet<ContaId>> todasAsContas = TodasAsContasPorLedger(cenario);

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            foreach (FotoDeLedger foto in registro.Fotos)
            {
                foreach (ContaId conta in todasAsContas[foto.Ledger])
                {
                    Saldo natural = foto.Snapshot.Natural(conta);
                    Saldo minimo = foto.Snapshot.MinimoNatural(conta);

                    Assert.True(
                        natural.Centavos >= 0,
                        $"semente={semente}: apos {registro} a conta {conta} ficou com saldo natural {natural}");

                    // O minimo e o que denuncia a excursao: uma conta que passou por -100 e voltou
                    // a zero tem saldo final impecavel e minimo negativo.
                    Assert.True(
                        minimo.Centavos >= 0,
                        $"semente={semente}: apos {registro} a conta {conta} acumulou minimo natural {minimo}");
                }
            }
        }

        foreach (Ledger ledger in cenario.Ledgers)
        {
            foreach (ContaId conta in todasAsContas[ledger.Id])
            {
                Saldo natural = ledger.SaldoNatural(conta);
                Saldo minimo = ledger.MinimoNatural(conta);

                Assert.True(
                    natural.Centavos >= 0,
                    $"semente={semente}: ao final a conta {conta} tem saldo natural {natural}");

                Assert.True(
                    minimo.Centavos >= 0,
                    $"semente={semente}: ao final a conta {conta} tem minimo natural {minimo}");
            }
        }

        // Guarda contra propriedade vazia: se a varredura tivesse deixado alguem de fora, as
        // assercoes acima passariam sem olhar a conta que mais importa. A contra-conta de abertura
        // tem de estar entre as varridas — e, sendo ativo debitado no genesis, ela vale o pool
        // inteiro do lado positivo. Era exatamente aqui que o modelo antigo exigia o oposto.
        bool aberturaFoiVarrida = todasAsContas[cenario.LedgerSpi.Id].Contains(cenario.AberturaSpi);

        Assert.True(
            aberturaFoiVarrida,
            $"semente={semente}: a contra-conta de abertura ficou de fora da varredura, entao as assercoes "
            + "acima passaram sem olhar justamente a conta que o modelo antigo mantinha negativa");

        Saldo abertura = cenario.LedgerSpi.SaldoNatural(cenario.AberturaSpi);
        long esperadoNaAbertura = checked(cenario.FundoInicial.Centavos * 2);

        Assert.True(
            abertura.Centavos == esperadoNaAbertura,
            $"semente={semente}: ABERTURA deveria valer {esperadoNaAbertura} centavos apos o genesis "
            + $"dos dois participantes, esta em {abertura}");

        Assert.False(
            abertura.EhNegativo,
            $"semente={semente}: nenhuma conta fica negativa, nem a de abertura, e ela esta em {abertura}");
    }

    // ---------------------------------------------------------------------------------------
    // P3 — reconciliacao cruzada
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void DiferencaEntrePiEEspelho_EmTodoPontoDaSequencia_IgualaOValorEmTransitoDoParticipante(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        // Modelo do transito, derivado so do que a sequencia registrou. Nao consulta ledger nenhum:
        // se o oraculo lesse a projecao, compararia a implementacao consigo mesma.
        Dictionary<EndToEndId, Valor> valores = [];
        HashSet<EndToEndId> debitados = [];
        HashSet<EndToEndId> liquidados = [];
        HashSet<EndToEndId> creditados = [];
        HashSet<EndToEndId> estornados = [];

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            if (registro.Aplicou)
            {
                valores[registro.E2E] = registro.Valor;
                Anotar(registro.Tipo, registro.E2E, debitados, liquidados, creditados, estornados);
            }

            // Em transito a favor do pagador: debitado no PSP e ainda nao liquidado no SPI.
            long transitoPagador = 0;
            foreach (EndToEndId e2e in debitados)
            {
                if (!liquidados.Contains(e2e) && !estornados.Contains(e2e))
                {
                    transitoPagador = checked(transitoPagador + valores[e2e].Centavos);
                }
            }

            // Em transito a favor do recebedor: liquidado no SPI e ainda nao creditado ao cliente.
            long transitoRecebedor = 0;
            foreach (EndToEndId e2e in liquidados)
            {
                if (!creditados.Contains(e2e))
                {
                    transitoRecebedor = checked(transitoRecebedor + valores[e2e].Centavos);
                }
            }

            SnapshotSaldos spi = registro.SnapshotDe(cenario.LedgerSpi.Id);
            SnapshotSaldos pagador = registro.SnapshotDe(cenario.LedgerPagador.Id);
            SnapshotSaldos recebedor = registro.SnapshotDe(cenario.LedgerRecebedor.Id);

            long diferencaPagador = checked(
                spi.Natural(cenario.PiPagador).Centavos - pagador.Natural(cenario.EspelhoPagador).Centavos);

            long diferencaRecebedor = checked(
                spi.Natural(cenario.PiRecebedor).Centavos - recebedor.Natural(cenario.EspelhoRecebedor).Centavos);

            Assert.True(
                diferencaPagador == transitoPagador,
                $"semente={semente}: apos {registro} o pagador tem PI - ESPELHO = {diferencaPagador} centavos "
                + $"e transito derivado dos E2E = {transitoPagador} centavos");

            Assert.True(
                diferencaRecebedor == transitoRecebedor,
                $"semente={semente}: apos {registro} o recebedor tem PI - ESPELHO = {diferencaRecebedor} centavos "
                + $"e transito derivado dos E2E = {transitoRecebedor} centavos");
        }
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void DiferencaEntrePiEEspelho_ComTodaPendenciaConcluida_EhZeroParaOsDoisParticipantes(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        Dictionary<EndToEndId, Valor> valores = [];
        List<EndToEndId> ordem = [];
        HashSet<EndToEndId> debitados = [];
        HashSet<EndToEndId> liquidados = [];
        HashSet<EndToEndId> creditados = [];
        HashSet<EndToEndId> estornados = [];

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            if (!registro.Aplicou)
            {
                continue;
            }

            if (registro.Tipo == TipoOperacao.DebitarPagador)
            {
                ordem.Add(registro.E2E);
            }

            valores[registro.E2E] = registro.Valor;
            Anotar(registro.Tipo, registro.E2E, debitados, liquidados, creditados, estornados);
        }

        int concluidas = 0;

        foreach (EndToEndId e2e in ordem)
        {
            try
            {
                if (debitados.Contains(e2e) && !liquidados.Contains(e2e) && !estornados.Contains(e2e))
                {
                    cenario.Liquidar(e2e, valores[e2e]);
                    cenario.CreditarRecebedor(e2e, valores[e2e]);
                    concluidas++;
                }
                else if (liquidados.Contains(e2e) && !creditados.Contains(e2e))
                {
                    cenario.CreditarRecebedor(e2e, valores[e2e]);
                    concluidas++;
                }
            }
            catch (MotorPixException erro)
            {
                Assert.Fail(
                    $"semente={semente}: concluir a pendencia de {e2e} ({valores[e2e]}) falhou com "
                    + $"{erro.GetType().Name}: {erro.Message}");
            }
        }

        long diferencaPagador = checked(
            cenario.LedgerSpi.SaldoNatural(cenario.PiPagador).Centavos
            - cenario.LedgerPagador.SaldoNatural(cenario.EspelhoPagador).Centavos);

        long diferencaRecebedor = checked(
            cenario.LedgerSpi.SaldoNatural(cenario.PiRecebedor).Centavos
            - cenario.LedgerRecebedor.SaldoNatural(cenario.EspelhoRecebedor).Centavos);

        Assert.True(
            diferencaPagador == 0,
            $"semente={semente}: em quiescencia ({concluidas} pendencias concluidas) o pagador ainda tem "
            + $"PI - ESPELHO = {diferencaPagador} centavos");

        Assert.True(
            diferencaRecebedor == 0,
            $"semente={semente}: em quiescencia ({concluidas} pendencias concluidas) o recebedor ainda tem "
            + $"PI - ESPELHO = {diferencaRecebedor} centavos");

        long pool = PoolDoSpi(cenario.LedgerSpi.Snapshot(), cenario.AberturaSpi);
        long esperado = checked(cenario.FundoInicial.Centavos * 2);

        Assert.True(
            pool == esperado,
            $"semente={semente}: em quiescencia o pool do SPI vale {pool} centavos, esperado {esperado}");
    }

    // ---------------------------------------------------------------------------------------
    // P4 — fechamento por E2E
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void CaminhoFelizCompleto_DeUmUnicoE2e_MoveExatamenteOValorEntreClientesEEntreContasPi(long semente)
    {
        CenarioContabil cenario = CenarioContabil.Padrao();
        Valor valor = ValorDaSemente(semente);
        EndToEndId e2e = EndToEndId.Compor(cenario.IspbPagador, cenario.Relogio.Agora, "P4FELIZ0001");

        Dictionary<LedgerId, SnapshotSaldos> antes = Fotografar(cenario);
        cenario.CaminhoFeliz(e2e, valor);
        Dictionary<LedgerId, SnapshotSaldos> depois = Fotografar(cenario);

        long v = valor.Centavos;
        Dictionary<ContaId, long> esperados = new()
        {
            [cenario.ClientePagador] = -v,
            [cenario.EspelhoPagador] = -v,
            [cenario.PiPagador] = -v,
            [cenario.PiRecebedor] = v,
            [cenario.EspelhoRecebedor] = v,
            [cenario.ClienteRecebedor] = v,
        };

        foreach (Ledger ledger in cenario.Ledgers)
        {
            foreach (ContaId conta in depois[ledger.Id].Contas)
            {
                long delta = checked(depois[ledger.Id].Natural(conta).Centavos - antes[ledger.Id].Natural(conta).Centavos);
                long esperado = esperados.TryGetValue(conta, out long previsto) ? previsto : 0L;

                Assert.True(
                    delta == esperado,
                    $"semente={semente}: caminho feliz de {valor} moveu {delta} centavos em {conta}, esperado {esperado}");
            }
        }
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void DebitoSeguidoDeEstorno_DoMesmoE2e_TemEfeitoLiquidoZeroEmTodasAsContas(long semente)
    {
        CenarioContabil cenario = CenarioContabil.Padrao();
        Valor valor = ValorDaSemente(semente);
        EndToEndId e2e = EndToEndId.Compor(cenario.IspbPagador, cenario.Relogio.Agora, "P4ESTORNO01");

        Dictionary<LedgerId, SnapshotSaldos> antes = Fotografar(cenario);
        int commitsAntes = cenario.LedgerPagador.Log().Count;

        cenario.DebitarPagador(e2e, valor);
        cenario.EstornarPagador(e2e, valor);

        Dictionary<LedgerId, SnapshotSaldos> depois = Fotografar(cenario);

        foreach (Ledger ledger in cenario.Ledgers)
        {
            SnapshotSaldos inicial = antes[ledger.Id];
            SnapshotSaldos final = depois[ledger.Id];

            Assert.True(
                inicial.EhIgualA(final),
                $"semente={semente}: debito de {valor} seguido de estorno mexeu no ledger {ledger.Id}: "
                + $"{Junte(inicial.Diferencas(final))}");
        }

        // Estorno e lancamento novo, nunca UPDATE do original: o log cresce em dois commits e o
        // debito original continua la.
        int commitsDepois = cenario.LedgerPagador.Log().Count;

        Assert.True(
            commitsDepois == commitsAntes + 2,
            $"semente={semente}: esperado {commitsAntes + 2} commits no ledger do pagador, obtidos {commitsDepois}");

        Assert.True(
            cenario.LedgerPagador.ContemChave(ChaveIdempotencia.De(e2e, EtapaLancamento.DebitoPagador)),
            $"semente={semente}: o debito original sumiu do ledger apos o estorno");
    }

    // ---------------------------------------------------------------------------------------
    // P5 — idempotencia de lancamento
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void RepeticaoDeOperacaoJaAplicada_ComAMesmaChave_LancaDuplicadaESemMexerEmSaldoNemNoLog(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        int repeticoes = 0;

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            if (!registro.Aplicou)
            {
                continue;
            }

            Dictionary<LedgerId, SnapshotSaldos> antes = Fotografar(cenario);
            Dictionary<LedgerId, int> commitsAntes = ContarCommits(cenario);

            MotorPixException erro = Capturar(
                () => GeradorDeSequencias.Despachar(cenario, registro.Tipo, registro.E2E, registro.Valor),
                $"semente={semente}: reenvio de {registro} deveria ter sido recusado");

            ChaveIdempotencia chave = ChaveIdempotencia.De(registro.E2E, GeradorDeSequencias.EtapaDe(registro.Tipo));

            Assert.True(
                erro is ChaveIdempotenciaDuplicadaException,
                $"semente={semente}: reenvio de {registro} lancou {erro.GetType().Name}, esperado "
                + $"{nameof(ChaveIdempotenciaDuplicadaException)}");

            ChaveIdempotenciaDuplicadaException duplicada = (ChaveIdempotenciaDuplicadaException)erro;

            Assert.True(
                string.Equals(duplicada.Chave, chave.Texto, StringComparison.Ordinal),
                $"semente={semente}: reenvio de {registro} acusou a chave '{duplicada.Chave}', esperado '{chave.Texto}'");

            Ledger ledgerDaOperacao = GeradorDeSequencias.LedgerDe(cenario, registro.Tipo);

            Assert.True(
                ledgerDaOperacao.ContemChave(chave),
                $"semente={semente}: apos o reenvio de {registro} a chave '{chave.Texto}' sumiu do ledger "
                + $"{ledgerDaOperacao.Id}");

            Dictionary<LedgerId, SnapshotSaldos> depois = Fotografar(cenario);
            Dictionary<LedgerId, int> commitsDepois = ContarCommits(cenario);

            foreach (Ledger ledger in cenario.Ledgers)
            {
                Assert.True(
                    antes[ledger.Id].EhIgualA(depois[ledger.Id]),
                    $"semente={semente}: reenvio de {registro} mexeu no ledger {ledger.Id}: "
                    + $"{Junte(antes[ledger.Id].Diferencas(depois[ledger.Id]))}");

                Assert.True(
                    commitsAntes[ledger.Id] == commitsDepois[ledger.Id],
                    $"semente={semente}: reenvio de {registro} acrescentou commit ao ledger {ledger.Id}: "
                    + $"{commitsAntes[ledger.Id]} antes, {commitsDepois[ledger.Id]} depois");
            }

            repeticoes++;
        }

        Assert.True(
            repeticoes > 0,
            $"semente={semente}: a sequencia nao aplicou nenhuma operacao, logo nao testou idempotencia de nada");
    }

    // ---------------------------------------------------------------------------------------
    // P6 — replay
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void Reconstruir_APartirDoLogCompleto_ReproduzOSnapshotMaterializadoEEhDeterministico(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        foreach (Ledger ledger in cenario.Ledgers)
        {
            // Log() seguido de Snapshot() sao duas aquisicoes do lock e nao compoem: um append
            // entre as duas faria o gate acusar divergencia que nunca existiu. LerConsistente
            // entrega o par do mesmo instante, que e o unico par que o replay pode comparar.
            (IReadOnlyList<Commit> log, SnapshotSaldos materializado) = ledger.LerConsistente();
            SnapshotSaldos reconstruido = ProjecaoSaldos.Reconstruir(log).Snapshot();

            // EhIgualA compara tambem o conjunto de chaves: uma conta que zerou e sumiu do
            // dicionario reconstruido passaria batido numa comparacao so por valores.
            Assert.True(
                reconstruido.EhIgualA(materializado),
                $"semente={semente}: replay do ledger {ledger.Id} divergiu: "
                + $"{Junte(reconstruido.Diferencas(materializado))}");

            Assert.True(
                reconstruido.Contas.Count == materializado.Contas.Count,
                $"semente={semente}: replay do ledger {ledger.Id} produziu {reconstruido.Contas.Count} contas, "
                + $"a projecao materializada tem {materializado.Contas.Count}");

            SnapshotSaldos segundaReconstrucao = ProjecaoSaldos.Reconstruir(log).Snapshot();

            Assert.True(
                reconstruido.EhIgualA(segundaReconstrucao),
                $"semente={semente}: duas reconstrucoes do mesmo log do ledger {ledger.Id} divergiram: "
                + $"{Junte(reconstruido.Diferencas(segundaReconstrucao))}");
        }
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void Reconstruir_APartirDeCadaPrefixoDoLog_ReproduzOSnapshotDaqueleMomento(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);
        CenarioContabil cenario = resultado.Cenario;

        Dictionary<LedgerId, IReadOnlyList<Commit>> logs = [];
        foreach (Ledger ledger in cenario.Ledgers)
        {
            logs.Add(ledger.Id, ledger.Log());
        }

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            foreach (FotoDeLedger foto in registro.Fotos)
            {
                // O prefixo e o que pega projecao que depende do futuro: comparar so o estado final
                // deixaria passar um fold que "sabe" o que ainda vai acontecer no log.
                SnapshotSaldos prefixo = ProjecaoSaldos.Reconstruir(logs[foto.Ledger].Take(foto.Commits)).Snapshot();

                Assert.True(
                    prefixo.EhIgualA(foto.Snapshot),
                    $"semente={semente}: apos {registro}, o replay dos {foto.Commits} primeiros commits do ledger "
                    + $"{foto.Ledger} divergiu do snapshot daquele momento: {Junte(prefixo.Diferencas(foto.Snapshot))}");
            }
        }
    }

    [Fact]
    [Trait("gate", "1")]
    public void Reconstruir_DeLogVazio_ProduzProjecaoVaziaERecusaConsultarContaAusente()
    {
        ProjecaoSaldos projecao = ProjecaoSaldos.Reconstruir([]);
        SnapshotSaldos snapshot = projecao.Snapshot();

        Assert.Empty(projecao.Contas);
        Assert.Empty(snapshot.Contas);

        Assert.True(
            snapshot.EhIgualA(ProjecaoSaldos.Reconstruir(Array.Empty<Commit>()).Snapshot()),
            "duas projecoes de log vazio deveriam ser iguais");

        ContaId inexistente = ContaId.Pi(Ispb.Criar("11111111"));

        // Devolver Saldo.Zero para conta ausente era o que enfraquecia o gate: a conta que SUMIU da
        // projecao reconstruida — o defeito exato que o replay existe para pegar — ficava
        // indistinguivel da conta que existe e esta zerada, e a comparacao de snapshots so acusaria
        // a diferenca por causa do conjunto de chaves. Lancando, ausencia e ausencia em toda leitura,
        // e o contrato fica igual ao do Ledger, que sempre recusou conta desconhecida.
        ContaDesconhecidaException noSaldo =
            Assert.Throws<ContaDesconhecidaException>(() => projecao.SaldoNatural(inexistente));
        ContaDesconhecidaException noMinimo =
            Assert.Throws<ContaDesconhecidaException>(() => projecao.MinimoNatural(inexistente));
        ContaDesconhecidaException noSnapshot =
            Assert.Throws<ContaDesconhecidaException>(() => snapshot.Natural(inexistente));
        ContaDesconhecidaException noMinimoDoSnapshot =
            Assert.Throws<ContaDesconhecidaException>(() => snapshot.MinimoNatural(inexistente));

        Assert.Equal(inexistente, noSaldo.Conta);
        Assert.Equal(inexistente, noMinimo.Conta);
        Assert.Equal(inexistente, noSnapshot.Conta);
        Assert.Equal(inexistente, noMinimoDoSnapshot.Conta);

        // A pergunta "esta conta existe na projecao?" tem resposta propria, e nao um saldo zero.
        Assert.False(snapshot.Contem(inexistente));
    }

    // ---------------------------------------------------------------------------------------
    // Por que a soma nao e medida como fold do log
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void PoolDoSpi_MedidoPelaProjecaoMaterializadaEPeloFoldDoLog_ConcordaSemQueOFoldSejaOOraculo()
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(SementeUnica, Operacoes);
        CenarioContabil cenario = resultado.Cenario;
        long esperado = checked(cenario.FundoInicial.Centavos * 2);

        // 1. Projecao materializada: escrita durante o append, no caminho de escrita do ledger.
        long materializado = PoolDoSpi(cenario.LedgerSpi.Snapshot(), cenario.AberturaSpi);

        // 2. Fold do log por ProjecaoSaldos: segunda implementacao, que percorre Partidas e aplica
        //    o sinal pelo Lado.
        long reconstruido = PoolDoSpi(
            ProjecaoSaldos.Reconstruir(cenario.LedgerSpi.Log()).Snapshot(),
            cenario.AberturaSpi);

        // 3. Soma escrita a mao aqui, direto de Debito/Credito dos lancamentos, sem passar por
        //    nenhuma das duas projecoes. Premissa explicita: toda conta PI e Passivo, entao o saldo
        //    natural e creditos menos debitos.
        foreach (Commit commit in cenario.LedgerSpi.Log())
        {
            foreach (Conta conta in commit.ContasAbertas)
            {
                if (!conta.Id.Equals(cenario.AberturaSpi))
                {
                    Assert.True(
                        conta.Natureza == Natureza.Passivo,
                        $"semente={SementeUnica}: a conta {conta.Id} do SPI nao e Passivo, e a soma manual "
                        + "abaixo assume que e");
                }
            }
        }

        long manual = 0;
        foreach (Commit commit in cenario.LedgerSpi.Log())
        {
            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                if (!lancamento.Credito.Equals(cenario.AberturaSpi))
                {
                    manual = checked(manual + lancamento.Valor.Centavos);
                }

                if (!lancamento.Debito.Equals(cenario.AberturaSpi))
                {
                    manual = checked(manual - lancamento.Valor.Centavos);
                }
            }
        }

        // O valor de cada medida esta certo...
        Assert.True(
            materializado == esperado,
            $"semente={SementeUnica}: pool materializado {materializado}, esperado {esperado}");

        Assert.True(
            manual == esperado,
            $"semente={SementeUnica}: pool somado a mao do log {manual}, esperado {esperado}");

        // ...e as tres medidas concordam. E esta concordancia, e nao a medida 2 sozinha, que tem
        // conteudo: se Sigma fosse medida apenas sobre o fold do proprio log, ela seria verdadeira
        // por construcao — o fold soma exatamente as pernas que gravou, entao passaria com o cliente
        // debitado duas vezes ou com dinheiro criado no recebedor, e provaria so que Aggregate e
        // deterministico. A propriedade que vale e a soma sobre a projecao materializada (medida 1),
        // que e escrita por outro caminho de codigo; o fold entra como segunda opiniao.
        Assert.True(
            materializado == reconstruido,
            $"semente={SementeUnica}: projecao materializada diz {materializado} e o fold do log diz {reconstruido}");

        Assert.True(
            materializado == manual,
            $"semente={SementeUnica}: projecao materializada diz {materializado} e a soma manual diz {manual}");
    }

    // ---------------------------------------------------------------------------------------
    // O gerador propriamente dito
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(7L)]
    [InlineData(13L)]
    [InlineData(42L)]
    [InlineData(99L)]
    [InlineData(1234L)]
    [InlineData(20260101L)]
    [InlineData(777777L)]
    [InlineData(8675309L)]
    [InlineData(-31337L)]
    [InlineData(3074457345618258602L)]
    public void SequenciaGerada_OperacaoAOperacao_TerminaExatamenteComoOGeradorPreviu(long semente)
    {
        ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);

        Assert.True(
            resultado.Registros.Count == Operacoes,
            $"semente={semente}: sequencia com {resultado.Registros.Count} operacoes, esperadas {Operacoes}");

        foreach (RegistroOperacao registro in resultado.Registros)
        {
            switch (registro.Expectativa)
            {
                case Expectativa.Aplica:
                    // Operacao valida pelo estado do modelo: recusa-la e defeito do dominio, e nao
                    // do gerador — nenhuma guarda deveria disparar aqui.
                    Assert.True(
                        registro.Aplicou,
                        $"semente={semente}: {registro} era valida pelo estado e foi recusada com "
                        + $"{registro.Excecao?.GetType().Name}: {registro.Excecao?.Message}");
                    break;

                case Expectativa.FalhaPorSaldo:
                    Assert.True(
                        registro.Excecao is SaldoNegativoException,
                        $"semente={semente}: {registro} pedia mais que o saldo e deveria bater na guarda de "
                        + $"saldo negativo, obtido {registro.Excecao?.GetType().Name ?? "nenhuma excecao"}");

                    // A conta acusada e uma das duas que o debito derruba juntas (cliente e
                    // espelho valem sempre o mesmo neste ledger), entao aqui so se exige que ela
                    // seja do ledger do pagador. Qual das duas a excecao nomeia depende da ordem de
                    // avaliacao do ato, e fixar isso seria testar ordem de dicionario.
                    SaldoNegativoException recusa = (SaldoNegativoException)registro.Excecao!;

                    Assert.True(
                        recusa.Conta.Ledger.Equals(
                            GeradorDeSequencias.LedgerDe(resultado.Cenario, registro.Tipo).Id),
                        $"semente={semente}: {registro} foi recusada na conta {recusa.Conta}, que nem sequer e do "
                        + "ledger em que a operacao seria gravada");

                    Assert.True(
                        recusa.SaldoResultante.EhNegativo,
                        $"semente={semente}: {registro} foi recusada por saldo, mas o saldo resultante acusado "
                        + $"({recusa.SaldoResultante}) nao e negativo");
                    break;

                case Expectativa.FalhaPorChaveRepetida:
                    Assert.True(
                        registro.Excecao is ChaveIdempotenciaDuplicadaException,
                        $"semente={semente}: {registro} era reenvio literal e deveria bater na chave de "
                        + $"idempotencia, obtido {registro.Excecao?.GetType().Name ?? "nenhuma excecao"}");
                    break;

                default:
                    Assert.Fail($"semente={semente}: expectativa desconhecida em {registro}");
                    break;
            }
        }
    }

    [Fact]
    [Trait("gate", "1")]
    public void CatalogoDeSementes_SomadasAsDozeSequencias_ExercitaAsQuatroOperacoesEAsDuasFalhas()
    {
        Dictionary<TipoOperacao, int> aplicadas = new()
        {
            [TipoOperacao.DebitarPagador] = 0,
            [TipoOperacao.Liquidar] = 0,
            [TipoOperacao.CreditarRecebedor] = 0,
            [TipoOperacao.EstornarPagador] = 0,
        };

        int falhasPorSaldo = 0;
        int falhasPorChave = 0;

        foreach (long semente in Sementes)
        {
            ResultadoSequencia resultado = GeradorDeSequencias.Executar(semente, Operacoes);

            foreach (RegistroOperacao registro in resultado.Registros)
            {
                if (registro.Aplicou)
                {
                    aplicadas[registro.Tipo]++;
                    continue;
                }

                if (registro.Expectativa == Expectativa.FalhaPorSaldo)
                {
                    falhasPorSaldo++;
                }
                else if (registro.Expectativa == Expectativa.FalhaPorChaveRepetida)
                {
                    falhasPorChave++;
                }
            }
        }

        // Sem esta cobertura, as propriedades acima poderiam estar verdes por vacuidade: uma
        // sequencia so de debitos satisfaz P1 e P3 sem nunca exercitar liquidacao nem estorno.
        string resumo =
            $"debitos={aplicadas[TipoOperacao.DebitarPagador]}, "
            + $"liquidacoes={aplicadas[TipoOperacao.Liquidar]}, "
            + $"creditos={aplicadas[TipoOperacao.CreditarRecebedor]}, "
            + $"estornos={aplicadas[TipoOperacao.EstornarPagador]}, "
            + $"falhas por saldo={falhasPorSaldo}, falhas por chave={falhasPorChave}";

        foreach (TipoOperacao tipo in aplicadas.Keys)
        {
            Assert.True(aplicadas[tipo] > 0, $"nenhuma operacao {tipo} aplicada nas doze sementes: {resumo}");
        }

        Assert.True(falhasPorSaldo > 0, $"nenhuma falha por saldo nas doze sementes: {resumo}");
        Assert.True(falhasPorChave > 0, $"nenhuma falha por chave repetida nas doze sementes: {resumo}");
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    /// <summary>Soma dos saldos naturais das contas do SPI, com ABERTURA de fora.</summary>
    private static long PoolDoSpi(SnapshotSaldos spi, ContaId abertura)
    {
        long soma = 0;

        foreach (ContaId conta in spi.Contas)
        {
            if (conta.Equals(abertura))
            {
                continue;
            }

            soma = checked(soma + spi.Natural(conta).Centavos);
        }

        return soma;
    }

    /// <summary>
    /// Todas as contas de cada ledger, lidas dos commits de abertura. Nao ha filtro: com o
    /// descoberto eliminado, "contas sem descoberto" deixou de ser um subconjunto e passou a ser
    /// o conjunto inteiro.
    /// </summary>
    private static Dictionary<LedgerId, HashSet<ContaId>> TodasAsContasPorLedger(CenarioContabil cenario)
    {
        Dictionary<LedgerId, HashSet<ContaId>> mapa = [];

        foreach (Ledger ledger in cenario.Ledgers)
        {
            HashSet<ContaId> contas = [];

            foreach (Commit commit in ledger.Log())
            {
                // Todas as contas entram: nenhuma admite saldo natural negativo, inclusive a
                // contra-conta de abertura, que sendo ativo fica positiva com o debito do genesis.
                foreach (Conta conta in commit.ContasAbertas)
                {
                    contas.Add(conta.Id);
                }
            }

            mapa.Add(ledger.Id, contas);
        }

        return mapa;
    }

    private static Dictionary<LedgerId, SnapshotSaldos> Fotografar(CenarioContabil cenario)
    {
        Dictionary<LedgerId, SnapshotSaldos> fotos = [];

        foreach (Ledger ledger in cenario.Ledgers)
        {
            fotos.Add(ledger.Id, ledger.Snapshot());
        }

        return fotos;
    }

    private static Dictionary<LedgerId, int> ContarCommits(CenarioContabil cenario)
    {
        Dictionary<LedgerId, int> commits = [];

        foreach (Ledger ledger in cenario.Ledgers)
        {
            commits.Add(ledger.Id, ledger.Log().Count);
        }

        return commits;
    }

    private static void Anotar(
        TipoOperacao tipo,
        EndToEndId e2e,
        HashSet<EndToEndId> debitados,
        HashSet<EndToEndId> liquidados,
        HashSet<EndToEndId> creditados,
        HashSet<EndToEndId> estornados)
    {
        switch (tipo)
        {
            case TipoOperacao.DebitarPagador:
                debitados.Add(e2e);
                break;

            case TipoOperacao.Liquidar:
                liquidados.Add(e2e);
                break;

            case TipoOperacao.CreditarRecebedor:
                creditados.Add(e2e);
                break;

            case TipoOperacao.EstornarPagador:
                estornados.Add(e2e);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "tipo de operacao desconhecido");
        }
    }

    /// <summary>
    /// Valor derivado da semente, limitado a metade do fundo padrao para que caiba no saldo do
    /// cliente sem depender de sorte.
    /// </summary>
    private static Valor ValorDaSemente(long semente)
    {
        long resto = semente % 50_000L;
        return Valor.DeCentavos(1 + (resto < 0 ? -resto : resto));
    }

    private static MotorPixException Capturar(Action acao, string contexto)
    {
        try
        {
            acao();
        }
        catch (MotorPixException erro)
        {
            return erro;
        }

        throw new Xunit.Sdk.XunitException($"{contexto}, mas nenhuma excecao de dominio foi lancada");
    }

    private static string Junte(IReadOnlyList<string> diferencas) =>
        diferencas.Count == 0 ? "(sem diferencas listadas)" : string.Join("; ", diferencas);
}
