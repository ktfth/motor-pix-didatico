namespace MotorPix.Psp.Nucleo;

/// <summary>
/// Estados da transacao. Transcricao literal de <c>motor-pix-maquina-estados.mermaid</c>, que e
/// normativo: nenhuma transicao fora dele existe.
/// </summary>
public enum EstadoTransacao
{
    Recebida = 1,
    Validada = 2,
    EnviadaSpi = 3,
    Liquidada = 4,
    Rejeitada = 5,
    Expirada = 6,
    Devolvida = 7,
}

/// <summary>
/// Eventos, derivados um a um dos rotulos das arestas do diagrama.
/// <para>
/// A aresta <c>[*] --&gt; RECEBIDA</c> nao aparece aqui: ela e construcao, nao transicao. Nascer
/// nao e um evento que se aplica a uma transacao existente.
/// </para>
/// </summary>
public enum TipoEvento
{
    /// <summary>estados:4 — "DICT resolve chave + saldo do cliente ok".</summary>
    ValidacaoLocalOk = 1,

    /// <summary>estados:5 — "validacao local falha".</summary>
    ValidacaoLocalFalhou = 2,

    /// <summary>estados:7 — "debito interno lancado + pacs.008 enviado".</summary>
    DebitoLancadoEPacs008Despachado = 3,

    /// <summary>estados:9 — "pacs.002 ACSC".</summary>
    Pacs002Acsc = 4,

    /// <summary>estados:10 — "pacs.002 RJCT".</summary>
    Pacs002Rjct = 5,

    /// <summary>estados:11 — "timeout via IClock, sem pacs.002".</summary>
    TimeoutDetectado = 6,

    /// <summary>estados:13 — "consulta de status confirma liquidacao".</summary>
    ConsultaConfirmaLiquidacao = 7,

    /// <summary>estados:14 — "consulta de status confirma rejeicao".</summary>
    ConsultaConfirmaRejeicao = 8,

    /// <summary>estados:16 — "pacs.004 liquidada (transacao nova ref. E2E original)".</summary>
    DevolucaoLiquidada = 9,
}

/// <summary>
/// A tabela de transicoes, escrita a mao a partir do diagrama.
/// <para>
/// Cada entrada carrega, em comentario, a linha do <c>.mermaid</c> de onde saiu. Um teste separado
/// parseia o diagrama e compara com esta tabela — mas a tabela <b>nao</b> e gerada do diagrama, e o
/// teste tambem nao: gerar os dois da mesma fonte provaria apenas que a fonte e igual a si mesma.
/// </para>
/// <para>
/// Sao 7 estados x 9 eventos = 63 celulas, das quais 9 sao validas. As outras 54 lancam
/// <c>TransicaoInvalidaException</c> — e sao testadas uma a uma.
/// </para>
/// </summary>
public static class MaquinaDeEstados
{
    private static readonly IReadOnlyDictionary<(EstadoTransacao, TipoEvento), EstadoTransacao> Transicoes =
        new Dictionary<(EstadoTransacao, TipoEvento), EstadoTransacao>
        {
            // estados:4
            [(EstadoTransacao.Recebida, TipoEvento.ValidacaoLocalOk)] = EstadoTransacao.Validada,

            // estados:5
            [(EstadoTransacao.Recebida, TipoEvento.ValidacaoLocalFalhou)] = EstadoTransacao.Rejeitada,

            // estados:7
            [(EstadoTransacao.Validada, TipoEvento.DebitoLancadoEPacs008Despachado)] = EstadoTransacao.EnviadaSpi,

            // estados:9
            [(EstadoTransacao.EnviadaSpi, TipoEvento.Pacs002Acsc)] = EstadoTransacao.Liquidada,

            // estados:10
            [(EstadoTransacao.EnviadaSpi, TipoEvento.Pacs002Rjct)] = EstadoTransacao.Rejeitada,

            // estados:11
            [(EstadoTransacao.EnviadaSpi, TipoEvento.TimeoutDetectado)] = EstadoTransacao.Expirada,

            // estados:13
            [(EstadoTransacao.Expirada, TipoEvento.ConsultaConfirmaLiquidacao)] = EstadoTransacao.Liquidada,

            // estados:14
            [(EstadoTransacao.Expirada, TipoEvento.ConsultaConfirmaRejeicao)] = EstadoTransacao.Rejeitada,

            // estados:16
            [(EstadoTransacao.Liquidada, TipoEvento.DevolucaoLiquidada)] = EstadoTransacao.Devolvida,
        };

    /// <summary>Quantidade de transicoes validas. Existe para o teste de exaustividade conferir a conta.</summary>
    public static int QuantidadeDeTransicoesValidas => Transicoes.Count;

    public static IReadOnlyCollection<(EstadoTransacao Origem, TipoEvento Evento, EstadoTransacao Destino)> Arestas() =>
        [.. Transicoes.Select(par => (par.Key.Item1, par.Key.Item2, par.Value))];

    public static bool EhValida(EstadoTransacao origem, TipoEvento evento) =>
        Transicoes.ContainsKey((origem, evento));

    public static bool TryDestino(EstadoTransacao origem, TipoEvento evento, out EstadoTransacao destino) =>
        Transicoes.TryGetValue((origem, evento), out destino);

    /// <summary>
    /// <c>LIQUIDADA</c> tem aresta para <c>[*]</c> e <b>nao</b> e terminal: ela ainda aceita
    /// <see cref="TipoEvento.DevolucaoLiquidada"/>. Derivar terminalidade das setas para
    /// <c>[*]</c> faria o motor recusar o <c>pacs.004</c> e mataria o gate 6 inteiro — por isso a
    /// propriedade e declarada aqui, e nao inferida do desenho.
    /// </summary>
    public static bool EhTerminal(EstadoTransacao estado) =>
        estado is EstadoTransacao.Rejeitada or EstadoTransacao.Devolvida;
}
