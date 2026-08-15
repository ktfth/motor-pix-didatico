using MotorPix.Composicao;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// A devolucao <c>pacs.004</c>: a seta pontilhada <c>SM_R -.-&gt; VAL</c> do diagrama de arquitetura
/// e a aresta <c>estados:16</c> do diagrama de estados.
/// <para>
/// Toda esta suite existe para sustentar tres afirmacoes, e cada teste diz qual delas exercita:
/// </para>
/// <para>
/// 1. A devolucao e transacao <b>nova</b>, com <c>EndToEndId</c> proprio, que apenas <em>referencia</em>
/// a original. Ela percorre a mesma maquina normativa desde <c>RECEBIDA</c> e termina em
/// <c>LIQUIDADA</c> — nao em <c>DEVOLVIDA</c>.
/// </para>
/// <para>
/// 2. Quem termina em <c>DEVOLVIDA</c> e a <b>original</b>, e so quando a devolucao liquida. O
/// gatilho da aresta <c>estados:16</c> e literalmente "pacs.004 liquidada": devolucao que e
/// rejeitada ou que nunca recebe resposta nao devolve nada, e a original permanece em
/// <c>LIQUIDADA</c>.
/// </para>
/// <para>
/// 3. A original nunca e reaberta nem tem lancamento alterado (invariante 7). O credito de volta
/// entra com a chave de idempotencia da <em>devolucao</em>, nao com a dela. O unico campo da
/// original que muda e o <c>Estado</c>.
/// </para>
/// <para>
/// Escopo declarado (decisao D4): devolucao total e unica, iniciada exclusivamente pelo PSP
/// recebedor. O valor e campo explicito, validado como igual ao original, para que a devolucao
/// parcial seja aditiva no futuro.
/// </para>
/// </summary>
public sealed class DevolucaoTestes
{
    private const long FundoInicial = 1_000_00;
    private const long ValorDoPagamento = 250_00;

    /// <summary>
    /// Credito que o recebedor registra <b>sem</b> lastro correspondente na conta PI. Existe so no
    /// cenario da devolucao rejeitada — ver a documentacao daquele teste.
    /// </summary>
    private const long CreditoSemLastro = 5_000_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // -------------------------------------------------------------------------------------------
    // Afirmacao 1 e 2 — o ciclo completo
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Pagar, drenar, devolver, drenar. Ao final a <b>devolucao</b> esta em <c>LIQUIDADA</c> e a
    /// <b>original</b> em <c>DEVOLVIDA</c> — e nao o contrario, que e o erro que este teste existe
    /// para tornar impossivel de passar despercebido.
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void Devolucao_DoPedidoAoAcsc_LiquidaComoTransacaoPropriaEDeixaAOriginalEmDevolvida()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoDoClienteRecebedor);

        VistaDaTransacao devolucao = cenario.Motor.Recebedor.SolicitarDevolucao(
            original,
            Valor.DeCentavos(ValorDoPagamento));

        // A devolucao nasce em RECEBIDA e ja percorreu VALIDADA ate ENVIADA_SPI quando
        // SolicitarDevolucao retorna: e a mesma ordem de operacoes do pagamento — transicao gravada
        // antes do efeito externo —, porque e a mesma maquina normativa.
        Assert.Equal(EstadoTransacao.Recebida, devolucao.Historico[0].Origem);
        Assert.Equal(EstadoTransacao.EnviadaSpi, devolucao.Estado);
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);

        // Transacao NOVA: E2E proprio, emitido por quem devolve, referenciando a original.
        Assert.NotEqual(original, devolucao.E2E);
        Assert.Equal(original, devolucao.E2eOriginal);
        Assert.Equal(IspbRecebedor, devolucao.E2E.Ispb);

        // O discriminador de tipo separa as duas: a original nao vira devolucao ao ser devolvida.
        Assert.Equal(TipoTransacao.Devolucao, devolucao.Tipo);
        Assert.Equal(TipoTransacao.Pagamento, cenario.Original(original).Tipo);
        Assert.Null(cenario.Original(original).E2eOriginal);

        // pacs.004 ao SPI, depois pacs.002 ACSC a quem devolveu e a quem recebe de volta.
        Assert.Equal(3, cenario.Motor.Barramento.Drenar());

        // O ponto do gate: a devolucao termina em LIQUIDADA. Se ela terminasse em DEVOLVIDA, a
        // aresta estados:16 estaria sendo aplicada a transacao errada.
        VistaDaTransacao liquidada = cenario.Devolucao(devolucao.E2E);
        Assert.Equal(EstadoTransacao.Liquidada, liquidada.Estado);
        Assert.NotEqual(EstadoTransacao.Devolvida, liquidada.Estado);

        // E a original — e so ela — vai para DEVOLVIDA.
        Assert.Equal(EstadoTransacao.Devolvida, cenario.Original(original).Estado);

        // O caminho da devolucao dentro da maquina e o mesmo do pagamento, aresta por aresta.
        Assert.Collection(
            liquidada.Historico,
            t =>
            {
                Assert.Equal(EstadoTransacao.Recebida, t.Origem);
                Assert.Equal(TipoEvento.ValidacaoLocalOk, t.Evento);
                Assert.Equal(EstadoTransacao.Validada, t.Destino);
            },
            t =>
            {
                Assert.Equal(EstadoTransacao.Validada, t.Origem);
                Assert.Equal(TipoEvento.DebitoLancadoEPacs008Despachado, t.Evento);
                Assert.Equal(EstadoTransacao.EnviadaSpi, t.Destino);
            },
            t =>
            {
                Assert.Equal(EstadoTransacao.EnviadaSpi, t.Origem);
                Assert.Equal(TipoEvento.Pacs002Acsc, t.Evento);
                Assert.Equal(EstadoTransacao.Liquidada, t.Destino);
            });

        // Todo mundo voltou ao ponto de partida: clientes, espelhos e as duas contas PI.
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClienteRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoPagador);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoRecebedor);
        Assert.Equal(FundoInicial, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial, cenario.SaldoPiRecebedor);
    }

    /// <summary>
    /// P4 aplicado ao par (original, devolucao): o efeito liquido dos dois juntos e zero em
    /// <b>todas</b> as contas dos tres ledgers, e nao apenas nas seis que a narrativa cita.
    /// <para>
    /// A fotografia e comparada conta a conta, incluindo o conjunto de chaves: uma conta que
    /// aparecesse ou sumisse no meio do caminho reprovaria aqui, o que a soma por ledger — sempre
    /// zero por partidas dobradas — nao pegaria.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void OriginalSomadaADevolucao_TemEfeitoLiquidoZero_EmTodasAsContasDosTresLedgers()
    {
        Cenario cenario = Cenario.Montar();
        Dictionary<LedgerId, SnapshotSaldos> antes = cenario.Fotografar();

        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);
        cenario.DevolverELiquidar(original, ValorDoPagamento);

        Dictionary<LedgerId, SnapshotSaldos> depois = cenario.Fotografar();

        foreach ((LedgerId ledger, SnapshotSaldos inicial) in antes)
        {
            SnapshotSaldos final = depois[ledger];

            Assert.True(
                inicial.EhIgualA(final),
                $"pagamento seguido de devolucao mexeu no ledger {ledger}: {Junte(inicial.Diferencas(final))}");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Afirmacao 3 — invariante 7: a original nao e reaberta
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Invariante 7 medido onde ele de fato mora: no <b>ledger</b>.
    /// <para>
    /// O credito de volta entra com a chave de idempotencia da <em>devolucao</em>. Se entrasse com a
    /// da original, o log ficaria com duas entradas para o mesmo E2E em etapas contraditorias, a
    /// idempotencia do estorno passaria a depender de qual delas chegasse primeiro, e o replay do
    /// gate 8 reconstruiria uma historia que nunca aconteceu — tudo isso com a soma por ledger
    /// fechando em zero, portanto invisivel para qualquer propriedade contabil.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void Devolucao_NaoReabreAOriginal_OCreditoDeVoltaEntraComAChaveDaDevolucao()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        string chavesDaOriginalNoPagador = ChavesDoE2e(cenario.Motor.Pagador.Ledger.Consulta, original);
        string chavesDaOriginalNoRecebedor = ChavesDoE2e(cenario.Motor.Recebedor.Ledger.Consulta, original);
        string chavesDaOriginalNoSpi = ChavesDoE2e(cenario.Motor.Spi.Consulta, original);

        int commitsDoPagador = cenario.CommitsDoPagador;
        int commitsDoRecebedor = cenario.CommitsDoRecebedor;
        int commitsDoSpi = cenario.CommitsDoSpi;
        int historicoDaOriginal = cenario.Original(original).Historico.Count;

        EndToEndId devolucao = cenario.DevolverELiquidar(original, ValorDoPagamento);

        // O credito de volta usa a chave da DEVOLUCAO, nunca a da original.
        Assert.True(cenario.Motor.Pagador.Ledger.HouveCredito(devolucao));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveCredito(original));

        // E os lancamentos da original continuam exatamente os que eram: nem um a mais, nem
        // alterados, nem removidos. Comparar o conjunto de chaves e mais forte que contar commits —
        // um estorno indevido da original entraria como commit novo com chave nova e apareceria aqui.
        Assert.Equal(chavesDaOriginalNoPagador, ChavesDoE2e(cenario.Motor.Pagador.Ledger.Consulta, original));
        Assert.Equal(chavesDaOriginalNoRecebedor, ChavesDoE2e(cenario.Motor.Recebedor.Ledger.Consulta, original));
        Assert.Equal(chavesDaOriginalNoSpi, ChavesDoE2e(cenario.Motor.Spi.Consulta, original));

        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(original));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(original));

        // O log so cresce: append-only vale tambem para a devolucao. Um lancamento a mais em cada
        // ledger — credito de volta, debito de quem devolve, liquidacao entre contas PI.
        Assert.True(
            cenario.CommitsDoPagador >= commitsDoPagador,
            $"o log do pagador encolheu de {commitsDoPagador} para {cenario.CommitsDoPagador}");
        Assert.True(
            cenario.CommitsDoRecebedor >= commitsDoRecebedor,
            $"o log do recebedor encolheu de {commitsDoRecebedor} para {cenario.CommitsDoRecebedor}");
        Assert.True(
            cenario.CommitsDoSpi >= commitsDoSpi,
            $"o log do SPI encolheu de {commitsDoSpi} para {cenario.CommitsDoSpi}");

        Assert.Equal(commitsDoPagador + 1, cenario.CommitsDoPagador);
        Assert.Equal(commitsDoRecebedor + 1, cenario.CommitsDoRecebedor);
        Assert.Equal(commitsDoSpi + 1, cenario.CommitsDoSpi);

        // O unico campo da original que mudou e o Estado, e a mudanca e uma unica entrada nova no
        // historico — a aresta estados:16, e nada alem dela.
        VistaDaTransacao depois = cenario.Original(original);
        Assert.Equal(historicoDaOriginal + 1, depois.Historico.Count);

        TransicaoAplicada ultima = depois.Historico[^1];
        Assert.Equal(EstadoTransacao.Liquidada, ultima.Origem);
        Assert.Equal(TipoEvento.DevolucaoLiquidada, ultima.Evento);
        Assert.Equal(EstadoTransacao.Devolvida, ultima.Destino);

        // E o resto da identidade da original nao se mexeu.
        Assert.Equal(ValorDoPagamento, depois.Valor.Centavos);
        Assert.Equal(TipoTransacao.Pagamento, depois.Tipo);
        Assert.Null(depois.E2eOriginal);
        Assert.Null(depois.MotivoRejeicao);
    }

    /// <summary>
    /// <c>DEVOLVIDA</c> e terminal; <c>LIQUIDADA</c> nao e.
    /// <para>
    /// As duas tem seta para o pseudo-estado final no diagrama, entao derivar terminalidade dessas
    /// setas daria <c>LIQUIDADA</c> como terminal — e o motor recusaria todo <c>pacs.004</c>,
    /// matando o gate 6 inteiro. Por isso a propriedade e declarada na tabela, e nao inferida do
    /// desenho: quem manda e a existencia da aresta <c>estados:16</c> saindo de <c>LIQUIDADA</c>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void EhTerminal_DevolvidaSim_LiquidadaNaoPorqueAindaAceitaAArestaDaDevolucao()
    {
        Assert.True(MaquinaDeEstados.EhTerminal(EstadoTransacao.Devolvida));
        Assert.False(MaquinaDeEstados.EhTerminal(EstadoTransacao.Liquidada));

        // A razao concreta de LIQUIDADA nao ser terminal, expressa como transicao valida.
        Assert.True(MaquinaDeEstados.EhValida(EstadoTransacao.Liquidada, TipoEvento.DevolucaoLiquidada));

        // E a razao concreta de DEVOLVIDA ser: nenhum evento sai de la, nem o proprio.
        Assert.False(MaquinaDeEstados.EhValida(EstadoTransacao.Devolvida, TipoEvento.DevolucaoLiquidada));

        foreach ((EstadoTransacao origem, TipoEvento evento, EstadoTransacao destino) in MaquinaDeEstados.Arestas())
        {
            Assert.NotEqual(EstadoTransacao.Devolvida, origem);
            Assert.True(
                destino != EstadoTransacao.Devolvida || (origem == EstadoTransacao.Liquidada && evento == TipoEvento.DevolucaoLiquidada),
                $"DEVOLVIDA so pode ser alcancada pela aresta estados:16, mas ({origem}, {evento}) chega la");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Afirmacao 2 — devolucao que nao liquida nao devolve nada
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Devolucao recusada pelo SPI: a original permanece em <c>LIQUIDADA</c> e o debito de quem
    /// devolve e estornado por lancamento novo.
    /// <para>
    /// O cenario precisa ser <b>construido a partir de uma divergencia</b>, e isso e uma propriedade
    /// do modelo, nao um defeito do teste: num fluxo bem formado o SPI nunca pode recusar uma
    /// devolucao por falta de saldo na conta PI. A conta PI de quem devolve recebeu o valor original
    /// na liquidacao, e a devolucao total vale exatamente esse valor — o saldo, portanto, nunca fica
    /// devendo. So ha como faltar lastro se os livros do recebedor ja divergirem do SPI.
    /// </para>
    /// <para>
    /// Por isso injetamos a divergencia: o recebedor registra um credito <em>maior</em> do que o SPI
    /// vai mover. E exatamente a classe de divergencia que a conciliacao do gate 7 existe para achar
    /// e, aqui, ela serve para exercitar a unica coisa que interessa: <b>o gatilho da aresta
    /// estados:16 e "pacs.004 LIQUIDADA"</b>. Devolucao que nao liquida nao move a original.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void DevolucaoRejeitadaPeloSpi_DeixaAOriginalEmLiquidada_EEstornaODebitoDeQuemDevolve()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.ProximoE2e();
        cenario.Pagar(original, ValorDoPagamento);

        // A divergencia: o recebedor aplica um credito de 5.000,00 para um E2E cujo lastro no SPI
        // sera de 250,00. O ACSC verdadeiro, que chega logo depois, e no-op — a guarda de credito
        // duplicado do recebedor e por chave de idempotencia do ledger.
        cenario.InjetarCreditoSemLastro(original, CreditoSemLastro);

        Assert.Equal(3, cenario.Motor.Barramento.Drenar());
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoPiRecebedor);

        int commitsDoSpi = cenario.CommitsDoSpi;
        int historicoDaOriginal = cenario.Original(original).Historico.Count;

        VistaDaTransacao pedido = cenario.Motor.Recebedor.SolicitarDevolucao(
            original,
            Valor.DeCentavos(CreditoSemLastro));

        Assert.Equal(EstadoTransacao.EnviadaSpi, pedido.Estado);
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveDebito(pedido.E2E));

        // pacs.004 ao SPI, e o RJCT so de volta a quem devolveu: o creditor nunca soube da ordem.
        Assert.Equal(2, cenario.Motor.Barramento.Drenar());

        Assert.True(cenario.Motor.Spi.TryRespostaDe(pedido.E2E, out Pacs002? resposta));
        Assert.NotNull(resposta);
        Assert.Equal(StatusPacs002.Rjct, resposta.Status);

        // O motivo e OrdemInvalida, e nao saldo: o cenario injeta um credito sem lastro no
        // recebedor, entao a devolucao pede de volta mais do que a original moveu — e o SPI agora
        // confere que a devolucao ESPELHA a original antes de liquidar. A guarda de saldo viria
        // depois, e nunca chega a ser alcancada. O que o teste prova continua sendo o mesmo:
        // devolucao que nao liquida nao devolve nada, e o debito de quem devolveu e estornado.
        Assert.Equal(MotivoRejeicao.OrdemInvalida, resposta.Motivo);

        // A devolucao morre pela aresta estados:10, como qualquer transacao rejeitada pelo SPI.
        VistaDaTransacao rejeitada = cenario.Devolucao(pedido.E2E);
        Assert.Equal(EstadoTransacao.Rejeitada, rejeitada.Estado);

        TransicaoAplicada ultima = rejeitada.Historico[^1];
        Assert.Equal(EstadoTransacao.EnviadaSpi, ultima.Origem);
        Assert.Equal(TipoEvento.Pacs002Rjct, ultima.Evento);
        Assert.Equal(EstadoTransacao.Rejeitada, ultima.Destino);

        // O debito de quem devolve foi compensado por lancamento novo, nunca por UPDATE: os dois
        // coexistem no log.
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveDebito(pedido.E2E));
        Assert.True(cenario.Motor.Recebedor.Ledger.HouveEstorno(pedido.E2E));

        // E a ORIGINAL nao se mexeu: nem estado, nem historico. Devolucao que nao liquida nao
        // devolve nada.
        VistaDaTransacao depois = cenario.Original(original);
        Assert.Equal(EstadoTransacao.Liquidada, depois.Estado);
        Assert.NotEqual(EstadoTransacao.Devolvida, depois.Estado);
        Assert.Equal(historicoDaOriginal, depois.Historico.Count);

        // Nada foi movido no SPI, e nada voltou para o cliente pagador.
        Assert.Equal(commitsDoSpi, cenario.CommitsDoSpi);
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoPiRecebedor);
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoDoClientePagador);
        Assert.False(cenario.Motor.Pagador.Ledger.HouveCredito(pedido.E2E));
    }

    /// <summary>
    /// Devolucao despachada e sem resposta nenhuma: a original continua em <c>LIQUIDADA</c>.
    /// <para>
    /// O ponto do teste e a original <b>nao se mover</b> enquanto a devolucao nao liquida — nao o
    /// destino final da devolucao. A varredura de vencidos do gate 5 e uma porta do PSP
    /// <em>pagador</em> e percorre as transacoes que ele originou; a devolucao vive no recebedor,
    /// entao ela nao e alcancada por <c>ExpirarVencidos</c>. Deixamos isso explicito em vez de
    /// forcar uma expiracao que a API de hoje nao oferece.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void DevolucaoSemRespostaDoSpi_MesmoDepoisDoPrazo_DeixaAOriginalEmLiquidada()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);
        int historicoDaOriginal = cenario.Original(original).Historico.Count;

        VistaDaTransacao pedido = cenario.Motor.Recebedor.SolicitarDevolucao(
            original,
            Valor.DeCentavos(ValorDoPagamento));

        // O pacs.004 fica na fila: ninguem drena, entao o SPI nunca ve a devolucao.
        Assert.Equal(1, cenario.Motor.Barramento.Pendentes);

        cenario.Relogio.Avancar(new TimeSpan(0, 5, 0));

        // A varredura do pagador nao tem nada a expirar: a unica transacao dele esta em LIQUIDADA, e
        // a devolucao — que esta em ENVIADA_SPI — nao e dele.
        Assert.Empty(cenario.Motor.Pagador.ExpirarVencidos());
        Assert.Equal(EstadoTransacao.EnviadaSpi, cenario.Devolucao(pedido.E2E).Estado);

        // O que importa: a original nao se moveu um milimetro.
        VistaDaTransacao depois = cenario.Original(original);
        Assert.Equal(EstadoTransacao.Liquidada, depois.Estado);
        Assert.NotEqual(EstadoTransacao.Devolvida, depois.Estado);
        Assert.Equal(historicoDaOriginal, depois.Historico.Count);

        // E nenhum dinheiro voltou: o credito de volta so existe depois do ACSC da devolucao.
        Assert.False(cenario.Motor.Pagador.Ledger.HouveCredito(pedido.E2E));
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial - ValorDoPagamento, cenario.SaldoPiPagador);
        Assert.Equal(FundoInicial + ValorDoPagamento, cenario.SaldoPiRecebedor);
    }

    // -------------------------------------------------------------------------------------------
    // Guardas de SolicitarDevolucao
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// So se devolve o que se recebeu. O insumo da devolucao e o <em>credito aplicado</em>, e nao a
    /// existencia de uma transacao em algum lugar: um E2E ainda em voo, cujo ACSC nao chegou, nao
    /// tem conta de destino nem valor confirmados aqui.
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void SolicitarDevolucao_DeE2eSemCreditoRecebido_RecusaSemTocarEmNada()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId nuncaVisto = cenario.ProximoE2e();

        Marco antes = cenario.Marcar();

        DevolucaoInvalidaException erro = Assert.Throws<DevolucaoInvalidaException>(
            () => cenario.Motor.Recebedor.SolicitarDevolucao(nuncaVisto, Valor.DeCentavos(ValorDoPagamento)));

        Assert.Equal(nuncaVisto, erro.E2eOriginal);
        Assert.Contains("credito recebido", erro.Motivo, StringComparison.Ordinal);
        cenario.ExigirIntocado(antes);

        // Mesmo pedido, agora com um pagamento de verdade em voo: o pacs.008 ja saiu, mas o ACSC
        // ainda nao voltou, entao o recebedor nao creditou nada e nao ha o que devolver.
        EndToEndId emVoo = cenario.ProximoE2e();
        cenario.Pagar(emVoo, ValorDoPagamento);

        Marco antesDoSegundo = cenario.Marcar();

        Assert.Throws<DevolucaoInvalidaException>(
            () => cenario.Motor.Recebedor.SolicitarDevolucao(emVoo, Valor.DeCentavos(ValorDoPagamento)));

        cenario.ExigirIntocado(antesDoSegundo);
        Assert.False(cenario.Motor.Recebedor.TryDevolucao(emVoo, out VistaDaTransacao? nenhuma));
        Assert.Null(nenhuma);
    }

    /// <summary>
    /// Devolucao parcial esta fora de escopo (decisao D4), e o valor e conferido contra o credito
    /// original nos <b>dois</b> sentidos.
    /// <para>
    /// A guarda de valor e a mais importante das tres. Sem ela, uma devolucao <em>maior</em> que a
    /// original injetaria dinheiro no sistema sem que nada acusasse: as partidas dobradas
    /// continuariam batendo — o excedente sairia da conta PI de quem devolve — e a soma por ledger
    /// tambem, porque a soma dos saldos brutos de um ledger e zero por construcao. Quem pagaria a
    /// diferenca seria o participante, em silencio.
    /// </para>
    /// <para>
    /// O valor <em>menor</em> e recusado pelo motivo oposto: nao e um risco contabil, e uma
    /// funcionalidade que nao existe. Aceita-la em silencio deixaria a original marcada como
    /// DEVOLVIDA com parte do dinheiro ainda com o recebedor.
    /// </para>
    /// </summary>
    [Theory]
    [Trait("gate", "6")]
    [InlineData(ValorDoPagamento + 1)]
    [InlineData(ValorDoPagamento * 2)]
    [InlineData(ValorDoPagamento - 1)]
    [InlineData(1L)]
    public void SolicitarDevolucao_ComValorDiferenteDoRecebido_RecusaCitandoQueParcialEstaForaDeEscopo(long pedido)
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        Marco antes = cenario.Marcar();
        int historicoDaOriginal = cenario.Original(original).Historico.Count;

        DevolucaoInvalidaException erro = Assert.Throws<DevolucaoInvalidaException>(
            () => cenario.Motor.Recebedor.SolicitarDevolucao(original, Valor.DeCentavos(pedido)));

        Assert.Equal(original, erro.E2eOriginal);
        Assert.Contains("parcial", erro.Motivo, StringComparison.Ordinal);

        cenario.ExigirIntocado(antes);
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);
        Assert.Equal(historicoDaOriginal, cenario.Original(original).Historico.Count);

        // A recusa nao queima a original: o pedido correto continua funcionando depois.
        EndToEndId devolucao = cenario.DevolverELiquidar(original, ValorDoPagamento);
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Devolucao(devolucao).Estado);
        Assert.Equal(EstadoTransacao.Devolvida, cenario.Original(original).Estado);
    }

    /// <summary>
    /// Devolucao unica (decisao D4): a segunda tentativa sobre a mesma original e recusada.
    /// <para>
    /// A recusa e do PSP, e nao da maquina de estados. Confiar so no estado terminal deixaria
    /// passar a segunda solicitacao feita <em>antes</em> de a primeira liquidar — as duas nasceriam
    /// com a original ainda em LIQUIDADA, as duas debitariam o cliente, e o segundo pacs.004 seria
    /// uma ordem legitima do ponto de vista do SPI.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void SolicitarDevolucao_PelaSegundaVezNaMesmaOriginal_RecusaAntesEDepoisDeLiquidar()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);

        VistaDaTransacao primeira = cenario.Motor.Recebedor.SolicitarDevolucao(
            original,
            Valor.DeCentavos(ValorDoPagamento));

        // Antes de liquidar: a original ainda esta em LIQUIDADA, entao a maquina de estados sozinha
        // aceitaria a segunda devolucao. Quem recusa e o indice de devolucoes do PSP.
        Marco antes = cenario.Marcar();

        DevolucaoInvalidaException emVoo = Assert.Throws<DevolucaoInvalidaException>(
            () => cenario.Motor.Recebedor.SolicitarDevolucao(original, Valor.DeCentavos(ValorDoPagamento)));

        Assert.Equal(original, emVoo.E2eOriginal);
        Assert.Contains("devolvida", emVoo.Motivo, StringComparison.Ordinal);
        cenario.ExigirIntocado(antes);
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Original(original).Estado);

        Assert.Equal(3, cenario.Motor.Barramento.Drenar());
        Assert.Equal(EstadoTransacao.Liquidada, cenario.Devolucao(primeira.E2E).Estado);
        Assert.Equal(EstadoTransacao.Devolvida, cenario.Original(original).Estado);

        // Depois de liquidar, a recusa continua — e agora com DEVOLVIDA terminal por tras dela.
        Marco depois = cenario.Marcar();

        Assert.Throws<DevolucaoInvalidaException>(
            () => cenario.Motor.Recebedor.SolicitarDevolucao(original, Valor.DeCentavos(ValorDoPagamento)));

        cenario.ExigirIntocado(depois);
        Assert.Equal(EstadoTransacao.Devolvida, cenario.Original(original).Estado);
    }

    /// <summary>
    /// Devolucao de devolucao e recusada, e a recursao morre no <b>discriminador de tipo</b>.
    /// <para>
    /// Uma devolucao liquidada e, no recebedor, uma <c>Transacao</c> de
    /// <see cref="TipoTransacao.Devolucao"/> guardada no indice de devolucoes — nunca um credito
    /// recebido. E o credito recebido que <c>SolicitarDevolucao</c> exige, entao nao ha por onde a
    /// cadeia continuar. Sem o discriminador, "devolver a devolucao" seria apenas mais um pacs.004
    /// bem formado, e o par de participantes ficaria devolvendo o mesmo dinheiro um ao outro
    /// indefinidamente, cada volta com E2E novo e todas as propriedades contabeis fechando.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void SolicitarDevolucao_DoE2eDaPropriaDevolucao_EhRecusadaPorqueDevolucaoNaoEhCreditoRecebido()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);
        EndToEndId devolucao = cenario.DevolverELiquidar(original, ValorDoPagamento);

        Assert.Equal(TipoTransacao.Devolucao, cenario.Devolucao(devolucao).Tipo);

        Marco antes = cenario.Marcar();

        DevolucaoInvalidaException erro = Assert.Throws<DevolucaoInvalidaException>(
            () => cenario.Motor.Recebedor.SolicitarDevolucao(devolucao, Valor.DeCentavos(ValorDoPagamento)));

        Assert.Equal(devolucao, erro.E2eOriginal);
        Assert.Contains("credito recebido", erro.Motivo, StringComparison.Ordinal);
        cenario.ExigirIntocado(antes);

        // O outro lado tambem nao tem por onde continuar: quem recebeu o dinheiro de volta foi o
        // pagador, e o pagador nao registra a devolucao como transacao sua — ele so creditou o
        // cliente e marcou a original.
        Assert.False(cenario.Motor.Pagador.TryTransacao(devolucao, out VistaDaTransacao? noPagador));
        Assert.Null(noPagador);
        Assert.Equal(EstadoTransacao.Devolvida, cenario.Original(original).Estado);
    }

    // -------------------------------------------------------------------------------------------
    // Reentrega
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Reentrega do ACSC da devolucao: nao credita duas vezes e nao transiciona a original de novo.
    /// <para>
    /// As duas guardas sao independentes e precisam ser, porque protegem coisas diferentes: o
    /// credito e barrado pela chave de idempotencia do ledger — que resiste a reentrega vinda de
    /// qualquer caminho, inclusive do reprocessamento da conciliacao do gate 7 —, e a transicao e
    /// barrada por <c>DEVOLVIDA</c> ser terminal.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void ReentregaDoAcscDaDevolucao_NaoCreditaDuasVezes_ENaoTransicionaAOriginalDeNovo()
    {
        Cenario cenario = Cenario.Montar();
        EndToEndId original = cenario.PagarELiquidar(ValorDoPagamento);
        EndToEndId devolucao = cenario.DevolverELiquidar(original, ValorDoPagamento);

        Assert.True(cenario.Motor.Spi.TryRespostaDe(devolucao, out Pacs002? acsc));
        Assert.NotNull(acsc);
        Assert.Equal(StatusPacs002.Acsc, acsc.Status);
        Assert.NotNull(acsc.Referencia);

        // O bloco de referencia carrega o E2E devolvido: e por ele que o pagador sabe qual transacao
        // marcar. Sem esse campo, o unico caminho seria adivinhar por valor e conta — ambiguo assim
        // que existirem dois pagamentos iguais entre as mesmas contas.
        Assert.Equal(original, acsc.Referencia.E2eDevolvido);

        int commitsDoPagador = cenario.CommitsDoPagador;
        int historicoDaOriginal = cenario.Original(original).Historico.Count;

        cenario.Motor.Pagador.Receber(acsc);
        cenario.Motor.Pagador.Receber(acsc);

        Assert.Equal(commitsDoPagador, cenario.CommitsDoPagador);
        Assert.Equal(FundoInicial, cenario.SaldoDoClientePagador);
        Assert.Equal(FundoInicial, cenario.SaldoEspelhoPagador);

        VistaDaTransacao depois = cenario.Original(original);
        Assert.Equal(EstadoTransacao.Devolvida, depois.Estado);
        Assert.Equal(historicoDaOriginal, depois.Historico.Count);
    }

    // -------------------------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Chaves de idempotencia gravadas no ledger para um E2E, na ordem do log, ja concatenadas.
    /// Uma unica string para que a falha do teste mostre o que apareceu ou sumiu, e nao apenas
    /// que duas colecoes diferem.
    /// </summary>
    private static string ChavesDoE2e(IConsultaLedger consulta, EndToEndId e2e)
    {
        List<string> chaves = [];

        foreach (Commit commit in consulta.Log())
        {
            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                if (e2e.Equals(lancamento.Chave.E2E))
                {
                    chaves.Add(lancamento.Chave.Texto);
                }
            }
        }

        return string.Join(" | ", chaves);
    }

    private static string Junte(IReadOnlyList<string> partes) =>
        partes.Count == 0 ? "(nenhuma diferenca)" : string.Join("; ", partes);

    /// <summary>Contadores que uma recusa nao pode mexer.</summary>
    private sealed record Marco(
        int CommitsDoPagador,
        int CommitsDoRecebedor,
        int CommitsDoSpi,
        int Pendentes,
        int TotalEntregue,
        long SaldoDoClientePagador,
        long SaldoDoClienteRecebedor);

    /// <summary>Motor montado, com os atalhos de leitura que a devolucao precisa.</summary>
    private sealed class Cenario
    {
        public const string ContaDoPagador = "0001";
        public const string ContaDoRecebedor = "0002";

        private readonly GeradorE2eDeterministico _gerador;

        private Cenario(RelogioFake relogio, MotorMontado motor, ChavePix chave, GeradorE2eDeterministico gerador)
        {
            Relogio = relogio;
            Motor = motor;
            Chave = chave;
            _gerador = gerador;

            ContaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
            ContaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor);
        }

        public RelogioFake Relogio { get; }

        public MotorMontado Motor { get; }

        public ChavePix Chave { get; }

        public ContaId ContaPagador { get; }

        public ContaId ContaRecebedor { get; }

        public long SaldoDoClientePagador => Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos;

        public long SaldoDoClienteRecebedor => Motor.Recebedor.Ledger.SaldoDoCliente(ContaRecebedor).Centavos;

        public long SaldoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoEspelhoRecebedor => Motor.Recebedor.Ledger.SaldoDoEspelho().Centavos;

        public long SaldoPiPagador => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbPagador)).Centavos;

        public long SaldoPiRecebedor => Motor.Spi.Inspecao.SaldoNatural(ContaId.Pi(IspbRecebedor)).Centavos;

        public int CommitsDoPagador => Motor.Pagador.Ledger.Consulta.Log().Count;

        public int CommitsDoRecebedor => Motor.Recebedor.Ledger.Consulta.Log().Count;

        public int CommitsDoSpi => Motor.Spi.Consulta.Log().Count;

        public static Cenario Montar()
        {
            RelogioFake relogio = new();

            MotorMontado motor = MotorMontado.Montar(
                relogio,
                IspbPagador,
                IspbRecebedor,
                Valor.DeCentavos(FundoInicial),
                ContaDoPagador,
                ContaDoRecebedor);

            ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
            motor.Recebedor.RegistrarChave(chave, ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor));

            return new Cenario(relogio, motor, chave, new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca));
        }

        public EndToEndId ProximoE2e() => _gerador.Proximo();

        public RespostaDePagamento Pagar(EndToEndId e2e, long centavos) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(e2e, ContaDoPagador, Chave, Valor.DeCentavos(centavos)));

        /// <summary>Pagamento completo, das setas 1 a 7. Devolve o E2E da transacao liquidada.</summary>
        public EndToEndId PagarELiquidar(long centavos)
        {
            EndToEndId e2e = ProximoE2e();
            Pagar(e2e, centavos);
            Motor.Barramento.Drenar();
            return e2e;
        }

        /// <summary>Devolucao completa. Devolve o E2E <b>proprio</b> da devolucao.</summary>
        public EndToEndId DevolverELiquidar(EndToEndId original, long centavos)
        {
            VistaDaTransacao devolucao = Motor.Recebedor.SolicitarDevolucao(original, Valor.DeCentavos(centavos));
            Motor.Barramento.Drenar();
            return devolucao.E2E;
        }

        /// <summary>
        /// Aplica no recebedor um credito que o SPI nao movera, montando um <c>pacs.002 ACSC</c> a
        /// mao. E a divergencia deliberada de que o cenario da devolucao rejeitada depende.
        /// </summary>
        public void InjetarCreditoSemLastro(EndToEndId e2e, long centavos) =>
            Motor.Recebedor.Receber(Pacs002.Aceito(
                e2e,
                new OrgnlTxRef(
                    IspbPagador,
                    ContaPagador,
                    IspbRecebedor,
                    ContaRecebedor,
                    Valor.DeCentavos(centavos))));

        public VistaDaTransacao Original(EndToEndId e2e)
        {
            Assert.True(Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? vista));
            Assert.NotNull(vista);
            return vista;
        }

        public VistaDaTransacao Devolucao(EndToEndId e2e)
        {
            Assert.True(Motor.Recebedor.TryDevolucao(e2e, out VistaDaTransacao? vista));
            Assert.NotNull(vista);
            return vista;
        }

        public Dictionary<LedgerId, SnapshotSaldos> Fotografar() =>
            new()
            {
                [Motor.Pagador.Ledger.Id] = Motor.Pagador.Ledger.Inspecao.Snapshot(),
                [Motor.Recebedor.Ledger.Id] = Motor.Recebedor.Ledger.Inspecao.Snapshot(),
                [LedgerId.Spi] = Motor.Spi.Inspecao.Snapshot(),
            };

        public Marco Marcar() => new(
            CommitsDoPagador,
            CommitsDoRecebedor,
            CommitsDoSpi,
            Motor.Barramento.Pendentes,
            Motor.Barramento.TotalEntregue,
            SaldoDoClientePagador,
            SaldoDoClienteRecebedor);

        /// <summary>
        /// Uma recusa nao lanca nada, nao enfileira nada e nao entrega nada. Conferir os tres
        /// separadamente importa: um pedido que so enfileirasse a mensagem passaria por "nenhum
        /// lancamento" e mesmo assim moveria dinheiro na drenagem seguinte.
        /// </summary>
        public void ExigirIntocado(Marco antes)
        {
            Assert.Equal(antes.CommitsDoPagador, CommitsDoPagador);
            Assert.Equal(antes.CommitsDoRecebedor, CommitsDoRecebedor);
            Assert.Equal(antes.CommitsDoSpi, CommitsDoSpi);
            Assert.Equal(antes.Pendentes, Motor.Barramento.Pendentes);
            Assert.Equal(antes.TotalEntregue, Motor.Barramento.TotalEntregue);
            Assert.Equal(antes.SaldoDoClientePagador, SaldoDoClientePagador);
            Assert.Equal(antes.SaldoDoClienteRecebedor, SaldoDoClienteRecebedor);
        }
    }
}
