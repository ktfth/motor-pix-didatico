using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>Operacao contabil que a sequencia sabe produzir. O valor numerico e a seta do desenho.</summary>
public enum TipoOperacao
{
    DebitarPagador = 3,
    Liquidar = 5,
    CreditarRecebedor = 7,
    EstornarPagador = 30,
}

/// <summary>
/// O que o gerador afirma que vai acontecer com a operacao.
/// <para>
/// Uma operacao marcada como falha e a representacao de "falha no meio" num sistema in-process:
/// o executor a envolve em try/catch de <see cref="MotorPixException"/> e segue com o estado como
/// ficou. Sem isso, o invariante 3 seria testado apenas no caminho feliz, que e onde ele nunca quebra.
/// </para>
/// </summary>
public enum Expectativa
{
    /// <summary>Operacao valida pelo estado do modelo: tem de ser aceita pelo ledger.</summary>
    Aplica = 0,

    /// <summary>
    /// Valor maior que o saldo do cliente pagador: tem de bater na guarda de saldo negativo, que e
    /// unica e vale para toda conta do sistema.
    /// </summary>
    FalhaPorSaldo = 1,

    /// <summary>Reenvio literal de uma operacao ja gravada: tem de bater na chave de idempotencia.</summary>
    FalhaPorChaveRepetida = 2,
}

/// <summary>Estado observavel de um ledger no instante imediatamente posterior a uma operacao.</summary>
public sealed record FotoDeLedger(LedgerId Ledger, int Commits, SnapshotSaldos Snapshot);

/// <summary>
/// O que foi tentado, se lancou, qual excecao, e como os tres ledgers ficaram depois disso.
/// <para>
/// As fotos existem para que as propriedades sejam avaliadas <em>em todo ponto</em> da sequencia,
/// e nao so no final: uma sequencia que passa por um estado ilegal e volta ao legal nao e denunciada
/// pelo estado final.
/// </para>
/// </summary>
public sealed record RegistroOperacao
{
    public required int Indice { get; init; }

    public required TipoOperacao Tipo { get; init; }

    public required EndToEndId E2E { get; init; }

    public required Valor Valor { get; init; }

    public required Expectativa Expectativa { get; init; }

    /// <summary>Verdadeiro quando a operacao lancou excecao de dominio.</summary>
    public required bool Lancou { get; init; }

    public required MotorPixException? Excecao { get; init; }

    /// <summary>Uma foto por ledger, na mesma ordem de <c>CenarioContabil.Ledgers</c>.</summary>
    public required IReadOnlyList<FotoDeLedger> Fotos { get; init; }

    public bool Aplicou => !Lancou;

    public SnapshotSaldos SnapshotDe(LedgerId ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        foreach (FotoDeLedger foto in Fotos)
        {
            if (foto.Ledger.Equals(ledger))
            {
                return foto.Snapshot;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(ledger), ledger, "ledger fora da foto da sequencia");
    }

    public override string ToString() =>
        $"#{Indice} {Tipo} {E2E} {Valor} esperado={Expectativa} obtido={Excecao?.GetType().Name ?? "aplicado"}";
}

/// <summary>Sequencia executada: a semente que a produziu, o cenario resultante e o que aconteceu.</summary>
public sealed record ResultadoSequencia
{
    public required long Semente { get; init; }

    public required CenarioContabil Cenario { get; init; }

    public required IReadOnlyList<RegistroOperacao> Registros { get; init; }
}

/// <summary>
/// Gerador deterministico de sequencias de operacoes contabeis, dirigido pelo estado.
/// <para>
/// Dirigido pelo estado de proposito: so produz <c>Liquidar</c> para um E2E ja debitado, so produz
/// <c>CreditarRecebedor</c> para um E2E ja liquidado e so produz estorno para um E2E debitado, nao
/// liquidado e nao estornado. Gerar operacoes cegas gastaria a maior parte das execucoes em
/// rejeicoes triviais, e a propriedade passaria por vacuidade.
/// </para>
/// <para>
/// Aleatoriedade vem de um xorshift proprio alimentado por semente explicita. <c>System.Random</c>
/// nao entra: alem de banido no dominio, sua sequencia nao e contratualmente estavel entre versoes
/// do runtime, e um contraexemplo que nao reproduz por semente nao vale nada.
/// </para>
/// </summary>
public sealed class GeradorDeSequencias
{
    /// <summary>Teto do valor de um debito valido. Segura a sequencia longe da exaustao imediata do fundo.</summary>
    private const long TetoDoValorEmCentavos = 2_000;

    private const long ExcessoMaximoEmCentavos = 1_000;

    private readonly long _semente;
    private readonly Xorshift _sorteio;
    private readonly CenarioContabil _cenario;
    private readonly GeradorE2eDeterministico _e2e;

    private readonly Dictionary<EndToEndId, EstadoE2e> _estados = [];

    /// <summary>Ordem de criacao dos E2E. A escolha nunca itera o dicionario: ordem de hash nao e estavel.</summary>
    private readonly List<EndToEndId> _ordem = [];

    private readonly List<OperacaoAplicada> _aplicados = [];

    private long _saldoClientePagador;

    private GeradorDeSequencias(long semente, CenarioContabil cenario)
    {
        _semente = semente;
        _sorteio = new Xorshift(semente);
        _cenario = cenario;
        _e2e = new GeradorE2eDeterministico(cenario.IspbPagador, cenario.Relogio.Agora);
        _saldoClientePagador = cenario.FundoInicial.Centavos;
    }

    /// <summary>Monta um cenario novo e roda <paramref name="operacoes"/> passos sobre ele.</summary>
    public static ResultadoSequencia Executar(long semente, int operacoes)
    {
        if (operacoes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operacoes), operacoes, "sequencia sem operacoes");
        }

        CenarioContabil cenario = CenarioContabil.Padrao();
        GeradorDeSequencias gerador = new(semente, cenario);
        return gerador.Rodar(operacoes);
    }

    /// <summary>
    /// Executa uma operacao contra o cenario. E o unico ponto que traduz <see cref="TipoOperacao"/>
    /// em chamada: o teste de idempotencia reenvia a operacao por aqui, e nao por uma copia do
    /// switch que pudesse divergir deste.
    /// </summary>
    public static Commit Despachar(CenarioContabil cenario, TipoOperacao tipo, EndToEndId e2e, Valor valor)
    {
        ArgumentNullException.ThrowIfNull(cenario);
        ArgumentNullException.ThrowIfNull(e2e);

        return tipo switch
        {
            TipoOperacao.DebitarPagador => cenario.DebitarPagador(e2e, valor),
            TipoOperacao.Liquidar => cenario.Liquidar(e2e, valor),
            TipoOperacao.CreditarRecebedor => cenario.CreditarRecebedor(e2e, valor),
            TipoOperacao.EstornarPagador => cenario.EstornarPagador(e2e, valor),
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "tipo de operacao desconhecido"),
        };
    }

    /// <summary>Etapa contabil correspondente a operacao. Escrita a mao, sem cast entre enums.</summary>
    public static EtapaLancamento EtapaDe(TipoOperacao tipo) => tipo switch
    {
        TipoOperacao.DebitarPagador => EtapaLancamento.DebitoPagador,
        TipoOperacao.Liquidar => EtapaLancamento.Liquidacao,
        TipoOperacao.CreditarRecebedor => EtapaLancamento.CreditoRecebedor,
        TipoOperacao.EstornarPagador => EtapaLancamento.EstornoPagador,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "tipo de operacao desconhecido"),
    };

    /// <summary>Ledger em que a operacao e gravada.</summary>
    public static Ledger LedgerDe(CenarioContabil cenario, TipoOperacao tipo)
    {
        ArgumentNullException.ThrowIfNull(cenario);

        return tipo switch
        {
            TipoOperacao.DebitarPagador => cenario.LedgerPagador,
            TipoOperacao.EstornarPagador => cenario.LedgerPagador,
            TipoOperacao.Liquidar => cenario.LedgerSpi,
            TipoOperacao.CreditarRecebedor => cenario.LedgerRecebedor,
            _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "tipo de operacao desconhecido"),
        };
    }

    private ResultadoSequencia Rodar(int operacoes)
    {
        List<RegistroOperacao> registros = new(operacoes);

        for (int indice = 0; indice < operacoes; indice++)
        {
            registros.Add(Passo(indice));
            _cenario.Relogio.AvancarSegundos(1);
        }

        return new ResultadoSequencia
        {
            Semente = _semente,
            Cenario = _cenario,
            Registros = registros,
        };
    }

    private RegistroOperacao Passo(int indice)
    {
        Plano plano = Planejar();

        MotorPixException? excecao = null;
        try
        {
            Aplicar(plano);
        }
        catch (MotorPixException erro)
        {
            // Falha no meio da sequencia: o estado fica como ficou e o proximo passo continua dali.
            excecao = erro;
        }

        return new RegistroOperacao
        {
            Indice = indice,
            Tipo = plano.Tipo,
            E2E = plano.E2E,
            Valor = plano.Valor,
            Expectativa = plano.Expectativa,
            Lancou = excecao is not null,
            Excecao = excecao,
            Fotos = Fotografar(),
        };
    }

    private Plano Planejar()
    {
        List<EstadoE2e> aguardandoLiquidacao = Pendentes(liquidacao: true);
        List<EstadoE2e> aguardandoCredito = Pendentes(liquidacao: false);

        List<Candidato> candidatos = [];

        if (_saldoClientePagador > 0)
        {
            candidatos.Add(new Candidato(TipoOperacao.DebitarPagador, Expectativa.Aplica, 5));
        }

        if (aguardandoLiquidacao.Count > 0)
        {
            candidatos.Add(new Candidato(TipoOperacao.Liquidar, Expectativa.Aplica, 4));
            candidatos.Add(new Candidato(TipoOperacao.EstornarPagador, Expectativa.Aplica, 3));
        }

        if (aguardandoCredito.Count > 0)
        {
            candidatos.Add(new Candidato(TipoOperacao.CreditarRecebedor, Expectativa.Aplica, 4));
        }

        // Falha por saldo cabe sempre: o E2E e novo e o valor e maior que o saldo do cliente.
        candidatos.Add(new Candidato(TipoOperacao.DebitarPagador, Expectativa.FalhaPorSaldo, 2));

        if (_aplicados.Count > 0)
        {
            // O tipo aqui e so o do candidato; o reenvio copia tipo, E2E e valor da operacao sorteada.
            candidatos.Add(new Candidato(TipoOperacao.DebitarPagador, Expectativa.FalhaPorChaveRepetida, 2));
        }

        Candidato escolhido = Sortear(candidatos);

        return escolhido.Expectativa switch
        {
            Expectativa.FalhaPorChaveRepetida => Reenviar(),
            Expectativa.FalhaPorSaldo => new Plano(
                TipoOperacao.DebitarPagador,
                _e2e.Proximo(),
                Valor.DeCentavos(_saldoClientePagador + 1 + _sorteio.Ate(ExcessoMaximoEmCentavos)),
                Expectativa.FalhaPorSaldo),
            _ => Valida(escolhido.Tipo, aguardandoLiquidacao, aguardandoCredito),
        };
    }

    private Plano Valida(
        TipoOperacao tipo,
        List<EstadoE2e> aguardandoLiquidacao,
        List<EstadoE2e> aguardandoCredito)
    {
        switch (tipo)
        {
            case TipoOperacao.DebitarPagador:
                long teto = Math.Min(_saldoClientePagador, TetoDoValorEmCentavos);
                return new Plano(
                    tipo,
                    _e2e.Proximo(),
                    Valor.DeCentavos(1 + _sorteio.Ate(teto)),
                    Expectativa.Aplica);

            case TipoOperacao.Liquidar:
            case TipoOperacao.EstornarPagador:
                EstadoE2e emAberto = aguardandoLiquidacao[_sorteio.Indice(aguardandoLiquidacao.Count)];
                return new Plano(tipo, emAberto.E2E, emAberto.Valor, Expectativa.Aplica);

            case TipoOperacao.CreditarRecebedor:
                EstadoE2e liquidado = aguardandoCredito[_sorteio.Indice(aguardandoCredito.Count)];
                return new Plano(tipo, liquidado.E2E, liquidado.Valor, Expectativa.Aplica);

            default:
                throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "tipo de operacao desconhecido");
        }
    }

    private Plano Reenviar()
    {
        OperacaoAplicada anterior = _aplicados[_sorteio.Indice(_aplicados.Count)];
        return new Plano(anterior.Tipo, anterior.E2E, anterior.Valor, Expectativa.FalhaPorChaveRepetida);
    }

    private void Aplicar(Plano plano)
    {
        Despachar(_cenario, plano.Tipo, plano.E2E, plano.Valor);

        if (plano.Expectativa != Expectativa.Aplica)
        {
            // Operacao que deveria ter sido recusada e passou: o modelo fica intocado de proposito,
            // para que a assercao de expectativa denuncie a regressao em vez de acompanha-la.
            return;
        }

        Registrar(plano);
    }

    private void Registrar(Plano plano)
    {
        switch (plano.Tipo)
        {
            case TipoOperacao.DebitarPagador:
                _estados.Add(plano.E2E, new EstadoE2e(plano.E2E, plano.Valor) { Debitado = true });
                _ordem.Add(plano.E2E);
                _saldoClientePagador -= plano.Valor.Centavos;
                break;

            case TipoOperacao.Liquidar:
                _estados[plano.E2E] = _estados[plano.E2E] with { Liquidado = true };
                break;

            case TipoOperacao.CreditarRecebedor:
                _estados[plano.E2E] = _estados[plano.E2E] with { Creditado = true };
                break;

            case TipoOperacao.EstornarPagador:
                _estados[plano.E2E] = _estados[plano.E2E] with { Estornado = true };
                _saldoClientePagador += plano.Valor.Centavos;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(plano), plano.Tipo, "tipo de operacao desconhecido");
        }

        _aplicados.Add(new OperacaoAplicada(plano.Tipo, plano.E2E, plano.Valor));
    }

    /// <summary>
    /// Pendentes de liquidacao (debitado, nao liquidado, nao estornado) ou pendentes de credito
    /// (liquidado, nao creditado).
    /// </summary>
    private List<EstadoE2e> Pendentes(bool liquidacao)
    {
        List<EstadoE2e> pendentes = [];

        foreach (EndToEndId id in _ordem)
        {
            EstadoE2e estado = _estados[id];

            bool selecionado = liquidacao
                ? estado.Debitado && !estado.Liquidado && !estado.Estornado
                : estado.Liquidado && !estado.Creditado;

            if (selecionado)
            {
                pendentes.Add(estado);
            }
        }

        return pendentes;
    }

    private IReadOnlyList<FotoDeLedger> Fotografar()
    {
        List<FotoDeLedger> fotos = new(_cenario.Ledgers.Count);

        foreach (Ledger ledger in _cenario.Ledgers)
        {
            // As duas leituras sob a mesma aquisicao do lock. A foto e o par "quantos commits" com
            // "qual projecao", e o teste de prefixo compara exatamente esse par: lido em dois
            // momentos, o par descreveria dois instantes e a divergencia acusada seria inventada
            // pela leitura, nao pelo fold.
            (IReadOnlyList<Commit> log, SnapshotSaldos projecao) = ledger.LerConsistente();
            fotos.Add(new FotoDeLedger(ledger.Id, log.Count, projecao));
        }

        return fotos;
    }

    private Candidato Sortear(List<Candidato> candidatos)
    {
        int total = 0;
        foreach (Candidato candidato in candidatos)
        {
            total += candidato.Peso;
        }

        int bilhete = _sorteio.Indice(total);

        foreach (Candidato candidato in candidatos)
        {
            if (bilhete < candidato.Peso)
            {
                return candidato;
            }

            bilhete -= candidato.Peso;
        }

        return candidatos[^1];
    }

    private sealed record Candidato(TipoOperacao Tipo, Expectativa Expectativa, int Peso);

    private sealed record Plano(TipoOperacao Tipo, EndToEndId E2E, Valor Valor, Expectativa Expectativa);

    private sealed record OperacaoAplicada(TipoOperacao Tipo, EndToEndId E2E, Valor Valor);

    private sealed record EstadoE2e(EndToEndId E2E, Valor Valor)
    {
        public bool Debitado { get; init; }

        public bool Liquidado { get; init; }

        public bool Creditado { get; init; }

        public bool Estornado { get; init; }
    }

    /// <summary>
    /// Xorshift64 de tres deslocamentos. Dez linhas, sem dependencia e com sequencia estavel para
    /// sempre — que e a unica propriedade que interessa a um contraexemplo reproduzivel.
    /// </summary>
    private sealed class Xorshift
    {
        private const ulong Semeadura = 0x9E37_79B9_7F4A_7C15UL;
        private const ulong Salvacao = 0xDEAD_BEEF_CAFE_BABEUL;

        private ulong _estado;

        internal Xorshift(long semente)
        {
            ulong inicial = unchecked((ulong)semente) ^ Semeadura;
            _estado = inicial == 0 ? Salvacao : inicial;
        }

        /// <summary>Inteiro em <c>[0, quantidade)</c>.</summary>
        internal int Indice(int quantidade) => (int)(Proximo() % (ulong)quantidade);

        /// <summary>Inteiro longo em <c>[0, limite)</c>.</summary>
        internal long Ate(long limite) => (long)(Proximo() % (ulong)limite);

        private ulong Proximo()
        {
            _estado ^= _estado << 13;
            _estado ^= _estado >> 7;
            _estado ^= _estado << 17;
            return _estado;
        }
    }
}
