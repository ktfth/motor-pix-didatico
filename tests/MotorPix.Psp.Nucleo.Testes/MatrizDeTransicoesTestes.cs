using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Psp.Nucleo.Testes;

/// <summary>
/// A matriz exaustiva de <c>EstadoTransacao</c> x <c>TipoEvento</c>, celula a celula, mais o
/// confronto com o arquivo normativo <c>motor-pix-maquina-estados.mermaid</c>.
/// <para>
/// Sao 7 estados x 9 eventos = 63 celulas: 9 validas e 54 que tem de lancar. A aresta
/// <c>[*] --&gt; RECEBIDA</c> nao entra na conta porque nao e evento — nascer nao e algo que se
/// aplica a uma transacao que ja existe, e por isso <c>TipoEvento</c> nao tem valor para ela.
/// </para>
/// <para>
/// A tabela de arestas usada como oraculo aqui e <b>escrita a mao</b> a partir do diagrama, e a
/// tabela de producao tambem. Gerar as duas da mesma fonte provaria apenas que a fonte e igual a
/// si mesma; o que da valor a este teste e serem duas transcricoes independentes do mesmo desenho,
/// conferidas contra o desenho.
/// </para>
/// </summary>
public sealed class MatrizDeTransicoesTestes
{
    private const int QuantidadeDeEstados = 7;
    private const int QuantidadeDeEventos = 9;
    private const int TotalDeCelulas = QuantidadeDeEstados * QuantidadeDeEventos;
    private const int CelulasValidas = 9;
    private const int CelulasInvalidas = TotalDeCelulas - CelulasValidas;

    private const int ArestasDeEntradaNoDiagrama = 1;
    private const int ArestasDeSaidaNoDiagrama = 3;

    /// <summary>Nome logico do recurso embarcado, declarado no csproj desta suite.</summary>
    private const string NomeDoRecurso = "maquina-de-estados.mermaid";

    /// <summary>Como o pseudo-estado inicial e final aparece no Mermaid.</summary>
    private const string PseudoEstado = "[*]";

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");

    private static readonly ContaId ContaDeOrigem = ContaId.Cliente(LedgerId.Psp(IspbPagador), "0001");

    private static readonly ChavePix ChaveDeDestino = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

    private static readonly Valor ValorDoPagamento = Valor.DeCentavos(250_00);

    /// <summary>
    /// As 9 arestas do diagrama, transcritas a mao deste teste. E o oraculo da matriz: se ele
    /// fosse lido de <see cref="MaquinaDeEstados"/>, a matriz inteira viraria "a tabela concorda
    /// consigo mesma" e nao testaria nada.
    /// </summary>
    private static readonly IReadOnlyDictionary<(EstadoTransacao Origem, TipoEvento Evento), EstadoTransacao> ArestasEsperadas =
        new Dictionary<(EstadoTransacao Origem, TipoEvento Evento), EstadoTransacao>
        {
            // estados:4  RECEBIDA --> VALIDADA
            [(EstadoTransacao.Recebida, TipoEvento.ValidacaoLocalOk)] = EstadoTransacao.Validada,

            // estados:5  RECEBIDA --> REJEITADA
            [(EstadoTransacao.Recebida, TipoEvento.ValidacaoLocalFalhou)] = EstadoTransacao.Rejeitada,

            // estados:7  VALIDADA --> ENVIADA_SPI
            [(EstadoTransacao.Validada, TipoEvento.DebitoLancadoEPacs008Despachado)] = EstadoTransacao.EnviadaSpi,

            // estados:9  ENVIADA_SPI --> LIQUIDADA
            [(EstadoTransacao.EnviadaSpi, TipoEvento.Pacs002Acsc)] = EstadoTransacao.Liquidada,

            // estados:10 ENVIADA_SPI --> REJEITADA
            [(EstadoTransacao.EnviadaSpi, TipoEvento.Pacs002Rjct)] = EstadoTransacao.Rejeitada,

            // estados:11 ENVIADA_SPI --> EXPIRADA
            [(EstadoTransacao.EnviadaSpi, TipoEvento.TimeoutDetectado)] = EstadoTransacao.Expirada,

            // estados:13 EXPIRADA --> LIQUIDADA
            [(EstadoTransacao.Expirada, TipoEvento.ConsultaConfirmaLiquidacao)] = EstadoTransacao.Liquidada,

            // estados:14 EXPIRADA --> REJEITADA
            [(EstadoTransacao.Expirada, TipoEvento.ConsultaConfirmaRejeicao)] = EstadoTransacao.Rejeitada,

            // estados:16 LIQUIDADA --> DEVOLVIDA
            [(EstadoTransacao.Liquidada, TipoEvento.DevolucaoLiquidada)] = EstadoTransacao.Devolvida,
        };

    /// <summary>
    /// Caminho mais curto ate cada estado, escrito a mao.
    /// <para>
    /// Existe para que a celula de RECEBIDA nao dependa da celula de VALIDADA: montar cada estado
    /// percorrendo o fluxo inteiro faria uma unica quebra derrubar a matriz em cascata e esconder
    /// qual celula realmente falhou. Como <see cref="Transacao"/> so muda de estado por
    /// <see cref="Transacao.Aplicar"/>, a reidratacao possivel e esta — o prefixo minimo — e
    /// <see cref="EmEstado"/> confere que ela parou onde prometeu antes de qualquer assercao.
    /// </para>
    /// <para>
    /// EXPIRADA e DEVOLVIDA nao tem alternativa: o diagrama so as alcanca por ENVIADA_SPI e por
    /// LIQUIDADA, respectivamente.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<EstadoTransacao, TipoEvento[]> CaminhoMaisCurto =
        new Dictionary<EstadoTransacao, TipoEvento[]>
        {
            [EstadoTransacao.Recebida] = [],

            [EstadoTransacao.Validada] = [TipoEvento.ValidacaoLocalOk],

            [EstadoTransacao.Rejeitada] = [TipoEvento.ValidacaoLocalFalhou],

            [EstadoTransacao.EnviadaSpi] =
            [
                TipoEvento.ValidacaoLocalOk,
                TipoEvento.DebitoLancadoEPacs008Despachado,
            ],

            [EstadoTransacao.Liquidada] =
            [
                TipoEvento.ValidacaoLocalOk,
                TipoEvento.DebitoLancadoEPacs008Despachado,
                TipoEvento.Pacs002Acsc,
            ],

            [EstadoTransacao.Expirada] =
            [
                TipoEvento.ValidacaoLocalOk,
                TipoEvento.DebitoLancadoEPacs008Despachado,
                TipoEvento.TimeoutDetectado,
            ],

            [EstadoTransacao.Devolvida] =
            [
                TipoEvento.ValidacaoLocalOk,
                TipoEvento.DebitoLancadoEPacs008Despachado,
                TipoEvento.Pacs002Acsc,
                TipoEvento.DevolucaoLiquidada,
            ],
        };

    /// <summary>
    /// Traducao dos nomes do diagrama para o enum, escrita a mao. E o unico ponto de contato entre
    /// o texto do <c>.mermaid</c> e o codigo: derivar o nome do enum por transformacao de string
    /// (<c>ENVIADA_SPI</c> para <c>EnviadaSpi</c>) faria o teste aceitar qualquer renomeacao
    /// coordenada, que e justamente o que ele deveria denunciar.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, EstadoTransacao> EstadoPorNomeNoDiagrama =
        new Dictionary<string, EstadoTransacao>(StringComparer.Ordinal)
        {
            ["RECEBIDA"] = EstadoTransacao.Recebida,
            ["VALIDADA"] = EstadoTransacao.Validada,
            ["ENVIADA_SPI"] = EstadoTransacao.EnviadaSpi,
            ["LIQUIDADA"] = EstadoTransacao.Liquidada,
            ["REJEITADA"] = EstadoTransacao.Rejeitada,
            ["EXPIRADA"] = EstadoTransacao.Expirada,
            ["DEVOLVIDA"] = EstadoTransacao.Devolvida,
        };

    /// <summary>
    /// Linha de transicao do Mermaid: <c>ORIGEM --&gt; DESTINO</c>, com rotulo opcional depois de
    /// dois-pontos. O rotulo nao e comparado com nada: e prosa em portugues com acento, e amarrar
    /// o teste a ela transformaria correcao de texto em quebra de suite.
    /// </summary>
    private static readonly Regex PadraoDeAresta = new(
        @"^\s*(?<origem>\[\*\]|[A-Z_]+)\s*-->\s*(?<destino>\[\*\]|[A-Z_]+)\s*(?::(?<rotulo>.*))?$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// O teste central: percorre <b>todos</b> os pares (estado, evento) obtidos de
    /// <c>Enum.GetValues</c> — nunca de uma lista escrita a mao, que envelheceria em silencio
    /// quando um valor novo entrasse no enum.
    /// <para>
    /// Para a celula valida, confere destino, estado resultante, carimbo de tempo e a entrada nova
    /// do historico. Para a invalida, confere que lancou <b>e</b> que nada mudou: e isso que separa
    /// "recusou" de "recusou depois de sujar o agregado", diferenca que so aparece no proximo
    /// evento, quando o estado ja esta errado e ninguem sabe desde quando.
    /// </para>
    /// <para>
    /// As divergencias sao acumuladas e reportadas juntas: com 63 celulas, parar na primeira faria
    /// um ciclo de build por celula quebrada.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Matriz_TodosOsParesDeEstadoEEvento_ObedecemAoDiagramaNormativo()
    {
        EstadoTransacao[] estados = Enum.GetValues<EstadoTransacao>();
        TipoEvento[] eventos = Enum.GetValues<TipoEvento>();

        Assert.True(
            estados.Length == QuantidadeDeEstados,
            $"A matriz normativa e {QuantidadeDeEstados} x {QuantidadeDeEventos}, mas EstadoTransacao tem "
                + $"{estados.Length} valores (diferenca de {estados.Length - QuantidadeDeEstados}): "
                + $"[{string.Join(", ", estados)}].");

        Assert.True(
            eventos.Length == QuantidadeDeEventos,
            $"A matriz normativa e {QuantidadeDeEstados} x {QuantidadeDeEventos}, mas TipoEvento tem "
                + $"{eventos.Length} valores (diferenca de {eventos.Length - QuantidadeDeEventos}): "
                + $"[{string.Join(", ", eventos)}].");

        Assert.True(
            ArestasEsperadas.Count == CelulasValidas,
            $"O oraculo escrito a mao neste teste tem {ArestasEsperadas.Count} arestas e o diagrama tem "
                + $"{CelulasValidas} (diferenca de {ArestasEsperadas.Count - CelulasValidas}).");

        List<string> divergencias = [];
        int pares = 0;
        int validas = 0;
        int invalidas = 0;

        foreach (EstadoTransacao origem in estados)
        {
            foreach (TipoEvento evento in eventos)
            {
                pares++;

                if (ArestasEsperadas.TryGetValue((origem, evento), out EstadoTransacao destinoEsperado))
                {
                    validas++;
                    ConferirCelulaValida(origem, evento, destinoEsperado, divergencias);
                }
                else
                {
                    invalidas++;
                    ConferirCelulaInvalida(origem, evento, divergencias);
                }
            }
        }

        Assert.True(
            divergencias.Count == 0,
            $"Celulas divergentes do diagrama normativo ({divergencias.Count} de {pares}):"
                + Environment.NewLine
                + string.Join(Environment.NewLine, divergencias));

        Assert.True(
            pares == TotalDeCelulas,
            $"Percorri {pares} celulas e a matriz tem {TotalDeCelulas} "
                + $"({QuantidadeDeEstados} estados x {QuantidadeDeEventos} eventos): "
                + $"diferenca de {pares - TotalDeCelulas}.");

        Assert.True(
            validas == CelulasValidas,
            $"Contei {validas} celulas validas e o diagrama tem {CelulasValidas}: "
                + $"diferenca de {validas - CelulasValidas}.");

        Assert.True(
            invalidas == CelulasInvalidas,
            $"Contei {invalidas} celulas invalidas e a matriz tem {CelulasInvalidas} "
                + $"({TotalDeCelulas} - {CelulasValidas}): diferenca de {invalidas - CelulasInvalidas}.");

        Assert.True(
            MaquinaDeEstados.QuantidadeDeTransicoesValidas == CelulasValidas,
            $"MaquinaDeEstados declara {MaquinaDeEstados.QuantidadeDeTransicoesValidas} transicoes validas e o "
                + $"diagrama tem {CelulasValidas}: "
                + $"diferenca de {MaquinaDeEstados.QuantidadeDeTransicoesValidas - CelulasValidas}.");
    }

    /// <summary>
    /// <c>(LIQUIDADA, DevolucaoLiquidada)</c> e a unica celula valida partindo de um estado que o
    /// diagrama liga ao pseudo-estado final.
    /// <para>
    /// LIQUIDADA tem <c>LIQUIDADA --&gt; [*]</c> em <c>estados:18</c> e mesmo assim <b>nao</b> e
    /// terminal: <c>estados:16</c> continua saindo dela. Derivar terminalidade das setas para
    /// <c>[*]</c> — a leitura ingenua do desenho — faria o motor recusar o <c>pacs.004</c> e
    /// mataria o gate 6 inteiro. Por isso a propriedade e declarada, e este fato existe para
    /// travar a declaracao.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Liquidada_ComDevolucaoLiquidada_EhAUnicaCelulaValidaDeUmEstadoLigadoAoFinal()
    {
        RelogioFake relogio = new();
        Transacao transacao = EmEstado(EstadoTransacao.Liquidada, relogio);
        relogio.AvancarSegundos(1);

        Assert.Equal(EstadoTransacao.Devolvida, transacao.Aplicar(TipoEvento.DevolucaoLiquidada, relogio.Agora));

        Assert.False(
            MaquinaDeEstados.EhTerminal(EstadoTransacao.Liquidada),
            "LIQUIDADA aceita DevolucaoLiquidada (estados:16) e portanto nao e terminal, "
                + "por mais que estados:18 a ligue ao pseudo-estado final.");

        Assert.True(MaquinaDeEstados.EhTerminal(EstadoTransacao.Rejeitada), "REJEITADA nao tem aresta de saida.");
        Assert.True(MaquinaDeEstados.EhTerminal(EstadoTransacao.Devolvida), "DEVOLVIDA nao tem aresta de saida.");

        TipoEvento[] saindoDeLiquidada = EventosValidosEm(EstadoTransacao.Liquidada);
        Assert.True(
            saindoDeLiquidada.Length == 1 && saindoDeLiquidada[0] == TipoEvento.DevolucaoLiquidada,
            $"De LIQUIDADA so sai DevolucaoLiquidada; encontrei [{string.Join(", ", saindoDeLiquidada)}].");

        // O outro lado da mesma regra: de estado terminal nao sai evento nenhum.
        TipoEvento[] saindoDeRejeitada = EventosValidosEm(EstadoTransacao.Rejeitada);
        TipoEvento[] saindoDeDevolvida = EventosValidosEm(EstadoTransacao.Devolvida);

        Assert.True(
            saindoDeRejeitada.Length == 0,
            $"REJEITADA e terminal e ainda assim aceita [{string.Join(", ", saindoDeRejeitada)}].");

        Assert.True(
            saindoDeDevolvida.Length == 0,
            $"DEVOLVIDA e terminal e ainda assim aceita [{string.Join(", ", saindoDeDevolvida)}].");
    }

    /// <summary>
    /// Reentrega idempotente: o <c>pacs.002</c> <b>concorda</b> com o estado, so chegou duas vezes.
    /// <para>
    /// A maquina lanca assim mesmo, e isso e deliberado: ela responde "esta transicao existe no
    /// diagrama?", e a segunda copia da mesma mensagem nao e transicao nenhuma. Reconhecer a
    /// reentrega e trata-la como no-op e trabalho do adapter que recebe a mensagem (dedup por
    /// EndToEndId, gate 4) — se a maquina absorvesse o duplicado em silencio, o dedup deixaria de
    /// ter dono e a mesma tolerancia passaria a valer para a mensagem que <em>discorda</em>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void ReentregaDoMesmoPacs002_SobreLiquidadaESobreRejeitada_Lanca()
    {
        TransicaoInvalidaException emLiquidada = Recusa(EstadoTransacao.Liquidada, TipoEvento.Pacs002Acsc);
        TransicaoInvalidaException emRejeitada = Recusa(EstadoTransacao.Rejeitada, TipoEvento.Pacs002Rjct);

        Assert.Equal(nameof(EstadoTransacao.Liquidada), emLiquidada.EstadoAtual);
        Assert.Equal(nameof(TipoEvento.Pacs002Acsc), emLiquidada.Evento);

        Assert.Equal(nameof(EstadoTransacao.Rejeitada), emRejeitada.EstadoAtual);
        Assert.Equal(nameof(TipoEvento.Pacs002Rjct), emRejeitada.Evento);
    }

    /// <summary>
    /// Contradicao entre SPI e PSP: o <c>pacs.002</c> <b>nega</b> o estado ja gravado.
    /// <para>
    /// Lanca, como qualquer celula fora do diagrama — mas o sintoma aqui nao e de protocolo, e
    /// contabil. Um RJCT sobre LIQUIDADA diz que o SPI nao moveu o dinheiro que o PSP ja deu por
    /// liquidado; um ACSC sobre REJEITADA diz que o SPI moveu o dinheiro que o PSP ja estornou. Nos
    /// dois casos os ledgers estao divergentes <em>antes</em> desta excecao, e quem apagar o
    /// lancamento tratando a excecao pela mensagem estara consertando o termometro. O destino
    /// correto e a conciliacao do gate 7.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Pacs002QueContradizOEstadoJaGravado_EmLiquidadaEEmRejeitada_Lanca()
    {
        TransicaoInvalidaException rjctSobreLiquidada = Recusa(EstadoTransacao.Liquidada, TipoEvento.Pacs002Rjct);
        TransicaoInvalidaException acscSobreRejeitada = Recusa(EstadoTransacao.Rejeitada, TipoEvento.Pacs002Acsc);

        Assert.Equal(nameof(TipoEvento.Pacs002Rjct), rjctSobreLiquidada.Evento);
        Assert.Equal(nameof(TipoEvento.Pacs002Acsc), acscSobreRejeitada.Evento);
    }

    /// <summary>
    /// De EXPIRADA so se sai por consulta de status (invariante 6, <c>estados:28-34</c>): o
    /// <c>pacs.002</c> atrasado nao fecha a transacao por conta propria.
    /// <para>
    /// Timeout nao e falha, e a evidencia que vale e a resposta do SPI a uma pergunta explicita.
    /// Quando o <c>pacs.002</c> tardio chega, o adapter do gate 5 dispara a consulta e e a resposta
    /// dela que vira <c>ConsultaConfirmaLiquidacao</c> ou <c>ConsultaConfirmaRejeicao</c> — a
    /// mensagem atrasada e gatilho, nunca prova.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Expirada_ComPacs002AcscOuRjct_Lanca_PorqueSoSeSaiPorConsultaDeStatus()
    {
        TransicaoInvalidaException comAcsc = Recusa(EstadoTransacao.Expirada, TipoEvento.Pacs002Acsc);
        TransicaoInvalidaException comRjct = Recusa(EstadoTransacao.Expirada, TipoEvento.Pacs002Rjct);

        Assert.Equal(nameof(EstadoTransacao.Expirada), comAcsc.EstadoAtual);
        Assert.Equal(nameof(EstadoTransacao.Expirada), comRjct.EstadoAtual);

        TipoEvento[] saidas = EventosValidosEm(EstadoTransacao.Expirada);
        Assert.True(
            saidas.Length == 2
                && saidas.Contains(TipoEvento.ConsultaConfirmaLiquidacao)
                && saidas.Contains(TipoEvento.ConsultaConfirmaRejeicao),
            $"De EXPIRADA so saem as duas consultas de status; encontrei [{string.Join(", ", saidas)}].");
    }

    /// <summary>
    /// <c>(EXPIRADA, TimeoutDetectado)</c> lanca — e no gate 5 sera inalcancavel por construcao.
    /// <para>
    /// A varredura de vencidos so olha para quem esta em ENVIADA_SPI, entao expirar duas vezes nao
    /// tem como acontecer por aquele caminho. Ainda assim a celula e testada: "inalcancavel por
    /// construcao" e uma afirmacao sobre o codigo que chama, e a maquina nao pode depender da
    /// disciplina do chamador para se manter correta.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Expirada_ComSegundoTimeoutDetectado_Lanca()
    {
        TransicaoInvalidaException excecao = Recusa(EstadoTransacao.Expirada, TipoEvento.TimeoutDetectado);

        Assert.Equal(nameof(EstadoTransacao.Expirada), excecao.EstadoAtual);
        Assert.Equal(nameof(TipoEvento.TimeoutDetectado), excecao.Evento);
    }

    /// <summary>
    /// Timeout so existe depois que o <c>pacs.008</c> saiu: em RECEBIDA e em VALIDADA nao ha nada
    /// pendente no SPI para vencer.
    /// <para>
    /// O prazo do gate 5 conta a partir do instante em que ENVIADA_SPI foi gravado. Uma transacao
    /// parada em RECEBIDA ou VALIDADA e trabalho local que nao terminou — expira-la converteria um
    /// bug do proprio motor em "o SPI nao respondeu", que e diagnostico errado sobre outro sistema.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void RecebidaEValidada_ComTimeoutDetectado_Lancam()
    {
        TransicaoInvalidaException emRecebida = Recusa(EstadoTransacao.Recebida, TipoEvento.TimeoutDetectado);
        TransicaoInvalidaException emValidada = Recusa(EstadoTransacao.Validada, TipoEvento.TimeoutDetectado);

        Assert.Equal(nameof(EstadoTransacao.Recebida), emRecebida.EstadoAtual);
        Assert.Equal(nameof(EstadoTransacao.Validada), emValidada.EstadoAtual);
        Assert.Equal(nameof(TipoEvento.TimeoutDetectado), emRecebida.Evento);
        Assert.Equal(nameof(TipoEvento.TimeoutDetectado), emValidada.Evento);
    }

    /// <summary>
    /// Segunda devolucao sobre a mesma transacao lanca.
    /// <para>
    /// DEVOLVIDA e terminal. Sem esta celula fechada, duas devolucoes da mesma liquidacao seriam
    /// aceitas pela maquina e o valor original sairia do PSP duas vezes — a devolucao e transacao
    /// nova com lancamentos proprios, entao nada no ledger impediria o segundo credito.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Devolvida_ComSegundaDevolucaoLiquidada_Lanca()
    {
        TransicaoInvalidaException excecao = Recusa(EstadoTransacao.Devolvida, TipoEvento.DevolucaoLiquidada);

        Assert.Equal(nameof(EstadoTransacao.Devolvida), excecao.EstadoAtual);
        Assert.Equal(nameof(TipoEvento.DevolucaoLiquidada), excecao.Evento);
    }

    /// <summary>
    /// O recurso embarcado existe, tem conteudo e nomeia exatamente os 7 estados conhecidos.
    /// <para>
    /// Sem este fato, um <c>LogicalName</c> trocado no csproj faria os dois testes de confronto
    /// abaixo passarem sobre zero aresta — verdes por vacuidade, que e o pior resultado possivel
    /// para um teste que existe para pegar divergencia.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Diagrama_EmbarcadoComoRecurso_NomeiaExatamenteOsSeteEstadosConhecidos()
    {
        string texto = LerDiagrama();
        Assert.True(texto.Length > 0, $"O recurso '{NomeDoRecurso}' esta embarcado e vazio.");

        IReadOnlyList<ArestaDoDiagrama> arestas = ArestasDoDiagrama(texto);
        Assert.True(arestas.Count > 0, $"Nenhuma linha de transicao reconhecida em '{NomeDoRecurso}'.");

        HashSet<EstadoTransacao> citados = [];

        foreach (ArestaDoDiagrama aresta in arestas)
        {
            foreach (string nome in new[] { aresta.Origem, aresta.Destino })
            {
                if (!string.Equals(nome, PseudoEstado, StringComparison.Ordinal))
                {
                    citados.Add(Traduzir(nome, aresta.Linha));
                }
            }
        }

        EstadoTransacao[] ausentes = Enum.GetValues<EstadoTransacao>()
            .Where(estado => !citados.Contains(estado))
            .ToArray();

        Assert.True(
            ausentes.Length == 0,
            $"Estados do enum que o diagrama nao cita: [{string.Join(", ", ausentes)}].");

        Assert.True(
            citados.Count == QuantidadeDeEstados,
            $"O diagrama cita {citados.Count} estados nomeados e o enum tem {QuantidadeDeEstados}: "
                + $"diferenca de {citados.Count - QuantidadeDeEstados}.");
    }

    /// <summary>
    /// Contagem das arestas do desenho: 9 entre estados nomeados, 1 de entrada e 3 de saida para o
    /// pseudo-estado final.
    /// <para>
    /// A aresta de entrada e a razao de a matriz ser 7 x 9 e nao 7 x 10: ela existe no desenho e
    /// nao vira <c>TipoEvento</c>. As tres de saida sao contadas aqui para deixar registrado que
    /// <b>nao</b> definem terminalidade — LIQUIDADA esta entre elas e ainda aceita evento.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Diagrama_TemNoveArestasEntreEstadosUmaDeEntradaETresDeSaida()
    {
        IReadOnlyList<ArestaDoDiagrama> arestas = ArestasDoDiagrama(LerDiagrama());

        ArestaDoDiagrama[] entradas = arestas
            .Where(a => string.Equals(a.Origem, PseudoEstado, StringComparison.Ordinal))
            .ToArray();

        ArestaDoDiagrama[] saidas = arestas
            .Where(a => string.Equals(a.Destino, PseudoEstado, StringComparison.Ordinal)
                && !string.Equals(a.Origem, PseudoEstado, StringComparison.Ordinal))
            .ToArray();

        ArestaDoDiagrama[] entreEstados = arestas
            .Where(a => !string.Equals(a.Origem, PseudoEstado, StringComparison.Ordinal)
                && !string.Equals(a.Destino, PseudoEstado, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            entradas.Length + saidas.Length + entreEstados.Length == arestas.Count,
            $"Ha aresta que nao caiu em nenhum dos tres grupos: {arestas.Count} lidas, "
                + $"{entradas.Length} de entrada, {saidas.Length} de saida, {entreEstados.Length} entre estados.");

        Assert.True(
            entreEstados.Length == CelulasValidas,
            $"O diagrama tem {entreEstados.Length} arestas entre estados nomeados e a maquina declara "
                + $"{CelulasValidas} transicoes (diferenca de {entreEstados.Length - CelulasValidas}): "
                + $"[{string.Join(", ", entreEstados.Select(a => a.ToString()))}].");

        Assert.True(
            entradas.Length == ArestasDeEntradaNoDiagrama,
            $"Esperava {ArestasDeEntradaNoDiagrama} aresta de entrada e achei {entradas.Length}: "
                + $"[{string.Join(", ", entradas.Select(a => a.ToString()))}].");

        Assert.True(
            saidas.Length == ArestasDeSaidaNoDiagrama,
            $"Esperava {ArestasDeSaidaNoDiagrama} arestas de saida e achei {saidas.Length}: "
                + $"[{string.Join(", ", saidas.Select(a => a.ToString()))}].");

        Assert.Equal(EstadoTransacao.Recebida, Traduzir(entradas[0].Destino, entradas[0].Linha));

        EstadoTransacao[] ligadosAoFinal = saidas
            .Select(a => Traduzir(a.Origem, a.Linha))
            .OrderBy(estado => estado)
            .ToArray();

        EstadoTransacao[] esperadosNoFinal =
        [
            EstadoTransacao.Liquidada,
            EstadoTransacao.Rejeitada,
            EstadoTransacao.Devolvida,
        ];

        Assert.Equal(
            string.Join(", ", esperadosNoFinal.OrderBy(estado => estado)),
            string.Join(", ", ligadosAoFinal));

        // A divergencia deliberada: dos tres estados ligados ao final, so dois sao terminais.
        EstadoTransacao[] terminais = ligadosAoFinal.Where(MaquinaDeEstados.EhTerminal).ToArray();
        Assert.True(
            terminais.Length == 2 && !terminais.Contains(EstadoTransacao.Liquidada),
            "Terminalidade nao se deriva das setas para o pseudo-estado final: LIQUIDADA esta ligada a ele "
                + $"e aceita o pacs.004. Terminais encontrados: [{string.Join(", ", terminais)}].");
    }

    /// <summary>
    /// Confronto de conjuntos: os pares (origem, destino) do diagrama contra os que
    /// <see cref="MaquinaDeEstados.Arestas"/> declara.
    /// <para>
    /// So o par de estados e comparavel: o rotulo do Mermaid e prosa ("pacs.002 ACSC", "validacao
    /// local falha") e o nome do <c>TipoEvento</c> e identificador C#. A dimensao do evento e
    /// conferida contra o oraculo escrito a mao, no fato seguinte.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Diagrama_ConjuntoDeArestasEntreEstados_CoincideComOQueAMaquinaDeclara()
    {
        HashSet<(EstadoTransacao Origem, EstadoTransacao Destino)> noDiagrama = [];

        foreach (ArestaDoDiagrama aresta in ArestasDoDiagrama(LerDiagrama()))
        {
            if (string.Equals(aresta.Origem, PseudoEstado, StringComparison.Ordinal)
                || string.Equals(aresta.Destino, PseudoEstado, StringComparison.Ordinal))
            {
                continue;
            }

            noDiagrama.Add((Traduzir(aresta.Origem, aresta.Linha), Traduzir(aresta.Destino, aresta.Linha)));
        }

        HashSet<(EstadoTransacao Origem, EstadoTransacao Destino)> naMaquina =
            [.. MaquinaDeEstados.Arestas().Select(a => (a.Origem, a.Destino))];

        string[] soNaMaquina = naMaquina
            .Where(a => !noDiagrama.Contains(a))
            .Select(a => $"{a.Origem} -> {a.Destino}")
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        string[] soNoDiagrama = noDiagrama
            .Where(a => !naMaquina.Contains(a))
            .Select(a => $"{a.Origem} -> {a.Destino}")
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            soNaMaquina.Length == 0 && soNoDiagrama.Length == 0,
            "A tabela de producao divergiu do arquivo normativo."
                + Environment.NewLine
                + $"So na maquina: [{string.Join(", ", soNaMaquina)}]."
                + Environment.NewLine
                + $"So no diagrama: [{string.Join(", ", soNoDiagrama)}].");
    }

    /// <summary>
    /// A terceira transcricao do mesmo desenho: as arestas com evento, escritas a mao neste teste,
    /// contra as que <see cref="MaquinaDeEstados.Arestas"/> publica. E o unico teste que confere a
    /// dimensao do evento ponta a ponta, que o rotulo em prosa do Mermaid nao permite conferir.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Arestas_PublicadasPelaMaquina_CoincidemComOOraculoEscritoAMao()
    {
        IReadOnlyCollection<(EstadoTransacao Origem, TipoEvento Evento, EstadoTransacao Destino)> publicadas =
            MaquinaDeEstados.Arestas();

        Assert.True(
            publicadas.Count == CelulasValidas,
            $"A maquina publica {publicadas.Count} arestas e o diagrama tem {CelulasValidas}: "
                + $"diferenca de {publicadas.Count - CelulasValidas}.");

        HashSet<string> naMaquina = [.. publicadas.Select(a => $"{a.Origem} --{a.Evento}--> {a.Destino}")];
        HashSet<string> noOraculo = [.. ArestasEsperadas.Select(a => $"{a.Key.Origem} --{a.Key.Evento}--> {a.Value}")];

        string[] soNaMaquina = naMaquina.Where(a => !noOraculo.Contains(a)).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        string[] soNoOraculo = noOraculo.Where(a => !naMaquina.Contains(a)).OrderBy(a => a, StringComparer.Ordinal).ToArray();

        Assert.True(
            soNaMaquina.Length == 0 && soNoOraculo.Length == 0,
            "As arestas publicadas divergiram das transcritas a mao."
                + Environment.NewLine
                + $"So na maquina: [{string.Join(", ", soNaMaquina)}]."
                + Environment.NewLine
                + $"So no oraculo: [{string.Join(", ", soNoOraculo)}].");
    }

    private static void ConferirCelulaValida(
        EstadoTransacao origem,
        TipoEvento evento,
        EstadoTransacao destinoEsperado,
        List<string> divergencias)
    {
        string celula = $"({origem}, {evento})";
        RelogioFake relogio = new();
        Transacao transacao = EmEstado(origem, relogio);
        int entradasAntes = transacao.Historico.Count;

        relogio.AvancarSegundos(1);
        DateTimeOffset instante = relogio.Agora;

        EstadoTransacao destino;

        try
        {
            destino = transacao.Aplicar(evento, instante);
        }
        catch (MotorPixException excecao)
        {
            divergencias.Add(
                $"{celula} esta no diagrama e deveria levar a {destinoEsperado}, mas recusou com "
                    + $"{excecao.GetType().Name}: {excecao.Message}");
            return;
        }

        if (destino != destinoEsperado)
        {
            divergencias.Add($"{celula} devolveu {destino} e o diagrama manda {destinoEsperado}.");
        }

        if (transacao.Estado != destinoEsperado)
        {
            divergencias.Add($"{celula} deixou a transacao em {transacao.Estado} e o diagrama manda {destinoEsperado}.");
        }

        if (transacao.AtualizadaEm != instante)
        {
            divergencias.Add($"{celula} nao empurrou AtualizadaEm: ficou em {transacao.AtualizadaEm:O}, esperado {instante:O}.");
        }

        if (!MaquinaDeEstados.EhValida(origem, evento))
        {
            divergencias.Add($"{celula} esta no diagrama e EhValida devolveu false.");
        }

        if (!MaquinaDeEstados.TryDestino(origem, evento, out EstadoTransacao consultado) || consultado != destinoEsperado)
        {
            divergencias.Add($"{celula}: TryDestino devolveu {consultado} e o diagrama manda {destinoEsperado}.");
        }

        IReadOnlyList<TransicaoAplicada> historico = transacao.Historico;

        if (historico.Count != entradasAntes + 1)
        {
            divergencias.Add(
                $"{celula} deveria acrescentar exatamente uma entrada ao historico: tinha {entradasAntes} e "
                    + $"terminou com {historico.Count}.");
            return;
        }

        TransicaoAplicada esperada = new(origem, evento, destinoEsperado, instante);

        if (historico[^1] != esperada)
        {
            divergencias.Add($"{celula} gravou '{historico[^1]}' no historico e o esperado era '{esperada}'.");
        }
    }

    private static void ConferirCelulaInvalida(EstadoTransacao origem, TipoEvento evento, List<string> divergencias)
    {
        string celula = $"({origem}, {evento})";
        RelogioFake relogio = new();
        Transacao transacao = EmEstado(origem, relogio);
        int entradasAntes = transacao.Historico.Count;
        DateTimeOffset atualizadaAntes = transacao.AtualizadaEm;

        relogio.AvancarSegundos(1);

        TransicaoInvalidaException? recusa = null;

        try
        {
            transacao.Aplicar(evento, relogio.Agora);
        }
        catch (TransicaoInvalidaException excecao)
        {
            recusa = excecao;
        }
        catch (MotorPixException excecao)
        {
            divergencias.Add(
                $"{celula} recusou com {excecao.GetType().Name} em vez de TransicaoInvalidaException: {excecao.Message}");
            return;
        }

        if (recusa is null)
        {
            divergencias.Add($"{celula} nao esta no diagrama e foi aceita: a transacao foi parar em {transacao.Estado}.");
            return;
        }

        if (!recusa.E2E.Equals(transacao.E2E))
        {
            divergencias.Add($"{celula}: a excecao carrega o E2E '{recusa.E2E}' e a transacao e '{transacao.E2E}'.");
        }

        if (!string.Equals(recusa.EstadoAtual, origem.ToString(), StringComparison.Ordinal))
        {
            divergencias.Add($"{celula}: a excecao diz estado '{recusa.EstadoAtual}' e o estado era '{origem}'.");
        }

        if (!string.Equals(recusa.Evento, evento.ToString(), StringComparison.Ordinal))
        {
            divergencias.Add($"{celula}: a excecao diz evento '{recusa.Evento}' e o evento era '{evento}'.");
        }

        // A parte que separa "lancou" de "lancou depois de sujar o agregado".
        if (transacao.Estado != origem)
        {
            divergencias.Add($"{celula} lancou e ainda assim moveu o estado de {origem} para {transacao.Estado}.");
        }

        if (transacao.Historico.Count != entradasAntes)
        {
            divergencias.Add(
                $"{celula} lancou e ainda assim mexeu no historico: tinha {entradasAntes} entradas e "
                    + $"terminou com {transacao.Historico.Count}.");
        }

        if (transacao.AtualizadaEm != atualizadaAntes)
        {
            divergencias.Add(
                $"{celula} lancou e ainda assim empurrou AtualizadaEm de {atualizadaAntes:O} para {transacao.AtualizadaEm:O}.");
        }

        if (MaquinaDeEstados.EhValida(origem, evento))
        {
            divergencias.Add($"{celula} lancou em Aplicar e mesmo assim EhValida devolveu true.");
        }

        if (MaquinaDeEstados.TryDestino(origem, evento, out EstadoTransacao consultado))
        {
            divergencias.Add($"{celula} lancou em Aplicar e mesmo assim TryDestino devolveu {consultado}.");
        }
    }

    /// <summary>
    /// Recusa esperada, com as assercoes que todo fato de celula invalida repete: excecao certa,
    /// identificacao completa do par e agregado intacto.
    /// </summary>
    private static TransicaoInvalidaException Recusa(EstadoTransacao origem, TipoEvento evento)
    {
        RelogioFake relogio = new();
        Transacao transacao = EmEstado(origem, relogio);
        int entradasAntes = transacao.Historico.Count;
        DateTimeOffset atualizadaAntes = transacao.AtualizadaEm;

        relogio.AvancarSegundos(1);

        TransicaoInvalidaException excecao = Assert.Throws<TransicaoInvalidaException>(
            () => { transacao.Aplicar(evento, relogio.Agora); });

        Assert.Equal(transacao.E2E, excecao.E2E);
        Assert.Equal(origem.ToString(), excecao.EstadoAtual);
        Assert.Equal(evento.ToString(), excecao.Evento);

        Assert.Equal(origem, transacao.Estado);
        Assert.Equal(atualizadaAntes, transacao.AtualizadaEm);
        Assert.True(
            transacao.Historico.Count == entradasAntes,
            $"({origem}, {evento}) lancou e mexeu no historico: tinha {entradasAntes} entradas e terminou com "
                + $"{transacao.Historico.Count}.");

        return excecao;
    }

    /// <summary>
    /// Leva uma transacao recem-criada ate <paramref name="alvo"/> pelo prefixo minimo de eventos, e
    /// confere que chegou. A conferencia importa: se a montagem parar no estado errado, o teste da
    /// celula passaria a medir outra celula sem que a mensagem de falha desse qualquer pista.
    /// </summary>
    private static Transacao EmEstado(EstadoTransacao alvo, RelogioFake relogio)
    {
        Assert.True(
            CaminhoMaisCurto.TryGetValue(alvo, out TipoEvento[]? caminho),
            $"Nao ha caminho de montagem escrito para o estado '{alvo}'; conhecidos: "
                + $"[{string.Join(", ", CaminhoMaisCurto.Keys)}].");

        Transacao transacao = Transacao.Iniciar(
            NovoE2e(),
            ContaDeOrigem,
            ChaveDeDestino,
            ValorDoPagamento,
            relogio.Agora);

        foreach (TipoEvento evento in caminho!)
        {
            relogio.AvancarSegundos(1);
            transacao.Aplicar(evento, relogio.Agora);
        }

        Assert.True(
            transacao.Estado == alvo,
            $"A montagem por caminho curto deveria parar em '{alvo}' e parou em '{transacao.Estado}' "
                + $"depois de [{string.Join(", ", caminho)}].");

        return transacao;
    }

    private static EndToEndId NovoE2e() => new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).Proximo();

    private static TipoEvento[] EventosValidosEm(EstadoTransacao estado) =>
        Enum.GetValues<TipoEvento>().Where(evento => MaquinaDeEstados.EhValida(estado, evento)).ToArray();

    private static EstadoTransacao Traduzir(string nomeNoDiagrama, int linha)
    {
        Assert.True(
            EstadoPorNomeNoDiagrama.TryGetValue(nomeNoDiagrama, out EstadoTransacao estado),
            $"{NomeDoRecurso}:{linha} cita o estado '{nomeNoDiagrama}', que a tabela de nomes deste teste nao "
                + $"conhece: [{string.Join(", ", EstadoPorNomeNoDiagrama.Keys)}].");

        return estado;
    }

    private static string LerDiagrama()
    {
        Assembly assembly = typeof(MatrizDeTransicoesTestes).Assembly;
        Stream? fluxo = assembly.GetManifestResourceStream(NomeDoRecurso);

        Assert.True(
            fluxo is not null,
            $"O recurso '{NomeDoRecurso}' nao esta embarcado no assembly de teste. Recursos presentes: "
                + $"[{string.Join(", ", assembly.GetManifestResourceNames())}].");

        using StreamReader leitor = new(fluxo!, Encoding.UTF8);
        return leitor.ReadToEnd();
    }

    private static IReadOnlyList<ArestaDoDiagrama> ArestasDoDiagrama(string texto)
    {
        List<ArestaDoDiagrama> arestas = [];
        string[] linhas = texto.Split('\n');

        for (int i = 0; i < linhas.Length; i++)
        {
            Match casamento = PadraoDeAresta.Match(linhas[i].TrimEnd('\r'));

            if (casamento.Success)
            {
                arestas.Add(new ArestaDoDiagrama(
                    casamento.Groups["origem"].Value,
                    casamento.Groups["destino"].Value,
                    i + 1));
            }
        }

        return arestas;
    }

    /// <summary>Uma linha de transicao do arquivo normativo, com a linha de origem para a mensagem de falha.</summary>
    private sealed record ArestaDoDiagrama(string Origem, string Destino, int Linha)
    {
        public override string ToString() => $"{NomeDoRecurso}:{Linha} {Origem} -> {Destino}";
    }
}
