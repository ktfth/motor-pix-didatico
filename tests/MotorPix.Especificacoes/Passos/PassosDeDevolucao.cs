using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Especificacoes.Suporte;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// Os passos da devolucao (aresta estados:16).
/// <para>
/// A devolucao precisa de passos proprios por uma razao de dominio, e nao de conveniencia: ela e
/// uma transacao NOVA, com E2E proprio, que vive no PSP recebedor. Os passos ja existentes leem a
/// transacao do PAGADOR pelo E2E do cenario — reaproveita-los para a devolucao afirmaria coisas
/// sobre a original enquanto o texto do cenario fala da devolucao, que e exatamente a troca de
/// papeis que este tema existe para tornar impossivel.
/// </para>
/// <para>
/// O E2E da devolucao nao pode ser escrito no cenario: quem o emite e o recebedor, no momento do
/// pedido. Ele fica guardado aqui, no binding — que o SpecFlow instancia por cenario, entao nao ha
/// vazamento entre cenarios.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDeDevolucao
{
    private readonly ContextoDoMotor _contexto;

    private EndToEndId? _e2eDaDevolucao;

    /// <summary>O E2E que a ultima tentativa recusada pediu de volta. Base da conferencia da recusa.</summary>
    private EndToEndId? _alvoDaTentativa;

    public PassosDeDevolucao(ContextoDoMotor contexto) => _contexto = contexto;

    // -----------------------------------------------------------------------------------------
    // Acoes
    // -----------------------------------------------------------------------------------------

    [When(@"o recebedor solicita a devolução de ""([^""]*)""")]
    public void QuandoSolicitaDevolucao(string valor)
    {
        VistaDaTransacao devolucao = _contexto.Motor.Recebedor.SolicitarDevolucao(
            _contexto.E2E,
            Valor.DeCentavos(Dinheiro.EmCentavos(valor)));

        _e2eDaDevolucao = devolucao.E2E;
    }

    // "tenta devolver" e o pedido que pode ser recusado; "solicita a devolucao" e o que tem de
    // funcionar. Sao passos distintos de proposito: um cenario que espera sucesso e falha morre com
    // a excecao original, e nao num passo adiante com o motivo da recusa ja perdido.
    [When(@"o recebedor tenta devolver ""([^""]*)""")]
    public void QuandoTentaDevolver(string valor) =>
        Tentar(_contexto.E2E, Dinheiro.EmCentavos(valor));

    [When(@"o recebedor tenta devolver a devolução de ""([^""]*)""")]
    public void QuandoTentaDevolverADevolucao(string valor) =>
        Tentar(E2eDaDevolucao, Dinheiro.EmCentavos(valor));

    /// <summary>
    /// Injeta no recebedor um credito que o SPI nao movera: os livros dele passam a divergir do SPI.
    /// <para>
    /// Nao e artificio para encurtar o cenario — e a unica forma de existir uma devolucao recusada.
    /// Num fluxo bem formado a devolucao total espelha a original que o SPI ja liquidou, entao ele
    /// nunca tem motivo para recusar. So a divergencia produz o pedido que ele nega.
    /// </para>
    /// </summary>
    [When(@"o recebedor credita ""([^""]*)"" pelo pagamento sem lastro no SPI")]
    public void QuandoCreditaSemLastro(string valor) =>
        _contexto.Motor.Recebedor.Receber(Pacs002.Aceito(
            _contexto.E2E,
            new OrgnlTxRef(
                ContextoDoMotor.IspbPagador,
                _contexto.ContaPagador,
                ContextoDoMotor.IspbRecebedor,
                _contexto.ContaRecebedor,
                Valor.DeCentavos(Dinheiro.EmCentavos(valor)))));

    [When(@"o pagador recebe novamente o pacs\.002 da devolução")]
    public void QuandoPagadorRecebeDeNovo() =>
        _contexto.Motor.Pagador.Receber(RespostaDoSpiParaADevolucao());

    // -----------------------------------------------------------------------------------------
    // Asserções sobre a devolucao
    // -----------------------------------------------------------------------------------------

    [Then(@"a devolução está em ""([^""]*)""")]
    public void EntaoDevolucaoEstaEm(string estado) =>
        Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(estado), VistaDaDevolucao().Estado);

    /// <summary>
    /// O coracao do invariante 7. Se a devolucao reabrisse a original em vez de nascer, os tres
    /// fatos aqui seriam falsos ao mesmo tempo — e nenhuma soma de saldos acusaria.
    /// </summary>
    [Then(@"a devolução é uma transação nova, com E2E próprio emitido pelo recebedor")]
    public void EntaoDevolucaoEhTransacaoNova()
    {
        VistaDaTransacao devolucao = VistaDaDevolucao();

        Assert.NotEqual(_contexto.E2E, devolucao.E2E);
        Assert.Equal(_contexto.E2E, devolucao.E2eOriginal);
        Assert.Equal(TipoTransacao.Devolucao, devolucao.Tipo);
        Assert.Equal(ContextoDoMotor.IspbRecebedor, devolucao.E2E.Ispb);
    }

    [Then(@"a original não virou devolução")]
    public void EntaoOriginalNaoVirouDevolucao()
    {
        VistaDaTransacao original = _contexto.TransacaoDoPagador(_contexto.E2E);

        Assert.Equal(TipoTransacao.Pagamento, original.Tipo);
        Assert.Null(original.E2eOriginal);
        Assert.Null(original.MotivoRejeicao);
    }

    // O historico e comparado na ordem e no tamanho exatos, como o da original: conferir so o
    // estado final deixaria passar uma devolucao que chegasse a LIQUIDADA por um caminho que o
    // diagrama nao tem.
    [Then(@"o histórico da devolução é exatamente:")]
    public void EntaoHistoricoDaDevolucao(Table tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);

        VistaDaTransacao devolucao = VistaDaDevolucao();

        Assert.Equal(tabela.RowCount, devolucao.Historico.Count);

        for (int i = 0; i < tabela.RowCount; i++)
        {
            TableRow linha = tabela.Rows[i];
            TransicaoAplicada aplicada = devolucao.Historico[i];

            Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(linha["origem"]), aplicada.Origem);
            Assert.Equal(Vocabulario.Enumerado<TipoEvento>(linha["evento"]), aplicada.Evento);
            Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(linha["destino"]), aplicada.Destino);
        }
    }

    /// <summary>
    /// O outro lado do corte da recursao: quem recebeu o dinheiro de volta foi o pagador, e ele nao
    /// registra a devolucao como transacao sua — so creditou o cliente e marcou a original.
    /// </summary>
    [Then(@"o pagador não conhece a devolução")]
    public void EntaoPagadorNaoConheceADevolucao() =>
        Assert.False(
            _contexto.Motor.Pagador.TryTransacao(E2eDaDevolucao, out _),
            "o pagador registrou a devolução como transação própria");

    [Then(@"o SPI respondeu ""([^""]*)"" à devolução")]
    public void EntaoSpiRespondeu(string motivo)
    {
        Pacs002 resposta = RespostaDoSpiParaADevolucao();

        Assert.Equal(StatusPacs002.Rjct, resposta.Status);
        Assert.Equal(Vocabulario.Enumerado<MotivoRejeicao>(motivo), resposta.Motivo);
    }

    // -----------------------------------------------------------------------------------------
    // Asserções sobre a recusa
    // -----------------------------------------------------------------------------------------

    [Then(@"o pedido de devolução é recusado porque não há crédito recebido para esse E2E")]
    public void EntaoRecusadoSemCredito() => ExigirMotivo("credito recebido");

    [Then(@"o pedido de devolução é recusado porque devolução parcial está fora de escopo")]
    public void EntaoRecusadoPorParcial() => ExigirMotivo("devolucao parcial esta fora de escopo");

    [Then(@"o pedido de devolução é recusado porque a original já foi devolvida")]
    public void EntaoRecusadoPorJaDevolvida() => ExigirMotivo("ja foi devolvida");

    // -----------------------------------------------------------------------------------------
    // Apoio
    // -----------------------------------------------------------------------------------------

    private EndToEndId E2eDaDevolucao =>
        _e2eDaDevolucao ?? throw new InvalidOperationException(
            "nenhuma devolução foi solicitada neste cenário: falta um passo 'o recebedor solicita a devolução'");

    private VistaDaTransacao VistaDaDevolucao() =>
        _contexto.Motor.Recebedor.TryDevolucao(E2eDaDevolucao, out VistaDaTransacao? vista)
            ? vista
            : throw new InvalidOperationException($"o recebedor não conhece a devolução {E2eDaDevolucao}");

    private Pacs002 RespostaDoSpiParaADevolucao()
    {
        bool respondeu = _contexto.Motor.Spi.TryRespostaDe(E2eDaDevolucao, out Pacs002? resposta);

        return respondeu && resposta is not null
            ? resposta
            : throw new InvalidOperationException(
                $"o SPI ainda não respondeu à devolução {E2eDaDevolucao}: falta drenar o barramento");
    }

    // So DevolucaoInvalidaException e capturada. Qualquer outra excecao e defeito, e tem de derrubar
    // o cenario onde aconteceu em vez de virar uma recusa que o passo seguinte aceitaria.
    private void Tentar(EndToEndId alvo, long centavos)
    {
        _alvoDaTentativa = alvo;

        try
        {
            _contexto.Motor.Recebedor.SolicitarDevolucao(alvo, Valor.DeCentavos(centavos));
        }
        catch (DevolucaoInvalidaException excecao)
        {
            _contexto.UltimaExcecao = excecao;
        }
    }

    private void ExigirMotivo(string trecho)
    {
        EndToEndId alvo = _alvoDaTentativa
            ?? throw new InvalidOperationException(
                "nenhum pedido de devolução foi tentado neste cenário: falta um passo 'o recebedor tenta devolver'");

        Exception excecao = _contexto.UltimaExcecao
            ?? throw new InvalidOperationException(
                $"o pedido de devolução de {alvo} não foi recusado: o motor aceitou o pedido");

        DevolucaoInvalidaException recusa = Assert.IsType<DevolucaoInvalidaException>(excecao);

        // A recusa tem de apontar para o E2E que o cenario pediu de volta, e nao para outro
        // qualquer: e o que separa "recusou este pedido" de "recusou algum pedido".
        Assert.Equal(alvo, recusa.E2eOriginal);
        Assert.Contains(trecho, recusa.Motivo, StringComparison.Ordinal);
    }
}
