using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Unidade atomica de append do ledger: ou todos os itens entram, ou nenhum entra.
/// <para>
/// Um commit carrega tanto contas abertas quanto lancamentos porque a abertura de conta e evento
/// do log, e nao configuracao externa. Se o plano de contas vivesse fora do log, o replay
/// reconstruiria saldos mas nao o conjunto de contas, e uma conta que zerou e sumiu passaria
/// despercebida na comparacao.
/// </para>
/// </summary>
public sealed record Commit
{
    internal Commit(
        long sequencia,
        LedgerId ledger,
        DateTimeOffset em,
        IReadOnlyList<Conta> contasAbertas,
        IReadOnlyList<Lancamento> lancamentos)
    {
        Sequencia = sequencia;
        Ledger = ledger;
        Em = em;
        ContasAbertas = contasAbertas;
        Lancamentos = lancamentos;
    }

    /// <summary>Numero de sequencia atribuido pelo ledger, iniciando em 1.</summary>
    public long Sequencia { get; }

    public LedgerId Ledger { get; }

    /// <summary>
    /// Instante lido do <c>IClock</c> no momento do append. Fica gravado para que nenhuma projecao
    /// precise consultar o relogio durante o replay.
    /// </summary>
    public DateTimeOffset Em { get; }

    public IReadOnlyList<Conta> ContasAbertas { get; }

    public IReadOnlyList<Lancamento> Lancamentos { get; }

    /// <summary>
    /// Igualdade pela chave natural do log.
    /// <para>
    /// A igualdade sintetizada pelo record compararia <see cref="ContasAbertas"/> e
    /// <see cref="Lancamentos"/> por <em>referencia</em>, entao dois commits de conteudo identico
    /// nunca seriam iguais e um <c>Assert.Equal</c> no gate de replay falharia sem motivo.
    /// (Sequencia, Ledger) identifica o commit univocamente.
    /// </para>
    /// </summary>
    public bool Equals(Commit? outro) =>
        outro is not null && Sequencia == outro.Sequencia && Ledger.Equals(outro.Ledger);

    public override int GetHashCode() => HashCode.Combine(Sequencia, Ledger);

    public override string ToString() =>
        $"#{Sequencia} {Ledger} contas={ContasAbertas.Count} lancamentos={Lancamentos.Count}";
}
