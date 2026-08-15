using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Mensagens;

namespace MotorPix.Contratos;

/// <summary>
/// O que o SPI sabe sobre um E2E quando perguntado.
/// <para>
/// <see cref="Indeterminada"/> nao e erro: e a resposta honesta para "nunca vi essa ordem" ou
/// "ainda estou processando". A nota do diagrama de estados prevê isso ao dizer que "o que a
/// consulta nao fechar, a conciliacao fecha" — sem essa possibilidade, a frase nao faria sentido.
/// </para>
/// </summary>
public enum ResultadoConsulta
{
    Indeterminada = 0,
    Liquidada = 1,
    Rejeitada = 2,
}

/// <summary>
/// O SPI, visto de fora (seta 4 e o pontilhado de consulta de status).
/// </summary>
public interface ISpi
{
    /// <summary>
    /// Consulta de status (seta pontilhada <c>SM_P -.-&gt; VAL</c>).
    /// <para>
    /// E a <b>unica</b> saida de <c>EXPIRADA</c>. O invariante 6 e explicito: timeout nao e falha,
    /// e proibido estornar ou confirmar por suposicao. Por isso a resposta vem de quem tem a
    /// verdade — o ledger do SPI —, e nunca de inferencia local.
    /// </para>
    /// </summary>
    ResultadoConsulta ConsultarStatus(EndToEndId e2e);

    /// <summary>
    /// Recebe uma ordem de pagamento. Valida, deduplica por E2E e liquida; o resultado volta como
    /// <c>pacs.002</c> pelo barramento, nunca como retorno desta chamada.
    /// <para>
    /// O retorno <c>void</c> e deliberado: devolver o <c>pacs.002</c> aqui faria o resultado chegar
    /// ao pagador dentro da propria pilha da seta 4, com a transacao ainda em <c>VALIDADA</c> — par
    /// que o diagrama nao tem, ou seja, excecao de transicao invalida no caminho feliz.
    /// </para>
    /// </summary>
    void ReceberPacs008(Pacs008 ordem);

    /// <summary>
    /// Recebe uma devolucao. Mesma mecanica do <c>pacs.008</c> — valida, deduplica por E2E proprio
    /// e liquida —, com os papeis invertidos e a referencia a original preservada.
    /// </summary>
    void ReceberPacs004(Pacs004 devolucao);

    /// <summary>
    /// A confirmacao ja emitida para um E2E, se houver.
    /// <para>
    /// Existe para o reprocessamento da conciliacao: quem perdeu o <c>pacs.002</c> nao tem os dados
    /// para creditar — eles viajam no <c>OrgnlTxRef</c>. Pedir a mensagem de volta ao SPI e o
    /// contrario de decidir por conta propria; a autoridade continua sendo ele.
    /// </para>
    /// </summary>
    bool TryConfirmacao(EndToEndId e2e, out Pacs002? confirmacao);
}

/// <summary>
/// Um participante do SPI, visto pelo SPI (seta 6).
/// <para>
/// O mesmo <c>pacs.002</c> e entregue ao debtor e ao creditor, exatamente como o diagrama mostra a
/// seta 6 se bifurcando. Cada participante decide o que fazer conforme o papel que teve na ordem:
/// para o debtor e resultado, para o creditor e ordem de creditar.
/// </para>
/// </summary>
public interface IParticipante
{
    Ispb Ispb { get; }

    void Receber(Pacs002 confirmacao);

    /// <summary>
    /// Diz se a conta pode receber credito neste participante.
    /// <para>
    /// Consultado pelo SPI <b>antes</b> de liquidar. Sem isso, uma chave vinculada a conta que o
    /// recebedor nunca abriu faz a liquidacao acontecer (seta 5) e so a seta 7 estourar: dinheiro
    /// liquidado no SPI sem contrapartida no recebedor, e a soma por ledger continua fechando em
    /// zero — a divergencia orfa que a conciliacao existe para achar.
    /// </para>
    /// </summary>
    bool PodeCreditar(ContaId conta);
}

/// <summary>
/// Barramento in-process com entrega diferida.
/// <para>
/// Nada e entregue na hora: <see cref="Enfileirar"/> so registra, e <see cref="Drenar"/> entrega.
/// E isso que impede a chamada reentrante da seta 4, torna expressavel o cenario do gate 5
/// (<c>pacs.002</c> que nunca chega — basta nao drenar) e cria a costura para injetar falha de
/// entrega sem sujar o dominio.
/// </para>
/// </summary>
public interface IBarramento
{
    void Enfileirar(Envelope envelope);

    /// <summary>Entrega tudo o que estiver pendente, inclusive o que for enfileirado durante a
    /// propria drenagem, e devolve quantas mensagens foram entregues.</summary>
    int Drenar();

    int Pendentes { get; }
}

/// <summary>Uma mensagem a caminho, com seu destinatario ja resolvido.</summary>
public abstract record Envelope
{
    private protected Envelope()
    {
    }
}

/// <summary>Seta 4: ordem do PSP pagador para o SPI.</summary>
public sealed record EnvelopePacs008(Pacs008 Ordem) : Envelope;

/// <summary>Devolucao a caminho do SPI. Seta pontilhada SM_R para VAL.</summary>
public sealed record EnvelopePacs004(Pacs004 Devolucao) : Envelope;

/// <summary>Seta 6: resultado do SPI para um participante especifico.</summary>
public sealed record EnvelopePacs002(Ispb Destinatario, Pacs002 Confirmacao) : Envelope;

/// <summary>
/// O que a conciliacao precisa de um PSP: ler o ledger, saber o que esta em <c>EXPIRADA</c> e
/// pedir que ele consulte o SPI.
/// <para>
/// Repare no que <em>nao</em> esta aqui: nenhuma forma de transicionar transacao. A conciliacao
/// detecta e pergunta; quem decide continua sendo o SPI, pelas arestas que ja existem. Dar-lhe
/// poder de fechar por conta propria violaria o invariante 6, porque a decisao viria de evidencia
/// indireta e local (ADR-013).
/// </para>
/// </summary>
public interface IPspConciliavel
{
    Ispb Ispb { get; }

    IConsultaLedger Consulta { get; }

    IReadOnlyCollection<EndToEndId> Expiradas { get; }

    ResultadoConsulta ConsultarStatus(EndToEndId e2e);

    /// <summary>
    /// Reaplica um credito que este PSP deixou de aplicar (aresta <c>MATCH -.-&gt; SM_R</c>).
    /// <para>
    /// Idempotente pela chave do ledger: reprocessar o que ja foi creditado e no-op. O PSP busca a
    /// confirmacao no SPI e a reentrega a si mesmo — nao inventa o credito a partir do relatorio.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> se o credito foi aplicado agora.</returns>
    bool ReprocessarCredito(EndToEndId e2e);
}

/// <summary>
/// Registro de chaves, separado de <see cref="IDiretorioChaves"/>.
/// <para>
/// Quem resolve nao registra. A separacao tambem coloca a responsabilidade no lugar certo: quem
/// vincula uma chave e o PSP do titular, que e o unico que sabe se aquela conta existe no proprio
/// ledger — o diretorio nao conhece ledgers, e nao deveria conhecer.
/// </para>
/// </summary>
public interface IRegistroDeChaves
{
    void Vincular(ResolucaoChave vinculo);
}
