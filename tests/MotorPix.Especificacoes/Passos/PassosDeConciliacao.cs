using MotorPix.Conciliacao;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Especificacoes.Suporte;
using MotorPix.Mensagens;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// Os passos da conciliacao: rodar a apuracao, fechar o que ela apurou, e afirmar sobre o
/// relatorio.
/// <para>
/// Nenhuma assercao daqui recalcula o que o motor deveria ter apurado. Os valores chegam escritos
/// no texto do cenario e passam por <see cref="Dinheiro"/>; comparar a conciliacao com uma
/// reimplementacao da conciliacao provaria so que as duas concordam.
/// </para>
/// <para>
/// Os dois passos que montam divergencia — a ordem entregue na mao do SPI e o credito sem lastro —
/// existem porque nao ha como escrever "a mensagem se perdeu" reusando os passos do caminho feliz.
/// Os dois evitam mock: a ordem e a mesma que o pagador despachou, e o credito sem lastro e
/// exatamente o que acontece quando um pacs.002 descreve uma liquidacao que o ledger do SPI nunca
/// gravou.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDeConciliacao
{
    private readonly ContextoDoMotor _contexto;

    private Conciliador? _conciliador;
    private int _consultasEmitidas;

    private string? _retratoPagador;
    private string? _retratoRecebedor;
    private string? _retratoSpi;

    public PassosDeConciliacao(ContextoDoMotor contexto) => _contexto = contexto;

    /// <summary>
    /// Um conciliador so por cenario. Ele nao guarda estado entre passadas — o relatorio e sempre
    /// reconstruido do log —, mas reusar a instancia deixa claro no codigo que "a conciliacao" do
    /// texto e uma coisa so, e nao uma nova a cada linha.
    /// </summary>
    private Conciliador ConciliadorLigado => _conciliador ??= _contexto.Conciliador();

    private RelatorioDeConciliacao Relatorio =>
        _contexto.UltimoRelatorio
        ?? throw new InvalidOperationException(
            "nenhuma conciliação rodou neste cenário: falta um passo 'a conciliação roda'");

    // -----------------------------------------------------------------------------------------
    // Acoes
    // -----------------------------------------------------------------------------------------

    [When(@"a conciliação roda")]
    public void QuandoConcilia() => _contexto.UltimoRelatorio = ConciliadorLigado.Conciliar();

    [When(@"a conciliação roda até estabilizar")]
    public void QuandoConciliaAteEstabilizar() =>
        _contexto.UltimoRelatorio = ConciliadorLigado.ConciliarAteEstabilizar();

    [When(@"a conciliação fecha o que apurou")]
    public void QuandoFecha() => _consultasEmitidas = ConciliadorLigado.Fechar(Relatorio);

    /// <summary>
    /// A ordem entregue na mao do SPI, com a resposta ficando na fila. A impressao coincide com a
    /// que o pagador despachou, entao o dedup do SPI reconhece o pacs.008 pendente como reentrega
    /// da mesma ordem, e nao como uma segunda.
    /// </summary>
    [When(@"o SPI recebe a ordem de ""([^""]*)"" na mão, sem que a resposta seja entregue")]
    public void QuandoSpiRecebeAOrdemNaMao(string valor) =>
        _contexto.Motor.Spi.ReceberPacs008(new Pacs008(
            _contexto.E2E,
            ContextoDoMotor.IspbPagador,
            _contexto.ContaPagador,
            ContextoDoMotor.IspbRecebedor,
            _contexto.ContaRecebedor,
            Valor.DeCentavos(Dinheiro.EmCentavos(valor)),
            _contexto.ChaveDoRecebedor));

    /// <summary>
    /// Um credito no recebedor que o SPI nunca movera: e a orfa deliberada. O E2E e novo e nao
    /// pertence a pagamento nenhum, que e o que "sem lastro" quer dizer.
    /// </summary>
    [When(@"um crédito de ""([^""]*)"" é aplicado no recebedor sem lastro no SPI")]
    public void QuandoCreditoSemLastro(string valor)
    {
        EndToEndId fantasma = _contexto.ProximoE2e("fantasma");

        _contexto.Motor.Recebedor.Receber(Pacs002.Aceito(
            fantasma,
            new OrgnlTxRef(
                ContextoDoMotor.IspbPagador,
                _contexto.ContaPagador,
                ContextoDoMotor.IspbRecebedor,
                _contexto.ContaRecebedor,
                Valor.DeCentavos(Dinheiro.EmCentavos(valor)))));
    }

    [When(@"os três ledgers são fotografados")]
    public void QuandoFotografaOsLedgers()
    {
        _retratoPagador = RetratoDoLedger(_contexto.Motor.Pagador.Ledger.Consulta);
        _retratoRecebedor = RetratoDoLedger(_contexto.Motor.Recebedor.Ledger.Consulta);
        _retratoSpi = RetratoDoLedger(_contexto.Motor.Spi.Consulta);
    }

    // -----------------------------------------------------------------------------------------
    // Assercoes sobre o relatorio
    // -----------------------------------------------------------------------------------------

    [Then(@"o relatório não acusa divergência")]
    public void EntaoSemDivergencia() =>
        Assert.True(
            Relatorio.Divergencias.Count == 0,
            $"esperava relatório limpo, e veio: {Resumo(Relatorio)}");

    [Then(@"o relatório acusa exatamente (\d+) divergências?")]
    public void EntaoQuantasDivergencias(int quantas) =>
        Assert.True(
            quantas == Relatorio.Divergencias.Count,
            $"esperava {quantas} divergência(s), veio {Relatorio.Divergencias.Count}: {Resumo(Relatorio)}");

    [Then(@"o relatório acusa uma divergência de ""([^""]*)"" no valor de ""([^""]*)"" atribuída ao (pagador|recebedor)")]
    public void EntaoDivergenciaDeClasse(string classe, string valor, string participante)
    {
        ClasseDeDivergencia alvo = Vocabulario.Enumerado<ClasseDeDivergencia>(classe);
        Ispb ispb = IspbDe(participante);

        List<DivergenciaPorE2E> encontradas =
            [.. Relatorio.Divergencias.Where(d => d.Classe == alvo && d.Participante.Equals(ispb))];

        DivergenciaPorE2E divergencia = Assert.Single(encontradas);

        Conferir($"divergência {classe} do {participante}", valor, divergencia.Valor.Centavos);

        // Divergencia explicada aponta para o pagamento do cenario. Se ela nao apontasse, seria
        // dinheiro em transito sem endereco — ou seja, orfa, e nao esta classe.
        Assert.Equal(_contexto.E2E, divergencia.E2E);
    }

    [Then(@"o relatório não acusa nenhuma órfã")]
    public void EntaoSemOrfa() =>
        Assert.True(
            Relatorio.Orfas.Count == 0,
            $"esperava nenhuma órfã, e veio: {Resumo(Relatorio)}");

    [Then(@"o relatório acusa uma órfã de ""([^""]*)"" no (pagador|recebedor)")]
    public void EntaoOrfaDeValor(string valor, string participante)
    {
        Ispb ispb = IspbDe(participante);

        List<DivergenciaPorE2E> orfas =
            [.. Relatorio.Orfas.Where(d => d.Participante.Equals(ispb))];

        DivergenciaPorE2E orfa = Assert.Single(orfas);

        Conferir($"órfã do {participante}", valor, orfa.Valor.Centavos);

        // Orfa e, por definicao, diferenca que NENHUM E2E explica. Um identificador de fachada aqui
        // seria mentir sobre a natureza do achado.
        Assert.Null(orfa.E2E);
    }

    [Then(@"a diferença apurada para o (pagador|recebedor) é ""([^""]*)""")]
    public void EntaoDiferencaDoParticipante(string participante, string esperado) =>
        Conferir(
            $"diferença entre conta PI e espelho do {participante}",
            esperado,
            Relatorio.DiferencaPorParticipante[IspbDe(participante)].Centavos);

    [Then(@"o relatório manda consultar o SPI sobre a transação")]
    public void EntaoMandaConsultar() => Assert.Contains(_contexto.E2E, Relatorio.AConsultar);

    [Then(@"o relatório não manda consultar o SPI sobre nada")]
    public void EntaoNaoMandaConsultar() => Assert.Empty(Relatorio.AConsultar);

    [Then(@"o relatório manda reprocessar o crédito da transação")]
    public void EntaoMandaCreditar() => Assert.Contains(_contexto.E2E, Relatorio.ACreditar);

    [Then(@"o relatório não manda reprocessar crédito nenhum")]
    public void EntaoNaoMandaCreditar() => Assert.Empty(Relatorio.ACreditar);

    [Then(@"o relatório lista a transação como expirada")]
    public void EntaoListaExpirada() => Assert.Contains(_contexto.E2E, Relatorio.Expiradas);

    [Then(@"o relatório não lista nenhuma expirada")]
    public void EntaoSemExpiradas() => Assert.Empty(Relatorio.Expiradas);

    [Then(@"o relatório está em quiescência")]
    public void EntaoEmQuiescencia() =>
        Assert.True(
            Relatorio.EmQuiescencia,
            $"esperava quiescência, e sobrou: {Resumo(Relatorio)}");

    [Then(@"o relatório não está em quiescência")]
    public void EntaoForaDeQuiescencia() =>
        Assert.False(
            Relatorio.EmQuiescencia,
            "esperava divergência em aberto, e o relatório veio em quiescência");

    [Then(@"a conciliação emitiu (\d+) consultas?")]
    public void EntaoConsultasEmitidas(int quantas) =>
        Assert.True(
            quantas == _consultasEmitidas,
            $"esperava {quantas} consulta(s) emitida(s), o fechamento emitiu {_consultasEmitidas}");

    // -----------------------------------------------------------------------------------------
    // Assercoes sobre os PSPs e os ledgers
    // -----------------------------------------------------------------------------------------

    [Then(@"nenhum dos dois PSPs tem transação em expirada")]
    public void EntaoNenhumPspComExpirada()
    {
        // Medido nos proprios PSPs, e nao apenas no relatorio: quem responde por "esta em EXPIRADA"
        // e o PSP, e o relatorio e so o que ele contou.
        Assert.Empty(_contexto.Motor.Pagador.Expiradas);
        Assert.Empty(_contexto.Motor.Recebedor.Expiradas);
    }

    [Then(@"os três ledgers estão idênticos à fotografia")]
    public void EntaoLedgersIdenticos()
    {
        if (_retratoPagador is null || _retratoRecebedor is null || _retratoSpi is null)
        {
            throw new InvalidOperationException(
                "nenhuma fotografia foi tirada neste cenário: falta o passo 'os três ledgers são fotografados'");
        }

        Assert.Equal(_retratoPagador, RetratoDoLedger(_contexto.Motor.Pagador.Ledger.Consulta));
        Assert.Equal(_retratoRecebedor, RetratoDoLedger(_contexto.Motor.Recebedor.Ledger.Consulta));
        Assert.Equal(_retratoSpi, RetratoDoLedger(_contexto.Motor.Spi.Consulta));
    }

    // -----------------------------------------------------------------------------------------
    // Apoio
    // -----------------------------------------------------------------------------------------

    private static Ispb IspbDe(string participante) =>
        string.Equals(participante, "pagador", StringComparison.Ordinal)
            ? ContextoDoMotor.IspbPagador
            : ContextoDoMotor.IspbRecebedor;

    private static void Conferir(string oQue, string esperado, long observado)
    {
        long alvo = Dinheiro.EmCentavos(esperado);

        Assert.True(
            alvo == observado,
            $"{oQue}: esperado {Dinheiro.Formatar(alvo)}, observado {Dinheiro.Formatar(observado)}");
    }

    /// <summary>
    /// O relatorio inteiro numa linha, para que a falha do cenario mostre o que apareceu em vez de
    /// dizer apenas que dois numeros diferem.
    /// </summary>
    private static string Resumo(RelatorioDeConciliacao relatorio)
    {
        IEnumerable<string> achados = relatorio.Divergencias
            .Select(d => $"{d.Classe} {Dinheiro.Formatar(d.Valor.Centavos)} ({d.Explicacao})")
            .Concat(relatorio.Expiradas.Select(e2e => $"EXPIRADA {e2e}"));

        string texto = string.Join(" | ", achados);

        return texto.Length == 0 ? "nada" : texto;
    }

    /// <summary>
    /// Todas as chaves de idempotencia gravadas num ledger, na ordem do log, ja concatenadas. O
    /// conjunto de chaves, e nao a contagem de commits: um lancamento indevido entraria com chave
    /// nova, e a contagem sozinha nao diria qual apareceu.
    /// </summary>
    private static string RetratoDoLedger(IConsultaLedger consulta)
    {
        List<string> chaves = [];

        foreach (Commit commit in consulta.Log())
        {
            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                chaves.Add(lancamento.Chave.Texto);
            }
        }

        return string.Join(" | ", chaves);
    }
}
