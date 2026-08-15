using MotorPix.Contratos;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;

namespace MotorPix.Psp.Nucleo;

/// <summary>
/// O que os dois PSPs fazem igual: guardar transacoes, classificar <c>pacs.002</c>, expirar
/// vencidos, fechar por consulta de status e aplicar credito.
/// <para>
/// Kernel de <b>papel</b>, nao um quinto modulo. Ate o gate 6 so o pagador tinha timeout, consulta,
/// classificacao de reentrega e registro de divergencia; a devolucao, que percorre a mesma maquina
/// normativa, ficava sem saida de <c>ENVIADA_SPI</c> — e o criterio de aceite da conciliacao
/// ("nenhuma transacao permanece em EXPIRADA") seria vacuamente verdadeiro para ela, porque nenhuma
/// devolucao conseguia <em>chegar</em> a EXPIRADA.
/// </para>
/// <para>
/// Quem origina o que continua sendo papel de cada PSP: o pagamento nasce no pagador, a devolucao
/// no recebedor. O que este nucleo remove e a divergencia onde deveria haver igualdade.
/// </para>
/// </summary>
public sealed class NucleoDePsp
{
    private readonly object _trava = new();
    private readonly Dictionary<EndToEndId, Transacao> _transacoes = [];
    private readonly HashSet<EndToEndId> _pacs002Atrasados = [];
    private readonly List<DivergenciaPatrimonial> _divergencias = [];
    private readonly IClock _relogio;

    private ISpi? _spi;

    public NucleoDePsp(Ispb ispb, IClock relogio, PoliticaDeTimeout? politica = null)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(relogio);

        Ispb = ispb;
        _relogio = relogio;
        Politica = politica ?? PoliticaDeTimeout.Padrao;
        Ledger = new LedgerPsp(ispb, relogio);
    }

    public Ispb Ispb { get; }

    public LedgerPsp Ledger { get; }

    public PoliticaDeTimeout Politica { get; }

    public DateTimeOffset Agora => _relogio.Agora;

    /// <summary>
    /// Liga a porta de consulta de status. Registro tardio: o SPI precisa dos participantes para
    /// nascer, entao nenhum PSP pode receber o SPI no proprio construtor.
    /// </summary>
    public void RegistrarSpi(ISpi spi)
    {
        ArgumentNullException.ThrowIfNull(spi);
        _spi = spi;
    }

    public void Registrar(Transacao transacao)
    {
        ArgumentNullException.ThrowIfNull(transacao);

        lock (_trava)
        {
            _transacoes[transacao.E2E] = transacao;
        }
    }

    public void Esquecer(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            _transacoes.Remove(e2e);
        }
    }

    public bool TryVista(EndToEndId e2e, out VistaDaTransacao? vista)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            if (_transacoes.TryGetValue(e2e, out Transacao? encontrada))
            {
                vista = VistaDaTransacao.De(encontrada);
                return true;
            }

            vista = null;
            return false;
        }
    }

    public bool Conhece(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            return _transacoes.ContainsKey(e2e);
        }
    }

    /// <summary>
    /// Diz se existe transacao que satisfaca o predicado, avaliado sob o lock.
    /// <para>
    /// Devolve um booleano, e nao as transacoes: expor a colecao viva daria ao chamador a entidade
    /// mutavel de volta, que e justamente o que <see cref="VistaDaTransacao"/> existe para impedir.
    /// </para>
    /// </summary>
    public bool ExisteTransacao(Func<Transacao, bool> predicado)
    {
        ArgumentNullException.ThrowIfNull(predicado);

        lock (_trava)
        {
            return _transacoes.Values.Any(predicado);
        }
    }

    public IReadOnlyList<DivergenciaPatrimonial> Divergencias
    {
        get
        {
            lock (_trava)
            {
                return [.. _divergencias];
            }
        }
    }

    /// <summary>E2E cujo <c>pacs.002</c> chegou depois de expirar e que ainda nao foram fechados.</summary>
    public IReadOnlyCollection<EndToEndId> Pacs002Atrasados
    {
        get
        {
            lock (_trava)
            {
                return [.. _pacs002Atrasados];
            }
        }
    }

    /// <summary>
    /// Os historicos append-only de todas as transacoes, para replay.
    /// <para>
    /// O historico de cada transacao e o log de estados: append-only como o do ledger, e a unica
    /// fonte a partir da qual <see cref="ProjecaoEstados"/> reconstroi. O que esta em memoria e
    /// projecao; isto e a verdade.
    /// </para>
    /// </summary>
    public IReadOnlyList<HistoricoDeTransacao> HistoricosParaReplay()
    {
        lock (_trava)
        {
            return [.. _transacoes.Values.Select(t => new HistoricoDeTransacao(t.E2E, t.Historico))];
        }
    }

    /// <summary>Transacoes atualmente em <c>EXPIRADA</c>. E esta a lista que a conciliacao zera.</summary>
    public IReadOnlyCollection<EndToEndId> Expiradas
    {
        get
        {
            lock (_trava)
            {
                return [.. _transacoes.Values
                    .Where(t => t.Estado == EstadoTransacao.Expirada)
                    .Select(t => t.E2E)];
            }
        }
    }

    /// <summary>
    /// Varredura de vencidos. Devolve <b>quais</b> E2E expiraram — a contagem sozinha tornaria o
    /// ciclo "expirar entao consultar" inexpressavel pela API publica.
    /// <para>
    /// O prazo vencido nao expira nada; a varredura expira. Um <c>pacs.002</c> que chegue depois do
    /// prazo mas antes dela fecha a transacao pela aresta legitima <c>estados:9</c>.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<EndToEndId> ExpirarVencidos()
    {
        lock (_trava)
        {
            DateTimeOffset agora = _relogio.Agora;
            List<EndToEndId> expiradas = [];

            foreach (Transacao transacao in _transacoes.Values)
            {
                if (transacao.Estado == EstadoTransacao.EnviadaSpi
                    && Politica.Venceu(transacao.AtualizadaEm, agora))
                {
                    transacao.Aplicar(TipoEvento.TimeoutDetectado, agora);
                    expiradas.Add(transacao.E2E);
                }
            }

            return expiradas;
        }
    }

    /// <summary>
    /// Consulta o SPI sobre uma transacao <c>EXPIRADA</c> e aplica o que a resposta determinar.
    /// <para>
    /// Resposta indeterminada e no-op: permanece <c>EXPIRADA</c>, sem lancamento, sem evento e sem
    /// excecao. E o caso que a nota do diagrama entrega a conciliacao.
    /// </para>
    /// </summary>
    public ResultadoConsulta ConsultarStatus(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        Transacao transacao;

        lock (_trava)
        {
            if (!_transacoes.TryGetValue(e2e, out Transacao? encontrada))
            {
                throw new TransacaoDesconhecidaException(e2e);
            }

            transacao = encontrada;
        }

        if (_spi is null)
        {
            throw new MensageriaInvalidaException("PSP sem porta de consulta ao SPI registrada");
        }

        // A consulta acontece FORA do lock. Segura-lo enquanto se chama o SPI criaria a aresta
        // PSP -> Spi na ordem de aquisicao, e o SPI ja chama de volta os participantes durante a
        // validacao — e assim que um deadlock nasce.
        ResultadoConsulta resultado = _spi.ConsultarStatus(e2e);

        lock (_trava)
        {
            DateTimeOffset agora = _relogio.Agora;

            switch (resultado)
            {
                case ResultadoConsulta.Liquidada when transacao.Estado == EstadoTransacao.Expirada:
                    transacao.Aplicar(TipoEvento.ConsultaConfirmaLiquidacao, agora);
                    _pacs002Atrasados.Remove(e2e);
                    break;

                case ResultadoConsulta.Rejeitada when transacao.Estado == EstadoTransacao.Expirada:
                    transacao.Aplicar(TipoEvento.ConsultaConfirmaRejeicao, agora);
                    transacao.RegistrarMotivoDeRejeicao(MotivoRejeicaoLocal.RejeitadaPeloSpi);
                    EstornarSeHouveDebito(transacao);
                    _pacs002Atrasados.Remove(e2e);
                    break;

                default:
                    // Indeterminada, ou transacao que ja saiu de EXPIRADA nesse meio-tempo.
                    break;
            }

            return resultado;
        }
    }

    /// <summary>
    /// Aplica um <c>pacs.002</c> a uma transacao que ESTE PSP originou.
    /// <para>
    /// Classifica antes de aplicar. Sem isso, um <c>pacs.002</c> reentregue — caso normal do gate 4
    /// e do atrasado do gate 5 — subiria <c>TransicaoInvalidaException</c> atraves do barramento e
    /// travaria a entrega de todas as outras mensagens.
    /// </para>
    /// </summary>
    /// <returns><c>false</c> se este PSP nao originou a transacao.</returns>
    public bool AplicarResultado(Pacs002 confirmacao)
    {
        ArgumentNullException.ThrowIfNull(confirmacao);

        lock (_trava)
        {
            if (!_transacoes.TryGetValue(confirmacao.E2E, out Transacao? transacao))
            {
                return false;
            }

            // pacs.002 atrasado sobre EXPIRADA: nao transiciona. O invariante 6 diz que de EXPIRADA
            // so se sai por consulta; a mensagem apenas registra que ha o que consultar. Descarta-la
            // jogaria fora a evidencia autoritativa do SPI.
            if (transacao.Estado == EstadoTransacao.Expirada)
            {
                _pacs002Atrasados.Add(confirmacao.E2E);
                return true;
            }

            TipoEvento evento = confirmacao.Status == StatusPacs002.Acsc
                ? TipoEvento.Pacs002Acsc
                : TipoEvento.Pacs002Rjct;

            if (!MaquinaDeEstados.EhValida(transacao.Estado, evento))
            {
                if (Concorda(transacao.Estado, confirmacao.Status))
                {
                    // Reentrega concordante: a mensagem diz o que a transacao ja sabe. No-op.
                    return true;
                }

                // Contradicao real: o dinheiro moveu no SPI e o PSP acredita no contrario. Sintoma
                // contabil, nao de protocolo — registrar e encaminhar, jamais engolir nem deixar
                // atravessar o barramento.
                _divergencias.Add(new DivergenciaPatrimonial(
                    confirmacao.E2E,
                    transacao.Estado.ToString(),
                    confirmacao.Status.ToString()));
                return true;
            }

            transacao.Aplicar(evento, _relogio.Agora);

            if (confirmacao.Status == StatusPacs002.Acsc)
            {
                return true;
            }

            transacao.RegistrarMotivoDeRejeicao(MotivoRejeicaoLocal.RejeitadaPeloSpi);
            EstornarSeHouveDebito(transacao);
            return true;
        }
    }

    /// <summary>
    /// Estorno condicionado ao <em>ledger</em>, e nao ao estado de origem.
    /// <c>REJEITADA</c> e alcancavel por tres arestas com consequencias contabeis opostas: vindo de
    /// <c>RECEBIDA</c> nao houve debito, e estornar criaria dinheiro do nada.
    /// </summary>
    public void EstornarSeHouveDebito(Transacao transacao)
    {
        ArgumentNullException.ThrowIfNull(transacao);

        if (Ledger.HouveDebito(transacao.E2E) && !Ledger.HouveEstorno(transacao.E2E))
        {
            Ledger.EstornarDebito(transacao.E2E, transacao.ContaOrigem, transacao.Valor);
        }
    }

    /// <summary>
    /// Busca no SPI a confirmacao de um E2E, para reprocessamento.
    /// <para>
    /// O PSP nao inventa o credito a partir do relatorio da conciliacao: ele pede a mensagem de
    /// volta a quem a emitiu. Os dados para creditar viajam no <c>OrgnlTxRef</c>, e so o SPI os tem.
    /// </para>
    /// </summary>
    public bool TryConfirmacaoDoSpi(EndToEndId e2e, out Pacs002? confirmacao)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        if (_spi is null)
        {
            throw new MensageriaInvalidaException("PSP sem porta de consulta ao SPI registrada");
        }

        return _spi.TryConfirmacao(e2e, out confirmacao);
    }

    /// <summary>Credita o cliente, uma vez so. A guarda e a chave de idempotencia do ledger.</summary>
    public bool CreditarUmaVez(EndToEndId e2eDoCredito, ContaId conta, Valor valor)
    {
        ArgumentNullException.ThrowIfNull(e2eDoCredito);
        ArgumentNullException.ThrowIfNull(conta);

        lock (_trava)
        {
            if (Ledger.HouveCredito(e2eDoCredito))
            {
                return false;
            }

            Ledger.CreditarCliente(e2eDoCredito, conta, valor);
            return true;
        }
    }

    public void RegistrarDivergencia(DivergenciaPatrimonial divergencia)
    {
        ArgumentNullException.ThrowIfNull(divergencia);

        lock (_trava)
        {
            _divergencias.Add(divergencia);
        }
    }

    /// <summary>Executa uma operacao sobre a entidade viva, sob o lock do nucleo.</summary>
    public T Com<T>(EndToEndId e2e, Func<Transacao, T> operacao)
    {
        ArgumentNullException.ThrowIfNull(e2e);
        ArgumentNullException.ThrowIfNull(operacao);

        lock (_trava)
        {
            return _transacoes.TryGetValue(e2e, out Transacao? transacao)
                ? operacao(transacao)
                : throw new TransacaoDesconhecidaException(e2e);
        }
    }

    private static bool Concorda(EstadoTransacao estado, StatusPacs002 status) =>
        (estado, status) switch
        {
            (EstadoTransacao.Liquidada, StatusPacs002.Acsc) => true,
            (EstadoTransacao.Rejeitada, StatusPacs002.Rjct) => true,
            _ => false,
        };
}
