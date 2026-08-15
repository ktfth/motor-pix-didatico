using MotorPix.Composicao;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Psp.Nucleo;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// O emissor de <c>EndToEndId</c> das transacoes que o proprio motor origina — hoje, so a devolucao.
/// <para>
/// O pagamento normal nao passa por aqui: o E2E dele vem de fora, com o cliente, pela seta 1. A
/// devolucao e diferente porque e transacao nova originada pelo PSP, e alguem tem de escolher o
/// identificador. Escolher significa ler duas fontes ambientais — o instante e o sufixo —, e as
/// duas entram <b>injetadas</b>: o instante pelo <see cref="IClock"/> do invariante 9, o sufixo
/// pela <see cref="IFonteAleatoria"/>.
/// </para>
/// <para>
/// Sem a segunda injecao a devolucao geraria um identificador diferente a cada execucao, o
/// contraexemplo encontrado por semente nao reproduziria, e o replay do gate 8 nao reconstruiria os
/// mesmos identificadores. E por isso que estes testes existem antes do gate 8, e nao depois.
/// </para>
/// </summary>
public sealed class GeradorDeE2eTestes
{
    private const string SufixoDeTeste = "SUFIXO00001";

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    /// <summary>Um minuto e meio depois da epoca: os segundos existem para serem descartados.</summary>
    private static readonly TimeSpan UmMinutoEMeio = new(0, 1, 30);

    // -------------------------------------------------------------------------------------------
    // Composicao
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Os tres componentes do identificador, conferidos um a um: quem origina, quando, e o sufixo.
    /// <para>
    /// Assertar so o texto inteiro deixaria passar a troca de dois componentes entre si — e a
    /// concatenacao continuaria com 32 caracteres e forma valida.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void Gerar_ComRelogioEFonteInjetados_ComponeOIdentificadorComIspbInstanteESufixo()
    {
        RelogioFake relogio = new();
        relogio.Avancar(UmMinutoEMeio);

        GeradorDeE2e gerador = new(relogio, new FonteDeSufixoFixo(SufixoDeTeste));

        EndToEndId e2e = gerador.Gerar(IspbPagador);

        // 1. O ISPB e o de quem ORIGINA, passado no argumento — nao o do destino nem o do relogio.
        Assert.Equal(IspbPagador, e2e.Ispb);

        // 2. O instante vem do IClock, truncado ao minuto: 12:01:30 vira 12:01.
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 1, 0, TimeSpan.Zero), e2e.InstanteDeclarado);

        // 3. O sufixo e exatamente o que a fonte devolveu, sem transformacao.
        Assert.Equal(SufixoDeTeste, e2e.Sufixo);

        // E o resultado e um EndToEndId de forma valida, porque foi montado pela fabrica que valida.
        Assert.Equal(EndToEndId.Comprimento, e2e.Texto.Length);
        Assert.Equal($"E{IspbPagador.Texto}202601011201{SufixoDeTeste}", e2e.Texto);
    }

    /// <summary>
    /// O mesmo gerador, no mesmo minuto, produz identificadores diferentes — e a diferenca esta
    /// toda no sufixo.
    /// <para>
    /// O resto do identificador tem precisao de <b>minuto</b>: prefixo, ISPB e carimbo
    /// <c>yyyyMMddHHmm</c> sao iguais para as duas chamadas. Sem sufixo distinto, duas devolucoes
    /// solicitadas no mesmo minuto nasceriam com o mesmo E2E — e o dedup do SPI trataria a segunda
    /// como reentrega da primeira, respondendo ACSC sem liquidar nada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void Gerar_DuasVezesNoMesmoMinuto_ProduzIdentificadoresQueDiferemApenasNoSufixo()
    {
        RelogioFake relogio = new();
        GeradorDeE2e gerador = new(relogio, new SufixoSequencial());

        EndToEndId primeiro = gerador.Gerar(IspbRecebedor);
        EndToEndId segundo = gerador.Gerar(IspbRecebedor);

        Assert.NotEqual(primeiro, segundo);
        Assert.NotEqual(primeiro.Sufixo, segundo.Sufixo);

        // Tudo o que nao e sufixo e identico: 1 char de prefixo + 8 de ISPB + 12 de instante.
        Assert.Equal(primeiro.Ispb, segundo.Ispb);
        Assert.Equal(primeiro.InstanteDeclarado, segundo.InstanteDeclarado);
        Assert.Equal(primeiro.Texto[..21], segundo.Texto[..21]);
    }

    // -------------------------------------------------------------------------------------------
    // Reprodutibilidade — o que o replay do gate 8 vai cobrar
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Duas execucoes independentes, com a mesma configuracao, produzem a MESMA sequencia de
    /// identificadores.
    /// <para>
    /// E esta propriedade, e nao a ausencia de <c>Guid.NewGuid</c>, que torna o replay do gate 8
    /// reproduzivel: reconstruir o ledger do zero exige que os identificadores gerados pelo proprio
    /// motor sejam funcao apenas das entradas injetadas. As duas instancias sao criadas do zero de
    /// proposito — reaproveitar uma so provaria que ela e deterministica consigo mesma.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void Gerar_ComMesmaFonteEMesmoRelogio_ReproduzASequenciaEmExecucoesIndependentes()
    {
        IReadOnlyList<string> primeiraExecucao = Executar();
        IReadOnlyList<string> segundaExecucao = Executar();

        Assert.Equal(primeiraExecucao.Count, segundaExecucao.Count);

        for (int i = 0; i < primeiraExecucao.Count; i++)
        {
            Assert.Equal(primeiraExecucao[i], segundaExecucao[i]);
        }

        // E a sequencia nao e trivialmente constante: os identificadores diferem entre si.
        Assert.Equal(primeiraExecucao.Count, new HashSet<string>(primeiraExecucao, StringComparer.Ordinal).Count);

        static IReadOnlyList<string> Executar()
        {
            RelogioFake relogio = new();
            GeradorDeE2e gerador = new(relogio, new SufixoSequencial());
            List<string> identificadores = [];

            for (int i = 0; i < 8; i++)
            {
                identificadores.Add(gerador.Gerar(IspbRecebedor).Texto);

                // O relogio tambem anda, para que a reproducao cubra o componente de instante e nao
                // apenas o sufixo.
                relogio.Avancar(new TimeSpan(0, 0, 45));
            }

            return identificadores;
        }
    }

    /// <summary>
    /// Por que a fonte padrao <b>nao</b> e aleatoria.
    /// <para>
    /// <c>System.Random</c> esta banido por compilacao. A razao nao e estilo: sem semente ele
    /// destroi o replay, e com semente o replay fica <em>ilusorio</em> — a sequencia passa a
    /// depender da implementacao interna do <c>Random</c>, que a plataforma pode mudar entre
    /// versoes, entao o contraexemplo guardado hoje reproduz outra coisa amanha.
    /// </para>
    /// <para>
    /// O que se perde e menos do que parece. O unico servico que a aleatoriedade prestaria e evitar
    /// colisao, e o ISPB de quem origina ja entra no identificador: dois participantes com
    /// exatamente a mesma sequencia de sufixos, no mesmo minuto, ainda produzem E2E diferentes.
    /// Dentro de um participante, um contador nao repete por construcao — o que uma fonte aleatoria
    /// so garante em probabilidade.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void FontePadrao_EhUmContadorENaoUmSorteio_ESemRiscoDeColisaoEntreParticipantes()
    {
        // Duas instancias recem-criadas percorrem a MESMA sequencia. Com um sorteio isso seria
        // impossivel, e e exatamente esta igualdade que o replay consome.
        SufixoSequencial primeira = new();
        SufixoSequencial segunda = new();

        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(primeira.SufixoDeE2e(), segunda.SufixoDeE2e());
        }

        // Mesma sequencia de sufixos em participantes diferentes nao colide, porque o ISPB de quem
        // origina faz parte do identificador.
        RelogioFake relogio = new();
        EndToEndId doPagador = new GeradorDeE2e(relogio, new SufixoSequencial()).Gerar(IspbPagador);
        EndToEndId doRecebedor = new GeradorDeE2e(relogio, new SufixoSequencial()).Gerar(IspbRecebedor);

        Assert.Equal(doPagador.Sufixo, doRecebedor.Sufixo);
        Assert.Equal(doPagador.InstanteDeclarado, doRecebedor.InstanteDeclarado);
        Assert.Equal(IspbPagador, doPagador.Ispb);
        Assert.Equal(IspbRecebedor, doRecebedor.Ispb);
        Assert.NotEqual(doPagador, doRecebedor);
    }

    // -------------------------------------------------------------------------------------------
    // A fonte padrao
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// O contrato do sufixo: 11 caracteres, alfanumericos ASCII maiusculos ou digitos, sem repetir
    /// dentro da mesma instancia.
    /// <para>
    /// As tres condicoes vem do <c>EndToEndId</c>, que valida forma e comparacao ordinal
    /// case-sensitive: um sufixo minusculo passaria na validacao de forma, mas dois E2E que
    /// diferissem so por caixa seriam transacoes distintas — nunca reenvio.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void SufixoDeE2e_DaFontePadrao_TemOnzeAlfanumericosMaiusculosENuncaRepeteNaInstancia()
    {
        SufixoSequencial fonte = new();
        HashSet<string> vistos = new(StringComparer.Ordinal);

        for (int i = 0; i < 100; i++)
        {
            string sufixo = fonte.SufixoDeE2e();

            Assert.True(sufixo.Length == 11, $"sufixo '{sufixo}' tem {sufixo.Length} caracteres, esperado 11");

            foreach (char caractere in sufixo)
            {
                Assert.True(
                    EhAlfanumericoMaiusculo(caractere),
                    $"sufixo '{sufixo}' tem o caractere '{caractere}', que nao e digito nem letra ASCII maiuscula");
            }

            Assert.True(vistos.Add(sufixo), $"sufixo repetido dentro da mesma instancia: {sufixo}");

            // E o sufixo e aceito por quem vai consumi-lo: a fabrica valida forma e nao reclama.
            Assert.Equal(sufixo, EndToEndId.Compor(IspbRecebedor, RelogioFake.Epoca, sufixo).Sufixo);
        }

        Assert.True(vistos.Count == 100, $"cem chamadas produziram apenas {vistos.Count} sufixos distintos");
    }

    // -------------------------------------------------------------------------------------------
    // Guardas
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Argumento nulo e defeito do chamador, nao recusa de negocio: <see cref="ArgumentNullException"/>
    /// nomeando o parametro, e nao uma excecao de dominio.
    /// <para>
    /// As duas dependencias sao conferidas no construtor, e nao no uso. Um gerador construido com
    /// relogio nulo so estouraria na primeira devolucao — dentro do lock do PSP, depois de a
    /// solicitacao ja ter sido aceita.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "6")]
    public void Construtor_EGerar_ComArgumentoNulo_LancamArgumentNullExceptionNomeandoOParametro()
    {
        ArgumentNullException semRelogio = Assert.Throws<ArgumentNullException>(
            () => new GeradorDeE2e(null!, new SufixoSequencial()));
        Assert.Equal("relogio", semRelogio.ParamName);

        ArgumentNullException semFonte = Assert.Throws<ArgumentNullException>(
            () => new GeradorDeE2e(new RelogioFake(), null!));
        Assert.Equal("aleatoria", semFonte.ParamName);

        GeradorDeE2e gerador = new(new RelogioFake(), new SufixoSequencial());

        ArgumentNullException semIspb = Assert.Throws<ArgumentNullException>(() => gerador.Gerar(null!));
        Assert.Equal("ispbDoOriginador", semIspb.ParamName);
    }

    // -------------------------------------------------------------------------------------------
    // Apoio
    // -------------------------------------------------------------------------------------------

    private static bool EhAlfanumericoMaiusculo(char caractere) =>
        caractere is (>= '0' and <= '9') or (>= 'A' and <= 'Z');

    /// <summary>
    /// Fonte que devolve sempre o mesmo sufixo. Serve para isolar o componente de sufixo na
    /// assercao de composicao — e para nada mais: repetir sufixo e exatamente o que a fonte de
    /// producao nao pode fazer.
    /// </summary>
    private sealed class FonteDeSufixoFixo(string sufixo) : IFonteAleatoria
    {
        private readonly string _sufixo = sufixo;

        public string SufixoDeE2e() => _sufixo;
    }
}
