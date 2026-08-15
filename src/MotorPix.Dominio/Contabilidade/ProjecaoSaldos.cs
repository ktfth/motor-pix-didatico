using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Reconstroi a projecao de saldos do zero, a partir do log.
/// <para>
/// E uma segunda implementacao de verdade, e nao um segundo laco sobre a mesma funcao. Ela acumula
/// <c>somaDebitos</c> e <c>somaCreditos</c> separados por conta e deriva o saldo natural com sua
/// propria expressao, sem chamar <see cref="Conta.NaturalDe"/>. Se as duas convergissem naquele
/// metodo, inverter o sinal la dentro produziria o mesmo valor errado dos dois lados, o replay
/// ficaria verde e nenhuma classe de bug de natureza contabil seria alcancavel pelo gate.
/// </para>
/// <para>
/// Funcao pura do log: nao consulta <c>IClock</c>, nao gera identificador e nao guarda nada fora
/// do que recebeu. Se precisasse de tempo, o tempo ja esta gravado no commit.
/// </para>
/// </summary>
public sealed class ProjecaoSaldos
{
    private readonly Dictionary<ContaId, Conta> _contas = [];
    private readonly Dictionary<ContaId, long> _debitos = [];
    private readonly Dictionary<ContaId, long> _creditos = [];
    private readonly Dictionary<ContaId, long> _minimosNaturais = [];

    private ProjecaoSaldos()
    {
    }

    public IReadOnlyCollection<ContaId> Contas => _contas.Keys;

    /// <summary>
    /// Reconstroi a partir do log <em>completo</em>, comecando na sequencia 1 e sem buracos.
    /// <para>
    /// A exigencia e explicita porque <c>IConsultaLedger.Log(desdeSequencia)</c> devolve exatamente
    /// o tipo aceito aqui: passar um recorte que nao inclua os commits de abertura e uma armadilha
    /// de API, nao um uso exotico, e o erro precisa ser de dominio em vez de
    /// <c>KeyNotFoundException</c> vinda do indexador.
    /// </para>
    /// </summary>
    public static ProjecaoSaldos Reconstruir(IEnumerable<Commit> log)
    {
        ArgumentNullException.ThrowIfNull(log);

        ProjecaoSaldos projecao = new();
        long esperada = 1;

        foreach (Commit commit in log)
        {
            if (commit.Sequencia != esperada)
            {
                throw new AtoContabilInvalidoException(
                    $"log incompleto para replay: esperava sequencia {esperada}, veio {commit.Sequencia}");
            }

            projecao.Aplicar(commit);
            esperada++;
        }

        return projecao;
    }

    public Saldo SaldoNatural(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        ExigirContaConhecida(conta);

        return NaturalPropria(_contas[conta].Natureza, conta);
    }

    public Saldo MinimoNatural(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        ExigirContaConhecida(conta);

        return new Saldo(_minimosNaturais[conta]);
    }

    public SnapshotSaldos Snapshot()
    {
        Dictionary<ContaId, Saldo> naturais = new(_contas.Count);
        Dictionary<ContaId, Saldo> minimos = new(_contas.Count);

        foreach ((ContaId conta, Conta definicao) in _contas)
        {
            naturais.Add(conta, NaturalPropria(definicao.Natureza, conta));
            minimos.Add(conta, new Saldo(_minimosNaturais[conta]));
        }

        return new SnapshotSaldos(naturais, minimos);
    }

    /// <summary>
    /// Deriva o saldo natural das somas acumuladas, sem passar por <see cref="Conta.NaturalDe"/>.
    /// Ativo cresce a debito, passivo cresce a credito — a definicao escrita aqui de novo, de
    /// proposito, para que o oraculo do replay seja independente do codigo sob teste.
    /// </summary>
    private Saldo NaturalPropria(Natureza natureza, ContaId conta)
    {
        long debitos = _debitos[conta];
        long creditos = _creditos[conta];

        try
        {
            return natureza == Natureza.Ativo
                ? new Saldo(checked(debitos - creditos))
                : new Saldo(checked(creditos - debitos));
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("saldo natural no replay", debitos, creditos);
        }
    }

    private void Aplicar(Commit commit)
    {
        foreach (Conta conta in commit.ContasAbertas)
        {
            _contas[conta.Id] = conta;
            _debitos[conta.Id] = 0L;
            _creditos[conta.Id] = 0L;
            _minimosNaturais[conta.Id] = 0L;
        }

        foreach (Lancamento lancamento in commit.Lancamentos)
        {
            foreach (Partida partida in lancamento.Partidas)
            {
                ExigirContaConhecida(partida.Conta);

                Dictionary<ContaId, long> lado = partida.Lado == Lado.Debito ? _debitos : _creditos;
                try
                {
                    lado[partida.Conta] = checked(lado[partida.Conta] + partida.Valor.Centavos);
                }
                catch (OverflowException)
                {
                    throw new EstouroDeValorException("acumulo no replay", lado[partida.Conta], partida.Valor.Centavos);
                }
            }
        }

        // O minimo e avaliado no estado final do commit, nao entre pernas: um commit e atomico,
        // entao seus estados intermediarios nunca foram observaveis.
        foreach ((ContaId conta, Conta definicao) in _contas)
        {
            long natural = NaturalPropria(definicao.Natureza, conta).Centavos;
            if (natural < _minimosNaturais[conta])
            {
                _minimosNaturais[conta] = natural;
            }
        }
    }

    private void ExigirContaConhecida(ContaId conta)
    {
        // Mesmo contrato do Ledger. Devolver Saldo.Zero para conta desconhecida faria a conta que
        // "zerou e sumiu da projecao" — o defeito que o gate de replay existe para pegar — ficar
        // indistinguivel de uma conta que existe e esta zerada.
        if (!_contas.ContainsKey(conta))
        {
            throw new ContaDesconhecidaException(conta);
        }
    }
}
