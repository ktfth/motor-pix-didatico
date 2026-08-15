using System.Collections.ObjectModel;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Ledger append-only de partidas dobradas, com projecao de saldos mantida de forma incremental.
/// <para>
/// A projecao aqui e escrita <em>durante</em> o append, acumulando debito e credito direto. O fold
/// puro que reconstroi a mesma projecao a partir do log vive em <see cref="ProjecaoSaldos"/> e e
/// deliberadamente uma segunda implementacao, que acumula somas separadas e deriva a natureza por
/// conta propria: e a divergencia entre as duas que o gate de replay procura.
/// </para>
/// </summary>
public sealed class Ledger : ILedger, IConsultaLedger, IInspecaoLedger
{
    private readonly object _trava = new();
    private readonly List<Commit> _commits = [];
    private readonly Dictionary<ContaId, Conta> _contas = [];

    /// <summary>Saldo bruto por conta: soma dos creditos menos soma dos debitos.</summary>
    private readonly Dictionary<ContaId, long> _brutos = [];

    private readonly Dictionary<ContaId, long> _minimosNaturais = [];
    private readonly HashSet<ChaveIdempotencia> _chaves = [];
    private readonly IClock _relogio;

    private long _proximaSequencia = 1;
    private bool _houveLancamentoOperacional;
    private bool _apendando;

    public Ledger(LedgerId id, IClock relogio)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(relogio);

        Id = id;
        _relogio = relogio;
    }

    public LedgerId Id { get; }

    public Commit Abrir(params Conta[] contas)
    {
        if (contas is null || contas.Length == 0)
        {
            throw new AtoContabilInvalidoException("nenhuma conta para abrir");
        }

        // Copia defensiva antes de qualquer validacao. O lock protege o estado do ledger, nao o
        // buffer do chamador: sem a copia, outra thread poderia trocar um item entre a validacao
        // e a gravacao, e o commit registrado nao seria o que foi validado.
        Conta[] itens = [.. contas];

        lock (_trava)
        {
            ExigirNaoReentrante();

            HashSet<ContaId> noLote = [];

            foreach (Conta conta in itens)
            {
                if (conta is null)
                {
                    throw new AtoContabilInvalidoException("conta nula no lote");
                }

                if (!conta.Id.Ledger.Equals(Id))
                {
                    throw new LedgerIncorretoException(Id, conta.Id.Ledger);
                }

                if (_contas.ContainsKey(conta.Id) || !noLote.Add(conta.Id))
                {
                    throw new ContaJaAbertaException(conta.Id);
                }
            }

            _apendando = true;
            try
            {
                Commit commit = new(_proximaSequencia, Id, _relogio.Agora, itens, []);

                foreach (Conta conta in itens)
                {
                    _contas.Add(conta.Id, conta);
                    _brutos.Add(conta.Id, 0L);
                    _minimosNaturais.Add(conta.Id, 0L);
                }

                _commits.Add(commit);
                _proximaSequencia++;
                return commit;
            }
            finally
            {
                _apendando = false;
            }
        }
    }

    public Commit Lancar(params Lancamento[] lancamentos)
    {
        if (lancamentos is null || lancamentos.Length == 0)
        {
            throw new AtoContabilInvalidoException("nenhum lancamento no ato");
        }

        Lancamento[] itens = [.. lancamentos];

        lock (_trava)
        {
            ExigirNaoReentrante();

            // Fase 1: validacao e calculo, sem tocar em nenhum estado.
            bool atoEhGenesis = ClassificarAto(itens);

            Dictionary<ContaId, long> deltas = [];
            HashSet<ChaveIdempotencia> chavesDoLote = [];

            foreach (Lancamento lancamento in itens)
            {
                if (!lancamento.Ledger.Equals(Id))
                {
                    throw new LedgerIncorretoException(Id, lancamento.Ledger);
                }

                if (_chaves.Contains(lancamento.Chave) || !chavesDoLote.Add(lancamento.Chave))
                {
                    throw new ChaveIdempotenciaDuplicadaException(lancamento.Chave.Texto);
                }

                ExigirContaConhecida(lancamento.Debito);
                ExigirContaConhecida(lancamento.Credito);

                if (!atoEhGenesis)
                {
                    ExigirQueNaoSejaContaDeAbertura(lancamento.Debito);
                    ExigirQueNaoSejaContaDeAbertura(lancamento.Credito);
                }

                Acumular(deltas, lancamento.Debito, -lancamento.Valor.Centavos);
                Acumular(deltas, lancamento.Credito, lancamento.Valor.Centavos);
            }

            // Fase 2: guarda de saldo sobre o resultado do ato inteiro.
            // Avaliar conta a conta durante a aplicacao deixaria o estado meio-gravado quando a
            // segunda perna violasse a guarda.
            Dictionary<ContaId, long> novosBrutos = new(deltas.Count);
            Dictionary<ContaId, Saldo> novosNaturais = new(deltas.Count);

            // Ordem estavel: quando um ato derruba mais de uma conta — o caso normal no PSP, em que
            // cliente e espelho caem juntos —, qual delas a excecao acusa nao pode depender da ordem
            // de enumeracao do dicionario, ou a mensagem de erro muda entre execucoes.
            foreach ((ContaId conta, long delta) in deltas.OrderBy(par => par.Key.Chave, StringComparer.Ordinal))
            {
                long atual = _brutos[conta];
                long novo;
                try
                {
                    novo = checked(atual + delta);
                }
                catch (OverflowException)
                {
                    throw new EstouroDeValorException("saldo do ledger", atual, delta);
                }

                Conta definicao = _contas[conta];
                Saldo resultante = definicao.NaturalDe(novo);

                if (resultante.EhNegativo)
                {
                    throw new SaldoNegativoException(conta, definicao.NaturalDe(atual), resultante);
                }

                novosBrutos.Add(conta, novo);
                novosNaturais.Add(conta, resultante);
            }

            // Fase 3: append. A partir daqui nada mais pode falhar — os saldos naturais ja foram
            // calculados na fase 2 e sao reaproveitados, em vez de recalculados sobre estado escrito.
            _apendando = true;
            try
            {
                Commit commit = new(_proximaSequencia, Id, _relogio.Agora, [], itens);

                foreach ((ContaId conta, long novo) in novosBrutos)
                {
                    _brutos[conta] = novo;

                    long naturalNovo = novosNaturais[conta].Centavos;
                    if (naturalNovo < _minimosNaturais[conta])
                    {
                        _minimosNaturais[conta] = naturalNovo;
                    }
                }

                foreach (Lancamento lancamento in itens)
                {
                    _chaves.Add(lancamento.Chave);
                }

                _commits.Add(commit);
                _proximaSequencia++;

                if (!atoEhGenesis)
                {
                    _houveLancamentoOperacional = true;
                }

                return commit;
            }
            finally
            {
                _apendando = false;
            }
        }
    }

    public Saldo SaldoNatural(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        lock (_trava)
        {
            ExigirContaConhecida(conta);
            return _contas[conta].NaturalDe(_brutos[conta]);
        }
    }

    /// <summary>
    /// Saldo bruto (<c>soma dos creditos - soma dos debitos</c>).
    /// <para>
    /// Exposto porque e a unica grandeza para a qual "a soma e constante" vale literalmente: a soma
    /// dos saldos <em>brutos</em> de um ledger e sempre zero, por partidas dobradas. A soma dos
    /// saldos <em>naturais</em> nao e constante — na seta 3, cliente e espelho caem juntos — e quem
    /// escrever a propriedade sobre naturais obtem uma falha falsa.
    /// </para>
    /// </summary>
    public Saldo SaldoBruto(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        lock (_trava)
        {
            ExigirContaConhecida(conta);
            return new Saldo(_brutos[conta]);
        }
    }

    public Saldo MinimoNatural(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        lock (_trava)
        {
            ExigirContaConhecida(conta);
            return new Saldo(_minimosNaturais[conta]);
        }
    }

    public bool ContemChave(ChaveIdempotencia chave)
    {
        ArgumentNullException.ThrowIfNull(chave);

        lock (_trava)
        {
            return _chaves.Contains(chave);
        }
    }

    public bool ContemConta(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        lock (_trava)
        {
            return _contas.ContainsKey(conta);
        }
    }

    public SnapshotSaldos Snapshot()
    {
        lock (_trava)
        {
            return SnapshotInterno();
        }
    }

    /// <summary>
    /// Log e projecao sob a mesma aquisicao do lock.
    /// <para>
    /// Existe porque <see cref="Log"/> seguido de <see cref="Snapshot"/> nao compoe: um append
    /// entre as duas chamadas faz o gate de replay acusar divergencia que nunca existiu.
    /// </para>
    /// </summary>
    public (IReadOnlyList<Commit> Log, SnapshotSaldos Projecao) LerConsistente()
    {
        lock (_trava)
        {
            return (new ReadOnlyCollection<Commit>([.. _commits]), SnapshotInterno());
        }
    }

    /// <summary>
    /// Commits com sequencia estritamente maior que <paramref name="desdeSequencia"/>.
    /// O default zero devolve o log inteiro, porque a numeracao comeca em 1.
    /// <para>
    /// Os dois ramos devolvem <see cref="ReadOnlyCollection{T}"/>. Devolver o tipo sintetizado de
    /// uma expressao de colecao num ramo e um <c>List</c> real no outro faria a imutabilidade do
    /// log depender de qual ramo foi chamado — e de qual compilador compilou.
    /// </para>
    /// </summary>
    public IReadOnlyList<Commit> Log(long desdeSequencia = 0)
    {
        lock (_trava)
        {
            if (desdeSequencia <= 0)
            {
                return new ReadOnlyCollection<Commit>([.. _commits]);
            }

            List<Commit> recorte = [];
            foreach (Commit commit in _commits)
            {
                if (commit.Sequencia > desdeSequencia)
                {
                    recorte.Add(commit);
                }
            }

            return new ReadOnlyCollection<Commit>(recorte);
        }
    }

    private SnapshotSaldos SnapshotInterno()
    {
        Dictionary<ContaId, Saldo> naturais = new(_brutos.Count);
        Dictionary<ContaId, Saldo> minimos = new(_minimosNaturais.Count);

        foreach ((ContaId conta, long bruto) in _brutos)
        {
            naturais.Add(conta, _contas[conta].NaturalDe(bruto));
        }

        foreach ((ContaId conta, long minimo) in _minimosNaturais)
        {
            minimos.Add(conta, new Saldo(minimo));
        }

        return new SnapshotSaldos(naturais, minimos);
    }

    /// <summary>
    /// Diz se o ato e de genesis, recusando ato misto e genesis tardio.
    /// O genesis e um prefixo do log; e isso que sela a emissao sem exigir uma chamada explicita
    /// de "fechar abertura", que alguem pode esquecer de fazer.
    /// </summary>
    private bool ClassificarAto(Lancamento[] itens)
    {
        int genesis = 0;

        foreach (Lancamento lancamento in itens)
        {
            if (lancamento is null)
            {
                throw new AtoContabilInvalidoException("lancamento nulo no ato");
            }

            if (lancamento.Chave.Etapa == EtapaLancamento.Genesis)
            {
                genesis++;
            }
        }

        if (genesis == 0)
        {
            return false;
        }

        if (genesis != itens.Length)
        {
            throw new AtoContabilInvalidoException("ato mistura lancamentos de genesis e operacionais");
        }

        if (_houveLancamentoOperacional)
        {
            throw new GenesisAposOperacaoException(Id);
        }

        return true;
    }

    private void ExigirNaoReentrante()
    {
        // lock e reentrante na mesma thread: um IClock que voltasse a chamar o ledger executaria um
        // append aninhado no meio da fase 3, duplicando o numero de sequencia e descartando em
        // silencio o efeito do commit interno.
        if (_apendando)
        {
            throw new AtoContabilInvalidoException("append reentrante no mesmo ledger");
        }
    }

    private void ExigirContaConhecida(ContaId conta)
    {
        if (!_contas.ContainsKey(conta))
        {
            throw new ContaDesconhecidaException(conta);
        }
    }

    private void ExigirQueNaoSejaContaDeAbertura(ContaId conta)
    {
        if (_contas[conta].EhContraContaDeAbertura)
        {
            throw new AberturaAposGenesisException(conta);
        }
    }

    private static void Acumular(Dictionary<ContaId, long> deltas, ContaId conta, long delta)
    {
        long atual = deltas.TryGetValue(conta, out long existente) ? existente : 0L;
        try
        {
            deltas[conta] = checked(atual + delta);
        }
        catch (OverflowException)
        {
            throw new EstouroDeValorException("acumulo do ato contabil", atual, delta);
        }
    }
}
