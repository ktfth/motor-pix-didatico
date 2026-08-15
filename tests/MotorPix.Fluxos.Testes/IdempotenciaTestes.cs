using MotorPix.Composicao;
using MotorPix.Contratos;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Invariante 5: idempotencia por <c>EndToEndId</c> — "reenvio retorna a resposta original, sem
/// novo lancamento" — e a nota de RECEBIDA no diagrama de estados: "E2E repetido nunca cria estado".
/// <para>
/// Sao <b>duas</b> camadas de deduplicacao, com semanticas diferentes e indices independentes. A
/// da API do PSP e indexada pelo E2E do <em>pedido do cliente</em> e devolve a resposta congelada. A
/// do SPI e indexada pelo E2E da <em>mensagem</em> e devolve o <c>pacs.002</c> ja emitido. Confundi-las
/// e o erro classico: um POST reenviado e um <c>pacs.008</c> reentregue nao sao o mesmo evento, e o
/// pacs.004 do gate 6 — que tem E2E proprio — precisa que os dois indices continuem separados.
/// </para>
/// <para>
/// Abaixo das duas, a chave de idempotencia do ledger e a ultima linha de defesa: e ela, e nao a
/// guarda de API, que garante "zero lancamento novo" quando a reentrega vem por um caminho que
/// ninguem previu.
/// </para>
/// </summary>
public sealed class IdempotenciaTestes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;
    private const long ValorDivergente = 200_00;

    /// <summary>Cliente com mais saldo do que o PSP mantem na propria conta PI: produz o RJCT do SPI.</summary>
    private const long FundoDoClienteRico = 5_000_00;
    private const long ValorAcimaDaContaPi = 3_000_00;

    private const string ContaDoPagador = "0001";
    private const string ContaDoRecebedor = "0002";
    private const string ContaAlternativa = "0003";
    private const string ContaDoClienteRico = "0004";

    /// <summary>Numero de conta valido em forma, jamais aberto em ledger nenhum.</summary>
    private const string ContaInexistente = "9999";

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    private static readonly ChavePix ChaveDoRecebedor = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
    private static readonly ChavePix ChaveAlternativa = ChavePix.Criar(TipoChave.Email, "outro@exemplo.com");
    private static readonly ChavePix ChaveOrfa = ChavePix.Criar(TipoChave.Email, "ninguem@exemplo.com");

    /// <summary>
    /// Os tres componentes da impressao digital do pedido: <c>origem|chave|centavos</c>. Divergir em
    /// qualquer um deles e um pedido diferente sob o mesmo E2E.
    /// </summary>
    public enum ComponenteDaImpressao
    {
        Valor = 1,
        ContaDeOrigem = 2,
        ChaveDeDestino = 3,
    }

    // ---------------------------------------------------------------------------------------
    // Reenvio identico, nos tres estados que a transacao alcanca no gate 4
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Estado REJEITADA, por cada motivo local que a validacao produz.
    /// <para>
    /// <see cref="MotivoRejeicaoLocal.RejeitadaPeloSpi"/> nao entra aqui de proposito: ele nao e
    /// produzido por <c>Pagar</c>, e sim pelo <c>pacs.002 RJCT</c> que chega depois. Esse caso tem
    /// teste proprio, e o interessante nele e justamente que a resposta congelada <em>nao</em> o
    /// acompanha.
    /// </para>
    /// </summary>
    [Theory]
    [Trait("gate", "4")]
    [InlineData(MotivoRejeicaoLocal.ChaveNaoEncontrada)]
    [InlineData(MotivoRejeicaoLocal.SaldoInsuficiente)]
    [InlineData(MotivoRejeicaoLocal.ContaDeOrigemDesconhecida)]
    [InlineData(MotivoRejeicaoLocal.OrdemInvalida)]
    public void Pagar_ReenviadoComATransacaoEmRejeitada_DevolveARespostaOriginalSemNovoEfeito(
        MotivoRejeicaoLocal motivo)
    {
        MotorMontado motor = MontarMotor();
        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = ComandoQueProduz(motor, motivo, e2e);

        RespostaDePagamento primeira = motor.Pagador.Pagar(comando);

        Assert.Equal(EstadoTransacao.Rejeitada, primeira.Estado);
        Assert.Equal(motivo, primeira.Motivo);

        RespostaDePagamento reenvio = ExigirReenvioSemEfeito(motor, comando, primeira);

        Assert.Equal(EstadoTransacao.Rejeitada, reenvio.Estado);
        Assert.Equal(motivo, reenvio.Motivo);

        // Nenhuma rejeicao local chega a debitar, entao o reenvio nao tem nem debito nem estorno
        // para duplicar. A assercao existe para separar "nao lancou de novo" de "nunca lancou".
        Assert.False(motor.Pagador.Ledger.HouveDebito(e2e));
        Assert.False(motor.Pagador.Ledger.HouveEstorno(e2e));
    }

    /// <summary>
    /// Estado ENVIADA_SPI, com a original ainda em voo: o <c>pacs.008</c> esta enfileirado e o
    /// resultado nao voltou. O reenvio devolve a resposta congelada, e a drenagem posterior acontece
    /// uma vez so.
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ReenviadoComATransacaoEmVooEmEnviadaSpi_DevolveARespostaOriginalEDrenaUmaVezSo()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
        ContaId contaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor);
        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = new(e2e, ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento));

        RespostaDePagamento primeira = motor.Pagador.Pagar(comando);

        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Estado);
        Assert.Null(primeira.Motivo);
        Assert.Equal(1, motor.Barramento.Pendentes);

        RespostaDePagamento reenvio = ExigirReenvioSemEfeito(motor, comando, primeira);

        Assert.Equal(EstadoTransacao.EnviadaSpi, reenvio.Estado);

        // O reenvio nao enfileirou um segundo pacs.008: continua havendo uma unica ordem a caminho.
        Assert.Equal(1, motor.Barramento.Pendentes);

        // pacs.008 ao SPI, depois pacs.002 ACSC ao pagador e ao recebedor. Se o reenvio tivesse
        // despachado uma segunda ordem, seriam seis.
        Assert.Equal(3, motor.Barramento.Drenar());

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        // Um debito, um credito, uma liquidacao — mesmo tendo havido dois POSTs.
        Assert.Equal(Fundo - Pagamento, motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos);
        Assert.Equal(Fundo + Pagamento, motor.Recebedor.Ledger.SaldoDoCliente(contaRecebedor).Centavos);
        Assert.True(motor.Recebedor.Ledger.HouveCredito(e2e));
        Assert.False(motor.Pagador.Ledger.HouveEstorno(e2e));
    }

    /// <summary>
    /// Estado LIQUIDADA. Este e o caso que separa "replay da resposta" de "consulta de status".
    /// <para>
    /// O diagrama de arquitetura diz literalmente <em>replay da resposta</em>: o que volta e o que
    /// foi respondido na primeira vez, ENVIADA_SPI, e nao o estado que a transacao alcancou depois.
    /// Devolver o estado atual faria o cliente ver duas respostas diferentes para o mesmo pedido, sem
    /// nada no protocolo que o avisasse de qual e a verdadeira — e a consulta de status, que e o
    /// canal certo para perguntar "e agora?", chega no gate 5 com nome proprio.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ReenviadoDepoisDeLiquidada_DevolveOEstadoRespondidoNaPrimeiraVezENaoOAtual()
    {
        MotorMontado motor = MontarMotor();
        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = new(e2e, ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento));

        RespostaDePagamento primeira = motor.Pagador.Pagar(comando);
        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Estado);

        Assert.Equal(3, motor.Barramento.Drenar());

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        RespostaDePagamento reenvio = ExigirReenvioSemEfeito(motor, comando, primeira);

        // O ponto central: a resposta e um snapshot congelado, nao uma projecao viva.
        Assert.Equal(EstadoTransacao.EnviadaSpi, reenvio.Estado);
        Assert.NotEqual(transacao.Estado, reenvio.Estado);

        // A transacao continua onde estava: o reenvio nao a reabriu nem acrescentou transicao.
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.Equal(3, transacao.Historico.Count);
    }

    /// <summary>
    /// O snapshot tambem nao acompanha a transacao ate REJEITADA pelo SPI.
    /// <para>
    /// Aqui o estorno ja aconteceu e a transacao terminou em REJEITADA/RejeitadaPeloSpi, mas o que
    /// foi respondido ao cliente na seta 1 continua sendo ENVIADA_SPI. E a mesma regra do caso
    /// liquidado, com o sinal contabil invertido — o que garante que a resposta congelada nao esta
    /// simplesmente "por acaso" igual ao estado final nos casos felizes.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ReenviadoDepoisDeRjctDoSpi_ContinuaDevolvendoEnviadaSpiEmVezDeRejeitada()
    {
        MotorMontado motor = MontarMotor();
        motor.Pagador.AbrirCliente(ContaDoClienteRico, Valor.DeCentavos(FundoDoClienteRico));

        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento comando = new(
            e2e,
            ContaDoClienteRico,
            ChaveDoRecebedor,
            Valor.DeCentavos(ValorAcimaDaContaPi));

        RespostaDePagamento primeira = motor.Pagador.Pagar(comando);
        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Estado);

        // Duas entregas: o RJCT so vai ao pagador.
        Assert.Equal(2, motor.Barramento.Drenar());

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        Assert.Equal(EstadoTransacao.Rejeitada, transacao.Estado);
        Assert.Equal(MotivoRejeicaoLocal.RejeitadaPeloSpi, transacao.MotivoRejeicao);
        Assert.True(motor.Pagador.Ledger.HouveEstorno(e2e));

        RespostaDePagamento reenvio = ExigirReenvioSemEfeito(motor, comando, primeira);

        Assert.Equal(EstadoTransacao.EnviadaSpi, reenvio.Estado);
        Assert.Null(reenvio.Motivo);
        Assert.NotEqual(transacao.MotivoRejeicao, reenvio.Motivo);

        // O estorno nao foi lancado duas vezes, e nada novo entrou no ledger.
        Assert.True(motor.Pagador.Ledger.HouveEstorno(e2e));
    }

    // ---------------------------------------------------------------------------------------
    // Decisao D3 — payload divergente sob o mesmo E2E nao faz replay cego
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Desvio declarado da letra do prompt, que manda "reenvio retorna a resposta original".
    /// <para>
    /// Responder "aceito" a um pagamento diferente do executado e o pior modo de falha de um motor
    /// de pagamentos: quem reenviasse o mesmo E2E com o dobro do valor receberia a confirmacao do
    /// pagamento antigo e teria toda razao de acreditar que o novo passou.
    /// </para>
    /// </summary>
    [Theory]
    [Trait("gate", "4")]
    [InlineData(ComponenteDaImpressao.Valor, "|10000", "|20000")]
    [InlineData(ComponenteDaImpressao.ContaDeOrigem, "CLIENTE:0001", "CLIENTE:0003")]
    [InlineData(ComponenteDaImpressao.ChaveDeDestino, "Email:recebedor@exemplo.com", "Email:outro@exemplo.com")]
    public void Pagar_ReenviadoComPayloadDivergente_LancaE2eConflitanteSemTocarNaTransacaoOriginal(
        ComponenteDaImpressao componente,
        string trechoDaOriginal,
        string trechoDaNova)
    {
        MotorMontado motor = MontarMotor();

        // Os dois enxertos entram em todos os casos, para que o ledger e o DICT partam do mesmo
        // ponto nos tres — senao a contagem de lancamentos mediria o setup, e nao o reenvio.
        motor.Pagador.AbrirCliente(ContaAlternativa, Valor.DeCentavos(Fundo));
        motor.Recebedor.RegistrarChave(ChaveAlternativa, ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor));

        EndToEndId e2e = E2eDe(1);
        ComandoDePagamento original = new(e2e, ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento));

        RespostaDePagamento primeira = motor.Pagador.Pagar(original);
        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Estado);

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);

        EstadoTransacao estadoAntes = transacao.Estado;
        int transicoesAntes = transacao.Historico.Count;
        int commitsAntes = motor.Pagador.Ledger.Consulta.Log().Count;
        int entradasAntes = motor.Pagador.Idempotencia.Log().Count;
        int pendentesAntes = motor.Barramento.Pendentes;

        ComandoDePagamento divergente = Divergir(original, componente);

        E2eConflitanteException excecao = Assert.Throws<E2eConflitanteException>(
            () => motor.Pagador.Pagar(divergente));

        Assert.Equal(e2e, excecao.E2E);

        // As duas impressoes chegam preenchidas e diferentes: sem isso a excecao nao permite a quem
        // recebe o erro descobrir em que o pedido divergiu.
        Assert.False(string.IsNullOrEmpty(excecao.ImpressaoOriginal));
        Assert.False(string.IsNullOrEmpty(excecao.ImpressaoNova));
        Assert.NotEqual(excecao.ImpressaoOriginal, excecao.ImpressaoNova);

        Assert.Contains(trechoDaOriginal, excecao.ImpressaoOriginal, StringComparison.Ordinal);
        Assert.Contains(trechoDaNova, excecao.ImpressaoNova, StringComparison.Ordinal);
        Assert.DoesNotContain(trechoDaNova, excecao.ImpressaoOriginal, StringComparison.Ordinal);

        // A transacao original nao foi alterada, e nenhum lancamento novo entrou.
        Assert.Equal(estadoAntes, transacao.Estado);
        Assert.Equal(transicoesAntes, transacao.Historico.Count);
        Assert.Equal(Valor.DeCentavos(Pagamento), transacao.Valor);
        Assert.Equal(ChaveDoRecebedor, transacao.ChaveDestino);
        Assert.Equal(commitsAntes, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(pendentesAntes, motor.Barramento.Pendentes);

        // E o E2E divergente nao reivindicou nada: continua havendo um unico pedido registrado.
        Assert.Equal(1, motor.Pagador.Idempotencia.Quantidade);
        Assert.Equal(entradasAntes, motor.Pagador.Idempotencia.Log().Count);

        // O pedido original continua reenviavel: o conflito nao corrompeu a entrada congelada.
        Assert.Equal(primeira, motor.Pagador.Pagar(original));
    }

    /// <summary>
    /// A impressao digital compara a forma <b>canonica</b> da chave, nao o texto que o cliente
    /// digitou. Sem isso, o mesmo CPF em duas grafias viraria conflito falso — e um reenvio
    /// perfeitamente legitimo receberia um erro dizendo que o pedido mudou.
    /// </summary>
    [Theory]
    [Trait("gate", "4")]
    [InlineData(TipoChave.Cpf, "52998224725", "529.982.247-25")]
    [InlineData(TipoChave.Cpf, "529.982.247-25", "529 982 247 25")]
    [InlineData(TipoChave.Email, "recebedor@exemplo.com", "Recebedor@Exemplo.COM")]
    public void Pagar_ReenviadoComOutraGrafiaDaMesmaChave_DevolveARespostaOriginalSemConflito(
        TipoChave tipo,
        string grafiaDaPrimeira,
        string grafiaDoReenvio)
    {
        // Guarda contra uma linha degenerada da tabela: se as duas grafias fossem o mesmo texto,
        // o teste passaria sem exercitar normalizacao nenhuma.
        Assert.False(string.Equals(grafiaDaPrimeira, grafiaDoReenvio, StringComparison.Ordinal));

        MotorMontado motor = MontarMotor(chaveDoRecebedor: ChavePix.Criar(tipo, grafiaDaPrimeira));
        EndToEndId e2e = E2eDe(1);

        ComandoDePagamento primeiraPostagem = new(
            e2e,
            ContaDoPagador,
            ChavePix.Criar(tipo, grafiaDaPrimeira),
            Valor.DeCentavos(Pagamento));

        RespostaDePagamento primeira = motor.Pagador.Pagar(primeiraPostagem);
        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Estado);

        ComandoDePagamento reenvioComOutraGrafia = new(
            e2e,
            ContaDoPagador,
            ChavePix.Criar(tipo, grafiaDoReenvio),
            Valor.DeCentavos(Pagamento));

        // Nao lanca: as duas grafias colapsam na mesma chave canonica, entao a impressao e a mesma.
        RespostaDePagamento reenvio = ExigirReenvioSemEfeito(motor, reenvioComOutraGrafia, primeira);

        Assert.Equal(EstadoTransacao.EnviadaSpi, reenvio.Estado);
    }

    /// <summary>
    /// A comparacao de E2E e ordinal e sensivel a caixa: dois identificadores que diferem apenas
    /// pela caixa do sufixo sao duas transacoes, jamais um reenvio.
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Pagar_ComDoisE2eQueDiferemApenasPelaCaixaDoSufixo_TrataOsComoTransacoesDistintas()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);

        EndToEndId maiusculo = EndToEndId.Compor(IspbPagador, RelogioFake.Epoca, "0000000000A");
        EndToEndId minusculo = EndToEndId.Compor(IspbPagador, RelogioFake.Epoca, "0000000000a");

        Assert.NotEqual(maiusculo, minusculo);
        Assert.Equal(maiusculo.Texto.ToUpperInvariant(), minusculo.Texto.ToUpperInvariant());

        RespostaDePagamento primeira = motor.Pagador.Pagar(
            new ComandoDePagamento(maiusculo, ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento)));

        RespostaDePagamento segunda = motor.Pagador.Pagar(
            new ComandoDePagamento(minusculo, ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento)));

        Assert.Equal(EstadoTransacao.EnviadaSpi, primeira.Estado);
        Assert.Equal(EstadoTransacao.EnviadaSpi, segunda.Estado);
        Assert.NotEqual(primeira.E2E, segunda.E2E);

        // Duas reivindicacoes, duas respostas congeladas, dois debitos, duas ordens a caminho.
        Assert.Equal(2, motor.Pagador.Idempotencia.Quantidade);
        Assert.Equal(2, motor.Pagador.Idempotencia.Log().Count);
        Assert.True(motor.Pagador.Ledger.HouveDebito(maiusculo));
        Assert.True(motor.Pagador.Ledger.HouveDebito(minusculo));
        Assert.Equal(2, motor.Barramento.Pendentes);
        Assert.Equal(Fundo - (2 * Pagamento), motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos);
    }

    // ---------------------------------------------------------------------------------------
    // O log do registro
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// O log e append-only e guarda a ordem de producao, como o do ledger.
    /// <para>
    /// E dele que o gate 8 reconstroi as respostas: se a ordem nao for a de producao, a
    /// reconstrucao nao consegue dizer qual resposta veio antes de qual, e uma entrada sobrescrita
    /// por reenvio apagaria o instante em que a resposta foi de fato dada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void Log_DoRegistroDeIdempotencia_GuardaAOrdemDeProducaoEReenvioNaoAcrescentaEntrada()
    {
        RelogioFake relogio = new();
        MotorMontado motor = MontarMotor(relogio);

        ComandoDePagamento[] comandos =
        [
            new(E2eDe(1), ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento)),
            new(E2eDe(2), ContaDoPagador, ChaveOrfa, Valor.DeCentavos(Pagamento)),
            new(E2eDe(3), ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Pagamento)),
            new(E2eDe(4), ContaDoPagador, ChaveDoRecebedor, Valor.DeCentavos(Fundo)),
        ];

        List<RespostaDePagamento> respostas = [];

        foreach (ComandoDePagamento comando in comandos)
        {
            respostas.Add(motor.Pagador.Pagar(comando));

            // Um segundo entre pedidos: o instante gravado em cada entrada tem de ser o da producao,
            // e nao o da leitura. Sem avancar o relogio, quatro entradas identicas no tempo nao
            // distinguiriam "guardou o instante certo" de "guardou qualquer instante".
            relogio.AvancarSegundos(1);
        }

        // Aceito, rejeitado, aceito, rejeitado: a ordem do log nao pode ser a de nenhum agrupamento
        // por resultado.
        Assert.Equal(EstadoTransacao.EnviadaSpi, respostas[0].Estado);
        Assert.Equal(MotivoRejeicaoLocal.ChaveNaoEncontrada, respostas[1].Motivo);
        Assert.Equal(EstadoTransacao.EnviadaSpi, respostas[2].Estado);
        Assert.Equal(MotivoRejeicaoLocal.SaldoInsuficiente, respostas[3].Motivo);

        ExigirLogEsperado();

        // Os reenvios vem na ordem inversa e com o relogio bem adiante: nem a ordem nem os instantes
        // gravados podem se mexer por causa deles.
        relogio.AvancarSegundos(3600);

        for (int i = comandos.Length - 1; i >= 0; i--)
        {
            Assert.Equal(respostas[i], motor.Pagador.Pagar(comandos[i]));
        }

        Assert.Equal(4, motor.Pagador.Idempotencia.Quantidade);
        ExigirLogEsperado();

        void ExigirLogEsperado() => Assert.Collection(
            motor.Pagador.Idempotencia.Log(),
            entrada => ExigirEntrada(entrada, comandos[0].E2E, EstadoTransacao.EnviadaSpi, null, Epoca(0)),
            entrada => ExigirEntrada(
                entrada,
                comandos[1].E2E,
                EstadoTransacao.Rejeitada,
                MotivoRejeicaoLocal.ChaveNaoEncontrada,
                Epoca(1)),
            entrada => ExigirEntrada(entrada, comandos[2].E2E, EstadoTransacao.EnviadaSpi, null, Epoca(2)),
            entrada => ExigirEntrada(
                entrada,
                comandos[3].E2E,
                EstadoTransacao.Rejeitada,
                MotivoRejeicaoLocal.SaldoInsuficiente,
                Epoca(3)));
    }

    // ---------------------------------------------------------------------------------------
    // A outra camada: dedup por E2E da mensagem, no SPI
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Os dois indices sao independentes, e e assim que tem de ser.
    /// <para>
    /// O do PSP deduplica <b>pedido do cliente</b>: um POST reenviado devolve a resposta congelada.
    /// O do SPI deduplica <b>mensagem interna</b>: um <c>pacs.008</c> reentregue devolve o mesmo
    /// <c>pacs.002</c>, sem segunda liquidacao. Uma reentrega de mensagem nao e um pedido novo e nao
    /// pode mexer no registro do PSP; um POST reenviado nao chega a produzir mensagem.
    /// </para>
    /// <para>
    /// E por serem independentes que o <c>pacs.004</c> do gate 6 — devolucao, com E2E proprio —
    /// nunca vai colidir com o <c>pacs.008</c> que ele referencia: sao chaves de indices diferentes,
    /// e nao dois usos do mesmo indice.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void ReceberPacs008_ComAMesmaOrdemEntregueDuasVezes_LiquidaUmaVezSoSemMexerNoRegistroDoPsp()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
        ContaId contaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor);
        EndToEndId e2e = E2eDe(1);

        motor.Pagador.Pagar(new ComandoDePagamento(
            e2e,
            ContaDoPagador,
            ChaveDoRecebedor,
            Valor.DeCentavos(Pagamento)));

        Assert.Equal(3, motor.Barramento.Drenar());

        Assert.True(motor.Spi.TryRespostaDe(e2e, out Pacs002? respostaDaPrimeiraEntrega));
        Assert.NotNull(respostaDaPrimeiraEntrega);
        Assert.Equal(StatusPacs002.Acsc, respostaDaPrimeiraEntrega.Status);

        int commitsDoSpi = motor.Spi.Consulta.Log().Count;
        int commitsDoRecebedor = motor.Recebedor.Ledger.Consulta.Log().Count;
        int commitsDoPagador = motor.Pagador.Ledger.Consulta.Log().Count;
        int entradasDoRegistro = motor.Pagador.Idempotencia.Log().Count;

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
        Assert.NotNull(transacao);
        int transicoesAntes = transacao.Historico.Count;

        // A mesma ordem, remontada campo a campo, reentregue pelo barramento. O SPI deduplica por
        // E2E da mensagem, entao a segunda entrega nao chega a tentar lancar nada.
        Pacs008 reentrega = new(
            e2e,
            IspbPagador,
            contaPagador,
            IspbRecebedor,
            contaRecebedor,
            Valor.DeCentavos(Pagamento),
            ChaveDoRecebedor);

        motor.Barramento.Enfileirar(new EnvelopePacs008(reentrega));

        // pacs.008 reentregue, e o mesmo pacs.002 de volta ao pagador e ao recebedor.
        Assert.Equal(3, motor.Barramento.Drenar());

        Assert.True(motor.Spi.TryRespostaDe(e2e, out Pacs002? respostaDaSegundaEntrega));
        Assert.NotNull(respostaDaSegundaEntrega);

        // Nao e uma resposta equivalente: e a MESMA, guardada no indice do SPI.
        Assert.Same(respostaDaPrimeiraEntrega, respostaDaSegundaEntrega);

        // Uma unica liquidacao, um unico credito, nenhum estorno, nenhuma divergencia.
        Assert.Equal(commitsDoSpi, motor.Spi.Consulta.Log().Count);
        Assert.Equal(commitsDoRecebedor, motor.Recebedor.Ledger.Consulta.Log().Count);
        Assert.Equal(commitsDoPagador, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(Fundo - Pagamento, motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos);
        Assert.Equal(Fundo + Pagamento, motor.Recebedor.Ledger.SaldoDoCliente(contaRecebedor).Centavos);
        Assert.False(motor.Pagador.Ledger.HouveEstorno(e2e));
        Assert.Empty(motor.Pagador.Divergencias);

        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);
        Assert.Equal(transicoesAntes, transacao.Historico.Count);

        // E o indice do PSP nao se mexeu: reentrega de mensagem nao e pedido do cliente.
        Assert.Equal(1, motor.Pagador.Idempotencia.Quantidade);
        Assert.Equal(entradasDoRegistro, motor.Pagador.Idempotencia.Log().Count);
    }

    // ---------------------------------------------------------------------------------------
    // A ultima linha de defesa: a chave de idempotencia do ledger
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Furando as duas camadas acima, o lancamento ainda e recusado.
    /// <para>
    /// E esta guarda, e nao a de API, que garante "zero lancamento novo" quando a reentrega vem por
    /// um caminho que ninguem previu — um reprocessamento manual, um replay de fila, a conciliacao
    /// do gate 7 refazendo um passo. A de API protege o cliente; esta protege o dinheiro, e por isso
    /// tem de valer mesmo quando o chamador esta errado.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "4")]
    public void DebitarCliente_ChamadoDuasVezesComOMesmoE2e_ERecusadoPeloLedgerSemSegundoLancamento()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador);
        EndToEndId e2e = E2eDe(1);

        int commitsAntes = motor.Pagador.Ledger.Consulta.Log().Count;

        motor.Pagador.Ledger.DebitarCliente(e2e, contaPagador, Valor.DeCentavos(Pagamento));

        ChaveIdempotenciaDuplicadaException excecao = Assert.Throws<ChaveIdempotenciaDuplicadaException>(
            () => motor.Pagador.Ledger.DebitarCliente(e2e, contaPagador, Valor.DeCentavos(Pagamento)));

        Assert.Equal($"{e2e}#DebitoPagador", excecao.Chave);

        // Nem com valor diferente: a chave e (transacao, etapa), e nao (transacao, etapa, valor).
        Assert.Throws<ChaveIdempotenciaDuplicadaException>(
            () => motor.Pagador.Ledger.DebitarCliente(e2e, contaPagador, Valor.DeCentavos(ValorDivergente)));

        Assert.Equal(commitsAntes + 1, motor.Pagador.Ledger.Consulta.Log().Count);
        Assert.Equal(Fundo - Pagamento, motor.Pagador.Ledger.SaldoDoCliente(contaPagador).Centavos);
        Assert.True(motor.Pagador.Ledger.HouveDebito(e2e));
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reenvia o comando e exige as quatro propriedades do invariante 5: mesma resposta campo a
    /// campo, ledger sem lancamento novo, registro sem entrada nova e barramento sem mensagem nova.
    /// </summary>
    private static RespostaDePagamento ExigirReenvioSemEfeito(
        MotorMontado motor,
        ComandoDePagamento comando,
        RespostaDePagamento primeira)
    {
        int commitsAntes = motor.Pagador.Ledger.Consulta.Log().Count;
        int entradasAntes = motor.Pagador.Idempotencia.Log().Count;
        int pendentesAntes = motor.Barramento.Pendentes;
        int entreguesAntes = motor.Barramento.TotalEntregue;

        RespostaDePagamento reenvio = motor.Pagador.Pagar(comando);

        // Campo a campo primeiro, porque e o que diz QUAL campo divergiu; a comparacao do registro
        // inteiro logo abaixo pega um campo que venha a ser acrescentado e que esta lista esqueca.
        Assert.Equal(primeira.E2E, reenvio.E2E);
        Assert.Equal(primeira.Estado, reenvio.Estado);
        Assert.Equal(primeira.Motivo, reenvio.Motivo);
        Assert.Equal(primeira, reenvio);

        // (b) o ledger do pagador nao cresceu.
        Assert.Equal(commitsAntes, motor.Pagador.Ledger.Consulta.Log().Count);

        // (c) o registro nao ganhou entrada, e o E2E continua contando uma vez so.
        Assert.Equal(1, motor.Pagador.Idempotencia.Quantidade);
        Assert.Equal(entradasAntes, motor.Pagador.Idempotencia.Log().Count);

        // (d) nenhuma mensagem nova, nem enfileirada nem entregue.
        Assert.Equal(pendentesAntes, motor.Barramento.Pendentes);
        Assert.Equal(entreguesAntes, motor.Barramento.TotalEntregue);

        // A entrada congelada continua sendo a que respondeu da primeira vez.
        Assert.True(motor.Pagador.Idempotencia.TryEntrada(comando.E2E, out EntradaDeIdempotencia? entrada));
        Assert.NotNull(entrada);
        Assert.Equal(primeira.Estado, entrada.EstadoRespondido);
        Assert.Equal(primeira.Motivo, entrada.MotivoRespondido);

        return reenvio;
    }

    private static void ExigirEntrada(
        EntradaDeIdempotencia entrada,
        EndToEndId e2e,
        EstadoTransacao estado,
        MotivoRejeicaoLocal? motivo,
        DateTimeOffset em)
    {
        Assert.Equal(e2e, entrada.E2E);
        Assert.Equal(estado, entrada.EstadoRespondido);
        Assert.Equal(motivo, entrada.MotivoRespondido);
        Assert.Equal(em, entrada.Em);
    }

    private static ComandoDePagamento ComandoQueProduz(
        MotorMontado motor,
        MotivoRejeicaoLocal motivo,
        EndToEndId e2e) => motivo switch
        {
            MotivoRejeicaoLocal.ChaveNaoEncontrada => new ComandoDePagamento(
                e2e,
                ContaDoPagador,
                ChaveOrfa,
                Valor.DeCentavos(Pagamento)),

            MotivoRejeicaoLocal.SaldoInsuficiente => new ComandoDePagamento(
                e2e,
                ContaDoPagador,
                ChaveDoRecebedor,
                Valor.DeCentavos(Fundo + 1)),

            MotivoRejeicaoLocal.ContaDeOrigemDesconhecida => new ComandoDePagamento(
                e2e,
                ContaInexistente,
                ChaveDoRecebedor,
                Valor.DeCentavos(Pagamento)),

            // Chave apontando para a propria conta de origem: o construtor do Pacs008 recusa, e a
            // recusa vira rejeicao local antes de qualquer debito.
            MotivoRejeicaoLocal.OrdemInvalida => new ComandoDePagamento(
                e2e,
                ContaDoPagador,
                VincularChaveNaPropriaContaDeOrigem(motor),
                Valor.DeCentavos(Pagamento)),

            _ => throw new ArgumentOutOfRangeException(
                nameof(motivo),
                motivo,
                "motivo sem cenario de rejeicao local montado"),
        };

    private static ComandoDePagamento Divergir(ComandoDePagamento original, ComponenteDaImpressao componente) =>
        componente switch
        {
            ComponenteDaImpressao.Valor => original with { Valor = Valor.DeCentavos(ValorDivergente) },
            ComponenteDaImpressao.ContaDeOrigem => original with { ContaDeOrigem = ContaAlternativa },
            ComponenteDaImpressao.ChaveDeDestino => original with { ChaveDestino = ChaveAlternativa },
            _ => throw new ArgumentOutOfRangeException(
                nameof(componente),
                componente,
                "componente de impressao desconhecido"),
        };

    private static ChavePix VincularChaveNaPropriaContaDeOrigem(MotorMontado motor)
    {
        ChavePix espelhada = ChavePix.Criar(TipoChave.Email, "eu@exemplo.com");
        motor.Dict.Vincular(espelhada, IspbPagador, ContaId.Cliente(motor.Pagador.Ledger.Id, ContaDoPagador));
        return espelhada;
    }

    private static MotorMontado MontarMotor(RelogioFake? relogio = null, ChavePix? chaveDoRecebedor = null)
    {
        MotorMontado motor = MotorMontado.Montar(
            relogio ?? new RelogioFake(),
            IspbPagador,
            IspbRecebedor,
            Valor.DeCentavos(Fundo),
            ContaDoPagador,
            ContaDoRecebedor);

        motor.Recebedor.RegistrarChave(
            chaveDoRecebedor ?? ChaveDoRecebedor,
            ContaId.Cliente(motor.Recebedor.Ledger.Id, ContaDoRecebedor));

        return motor;
    }

    /// <summary>
    /// Instante esperado, contado da epoca do relogio fake. Escrito com <c>TimeSpan</c> de inteiros
    /// porque <c>AddSeconds</c> e <c>TimeSpan.FromSeconds</c> passam por <c>double</c>.
    /// </summary>
    private static DateTimeOffset Epoca(int segundos) => RelogioFake.Epoca + new TimeSpan(0, 0, segundos);

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);
}
