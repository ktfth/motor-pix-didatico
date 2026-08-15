using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Psp.Nucleo;

/// <summary>
/// Forma canonica do pedido, usada para decidir se um reenvio e o mesmo pagamento.
/// <para>
/// A chave entra <b>normalizada</b> (a forma canonica do value object), e nao como o cliente
/// digitou: sem isso, <c>"529.982.247-25"</c> e <c>"52998224725"</c> — o mesmo CPF — produziriam
/// impressoes diferentes e um reenvio legitimo viraria conflito.
/// </para>
/// </summary>
public readonly record struct ImpressaoDoPedido
{
    private ImpressaoDoPedido(string texto) => Texto = texto;

    public string Texto { get; }

    public static ImpressaoDoPedido De(ContaId origem, ChavePix destino, Valor valor)
    {
        ArgumentNullException.ThrowIfNull(origem);
        ArgumentNullException.ThrowIfNull(destino);

        return new ImpressaoDoPedido($"{origem}|{destino.Tipo}:{destino.Texto}|{valor.Centavos}");
    }

    public override string ToString() => Texto ?? string.Empty;
}

/// <summary>Uma resposta congelada, com o pedido que a produziu.</summary>
public sealed record EntradaDeIdempotencia(
    EndToEndId E2E,
    ImpressaoDoPedido Impressao,
    EstadoTransacao EstadoRespondido,
    MotivoRejeicaoLocal? MotivoRespondido,
    DateTimeOffset Em)
{
    /// <summary>
    /// Marca de reivindicacao, ainda sem resposta congelada.
    /// <para>
    /// Um sinalizador explicito, e nao a inferencia "estado e Recebida": <c>RECEBIDA</c> e um
    /// estado legitimo de resposta, e confundir os dois faria uma reivindicacao abandonada parecer
    /// um pedido respondido.
    /// </para>
    /// </summary>
    public bool EstaEmVoo { get; init; }
}

/// <summary>
/// O indice de idempotencia da API de pagamento.
/// <para>
/// A reivindicacao e um <b>insert atomico</b>: consultar se o E2E existe e inserir depois seria
/// check-then-act, e dois POSTs simultaneos fariam duas resolucoes de chave antes de qualquer
/// colisao ser notada.
/// </para>
/// <para>
/// A resposta guardada e um <b>snapshot congelado</b>, nao uma projecao viva. O diagrama diz
/// literalmente "replay da resposta": um reenvio feito enquanto a original ainda esta em voo
/// devolve o que foi respondido na primeira vez, e nao o estado que a transacao alcancou depois.
/// </para>
/// <para>
/// O log e append-only, como o do ledger: e dele que o gate 8 reconstroi as respostas.
/// </para>
/// </summary>
public sealed class RegistroDeIdempotencia
{
    private readonly object _trava = new();
    private readonly Dictionary<EndToEndId, EntradaDeIdempotencia> _porE2e = [];
    private readonly List<EntradaDeIdempotencia> _log = [];

    public int Quantidade
    {
        get
        {
            lock (_trava)
            {
                return _porE2e.Count;
            }
        }
    }

    /// <summary>Log append-only das respostas congeladas, na ordem em que foram produzidas.</summary>
    public IReadOnlyList<EntradaDeIdempotencia> Log()
    {
        lock (_trava)
        {
            return [.. _log];
        }
    }

    /// <summary>
    /// Reivindica o E2E para este pedido.
    /// <para>
    /// Devolve <c>true</c> se a reivindicacao e nova — o chamador deve processar e depois chamar
    /// <see cref="Congelar"/>. Devolve <c>false</c> se o E2E ja era conhecido, e nesse caso
    /// <paramref name="jaRegistrada"/> traz a entrada original.
    /// </para>
    /// <para>
    /// Lanca <see cref="E2eConflitanteException"/> quando o E2E e conhecido mas o pedido diverge.
    /// </para>
    /// </summary>
    public bool TentarReivindicar(
        EndToEndId e2e,
        ImpressaoDoPedido impressao,
        out EntradaDeIdempotencia? jaRegistrada)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            if (_porE2e.TryGetValue(e2e, out EntradaDeIdempotencia? existente))
            {
                if (!string.Equals(existente.Impressao.Texto, impressao.Texto, StringComparison.Ordinal))
                {
                    throw new E2eConflitanteException(e2e, impressao.Texto, existente.Impressao.Texto);
                }

                jaRegistrada = existente;
                return false;
            }

            // Reivindicacao sem resposta ainda: o E2E fica ocupado a partir daqui, antes de
            // qualquer consulta externa. Um segundo POST simultaneo ja encontra a marca.
            _porE2e.Add(e2e, EmVoo(e2e, impressao));
            jaRegistrada = null;
            return true;
        }
    }

    /// <summary>Congela a resposta produzida para um E2E ja reivindicado.</summary>
    public EntradaDeIdempotencia Congelar(
        EndToEndId e2e,
        ImpressaoDoPedido impressao,
        EstadoTransacao estado,
        MotivoRejeicaoLocal? motivo,
        DateTimeOffset em)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        EntradaDeIdempotencia entrada = new(e2e, impressao, estado, motivo, em);

        lock (_trava)
        {
            // Congelar duas vezes o mesmo E2E nao sobrescreve: vale a PRIMEIRA resposta, que e a
            // que o cliente recebeu. Sobrescrever fazia o indice vivo desempatar por "ultima vence"
            // enquanto o replay desempata por "primeira vence" — os dois divergiam sobre o mesmo
            // log, que e exatamente o que o gate 8 existe para impedir. E apagava do indice a
            // resposta que ja tinha saido pela API.
            if (_porE2e.TryGetValue(e2e, out EntradaDeIdempotencia? existente) && !existente.EstaEmVoo)
            {
                return existente;
            }

            _porE2e[e2e] = entrada;
            _log.Add(entrada);
        }

        return entrada;
    }

    public bool TryEntrada(EndToEndId e2e, out EntradaDeIdempotencia? entrada)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            return _porE2e.TryGetValue(e2e, out entrada);
        }
    }

    /// <summary>
    /// Desfaz uma reivindicacao que nao chegou a produzir resposta.
    /// <para>
    /// Existe para o caso em que o processamento lanca entre a reivindicacao e o congelamento. Sem
    /// isto, o E2E ficaria ocupado para sempre por um pedido que nunca foi executado: o cliente
    /// receberia a excecao, tentaria de novo com o mesmo identificador — que e o comportamento
    /// correto dele — e levaria "ja reivindicado" para sempre, sem nunca conseguir pagar.
    /// </para>
    /// <para>
    /// So libera reivindicacao <em>em voo</em>. Uma entrada ja congelada e resposta definitiva e
    /// nao pode ser apagada, ou a idempotencia deixaria de valer.
    /// </para>
    /// </summary>
    public bool LiberarSeEmVoo(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            if (_porE2e.TryGetValue(e2e, out EntradaDeIdempotencia? entrada) && entrada.EstaEmVoo)
            {
                _porE2e.Remove(e2e);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Marca de reivindicacao: existe entre o insert e o congelamento da resposta.
    /// <para>
    /// Na composicao atual a janela nao e observavel de fora, porque <c>Pagar</c> reivindica e
    /// congela sob o mesmo lock do PSP. A marca existe pelo que ela permite detectar: uma
    /// reivindicacao que ficou orfa porque o processamento lancou.
    /// </para>
    /// </summary>
    private static EntradaDeIdempotencia EmVoo(EndToEndId e2e, ImpressaoDoPedido impressao) =>
        new(e2e, impressao, EstadoTransacao.Recebida, MotivoRespondido: null, Em: default)
        {
            EstaEmVoo = true,
        };
}
