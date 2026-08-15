using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Conciliacao;

/// <summary>
/// Como uma divergencia entre os ledgers se explica.
/// <para>
/// As duas primeiras sao <b>explicadas</b>: dinheiro em transito, com um E2E que diz onde ele
/// esta. <see cref="Orfa"/> e o alarme — diferenca que nenhum E2E justifica, ou seja, dinheiro
/// criado ou destruido. Ela nao e fechavel por consulta; existe para ser vista.
/// </para>
/// </summary>
public enum ClasseDeDivergencia
{
    /// <summary>Debitado no pagador e nao liquidado no SPI. O dinheiro saiu e nao chegou.</summary>
    PendenciaDoPagador = 1,

    /// <summary>Liquidado no SPI e nao creditado no recebedor. O dinheiro chegou e nao foi entregue.</summary>
    PendenciaDoRecebedor = 2,

    /// <summary>Sem explicacao possivel a partir dos lancamentos. Alarme.</summary>
    Orfa = 3,
}

/// <summary>
/// Uma divergencia apurada pela conciliacao.
/// <para>
/// <see cref="E2E"/> e <c>null</c> exatamente quando a classe e <see cref="ClasseDeDivergencia.Orfa"/>:
/// orfa e, por definicao, diferenca que <b>nenhum</b> E2E explica. Fabricar um identificador de
/// fachada para preencher o campo seria mentir sobre a natureza do achado — e a primeira tentativa
/// deste codigo fez isso, com um E2E sintetico cujo miolo <c>yyyyMMddHHmm</c> era invalido, de modo
/// que a primeira orfa derrubava a conciliacao inteira.
/// </para>
/// </summary>
public sealed record DivergenciaPorE2E(
    EndToEndId? E2E,
    ClasseDeDivergencia Classe,
    Ispb Participante,
    Valor Valor,
    string Explicacao);

/// <summary>
/// O que a conciliacao apurou numa passada.
/// <para>
/// <see cref="EmQuiescencia"/> e o criterio de aceite do gate: nao ha divergencia alguma, o que
/// significa que espelho e conta PI coincidem em todos os participantes e nenhuma transacao ficou
/// pelo caminho.
/// </para>
/// </summary>
public sealed record RelatorioDeConciliacao(
    IReadOnlyList<DivergenciaPorE2E> Divergencias,
    IReadOnlyDictionary<Ispb, Saldo> DiferencaPorParticipante,
    IReadOnlyCollection<EndToEndId> Expiradas)
{
    public bool EmQuiescencia => Divergencias.Count == 0 && Expiradas.Count == 0;

    public IReadOnlyList<DivergenciaPorE2E> Orfas =>
        [.. Divergencias.Where(d => d.Classe == ClasseDeDivergencia.Orfa)];

    /// <summary>
    /// E2E que vale a pena perguntar ao SPI: os que estao em <c>EXPIRADA</c> e os que aparecem como
    /// pendencia do pagador — dinheiro que saiu do cliente e nao se sabe se chegou.
    /// </summary>
    public IReadOnlyCollection<EndToEndId> AConsultar =>
        [.. Expiradas
            .Concat(Divergencias
                .Where(d => d.Classe == ClasseDeDivergencia.PendenciaDoPagador && d.E2E is not null)
                .Select(d => d.E2E!))
            .Distinct()];

    /// <summary>Creditos que o recebedor deixou de aplicar e que podem ser reprocessados.</summary>
    public IReadOnlyCollection<EndToEndId> ACreditar =>
        [.. Divergencias
            .Where(d => d.Classe == ClasseDeDivergencia.PendenciaDoRecebedor && d.E2E is not null)
            .Select(d => d.E2E!)
            .Distinct()];
}
