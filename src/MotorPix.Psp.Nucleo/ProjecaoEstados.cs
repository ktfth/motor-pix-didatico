using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Psp.Nucleo;

/// <summary>O historico append-only de uma transacao, como insumo de replay.</summary>
public sealed record HistoricoDeTransacao(EndToEndId E2E, IReadOnlyList<TransicaoAplicada> Transicoes);

/// <summary>
/// Reconstroi o estado de cada transacao a partir do historico de transicoes, do zero.
/// <para>
/// Funcao pura: nao consulta <c>IClock</c>, nao gera identificador e nao guarda nada fora do que
/// recebeu. Se precisasse de tempo, o tempo ja esta em cada <see cref="TransicaoAplicada"/>.
/// </para>
/// <para>
/// A reconstrucao <b>reaplica a tabela</b> em vez de confiar no destino gravado: se uma transicao
/// do historico nao existir na maquina normativa, o replay recusa. Copiar o destino faria o replay
/// concordar com qualquer historico, inclusive um corrompido — e ele existe justamente para nao
/// concordar.
/// </para>
/// </summary>
public sealed class ProjecaoEstados
{
    private readonly Dictionary<EndToEndId, EstadoTransacao> _estados = [];
    private readonly Dictionary<EndToEndId, int> _quantidadeDeTransicoes = [];

    private ProjecaoEstados()
    {
    }

    public IReadOnlyCollection<EndToEndId> Transacoes => _estados.Keys;

    public static ProjecaoEstados Reconstruir(IEnumerable<HistoricoDeTransacao> historicos)
    {
        ArgumentNullException.ThrowIfNull(historicos);

        ProjecaoEstados projecao = new();

        foreach (HistoricoDeTransacao historico in historicos)
        {
            // Toda transacao nasce em RECEBIDA: e a unica porta de entrada do diagrama.
            EstadoTransacao estado = EstadoTransacao.Recebida;

            foreach (TransicaoAplicada transicao in historico.Transicoes)
            {
                if (transicao.Origem != estado)
                {
                    throw new TransicaoInvalidaException(
                        historico.E2E,
                        estado.ToString(),
                        $"{transicao.Evento} (historico diz que a origem era {transicao.Origem})");
                }

                if (!MaquinaDeEstados.TryDestino(estado, transicao.Evento, out EstadoTransacao destino))
                {
                    throw new TransicaoInvalidaException(historico.E2E, estado.ToString(), transicao.Evento.ToString());
                }

                if (destino != transicao.Destino)
                {
                    throw new TransicaoInvalidaException(
                        historico.E2E,
                        estado.ToString(),
                        $"{transicao.Evento} (historico grava destino {transicao.Destino}, a tabela diz {destino})");
                }

                estado = destino;
            }

            projecao._estados[historico.E2E] = estado;
            projecao._quantidadeDeTransicoes[historico.E2E] = historico.Transicoes.Count;
        }

        return projecao;
    }

    public EstadoTransacao Estado(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        return _estados.TryGetValue(e2e, out EstadoTransacao estado)
            ? estado
            : throw new TransacaoDesconhecidaException(e2e);
    }

    public int QuantidadeDeTransicoes(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        return _quantidadeDeTransicoes.TryGetValue(e2e, out int quantidade)
            ? quantidade
            : throw new TransacaoDesconhecidaException(e2e);
    }

    /// <summary>
    /// Igualdade estrutural incluindo o <em>conjunto de chaves</em>. Comparar so os estados deixaria
    /// passar uma transacao que sumiu da projecao reconstruida.
    /// </summary>
    public bool EhIgualA(ProjecaoEstados outra)
    {
        ArgumentNullException.ThrowIfNull(outra);

        if (_estados.Count != outra._estados.Count)
        {
            return false;
        }

        foreach ((EndToEndId e2e, EstadoTransacao estado) in _estados)
        {
            if (!outra._estados.TryGetValue(e2e, out EstadoTransacao outroEstado) || estado != outroEstado)
            {
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<string> Diferencas(ProjecaoEstados outra)
    {
        ArgumentNullException.ThrowIfNull(outra);

        List<string> diferencas = [];

        foreach (EndToEndId e2e in _estados.Keys.Union(outra._estados.Keys))
        {
            bool aqui = _estados.TryGetValue(e2e, out EstadoTransacao estadoAqui);
            bool la = outra._estados.TryGetValue(e2e, out EstadoTransacao estadoLa);

            if (!aqui)
            {
                diferencas.Add($"{e2e}: ausente a esquerda, {estadoLa} a direita");
            }
            else if (!la)
            {
                diferencas.Add($"{e2e}: {estadoAqui} a esquerda, ausente a direita");
            }
            else if (estadoAqui != estadoLa)
            {
                diferencas.Add($"{e2e}: {estadoAqui} a esquerda, {estadoLa} a direita");
            }
        }

        return diferencas;
    }
}
