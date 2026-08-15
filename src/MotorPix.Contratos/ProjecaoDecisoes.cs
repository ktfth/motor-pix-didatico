using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Mensagens;

namespace MotorPix.Contratos;

/// <summary>
/// Uma decisao do SPI, como gravada no log: o que ele respondeu para um E2E e sobre qual ordem.
/// </summary>
public sealed record DecisaoDoSpi(EndToEndId E2E, Pacs002 Resposta, string ImpressaoDaOrdem, long Sequencia);

/// <summary>
/// Reconstroi as decisoes do SPI a partir do log, do zero.
/// <para>
/// Existe porque a decisao do SPI e o unico estado do motor de que dependem <b>dois</b> invariantes
/// — o 5, pelo dedup de mensagem, e o 6, porque a consulta de status responde a partir dela. Um
/// motor reconstruido apenas dos tres ledgers responderia <c>Indeterminada</c> onde o vivo responde
/// <c>Rejeitada</c>, porque uma ordem recusada por participante desconhecido nem chega a lancar: a
/// transacao em <c>EXPIRADA</c> perderia sua unica saida legitima.
/// </para>
/// <para>
/// Vale a PRIMEIRA decisao de cada E2E, como no dedup vivo: reentrega devolve a resposta original.
/// </para>
/// </summary>
public sealed class ProjecaoDecisoes
{
    private readonly Dictionary<EndToEndId, DecisaoDoSpi> _decisoes = [];
    private readonly List<EndToEndId> _ordem = [];

    private ProjecaoDecisoes()
    {
    }

    public IReadOnlyCollection<EndToEndId> Decididos => _decisoes.Keys;

    public IReadOnlyList<EndToEndId> Ordem => _ordem;

    public static ProjecaoDecisoes Reconstruir(IEnumerable<DecisaoDoSpi> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        ProjecaoDecisoes projecao = new();
        long esperada = 1;

        foreach (DecisaoDoSpi decisao in log)
        {
            if (decisao.Sequencia != esperada)
            {
                throw new AtoContabilInvalidoException(
                    $"log de decisoes incompleto: esperava sequencia {esperada}, veio {decisao.Sequencia}");
            }

            if (projecao._decisoes.TryAdd(decisao.E2E, decisao))
            {
                projecao._ordem.Add(decisao.E2E);
            }

            esperada++;
        }

        return projecao;
    }

    /// <summary>
    /// A mesma resposta que <c>ISpi.ConsultarStatus</c> daria, derivada so do log.
    /// E este metodo que prova que a saida de <c>EXPIRADA</c> sobrevive a um replay.
    /// </summary>
    public ResultadoConsulta ConsultarStatus(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        if (!_decisoes.TryGetValue(e2e, out DecisaoDoSpi? decisao))
        {
            return ResultadoConsulta.Indeterminada;
        }

        return decisao.Resposta.Status == StatusPacs002.Acsc
            ? ResultadoConsulta.Liquidada
            : ResultadoConsulta.Rejeitada;
    }

    public bool TryDecisao(EndToEndId e2e, out DecisaoDoSpi? decisao)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        return _decisoes.TryGetValue(e2e, out decisao);
    }

    /// <summary>Igualdade estrutural incluindo o conjunto de chaves e a ordem de emissao.</summary>
    public bool EhIgualA(ProjecaoDecisoes outra)
    {
        ArgumentNullException.ThrowIfNull(outra);

        if (_decisoes.Count != outra._decisoes.Count || _ordem.Count != outra._ordem.Count)
        {
            return false;
        }

        for (int i = 0; i < _ordem.Count; i++)
        {
            if (!_ordem[i].Equals(outra._ordem[i]))
            {
                return false;
            }
        }

        foreach ((EndToEndId e2e, DecisaoDoSpi decisao) in _decisoes)
        {
            if (!outra._decisoes.TryGetValue(e2e, out DecisaoDoSpi? outraDecisao) || decisao != outraDecisao)
            {
                return false;
            }
        }

        return true;
    }
}
