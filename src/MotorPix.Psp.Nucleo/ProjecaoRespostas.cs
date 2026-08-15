using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Psp.Nucleo;

/// <summary>
/// Reconstroi as respostas congeladas da API a partir do log append-only de idempotencia.
/// <para>
/// Existe porque a resposta idempotente e <b>estado</b>, e todo estado do motor tem de ser
/// derivavel do log. Sem esta projecao, o replay reconstruiria os saldos e os estados das
/// transacoes mas nao o que a API respondeu — e o reenvio de um E2E depois de um replay devolveria
/// coisa diferente do que devolveu antes.
/// </para>
/// <para>
/// O log tem uma entrada por resposta produzida, na ordem de producao. Quando o mesmo E2E aparece
/// mais de uma vez — o que so acontece se alguem congelar duas vezes —, vale a <b>primeira</b>: e
/// ela que o cliente recebeu, e "replay da resposta" significa a resposta original, nao a ultima.
/// </para>
/// </summary>
public sealed class ProjecaoRespostas
{
    private readonly Dictionary<EndToEndId, EntradaDeIdempotencia> _respostas = [];
    private readonly List<EndToEndId> _ordem = [];

    private ProjecaoRespostas()
    {
    }

    public IReadOnlyCollection<EndToEndId> Respondidos => _respostas.Keys;

    /// <summary>A ordem em que as respostas foram produzidas. Sensivel a ordem, de proposito.</summary>
    public IReadOnlyList<EndToEndId> Ordem => _ordem;

    public static ProjecaoRespostas Reconstruir(IEnumerable<EntradaDeIdempotencia> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        ProjecaoRespostas projecao = new();

        foreach (EntradaDeIdempotencia entrada in log)
        {
            if (entrada.EstaEmVoo)
            {
                // Reivindicacao sem resposta nao e resposta. Ela nunca deveria estar no log — o
                // log so recebe entradas congeladas —, mas ignorar aqui e mais seguro do que
                // reconstruir uma resposta que ninguem recebeu.
                continue;
            }

            if (projecao._respostas.TryAdd(entrada.E2E, entrada))
            {
                projecao._ordem.Add(entrada.E2E);
            }
        }

        return projecao;
    }

    public EntradaDeIdempotencia Resposta(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        return _respostas.TryGetValue(e2e, out EntradaDeIdempotencia? entrada)
            ? entrada
            : throw new TransacaoDesconhecidaException(e2e);
    }

    public bool TryResposta(EndToEndId e2e, out EntradaDeIdempotencia? entrada)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        return _respostas.TryGetValue(e2e, out entrada);
    }

    /// <summary>
    /// Igualdade estrutural incluindo o conjunto de chaves e a ORDEM de producao.
    /// <para>
    /// A ordem entra na comparacao de proposito: uma projecao que produzisse o mesmo conjunto em
    /// ordem diferente seria comutativa demais, e o teste de replay nao provaria que o log e a
    /// verdade — provaria apenas que os dois lados tem os mesmos elementos.
    /// </para>
    /// </summary>
    public bool EhIgualA(ProjecaoRespostas outra)
    {
        ArgumentNullException.ThrowIfNull(outra);

        if (_respostas.Count != outra._respostas.Count || _ordem.Count != outra._ordem.Count)
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

        foreach ((EndToEndId e2e, EntradaDeIdempotencia entrada) in _respostas)
        {
            if (!outra._respostas.TryGetValue(e2e, out EntradaDeIdempotencia? outraEntrada)
                || entrada != outraEntrada)
            {
                return false;
            }
        }

        return true;
    }
}
