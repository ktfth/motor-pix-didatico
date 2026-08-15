using System.Collections.Concurrent;
using MotorPix.Composicao;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Idempotencia sob concorrencia: N POSTs simultaneos com o mesmo <c>EndToEndId</c>.
/// <para>
/// Suite separada de proposito. O resto da bateria e deterministico — relogio injetado, gerador de
/// E2E por contador, barramento drenado a mao —, e este arquivo depende de escalonamento, que e a
/// unica coisa que nenhum teste controla. Misturar os dois faria uma falha intermitente aqui parecer
/// falha da suite inteira.
/// </para>
/// <para>
/// Por isso as assercoes miram o <b>invariante</b>: um unico agregado, um unico debito, uma unica
/// entrada no registro. Quem venceu a corrida e irrelevante e nao e afirmado em lugar nenhum. As
/// contagens exatas de vencedores e perdedores que aparecem aqui nao dependem de ordem: a
/// reivindicacao e um insert atomico, entao a primeira a chegar vence e todas as outras encontram a
/// marca — em qualquer intercalacao.
/// </para>
/// </summary>
public sealed class ConcorrenciaDeIdempotenciaTestes
{
    private const int Postagens = 32;

    private const long Fundo = 1_000_00;
    private const long Pagamento = 1_00;

    /// <summary>Valores distintos dois a dois: <c>1,00</c> ate <c>32,00</c>, todos dentro do saldo.</summary>
    private const long PrimeiroValorDivergente = 1_00;
    private const long PassoDoValorDivergente = 1_00;

    private const string ContaDoPagador = "0001";
    private const string ContaDoRecebedor = "0002";

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    private static readonly ChavePix ChaveDoRecebedor = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

    /// <summary>
    /// O caso central: o mesmo pedido postado 32 vezes ao mesmo tempo produz um unico agregado.
    /// <para>
    /// A reivindicacao acontece <em>antes</em> da consulta ao DICT justamente por isto. Consultar
    /// primeiro e reivindicar depois seria check-then-act: as 32 chamadas resolveriam a chave, e so
    /// entao a colisao seria notada — com 32 resolucoes feitas e a janela aberta para 32 debitos.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ComTrintaEDoisPostsParalelosDoMesmoE2e_ProduzUmUnicoAgregado()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = new(e2e, ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento));

        int commitsAntes = motor.Pagador.Ledger.Consulta.Log().Count;

        ResultadoParalelo resultado = DispararEmParalelo(Postagens, _ => motor.Pagador.Pagar(comando));

        ExigirNenhumaFalha(resultado.Falhas);

        // Uma reivindicacao venceu; as outras 31 receberam o replay, e nenhuma ficou sem resposta.
        Assert.Equal(Postagens, resultado.Respostas.Count);
        Assert.Equal(1, motor.Pagador.Idempotencia.Quantidade);
        Assert.Single(motor.Pagador.Idempotencia.Log());

        RespostaDePagamento referencia = resultado.Respostas[0];

        Assert.Equal(e2e, referencia.E2E);
        Assert.Equal(EstadoTransacao.EnviadaSpi, referencia.Estado);
        Assert.Null(referencia.Motivo);
        Assert.All(resultado.Respostas, resposta => Assert.Equal(referencia, resposta));

        // O invariante que importa: UM debito para aquele E2E, contado no log do ledger.
        Assert.Equal(1, DebitosDe(motor, e2e));
        Assert.Equal(commitsAntes + 1, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(Fundo - Pagamento, motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos);

        // E uma unica ordem a caminho: 32 pacs.008 liquidariam 32 vezes no SPI.
        Assert.Equal(1, motor.Barramento.Pendentes);
    }

    /// <summary>
    /// O contraponto necessario: a guarda de idempotencia nao pode serializar o que e distinto.
    /// Sem este teste, um <c>Pagar</c> que recusasse tudo o que chega em paralelo passaria no teste
    /// acima com louvor.
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ComTrintaEDoisPostsParalelosDeE2eDistintos_ProcessaTodosSemPerderNenhum()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);

        int commitsAntes = motor.Pagador.Ledger.Consulta.Log().Count;

        ResultadoParalelo resultado = DispararEmParalelo(
            Postagens,
            indice => motor.Pagador.Pagar(new ComandoDePagamento(
                E2eDe(indice + 1),
                ContaDoPagador,
                ChaveDoRecebedor,
                Valor.DeCentavos(Pagamento))));

        ExigirNenhumaFalha(resultado.Falhas);

        Assert.Equal(Postagens, resultado.Respostas.Count);
        Assert.All(resultado.Respostas, resposta => Assert.Equal(EstadoTransacao.EnviadaSpi, resposta.Estado));

        // 32 E2E, 32 entradas no registro, e nenhuma repetida.
        Assert.Equal(Postagens, motor.Pagador.Idempotencia.Quantidade);
        Assert.Equal(Postagens, motor.Pagador.Idempotencia.Log().Count);
        Assert.Equal(Postagens, motor.Pagador.Idempotencia.Log().Select(entrada => entrada.E2E).Distinct().Count());

        for (int indice = 1; indice <= Postagens; indice++)
        {
            EndToEndId e2e = E2eDe(indice);

            Assert.True(motor.Pagador.Ledger.HouveDebito(e2e));
            Assert.Equal(1, DebitosDe(motor, e2e));
        }

        Assert.Equal(commitsAntes + Postagens, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(Fundo - (Postagens * Pagamento), motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos);
        Assert.Equal(Postagens, motor.Barramento.Pendentes);
    }

    /// <summary>
    /// 32 POSTs simultaneos com o mesmo E2E e payloads todos diferentes (decisao D3).
    /// <para>
    /// O ponto nao e quem vence — isso e escalonamento —, e sim que nenhum perdedor produz um
    /// segundo lancamento. As contagens exatas abaixo valem em qualquer intercalacao: como as 32
    /// impressoes sao distintas duas a duas, quem reivindicar primeiro e o unico que passa, e todos
    /// os demais divergem da impressao gravada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ComPostsParalelosDoMesmoE2eEPayloadsDivergentes_SoUmVenceENenhumOutroLanca()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
        EndToEndId e2e = E2eDe(1);

        long[] valores = [.. Enumerable.Range(0, Postagens).Select(ValorDoIndice)];

        Assert.Equal(Postagens, valores.Distinct().Count());

        int commitsAntes = motor.Pagador.Ledger.Consulta.Log().Count;

        ResultadoParalelo resultado = DispararEmParalelo(
            Postagens,
            indice => motor.Pagador.Pagar(new ComandoDePagamento(
                e2e,
                ContaDoPagador,
                ChaveDoRecebedor,
                Valor.DeCentavos(ValorDoIndice(indice)))));

        IReadOnlyList<E2eConflitanteException> conflitos = [.. resultado.Falhas.OfType<E2eConflitanteException>()];
        IReadOnlyList<Exception> inesperadas =
            [.. resultado.Falhas.Where(falha => falha is not E2eConflitanteException)];

        ExigirNenhumaFalha(inesperadas);

        Assert.Single(resultado.Respostas);
        Assert.Equal(Postagens - 1, conflitos.Count);
        Assert.Equal(EstadoTransacao.EnviadaSpi, resultado.Respostas[0].Estado);

        // Um unico pedido reivindicado, uma unica resposta congelada, um unico debito.
        Assert.Equal(1, motor.Pagador.Idempotencia.Quantidade);
        Assert.Single(motor.Pagador.Idempotencia.Log());
        Assert.Equal(1, DebitosDe(motor, e2e));
        Assert.Equal(commitsAntes + 1, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(1, motor.Barramento.Pendentes);

        // O valor que saiu da conta e um dos 32 propostos — o do vencedor, seja ele qual for.
        long debitado = Fundo - motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos;
        Assert.Contains(debitado, valores);

        // E todos os perdedores viram a mesma impressao original: a do vencedor, e nao a de um
        // estado intermediario qualquer.
        Assert.All(conflitos, conflito =>
        {
            Assert.Equal(e2e, conflito.E2E);
            Assert.EndsWith($"|{debitado}", conflito.ImpressaoOriginal, StringComparison.Ordinal);
            Assert.NotEqual(conflito.ImpressaoOriginal, conflito.ImpressaoNova);
        });
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    /// <summary>Respostas e excecoes de uma rodada paralela, ja materializadas.</summary>
    private sealed record ResultadoParalelo(
        IReadOnlyList<RespostaDePagamento> Respostas,
        IReadOnlyList<Exception> Falhas);

    /// <summary>
    /// Dispara <paramref name="quantidade"/> chamadas em paralelo, todas soltas pela mesma largada.
    /// <para>
    /// A largada existe porque sem ela as primeiras tarefas terminariam antes de as ultimas serem
    /// sequer agendadas, e o teste chamaria de "concorrencia" uma execucao sequencial. Nao ha espera
    /// por tempo em lugar nenhum: <c>Thread.Sleep</c> e <c>Task.Delay</c> transformariam escalonamento
    /// em relogio de parede, que e exatamente o que produz teste intermitente.
    /// </para>
    /// </summary>
    private static ResultadoParalelo DispararEmParalelo(int quantidade, Func<int, RespostaDePagamento> pagar)
    {
        ConcurrentBag<RespostaDePagamento> respostas = new();
        ConcurrentBag<Exception> falhas = new();

        using ManualResetEventSlim largada = new(initialState: false);

        Task[] tarefas = new Task[quantidade];

        for (int i = 0; i < quantidade; i++)
        {
            int indice = i;

            tarefas[i] = Task.Run(() =>
            {
                try
                {
                    largada.Wait();
                    respostas.Add(pagar(indice));
                }
                catch (Exception excecao)
                {
                    // Captura ampla de proposito: o teste precisa RELATAR o que escapou, e uma
                    // excecao inesperada engolida aqui viraria "0 respostas" sem nenhuma pista.
                    falhas.Add(excecao);
                }
            });
        }

        // A largada e liberada pela thread do teste, nunca por uma das tarefas: se dependesse de
        // todas estarem prontas, um pool que ainda nao injetou threads travaria a suite.
        largada.Set();
        Task.WaitAll(tarefas);

        return new ResultadoParalelo([.. respostas], [.. falhas]);
    }

    private static void ExigirNenhumaFalha(IReadOnlyList<Exception> falhas) =>
        Assert.True(
            falhas.Count == 0,
            $"excecoes inesperadas ({falhas.Count}): {string.Join(", ", falhas.Select(falha => falha.GetType().Name))}");

    /// <summary>
    /// Conta, no log do ledger, quantos lancamentos de debito existem para aquele E2E.
    /// <para>
    /// Conta em vez de perguntar <c>HouveDebito</c>: o predicado responde "existe", e "existe" nao
    /// distingue um debito de dois. O que este arquivo precisa provar e a cardinalidade.
    /// </para>
    /// </summary>
    private static int DebitosDe(MotorMontado motor, EndToEndId e2e)
    {
        ChaveIdempotencia chave = ChaveIdempotencia.De(e2e, EtapaLancamento.DebitoPagador);

        return motor.Pagador.Ledger.Consulta.Log()
            .SelectMany(commit => commit.Lancamentos)
            .Count(lancamento => lancamento.Chave.Equals(chave));
    }

    private static long ValorDoIndice(int indice) =>
        PrimeiroValorDivergente + (indice * PassoDoValorDivergente);

    private static MotorMontado MontarMotor()
    {
        MotorMontado motor = MotorMontado.Montar(
            new RelogioFake(),
            IspbPagador,
            IspbRecebedor,
            Valor.DeCentavos(Fundo),
            ContaDoPagador,
            ContaDoRecebedor);

        motor.Recebedor.RegistrarChave(
            ChaveDoRecebedor,
            ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor));

        return motor;
    }

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);
}
