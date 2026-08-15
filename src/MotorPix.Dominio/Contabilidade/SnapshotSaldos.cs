using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Fotografia comparavel de uma projecao de saldos.
/// <para>
/// Serve de moeda comum entre a projecao incremental do <see cref="Ledger"/> e o fold puro de
/// <see cref="ProjecaoSaldos"/>. As duas produzem este tipo por caminhos de codigo independentes;
/// se produzissem o mesmo objeto pela mesma funcao, o teste de replay provaria apenas que a
/// funcao e deterministica.
/// </para>
/// </summary>
public sealed class SnapshotSaldos : IEquatable<SnapshotSaldos>
{
    private readonly Dictionary<ContaId, Saldo> _naturais;
    private readonly Dictionary<ContaId, Saldo> _minimos;

    internal SnapshotSaldos(Dictionary<ContaId, Saldo> naturais, Dictionary<ContaId, Saldo> minimos)
    {
        _naturais = naturais;
        _minimos = minimos;
    }

    public IReadOnlyCollection<ContaId> Contas => _naturais.Keys;

    /// <summary>
    /// Saldo natural da conta.
    /// <para>
    /// Lanca para conta ausente em vez de devolver zero: se devolvesse zero, a conta que sumiu da
    /// projecao reconstruida ficaria indistinguivel da conta que existe e esta zerada — que e
    /// exatamente o defeito que o replay existe para pegar.
    /// </para>
    /// </summary>
    public Saldo Natural(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        return _naturais.TryGetValue(conta, out Saldo saldo)
            ? saldo
            : throw new ContaDesconhecidaException(conta);
    }

    public Saldo MinimoNatural(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        return _minimos.TryGetValue(conta, out Saldo saldo)
            ? saldo
            : throw new ContaDesconhecidaException(conta);
    }

    public bool Contem(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        return _naturais.ContainsKey(conta);
    }

    /// <summary>Delega a <see cref="EhIgualA"/>, para que <c>Assert.Equal</c> nao compare referencia.</summary>
    public bool Equals(SnapshotSaldos? outro) => outro is not null && EhIgualA(outro);

    public override bool Equals(object? obj) => obj is SnapshotSaldos outro && Equals(outro);

    public override int GetHashCode() => _naturais.Count;

    /// <summary>
    /// Igualdade estrutural incluindo o <em>conjunto de chaves</em>.
    /// Comparar somente valores deixaria passar uma conta que zerou e desapareceu da projecao
    /// reconstruida — que e exatamente o defeito que o gate de replay precisa pegar.
    /// </summary>
    public bool EhIgualA(SnapshotSaldos outro)
    {
        ArgumentNullException.ThrowIfNull(outro);

        if (_naturais.Count != outro._naturais.Count || _minimos.Count != outro._minimos.Count)
        {
            return false;
        }

        foreach ((ContaId conta, Saldo saldo) in _naturais)
        {
            if (!outro._naturais.TryGetValue(conta, out Saldo saldoOutro) || saldo != saldoOutro)
            {
                return false;
            }
        }

        foreach ((ContaId conta, Saldo minimo) in _minimos)
        {
            if (!outro._minimos.TryGetValue(conta, out Saldo minimoOutro) || minimo != minimoOutro)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Descricao das diferencas, para que a falha do teste diga qual conta divergiu.</summary>
    public IReadOnlyList<string> Diferencas(SnapshotSaldos outro)
    {
        ArgumentNullException.ThrowIfNull(outro);

        List<string> diferencas = [];

        foreach (ContaId conta in _naturais.Keys.Union(outro._naturais.Keys))
        {
            bool aqui = _naturais.TryGetValue(conta, out Saldo saldoAqui);
            bool la = outro._naturais.TryGetValue(conta, out Saldo saldoLa);

            if (!aqui)
            {
                diferencas.Add($"{conta}: ausente a esquerda, {saldoLa} a direita");
            }
            else if (!la)
            {
                diferencas.Add($"{conta}: {saldoAqui} a esquerda, ausente a direita");
            }
            else if (saldoAqui != saldoLa)
            {
                diferencas.Add($"{conta}: {saldoAqui} a esquerda, {saldoLa} a direita");
            }
        }

        foreach (ContaId conta in _minimos.Keys.Union(outro._minimos.Keys))
        {
            Saldo minimoAqui = _minimos.TryGetValue(conta, out Saldo mA) ? mA : Saldo.Zero;
            Saldo minimoLa = outro._minimos.TryGetValue(conta, out Saldo mL) ? mL : Saldo.Zero;
            if (minimoAqui != minimoLa)
            {
                diferencas.Add($"{conta} (minimo): {minimoAqui} a esquerda, {minimoLa} a direita");
            }
        }

        return diferencas;
    }
}
