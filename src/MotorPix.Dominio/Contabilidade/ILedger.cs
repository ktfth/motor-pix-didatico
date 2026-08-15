using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Porta de escrita do ledger append-only de partidas dobradas.
/// <para>
/// Repare no que <em>nao</em> existe nesta interface: nenhuma operacao de alterar ou remover
/// lancamento. Correcao e estorno sao lancamentos novos, e essa regra e imposta pela ausencia
/// de API, nao por disciplina.
/// </para>
/// </summary>
public interface ILedger
{
    LedgerId Id { get; }

    /// <summary>Abre contas no plano de contas. Reabrir uma conta existente e erro.</summary>
    Commit Abrir(params Conta[] contas);

    /// <summary>
    /// Grava um ato contabil atomico. Guardas de conta desconhecida, chave duplicada, contra-conta
    /// de abertura e saldo negativo sao avaliadas <em>antes</em> de qualquer mutacao: se uma
    /// falhar, nada e gravado.
    /// </summary>
    Commit Lancar(params Lancamento[] lancamentos);

    Saldo SaldoNatural(ContaId conta);
}

/// <summary>
/// Porta de leitura do log. Separada de <see cref="ILedger"/> porque a conciliacao e as projecoes
/// leem o historico sem nenhum direito de escrever nele.
/// </summary>
public interface IConsultaLedger
{
    LedgerId Id { get; }

    IReadOnlyList<Commit> Log(long desdeSequencia = 0);
}

/// <summary>
/// Porta de inspecao, consumida pelo harness de teste e pela conciliacao.
/// <para>
/// Separada de <see cref="ILedger"/> de proposito: <see cref="MinimoNatural"/> e
/// <see cref="Snapshot"/> existem para as propriedades de teste, e mante-los na porta de escrita
/// daria a quem so precisa inspecionar o direito de lancar.
/// </para>
/// </summary>
public interface IInspecaoLedger
{
    LedgerId Id { get; }

    Saldo SaldoNatural(ContaId conta);

    /// <summary>
    /// Saldo bruto: soma dos creditos menos soma dos debitos. E a grandeza para a qual a soma por
    /// ledger e sempre zero — a soma dos saldos naturais nao e constante.
    /// </summary>
    Saldo SaldoBruto(ContaId conta);

    /// <summary>
    /// Menor saldo natural que a conta ja teve.
    /// <para>
    /// <b>Hoje este valor e sempre zero</b>, e isso e uma consequencia demonstravel do modelo: a
    /// conta nasce em zero, o minimo so desce, e a guarda de saldo negativo impede o append de
    /// qualquer ato que levaria o natural abaixo de zero. Ou seja, o estado negativo nunca chega a
    /// ser gravado.
    /// </para>
    /// <para>
    /// O campo continua valendo o que custa porque e uma <em>testemunha independente da guarda</em>:
    /// ele e calculado a partir do saldo efetivamente gravado, nao a partir da decisao que a guarda
    /// tomou. Se a guarda ganhar um furo — sinal invertido, conta esquecida, ordem de fases trocada —
    /// um minimo negativo aparece na projecao e a propriedade P2 falha, mesmo que a guarda continue
    /// jurando que aprovou.
    /// </para>
    /// <para>
    /// O que ele <em>nao</em> faz, ao contrario do que a formulacao original da propriedade sugeria,
    /// e registrar a excursao mais baixa depois da abertura: como o piso inicial e zero e nenhuma
    /// conta desce abaixo disso, uma conta que vai a 1000, cai para 100 e volta a 900 continua com
    /// minimo zero.
    /// </para>
    /// </summary>
    Saldo MinimoNatural(ContaId conta);

    bool ContemChave(ChaveIdempotencia chave);

    bool ContemConta(ContaId conta);

    /// <summary>Estado corrente da projecao incremental, para comparacao com o replay.</summary>
    SnapshotSaldos Snapshot();

    /// <summary>Log e projecao sob a mesma aquisicao do lock, para que o replay compare o mesmo instante.</summary>
    (IReadOnlyList<Commit> Log, SnapshotSaldos Projecao) LerConsistente();
}
