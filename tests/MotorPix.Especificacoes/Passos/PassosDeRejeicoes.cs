using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Especificacoes.Suporte;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// Os passos que so as rejeicoes precisam: montar as situacoes que fazem o motor recusar, e ler o
/// que uma recusa deixou (ou nao deixou) nos tres ledgers.
/// <para>
/// A assercao central deste tema nao e o estado final — REJEITADA e alcancavel por arestas com
/// consequencias contabeis <em>opostas</em>. Por isso aqui existem passos que leem o ledger direto:
/// "houve debito", "houve estorno" e "quantos lancamentos entraram". Sem eles, um cenario que so
/// olhasse o estado nao distinguiria a rejeicao que veio de RECEBIDA, sem debito nenhum a desfazer,
/// da que veio de ENVIADA_SPI, em que o estorno e obrigatorio.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDeRejeicoes
{
    /// <summary>A conta que <c>ContextoDoMotor.Montar</c> abre para o cliente pagador.</summary>
    private const string ContaDoPagador = "0001";

    /// <summary>Numero de conta valido em forma, jamais aberto em ledger nenhum.</summary>
    private const string ContaNuncaAberta = "9999";

    /// <summary>ISPB que nunca foi registrado como participante nem tem conta PI aberta.</summary>
    private static readonly Ispb IspbForaDoSpi = Ispb.Criar("33333333");

    private readonly ContextoDoMotor _contexto;

    /// <summary>Chaves declaradas pelo cenario, sob o apelido com que ele as chama.</summary>
    private readonly Dictionary<string, ChavePix> _chaves = [];

    /// <summary>
    /// Contagem de lancamentos por ledger no instante em que o cenario mandou contar. Negativo
    /// significa "ninguem contou": um cenario que afirme "nenhum lancamento novo" sem ter contado
    /// antes tem de falhar como erro, e nao passar comparando lixo com lixo.
    /// </summary>
    private int _lancamentosDoPagador = -1;
    private int _lancamentosDoRecebedor = -1;
    private int _lancamentosDoSpi = -1;

    public PassosDeRejeicoes(ContextoDoMotor contexto) => _contexto = contexto;

    [Given(@"que a chave ""([^""]*)"" não está cadastrada no DICT")]
    public void DadoChaveForaDoDict(string apelido) => Declarar(apelido);

    /// <summary>
    /// Vincula a chave direto no diretorio, apontando para conta que o recebedor nunca abriu.
    /// <para>
    /// Vai pelo DICT, e nao por <c>PspRecebedor.RegistrarChave</c>, porque aquele caminho confere a
    /// existencia da conta e recusaria — a guarda do recebedor e justamente o que impede este estado
    /// de nascer pela porta da frente. O que o cenario exercita e a segunda linha de defesa: o SPI
    /// perguntando <c>PodeCreditar</c> antes de liquidar.
    /// </para>
    /// </summary>
    [Given(@"que a chave ""([^""]*)"" está vinculada a uma conta que o recebedor nunca abriu")]
    public void DadoChaveEmContaNuncaAberta(string apelido)
    {
        ChavePix chave = Declarar(apelido);
        ContaId fantasma = ContaId.Cliente(_contexto.Motor.Recebedor.Ledger.Id, ContaNuncaAberta);

        _contexto.Motor.Dict.Vincular(chave, ContextoDoMotor.IspbRecebedor, fantasma);
    }

    [Given(@"que a chave ""([^""]*)"" aponta para a própria conta do pagador")]
    public void DadoChaveNaPropriaConta(string apelido)
    {
        ChavePix chave = Declarar(apelido);

        _contexto.Motor.Dict.Vincular(chave, ContextoDoMotor.IspbPagador, _contexto.ContaPagador);
    }

    /// <summary>
    /// Abre um segundo cliente no pagador, com genesis proprio.
    /// <para>
    /// O aporte entra no espelho do PSP mas <em>nao</em> na conta PI do SPI — e um PSP que prometeu
    /// aos clientes mais do que mantem no SPI. E assim que nasce o cenario em que a validacao local
    /// passa, o debito acontece, e so o SPI descobre que nao ha lastro.
    /// </para>
    /// </summary>
    [Given(@"que o pagador abriu o cliente ""([^""]*)"" com fundo de ""([^""]*)"" sem lastro na conta PI")]
    public void DadoClienteSemLastro(string conta, string fundo) =>
        _contexto.Motor.Pagador.AbrirCliente(conta, Valor.DeCentavos(Dinheiro.EmCentavos(fundo)));

    [Given(@"que os lançamentos dos três ledgers foram contados")]
    public void DadoLancamentosContados()
    {
        _lancamentosDoPagador = _contexto.Motor.Pagador.Ledger.Consulta.Log().Count;
        _lancamentosDoRecebedor = _contexto.Motor.Recebedor.Ledger.Consulta.Log().Count;
        _lancamentosDoSpi = _contexto.Motor.Spi.Consulta.Log().Count;
    }

    [When(@"o cliente pagador paga ""([^""]*)"" para a chave ""([^""]*)""")]
    public void QuandoPagaParaChaveDeclarada(string valor, string apelido) =>
        Pagar(_contexto.E2E, ContaDoPagador, Chave(apelido), valor);

    [When(@"o cliente pagador paga ""([^""]*)"" para a chave ""([^""]*)"", como pagamento ""([^""]*)""")]
    public void QuandoPagaParaChaveDeclaradaApelidado(string valor, string apelido, string pagamento) =>
        Pagar(_contexto.ProximoE2e(pagamento), ContaDoPagador, Chave(apelido), valor);

    [When(@"o cliente pagador paga ""([^""]*)"" a partir da conta ""([^""]*)""")]
    public void QuandoPagaDeOutraConta(string valor, string conta) =>
        Pagar(_contexto.E2E, conta, _contexto.ChaveDoRecebedor, valor);

    [When(@"o cliente ""([^""]*)"" paga ""([^""]*)"" para a chave do recebedor")]
    public void QuandoOutroClientePaga(string conta, string valor) =>
        Pagar(_contexto.E2E, conta, _contexto.ChaveDoRecebedor, valor);

    [When(@"o cliente ""([^""]*)"" paga ""([^""]*)"" para a chave do recebedor, como pagamento ""([^""]*)""")]
    public void QuandoOutroClientePagaApelidado(string conta, string valor, string pagamento) =>
        Pagar(_contexto.ProximoE2e(pagamento), conta, _contexto.ChaveDoRecebedor, valor);

    /// <summary>
    /// Entrega a ordem direto ao SPI, sem passar pelo barramento.
    /// <para>
    /// Nenhum PSP montado consegue produzir esta mensagem: o destino sai do DICT, e o DICT so conhece
    /// participantes reais. Enfileira-la seria simular um participante que o motor nao tem.
    /// </para>
    /// </summary>
    [When(@"um pacs\.008 de ""([^""]*)"" chega ao SPI endereçado a um participante que ele não conhece")]
    public void QuandoPacs008ParaParticipanteDesconhecido(string valor)
    {
        Pacs008 ordem = new(
            _contexto.E2E,
            ContextoDoMotor.IspbPagador,
            _contexto.ContaPagador,
            IspbForaDoSpi,
            ContaId.Cliente(LedgerId.Psp(IspbForaDoSpi), "0004"),
            Valor.DeCentavos(Dinheiro.EmCentavos(valor)),
            ChavePix.Criar(TipoChave.Email, "terceiro@exemplo.com"));

        _contexto.Motor.Spi.ReceberPacs008(ordem);
    }

    [Then(@"a transação não registrou destino")]
    public void EntaoSemDestino()
    {
        VistaDaTransacao transacao = _contexto.TransacaoDoPagador(_contexto.E2E);

        // A guarda de origem roda ANTES da seta 2: um pagamento que nao tem de onde sair nao precisa
        // saber para onde iria, e resolver a chave primeiro exporia o DICT a quem nem e correntista.
        Assert.True(
            transacao.IspbDestino is null && transacao.ContaDestino is null,
            $"o destino deveria estar em branco, mas veio {transacao.IspbDestino} / {transacao.ContaDestino}");
    }

    [Then(@"a transação registrou o recebedor como destino")]
    public void EntaoDestinoResolvido()
    {
        VistaDaTransacao transacao = _contexto.TransacaoDoPagador(_contexto.E2E);

        // Sem esta assercao, um defeito que fizesse a resolucao de chave falhar em silencio passaria
        // por "saldo insuficiente".
        Assert.Equal(ContextoDoMotor.IspbRecebedor, transacao.IspbDestino);
        Assert.Equal(_contexto.ContaRecebedor, transacao.ContaDestino);
    }

    [Then(@"a transação traz motivo ""([^""]*)""")]
    public void EntaoTransacaoTrazMotivo(string motivo) =>
        Assert.Equal(
            Vocabulario.Enumerado<MotivoRejeicaoLocal>(motivo),
            _contexto.TransacaoDoPagador(_contexto.E2E).MotivoRejeicao);

    [Then(@"o pagamento ""([^""]*)"" traz motivo ""([^""]*)""")]
    public void EntaoPagamentoTrazMotivo(string apelido, string motivo) =>
        Assert.Equal(
            Vocabulario.Enumerado<MotivoRejeicaoLocal>(motivo),
            _contexto.TransacaoDoPagador(_contexto.E2eDe(apelido)).MotivoRejeicao);

    [Then(@"o débito da transação foi lançado no ledger do pagador")]
    public void EntaoHouveDebito() =>
        Assert.True(
            _contexto.Motor.Pagador.Ledger.HouveDebito(_contexto.E2E),
            "a seta 3 deveria ter acontecido: o débito é lançado antes de o pacs.008 ser despachado");

    [Then(@"nenhum débito foi lançado no ledger do pagador")]
    public void EntaoSemDebito() =>
        Assert.False(
            _contexto.Motor.Pagador.Ledger.HouveDebito(_contexto.E2E),
            "a recusa local acontece antes da seta 3: não pode haver débito lançado");

    [Then(@"o débito da transação foi estornado no ledger do pagador")]
    public void EntaoHouveEstorno() =>
        Assert.True(
            _contexto.Motor.Pagador.Ledger.HouveEstorno(_contexto.E2E),
            "o débito já lançado tem de ser compensado por um estorno, nunca corrigido no lugar");

    [Then(@"nenhum estorno foi lançado no ledger do pagador")]
    public void EntaoSemEstorno() =>
        Assert.False(
            _contexto.Motor.Pagador.Ledger.HouveEstorno(_contexto.E2E),
            "estornar um débito que nunca foi lançado seria emitir dinheiro do nada");

    [Then(@"o pagamento ""([^""]*)"" teve débito e estorno no ledger do pagador")]
    public void EntaoPagamentoComDebitoEEstorno(string apelido)
    {
        EndToEndId e2e = _contexto.E2eDe(apelido);

        Assert.True(
            _contexto.Motor.Pagador.Ledger.HouveDebito(e2e),
            $"o pagamento '{apelido}' chegou a ENVIADA_SPI, então o débito tem de estar no log");
        Assert.True(
            _contexto.Motor.Pagador.Ledger.HouveEstorno(e2e),
            $"o pagamento '{apelido}' foi recusado depois do débito, então o estorno é obrigatório");
    }

    [Then(@"o pagamento ""([^""]*)"" não teve débito nem estorno no ledger do pagador")]
    public void EntaoPagamentoSemDebitoNemEstorno(string apelido)
    {
        EndToEndId e2e = _contexto.E2eDe(apelido);

        Assert.False(
            _contexto.Motor.Pagador.Ledger.HouveDebito(e2e),
            $"o pagamento '{apelido}' morreu na validação local: nada foi lançado");
        Assert.False(
            _contexto.Motor.Pagador.Ledger.HouveEstorno(e2e),
            $"o pagamento '{apelido}' não tem débito a desfazer, então estorná-lo criaria dinheiro");
    }

    [Then(@"o SPI respondeu RJCT com motivo ""([^""]*)""")]
    public void EntaoSpiRecusou(string motivo)
    {
        Assert.True(
            _contexto.Motor.Spi.TryRespostaDe(_contexto.E2E, out Pacs002? resposta),
            "o SPI não registrou decisão nenhuma para este E2E");
        Assert.NotNull(resposta);

        Assert.Equal(StatusPacs002.Rjct, resposta.Status);
        Assert.Equal(Vocabulario.Enumerado<MotivoRejeicao>(motivo), resposta.Motivo);

        // RJCT nao carrega OrgnlTxRef: nao houve liquidacao a referenciar.
        Assert.Null(resposta.Referencia);
    }

    [Then(@"o recebedor não creditou nada")]
    public void EntaoRecebedorSemCredito()
    {
        Assert.False(
            _contexto.Motor.Recebedor.Ledger.HouveCredito(_contexto.E2E),
            "a seta 7 não pode ter acontecido: o SPI recusou antes de mover as contas PI");
        Assert.Null(_contexto.Motor.Recebedor.UltimoCreditoEm);
    }

    [Then(@"o saldo do cliente ""([^""]*)"" é ""([^""]*)""")]
    public void EntaoSaldoDoClienteDoPagador(string conta, string esperado)
    {
        long alvo = Dinheiro.EmCentavos(esperado);
        long observado = _contexto.Motor.Pagador.Ledger
            .SaldoDoCliente(ContaId.Cliente(_contexto.Motor.Pagador.Ledger.Id, conta))
            .Centavos;

        Assert.True(
            alvo == observado,
            $"saldo do cliente {conta}: esperado {Dinheiro.Formatar(alvo)}, observado {Dinheiro.Formatar(observado)}");
    }

    [Then(@"o barramento nunca entregou nenhuma mensagem")]
    public void EntaoNadaFoiEntregue() =>
        Assert.True(
            _contexto.Motor.Barramento.TotalEntregue == 0,
            $"a seta 4 nunca saiu, mas o barramento entregou {_contexto.Motor.Barramento.TotalEntregue} mensagens");

    [Then(@"nenhum lançamento novo entrou em nenhum dos três ledgers")]
    public void EntaoNenhumLancamentoNovo()
    {
        ExigirContagem();

        Conferir("ledger do pagador", _lancamentosDoPagador, _contexto.Motor.Pagador.Ledger.Consulta.Log().Count);
        Conferir("ledger do recebedor", _lancamentosDoRecebedor, _contexto.Motor.Recebedor.Ledger.Consulta.Log().Count);
        Conferir("ledger do SPI", _lancamentosDoSpi, _contexto.Motor.Spi.Consulta.Log().Count);
    }

    [Then(@"o ledger do SPI não ganhou nenhum lançamento novo")]
    public void EntaoSpiSemLancamentoNovo()
    {
        ExigirContagem();

        Conferir("ledger do SPI", _lancamentosDoSpi, _contexto.Motor.Spi.Consulta.Log().Count);
    }

    private static void Conferir(string qualLedger, int antes, int agora) =>
        Assert.True(
            antes == agora,
            $"{qualLedger}: tinha {antes} lançamentos e agora tem {agora} — a recusa não foi neutra");

    /// <summary>
    /// Cria uma chave nova sob o apelido com que o cenario a chama.
    /// <para>
    /// O texto da chave e canonico e sai de um contador, nao do apelido: o apelido e rotulo de
    /// leitura ("orfa", "fantasma", "espelhada") e nao tem de ser um e-mail bem formado. O que
    /// importa e que duas chaves declaradas no mesmo cenario sejam distintas entre si e distintas da
    /// chave do recebedor, que <c>ContextoDoMotor</c> ja registrou.
    /// </para>
    /// </summary>
    private ChavePix Declarar(string apelido)
    {
        if (_chaves.ContainsKey(apelido))
        {
            throw new InvalidOperationException($"a chave '{apelido}' ja foi declarada neste cenario");
        }

        ChavePix chave = ChavePix.Criar(TipoChave.Email, $"chave{_chaves.Count + 1}@exemplo.com");
        _chaves.Add(apelido, chave);
        return chave;
    }

    private ChavePix Chave(string apelido) =>
        _chaves.TryGetValue(apelido, out ChavePix? chave)
            ? chave
            : throw new InvalidOperationException(
                $"nenhuma chave chamada '{apelido}' foi declarada neste cenario");

    private void Pagar(EndToEndId e2e, string conta, ChavePix chave, string valor) =>
        _contexto.UltimaResposta = _contexto.Motor.Pagador.Pagar(new ComandoDePagamento(
            e2e,
            conta,
            chave,
            Valor.DeCentavos(Dinheiro.EmCentavos(valor))));

    private void ExigirContagem()
    {
        if (_lancamentosDoSpi < 0)
        {
            throw new InvalidOperationException(
                "o cenario precisa contar os lancamentos antes de afirmar que nenhum entrou");
        }
    }
}
