using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Psp.Nucleo;

/// <summary>
/// Fotografia imutavel de uma transacao, para quem so precisa ler.
/// <para>
/// Existe porque devolver a entidade viva punha o invariante 4 nas maos da boa vontade do
/// chamador: <c>Transacao.Aplicar</c> e publico, entao qualquer consumidor podia mover uma
/// transacao de <c>EXPIRADA</c> para <c>REJEITADA</c> em uma linha — sem consultar o SPI, sem
/// estornar o debito (o estorno mora no PSP, nao na entidade) e fora do lock que serializa o
/// agregado. O invariante 6 passa a ser imposto pelo tipo, e nao por convencao.
/// </para>
/// <para>
/// Nao adianta tornar <c>Aplicar</c> internal: o nucleo e o PSP sao assemblies diferentes, e expor
/// internals de producao para producao e proibido pelo teste de arquitetura.
/// </para>
/// </summary>
public sealed record VistaDaTransacao(
    EndToEndId E2E,
    TipoTransacao Tipo,
    EstadoTransacao Estado,
    ContaId ContaOrigem,
    ChavePix ChaveDestino,
    Valor Valor,
    DateTimeOffset CriadaEm,
    DateTimeOffset AtualizadaEm,
    Ispb? IspbDestino,
    ContaId? ContaDestino,
    MotivoRejeicaoLocal? MotivoRejeicao,
    EndToEndId? E2eOriginal,
    EndToEndId? E2eDevolucao,
    IReadOnlyList<TransicaoAplicada> Historico)
{
    public static VistaDaTransacao De(Transacao transacao)
    {
        ArgumentNullException.ThrowIfNull(transacao);

        return new VistaDaTransacao(
            transacao.E2E,
            transacao.Tipo,
            transacao.Estado,
            transacao.ContaOrigem,
            transacao.ChaveDestino,
            transacao.Valor,
            transacao.CriadaEm,
            transacao.AtualizadaEm,
            transacao.IspbDestino,
            transacao.ContaDestino,
            transacao.MotivoRejeicao,
            transacao.E2eOriginal,
            transacao.E2eDevolucao,
            transacao.Historico);
    }
}
