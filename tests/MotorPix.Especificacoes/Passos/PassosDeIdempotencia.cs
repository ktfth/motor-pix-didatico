using System.Collections.Concurrent;
using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Especificacoes.Suporte;
using MotorPix.Mensagens;
using MotorPix.PspPagador;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// Os passos proprios do invariante 5: guardar a resposta original para compara-la com a do
/// reenvio, contar lancamentos por E2E, e alcancar as duas camadas de deduplicacao que ficam
/// abaixo da API — a do SPI, indexada pelo E2E da mensagem, e a do ledger, indexada pelo par
/// (transacao, etapa).
/// <para>
/// Nenhum passo daqui recalcula o que o motor deveria ter feito: os numeros vem escritos no
/// texto do cenario. Contar lancamentos em vez de perguntar "houve debito?" e deliberado — o
/// predicado responde "existe", e "existe" nao distingue um debito de dois, que e exatamente a
/// cardinalidade que este gate precisa provar.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDeIdempotencia
{
    private readonly ContextoDoMotor _contexto;

    /// <summary>Respostas e falhas de uma rodada de postagens simultaneas, ja materializadas.</summary>
    private readonly List<RespostaDePagamento> _respostas = [];
    private readonly List<Exception> _falhas = [];

    /// <summary>A resposta que o cliente recebeu da primeira vez, para comparar com a do reenvio.</summary>
    private RespostaDePagamento? _guardada;

    public PassosDeIdempotencia(ContextoDoMotor contexto) => _contexto = contexto;

    // ---------------------------------------------------------------------------------------
    // Acoes
    // ---------------------------------------------------------------------------------------

    [Given(@"o cliente guarda a resposta recebida")]
    [When(@"o cliente guarda a resposta recebida")]
    public void QuandoGuardaAResposta() =>
        _guardada = _contexto.UltimaResposta
            ?? throw new InvalidOperationException("nenhum pagamento foi feito neste cenário");

    /// <summary>
    /// Reenvio com a chave escrita de outro jeito. O pedido e o mesmo: a impressao digital compara
    /// a forma canonica do value object, e nao o texto que o cliente digitou.
    /// </summary>
    [When(@"o cliente pagador reenvia o mesmo pagamento de ""([^""]*)"" digitando a chave como ""([^""]*)""")]
    public void QuandoReenviaComOutraGrafiaDaChave(string valor, string grafia)
    {
        ChavePix comOutraGrafia = ChavePix.Criar(_contexto.ChaveDoRecebedor.Tipo, grafia);

        // A guarda existe contra uma linha degenerada de Exemplos: duas grafias identicas nao
        // exercitariam normalizacao nenhuma, e o cenario passaria sem afirmar nada.
        Assert.True(
            !string.Equals(comOutraGrafia.Texto, grafia, StringComparison.Ordinal),
            $"a grafia '{grafia}' já é a forma canônica da chave: o cenário não exercitaria normalização");

        _contexto.UltimaResposta = _contexto.Motor.Pagador.Pagar(new ComandoDePagamento(
            _contexto.E2E,
            NumeroDaContaDoPagador,
            comOutraGrafia,
            Valor.DeCentavos(Dinheiro.EmCentavos(valor))));
    }

    /// <summary>
    /// A mesma ordem remontada campo a campo e devolvida ao barramento: e a reentrega de mensagem
    /// interna, que nao e um pedido novo do cliente e nao pode tocar o registro do PSP.
    /// </summary>
    [When(@"o mesmo pacs\.008 de ""([^""]*)"" é reentregue ao SPI")]
    public void QuandoReentregaOPacs008(string valor)
    {
        Pacs008 reentrega = new(
            _contexto.E2E,
            ContextoDoMotor.IspbPagador,
            _contexto.ContaPagador,
            ContextoDoMotor.IspbRecebedor,
            _contexto.ContaRecebedor,
            Valor.DeCentavos(Dinheiro.EmCentavos(valor)),
            _contexto.ChaveDoRecebedor);

        _contexto.Motor.Barramento.Enfileirar(new EnvelopePacs008(reentrega));
    }

    /// <summary>
    /// Furando as duas guardas de cima: o lancamento vai direto ao ledger, como um reprocessamento
    /// manual ou um replay de fila faria. E aqui que "zero lancamento novo" tem de valer mesmo
    /// quando o chamador esta errado.
    /// </summary>
    [When(@"o mesmo débito de ""([^""]*)"" é lançado outra vez direto no ledger do pagador")]
    public void QuandoLancaODebitoDeNovo(string valor)
    {
        try
        {
            _contexto.Motor.Pagador.Ledger.DebitarCliente(
                _contexto.E2E,
                _contexto.ContaPagador,
                Valor.DeCentavos(Dinheiro.EmCentavos(valor)));
        }
        catch (Exception excecao)
        {
            _contexto.UltimaExcecao = excecao;
        }
    }

    [When(@"(\d+) postagens do mesmo pagamento de ""([^""]*)"" chegam ao mesmo tempo")]
    public void QuandoPostagensIdenticasSimultaneas(int quantidade, string valor)
    {
        long centavos = Dinheiro.EmCentavos(valor);

        DispararEmParalelo(quantidade, _ => Postar(centavos));
    }

    [When(@"estas postagens do mesmo E2E chegam ao mesmo tempo:")]
    public void QuandoPostagensDivergentesSimultaneas(Table tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);

        long[] valores = [.. tabela.Rows.Select(linha => Dinheiro.EmCentavos(linha["valor"]))];

        // Dois valores iguais seriam o mesmo pedido, e nao um conflito: a tabela precisa ser
        // distinta dois a dois para que "so uma vence" signifique alguma coisa.
        Assert.True(
            valores.Distinct().Count() == valores.Length,
            "os valores da tabela precisam ser distintos dois a dois");

        DispararEmParalelo(valores.Length, indice => Postar(valores[indice]));
    }

    // ---------------------------------------------------------------------------------------
    // Asserções
    // ---------------------------------------------------------------------------------------

    [Then(@"a resposta é idêntica à que o cliente guardou")]
    public void EntaoRespostaIdenticaAGuardada()
    {
        RespostaDePagamento guardada = _guardada
            ?? throw new InvalidOperationException("nenhuma resposta foi guardada neste cenário");

        RespostaDePagamento agora = _contexto.UltimaResposta
            ?? throw new InvalidOperationException("nenhum pagamento foi feito neste cenário");

        // Campo a campo primeiro, porque e o que diz QUAL campo divergiu; a comparacao do registro
        // inteiro logo abaixo pega um campo que venha a ser acrescentado e que esta lista esqueca.
        Assert.Equal(guardada.E2E, agora.E2E);
        Assert.Equal(guardada.Estado, agora.Estado);
        Assert.Equal(guardada.Motivo, agora.Motivo);
        Assert.Equal(guardada, agora);
    }

    [Then(@"o reenvio é recusado por conflito de E2E")]
    public void EntaoReenvioRecusadoPorConflito()
    {
        E2eConflitanteException conflito = ExigirConflito();

        Assert.Equal(_contexto.E2E, conflito.E2E);
    }

    [Then(@"o conflito mostra o pedido original de ""([^""]*)"" e o pedido recusado de ""([^""]*)""")]
    public void EntaoConflitoMostraOsDoisPedidos(string original, string recusado)
    {
        E2eConflitanteException conflito = ExigirConflito();

        // A impressao termina em "|centavos". Sem as duas preenchidas e diferentes, quem recebe o
        // erro nao consegue descobrir em que o pedido divergiu.
        ExigirTerminaEmCentavos(conflito.ImpressaoOriginal, original);
        ExigirTerminaEmCentavos(conflito.ImpressaoNova, recusado);

        Assert.True(
            !string.Equals(conflito.ImpressaoOriginal, conflito.ImpressaoNova, StringComparison.Ordinal),
            $"as duas impressões do conflito são iguais: '{conflito.ImpressaoOriginal}'");
    }

    [Then(@"o ledger recusa o lançamento duplicado")]
    public void EntaoLedgerRecusaODuplicado()
    {
        Exception excecao = _contexto.UltimaExcecao
            ?? throw new InvalidOperationException("nenhuma exceção foi capturada neste cenário");

        ChaveIdempotenciaDuplicadaException duplicada =
            Assert.IsType<ChaveIdempotenciaDuplicadaException>(excecao);

        // A chave e (transacao, etapa) — sem o valor. E por isso que nem um debito de outro valor
        // passa: o segundo lancamento seria um segundo debito da mesma etapa da mesma transacao.
        Assert.Equal($"{_contexto.E2E.Texto}#DebitoPagador", duplicada.Chave);
    }

    [Then(@"o pagador lançou exatamente (\d+) débitos? para essa transação")]
    public void EntaoDebitosDoPagador(int quantos) =>
        ExigirLancamentos(
            "débitos do pagador",
            quantos,
            _contexto.Motor.Pagador.Ledger.Consulta,
            EtapaLancamento.DebitoPagador);

    [Then(@"o recebedor lançou exatamente (\d+) créditos? para essa transação")]
    public void EntaoCreditosDoRecebedor(int quantos) =>
        ExigirLancamentos(
            "créditos do recebedor",
            quantos,
            _contexto.Motor.Recebedor.Ledger.Consulta,
            EtapaLancamento.CreditoRecebedor);

    [Then(@"o registro de idempotência conhece exatamente (\d+) pedidos?")]
    public void EntaoRegistroConhece(int quantos)
    {
        int observado = _contexto.Motor.Pagador.Idempotencia.Quantidade;

        Assert.True(
            quantos == observado,
            $"pedidos reivindicados no registro de idempotência: esperado {quantos}, observado {observado}");
    }

    [Then(@"o registro de idempotência guarda exatamente (\d+) respostas?")]
    public void EntaoRegistroGuarda(int quantas)
    {
        int observado = _contexto.Motor.Pagador.Idempotencia.Log().Count;

        Assert.True(
            quantas == observado,
            $"entradas no log do registro de idempotência: esperado {quantas}, observado {observado}");
    }

    [Then(@"o log de decisões do SPI tem exatamente (\d+) decis(?:ão|ões)")]
    public void EntaoDecisoesDoSpi(int quantas)
    {
        int observado = _contexto.Motor.Spi.LogDeDecisoes().Count;

        Assert.True(
            quantas == observado,
            $"decisões emitidas pelo SPI: esperado {quantas}, observado {observado}");
    }

    [Then(@"as (\d+) respostas são idênticas entre si")]
    public void EntaoRespostasIdenticas(int quantas)
    {
        ExigirNenhumaFalhaInesperada(esperadas: 0);

        Assert.True(
            quantas == _respostas.Count,
            $"postagens respondidas: esperado {quantas}, observado {_respostas.Count}");

        RespostaDePagamento referencia = _respostas[0];

        foreach (RespostaDePagamento resposta in _respostas)
        {
            Assert.Equal(referencia, resposta);
        }
    }

    [Then(@"apenas uma postagem foi aceita")]
    public void EntaoApenasUmaAceita() =>
        Assert.True(
            _respostas.Count == 1,
            $"postagens aceitas: esperada exatamente uma, observadas {_respostas.Count}");

    [Then(@"as outras (\d+) postagens foram recusadas por conflito de E2E")]
    public void EntaoAsOutrasForamRecusadas(int quantas)
    {
        ExigirNenhumaFalhaInesperada(quantas);

        int conflitos = _falhas.OfType<E2eConflitanteException>().Count();

        Assert.True(
            quantas == conflitos,
            $"postagens recusadas por conflito: esperadas {quantas}, observadas {conflitos}");

        foreach (E2eConflitanteException conflito in _falhas.OfType<E2eConflitanteException>())
        {
            Assert.Equal(_contexto.E2E, conflito.E2E);

            Assert.True(
                !string.Equals(conflito.ImpressaoOriginal, conflito.ImpressaoNova, StringComparison.Ordinal),
                "uma postagem recusada trouxe as duas impressões iguais");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// O numero da conta como o comando de pagamento o exige. Lido do proprio contexto, e nao
    /// escrito de novo aqui: dois lugares com o mesmo "0001" divergiriam no dia em que um mudasse.
    /// </summary>
    private string NumeroDaContaDoPagador =>
        _contexto.ContaPagador.Chave[ContaId.PrefixoCliente.Length..];

    private RespostaDePagamento Postar(long centavos) =>
        _contexto.Motor.Pagador.Pagar(new ComandoDePagamento(
            _contexto.E2E,
            NumeroDaContaDoPagador,
            _contexto.ChaveDoRecebedor,
            Valor.DeCentavos(centavos)));

    private E2eConflitanteException ExigirConflito()
    {
        Exception excecao = _contexto.UltimaExcecao
            ?? throw new InvalidOperationException(
                "nenhuma exceção foi capturada neste cenário: o reenvio divergente foi aceito");

        return Assert.IsType<E2eConflitanteException>(excecao);
    }

    private void ExigirLancamentos(
        string oQue,
        int esperados,
        IConsultaLedger consulta,
        EtapaLancamento etapa)
    {
        ChaveIdempotencia chave = ChaveIdempotencia.De(_contexto.E2E, etapa);

        int observados = consulta.Log()
            .SelectMany(commit => commit.Lancamentos)
            .Count(lancamento => lancamento.Chave.Equals(chave));

        Assert.True(
            esperados == observados,
            $"{oQue} para {_contexto.E2E.Texto}: esperados {esperados}, observados {observados}");
    }

    private void ExigirNenhumaFalhaInesperada(int esperadas)
    {
        IReadOnlyList<Exception> inesperadas =
            [.. _falhas.Where(falha => falha is not E2eConflitanteException)];

        Assert.True(
            inesperadas.Count == 0,
            $"exceções inesperadas ({inesperadas.Count}): "
                + string.Join(", ", inesperadas.Select(falha => falha.GetType().Name)));

        Assert.True(
            _falhas.Count == esperadas,
            $"postagens recusadas: esperadas {esperadas}, observadas {_falhas.Count}");
    }

    /// <summary>
    /// A impressao do pedido termina em <c>|centavos</c>. E o unico trecho dela que o cenario
    /// afirma: o valor escrito no texto, em centavos, no fim da impressao que a excecao carrega.
    /// </summary>
    private static void ExigirTerminaEmCentavos(string impressao, string valor) =>
        Assert.EndsWith($"|{Dinheiro.EmCentavos(valor)}", impressao, StringComparison.Ordinal);

    /// <summary>
    /// Dispara as postagens em paralelo, todas soltas pela mesma largada.
    /// <para>
    /// A largada existe porque sem ela as primeiras tarefas terminariam antes de as ultimas serem
    /// sequer agendadas, e o cenario chamaria de "ao mesmo tempo" uma execucao sequencial. Nao ha
    /// espera por tempo em lugar nenhum: relogio de parede aqui produziria cenario intermitente.
    /// </para>
    /// </summary>
    private void DispararEmParalelo(int quantidade, Func<int, RespostaDePagamento> postar)
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
                    respostas.Add(postar(indice));
                }
                catch (Exception excecao)
                {
                    // Captura ampla de proposito: o cenario precisa RELATAR o que escapou, e uma
                    // excecao engolida aqui viraria "nenhuma resposta" sem nenhuma pista.
                    falhas.Add(excecao);
                }
            });
        }

        // A largada e liberada pela thread do cenario, nunca por uma das tarefas: se dependesse de
        // todas estarem prontas, um pool que ainda nao injetou threads travaria a suite.
        largada.Set();
        Task.WaitAll(tarefas);

        _respostas.AddRange(respostas);
        _falhas.AddRange(falhas);

        _contexto.UltimaResposta = _respostas.Count > 0 ? _respostas[0] : null;
    }
}
