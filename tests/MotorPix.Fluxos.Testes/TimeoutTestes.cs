using MotorPix.Composicao;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Gate 5, primeira metade: a aresta <c>ENVIADA_SPI --&gt; EXPIRADA</c> (estados:11).
/// <para>
/// Todo instante deste arquivo vem do <see cref="RelogioFake"/>. Nenhum teste usa
/// <c>Task.Delay</c> nem <c>Thread.Sleep</c>: o criterio de aceite pede "timeout deterministico com
/// IClock fake", e determinismo aqui significa que o tempo e um parametro do teste, nunca uma
/// espera. O limite tambem e escolhido pelo teste, e de proposito diferente do
/// <see cref="PoliticaDeTimeout.Padrao"/> — assim um motor que ignorasse a politica injetada e
/// usasse os 30 segundos de fabrica falharia em vez de passar por coincidencia.
/// </para>
/// </summary>
public sealed class TimeoutTestes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;

    /// <summary>Precisa ser <c>const</c> para caber em <c>[InlineData]</c>.</summary>
    private const int LimiteEmSegundos = 20;

    // TimeSpan.FromSeconds recebe double, que e simbolo banido: o limite se escreve pelo construtor.
    private static readonly TimeSpan Limite = new(0, 0, LimiteEmSegundos);
    private static readonly TimeSpan UmSegundo = new(0, 0, 1);
    private static readonly TimeSpan UmDia = new(1, 0, 0, 0);

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // ---------------------------------------------------------------------------------------
    // A fronteira, nos tres pontos
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "5")]
    // Um segundo antes do limite: nao venceu.
    [InlineData(LimiteEmSegundos - 1, 0, false)]
    // Exatamente no limite: JA venceu. A fronteira e fechada (decorrido >= Limite) por decisao
    // documentada em PoliticaDeTimeout.Venceu. Qual lado se escolhe e arbitrario; o que produz o
    // teste intermitente e escolher sem documentar — dai o proximo leitor assume o outro lado,
    // escreve o teste com o "obvio" dele, e o resultado passa a depender de quem leu por ultimo.
    [InlineData(LimiteEmSegundos, 1, true)]
    // Um segundo depois: continua vencido, e nao ha segunda fronteira escondida adiante.
    [InlineData(LimiteEmSegundos + 1, 1, true)]
    public void ExpirarVencidos_NosTresPontosDaFronteira_ExpiraSomenteDoLimiteEmDiante(
        int segundosDecorridos,
        int expiradasEsperadas,
        bool esperaExpirada)
    {
        Cenario cenario = Cenario.Montar(Limite);
        cenario.Pagar();

        // Sem drenar: o pacs.008 fica na fila e nenhum pacs.002 chega. E este o unico cenario que
        // produz EXPIRADA — a transacao esta em ENVIADA_SPI e o SPI nunca respondeu.
        Assert.Equal(EstadoTransacao.EnviadaSpi, cenario.TransacaoDe(cenario.E2E).Estado);

        cenario.Relogio.AvancarSegundos(segundosDecorridos);

        Assert.Equal(expiradasEsperadas, cenario.Motor.Pagador.ExpirarVencidos().Count);

        EstadoTransacao esperado = esperaExpirada ? EstadoTransacao.Expirada : EstadoTransacao.EnviadaSpi;
        Assert.Equal(esperado, cenario.TransacaoDe(cenario.E2E).Estado);
    }

    // ---------------------------------------------------------------------------------------
    // De onde a contagem parte
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void Venceu_TransacaoQueDemorouAChegarEmEnviadaSpi_ContaDoDespachoENaoDaCriacao()
    {
        // A API de pagamento le o IClock uma unica vez e usa o mesmo instante nas tres transicoes,
        // entao pelo motor inteiro CriadaEm e AtualizadaEm coincidem e a distincao ficaria invisivel.
        // Aqui a transacao e conduzida a mao justamente para separar os dois marcos: ela passa um
        // limite inteiro em RECEBIDA e outro em VALIDADA antes de chegar a ENVIADA_SPI.
        PoliticaDeTimeout politica = new(Limite);

        DateTimeOffset criacao = RelogioFake.Epoca;
        DateTimeOffset validacao = criacao + Limite;
        DateTimeOffset despacho = validacao + Limite;

        Transacao transacao = Transacao.Iniciar(
            E2eDe(1),
            ContaId.Cliente(LedgerId.Psp(IspbPagador), "0001"),
            ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com"),
            Valor.DeCentavos(Pagamento),
            criacao);

        transacao.Aplicar(TipoEvento.ValidacaoLocalOk, validacao);
        transacao.Aplicar(TipoEvento.DebitoLancadoEPacs008Despachado, despacho);

        Assert.Equal(criacao, transacao.CriadaEm);
        Assert.Equal(despacho, transacao.AtualizadaEm);

        // Um segundo antes de o prazo contado do DESPACHO fechar.
        DateTimeOffset agora = despacho + Limite - UmSegundo;

        // Contando da criacao, ja teriam passado quase tres limites — a transacao seria expirada
        // por ter demorado a ser validada, e nao por o SPI ter deixado de responder. O relogio do
        // timeout comeca a correr quando o pacs.008 sai, porque e a resposta a ELE que falta.
        Assert.True(politica.Venceu(transacao.CriadaEm, agora));
        Assert.False(politica.Venceu(transacao.AtualizadaEm, agora));

        // E no instante seguinte, pelo marco certo, vence.
        Assert.True(politica.Venceu(transacao.AtualizadaEm, agora + UmSegundo));
    }

    [Fact]
    [Trait("gate", "5")]
    public void ExpirarVencidos_ComE2eCujoInstanteEmbutidoEDeUmDiaAtras_NaoExpiraNada()
    {
        // O EndToEndId carrega yyyyMMddHHmm de quem o emitiu. Usar esse campo como marco poria o
        // vencimento nas maos de quem montou a mensagem — bastaria datar o E2E no passado para
        // nascer expirado, ou no futuro para nunca vencer —, e o invariante 9 diz que tempo so
        // entra pelo IClock injetado. Aqui o E2E declara um dia atras e a politica e de segundos:
        // se a contagem viesse dele, a varredura expiraria a transacao imediatamente.
        Cenario cenario = Cenario.Montar(Limite, RelogioFake.Epoca - UmDia);
        cenario.Pagar();

        Assert.Equal(RelogioFake.Epoca - UmDia, cenario.E2E.InstanteDeclarado);
        Assert.Empty(cenario.Motor.Pagador.ExpirarVencidos());

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.EnviadaSpi, transacao.Estado);
        Assert.Equal(RelogioFake.Epoca, transacao.AtualizadaEm);
    }

    // ---------------------------------------------------------------------------------------
    // Quem expira e a varredura, nao o relogio
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void Drenar_ComPrazoVencidoMasSemVarredura_LiquidaPelaArestaDeEnviadaSpi()
    {
        // A decisao mais contra-intuitiva do gate: o prazo vencido nao expira nada; a VARREDURA
        // expira. Nao ha scheduler — o diagrama de arquitetura nao tem esse no —, entao enquanto
        // ninguem chamar ExpirarVencidos a transacao continua em ENVIADA_SPI, por mais tempo que
        // tenha passado. Um pacs.002 ACSC que chegue nessa janela fecha pela aresta estados:9, que
        // e legitima. O determinismo do teste esta na ordem dos eventos, nao no relogio.
        Cenario cenario = Cenario.Montar(Limite);
        cenario.Pagar();

        cenario.Relogio.Avancar(Limite + Limite + Limite);

        Assert.Equal(EstadoTransacao.EnviadaSpi, cenario.TransacaoDe(cenario.E2E).Estado);

        // pacs.008 ao SPI, depois pacs.002 ACSC ao pagador e ao recebedor.
        Assert.Equal(3, cenario.Motor.Barramento.Drenar());

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        TransicaoAplicada ultima = transacao.Historico[^1];
        Assert.Equal(EstadoTransacao.EnviadaSpi, ultima.Origem);
        Assert.Equal(TipoEvento.Pacs002Acsc, ultima.Evento);
        Assert.Equal(EstadoTransacao.Liquidada, ultima.Destino);

        // E a varredura que chega atrasada nao tem mais o que fazer: LIQUIDADA nao e ENVIADA_SPI.
        Assert.Empty(cenario.Motor.Pagador.ExpirarVencidos());
        Assert.Equal(EstadoTransacao.Liquidada, cenario.TransacaoDe(cenario.E2E).Estado);
    }

    // ---------------------------------------------------------------------------------------
    // O alcance da varredura
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ExpirarVencidos_ComTransacoesEmVariosEstados_SoAlcancaAsQueEstaoEmEnviadaSpi()
    {
        // estados:11 sai de ENVIADA_SPI e de lugar nenhum mais. LIQUIDADA e REJEITADA ja fecharam,
        // e EXPIRADA so se move por consulta de status — expirar de novo seria inventar uma aresta
        // EXPIRADA -> EXPIRADA que o diagrama nao tem.
        Cenario cenario = Cenario.Montar(Limite);

        EndToEndId liquidada = E2eDe(1);
        EndToEndId rejeitada = E2eDe(2);
        EndToEndId expirada = E2eDe(3);
        EndToEndId aVencer = E2eDe(4);

        cenario.Pagar(liquidada, cenario.Chave);
        cenario.Motor.Barramento.Drenar();

        // Chave sem dono no DICT: recusa local, direto de RECEBIDA para REJEITADA, sem debito.
        cenario.Pagar(rejeitada, ChavePix.Criar(TipoChave.Email, "ninguem@exemplo.com"));

        cenario.Pagar(expirada, cenario.Chave);
        cenario.Relogio.Avancar(Limite);
        Assert.Single(cenario.Motor.Pagador.ExpirarVencidos());

        // Nasce depois da primeira varredura e vence exatamente na segunda.
        cenario.Pagar(aVencer, cenario.Chave);
        cenario.Relogio.Avancar(Limite);

        Assert.Single(cenario.Motor.Pagador.ExpirarVencidos());

        Assert.Equal(EstadoTransacao.Liquidada, cenario.TransacaoDe(liquidada).Estado);
        Assert.Equal(EstadoTransacao.Rejeitada, cenario.TransacaoDe(rejeitada).Estado);
        Assert.Equal(EstadoTransacao.Expirada, cenario.TransacaoDe(expirada).Estado);
        Assert.Equal(EstadoTransacao.Expirada, cenario.TransacaoDe(aVencer).Estado);

        // A que ja estava expirada nao ganhou uma segunda transicao de timeout.
        TransicaoAplicada ultima = cenario.TransacaoDe(expirada).Historico[^1];
        Assert.Equal(TipoEvento.TimeoutDetectado, ultima.Evento);
        Assert.Equal(RelogioFake.Epoca + Limite, ultima.Em);
    }

    [Fact]
    [Trait("gate", "5")]
    public void ExpirarVencidos_ChamadoDuasVezesSeguidas_NaoExpiraNadaNaSegunda()
    {
        // A varredura e uma porta explicita, chamada pelo host: chamar duas vezes tem de ser tao
        // seguro quanto chamar uma. Se a segunda contasse de novo, qualquer metrica de "quantas
        // expiraram" viraria funcao de quantas vezes o host varreu.
        Cenario cenario = Cenario.Montar(Limite);
        cenario.Pagar();
        cenario.Relogio.Avancar(Limite);

        Assert.Single(cenario.Motor.Pagador.ExpirarVencidos());

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);
        int historicoDepoisDaPrimeira = transacao.Historico.Count;

        Assert.Empty(cenario.Motor.Pagador.ExpirarVencidos());

        Assert.Equal(EstadoTransacao.Expirada, cenario.TransacaoDe(cenario.E2E).Estado);
        Assert.Equal(historicoDepoisDaPrimeira, cenario.TransacaoDe(cenario.E2E).Historico.Count);
    }

    // ---------------------------------------------------------------------------------------
    // Expirar nao e rejeitar
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ExpirarVencidos_DepoisDeExpirar_NaoEstornaNemLancaNadaNoLedger()
    {
        // Invariante 6 ao pe da letra: timeout nao e falha. O pacs.008 pode ter chegado ao SPI e
        // liquidado; o que faltou foi a RESPOSTA. Estornar aqui devolveria ao cliente um dinheiro
        // que talvez ja esteja na conta do recebedor — que e precisamente "confirmar ou estornar
        // por suposicao". Quem decide e a consulta de status, e ate la o debito fica de pe.
        Cenario cenario = Cenario.Montar(Limite);
        cenario.Pagar();

        int commitsAntes = cenario.Motor.Pagador.Ledger.Consulta.Log().Count;

        cenario.Relogio.Avancar(Limite);
        Assert.Single(cenario.Motor.Pagador.ExpirarVencidos());
        Assert.Equal(EstadoTransacao.Expirada, cenario.TransacaoDe(cenario.E2E).Estado);

        Assert.True(cenario.Motor.Pagador.Ledger.HouveDebito(cenario.E2E));
        Assert.False(cenario.Motor.Pagador.Ledger.HouveEstorno(cenario.E2E));

        // Nenhum lancamento novo: a transicao para EXPIRADA e um fato da maquina de estados, e o
        // ledger nao registra fatos que nao movem dinheiro.
        Assert.Equal(commitsAntes, cenario.Motor.Pagador.Ledger.Consulta.Log().Count);

        Assert.Equal(Fundo - Pagamento, cenario.SaldoDoClientePagador);
        Assert.Equal(Fundo - Pagamento, cenario.SaldoDoEspelhoPagador);
    }

    [Fact]
    [Trait("gate", "5")]
    public void Historico_DaTransacaoExpirada_RegistraATransicaoComOInstanteDoRelogioFake()
    {
        Cenario cenario = Cenario.Montar(Limite);
        cenario.Pagar();

        cenario.Relogio.Avancar(Limite);
        cenario.Motor.Pagador.ExpirarVencidos();

        VistaDaTransacao transacao = cenario.TransacaoDe(cenario.E2E);

        Assert.Collection(
            transacao.Historico,
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
                // estados:11 — "timeout via IClock, sem pacs.002".
                Assert.Equal(EstadoTransacao.EnviadaSpi, t.Origem);
                Assert.Equal(TipoEvento.TimeoutDetectado, t.Evento);
                Assert.Equal(EstadoTransacao.Expirada, t.Destino);

                // O instante e o do relogio fake, e nao o de parede: e isto que torna o valor
                // esperado escrevivel a mao.
                Assert.Equal(RelogioFake.Epoca + Limite, t.Em);
            });

        Assert.Equal(RelogioFake.Epoca + Limite, transacao.AtualizadaEm);
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);

    /// <summary>Motor montado com politica de timeout escolhida pelo teste.</summary>
    private sealed class Cenario
    {
        private const string NumeroDaContaPagador = "0001";
        private const string NumeroDaContaRecebedor = "0002";

        private Cenario(RelogioFake relogio, MotorMontado motor, ChavePix chave, EndToEndId e2e)
        {
            Relogio = relogio;
            Motor = motor;
            Chave = chave;
            E2E = e2e;
            ContaPagador = ContaId.Cliente(motor.Pagador.Ledger.Id, NumeroDaContaPagador);
        }

        public RelogioFake Relogio { get; }

        public MotorMontado Motor { get; }

        public ChavePix Chave { get; }

        public EndToEndId E2E { get; }

        public ContaId ContaPagador { get; }

        public long SaldoDoClientePagador => Motor.Pagador.Ledger.SaldoDoCliente(ContaPagador).Centavos;

        public long SaldoDoEspelhoPagador => Motor.Pagador.Ledger.SaldoDoEspelho().Centavos;

        /// <param name="limite">Prazo da politica injetada.</param>
        /// <param name="instanteDoE2e">
        /// Instante que o emissor grava dentro do EndToEndId. Por padrao coincide com a epoca do
        /// relogio; um teste o move para o passado para provar que ele nao e marco de contagem.
        /// </param>
        public static Cenario Montar(TimeSpan limite, DateTimeOffset? instanteDoE2e = null)
        {
            RelogioFake relogio = new();

            MotorMontado motor = MotorMontado.Montar(
                relogio,
                IspbPagador,
                IspbRecebedor,
                Valor.DeCentavos(Fundo),
                NumeroDaContaPagador,
                NumeroDaContaRecebedor,
                new PoliticaDeTimeout(limite));

            ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
            motor.Recebedor.RegistrarChave(
                chave,
                ContaId.Cliente(motor.Recebedor.Ledger.Id, NumeroDaContaRecebedor));

            EndToEndId e2e = new GeradorE2eDeterministico(IspbPagador, instanteDoE2e ?? RelogioFake.Epoca)
                .EmSequencia(1);

            return new Cenario(relogio, motor, chave, e2e);
        }

        public RespostaDePagamento Pagar() => Pagar(E2E, Chave);

        public RespostaDePagamento Pagar(EndToEndId e2e, ChavePix chave) =>
            Motor.Pagador.Pagar(new ComandoDePagamento(
                e2e,
                NumeroDaContaPagador,
                chave,
                Valor.DeCentavos(Pagamento)));

        public VistaDaTransacao TransacaoDe(EndToEndId e2e)
        {
            Assert.True(Motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? transacao));
            Assert.NotNull(transacao);
            return transacao;
        }
    }
}
