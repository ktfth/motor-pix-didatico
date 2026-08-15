using System.Diagnostics.CodeAnalysis;
using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;

namespace MotorPix.PspPagador;

/// <summary>Pedido de pagamento: o que entra pela seta 1.</summary>
public sealed record ComandoDePagamento(EndToEndId E2E, string ContaDeOrigem, ChavePix ChaveDestino, Valor Valor);

/// <summary>Resposta da API, congelada no momento em que a transacao alcanca seu estado.</summary>
public sealed record RespostaDePagamento(EndToEndId E2E, EstadoTransacao Estado, MotivoRejeicaoLocal? Motivo);

/// <summary>
/// O PSP pagador: API de pagamento e origem do fluxo das setas 1 a 4.
/// <para>
/// O que ele faz igual ao recebedor — guardar transacoes, classificar <c>pacs.002</c>, expirar
/// vencidos, fechar por consulta e creditar — mora em <see cref="NucleoDePsp"/>. O que sobra aqui e
/// o papel: originar pagamento, resolver chave no DICT e responder a API.
/// </para>
/// <para>
/// A ordem das operacoes obedece a uma regra unica: <b>persistir a transicao antes de produzir o
/// efeito externo</b>. E ela que torna inalcancavel o par <c>(VALIDADA, pacs.002)</c>.
/// </para>
/// </summary>
public sealed class PspPagador : IParticipante, IPspConciliavel
{
    private readonly object _trava = new();
    private readonly NucleoDePsp _nucleo;
    private readonly IDiretorioChaves _dict;
    private readonly IBarramento _barramento;
    private readonly IClock _relogio;

    public PspPagador(
        Ispb ispb,
        IClock relogio,
        IDiretorioChaves dict,
        IBarramento barramento,
        PoliticaDeTimeout? politica = null)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(dict);
        ArgumentNullException.ThrowIfNull(barramento);

        Ispb = ispb;
        _relogio = relogio;
        _dict = dict;
        _barramento = barramento;
        _nucleo = new NucleoDePsp(ispb, relogio, politica);
    }

    public Ispb Ispb { get; }

    public LedgerPsp Ledger => _nucleo.Ledger;

    /// <summary>Porta de leitura do ledger para a conciliacao. Sem nenhum direito de escrever.</summary>
    public IConsultaLedger Consulta => _nucleo.Ledger.Consulta;

    public PoliticaDeTimeout Politica => _nucleo.Politica;

    /// <summary>Indice de idempotencia da API: uma resposta congelada por EndToEndId.</summary>
    public RegistroDeIdempotencia Idempotencia { get; } = new();

    public IReadOnlyList<DivergenciaPatrimonial> Divergencias => _nucleo.Divergencias;

    public IReadOnlyCollection<EndToEndId> Pacs002Atrasados => _nucleo.Pacs002Atrasados;

    public IReadOnlyCollection<EndToEndId> Expiradas => _nucleo.Expiradas;

    public void RegistrarSpi(ISpi spi) => _nucleo.RegistrarSpi(spi);

    public IReadOnlyCollection<EndToEndId> ExpirarVencidos() => _nucleo.ExpirarVencidos();

    public ResultadoConsulta ConsultarStatus(EndToEndId e2e) => _nucleo.ConsultarStatus(e2e);

    public bool TryTransacao(EndToEndId e2e, [NotNullWhen(true)] out VistaDaTransacao? transacao) =>
        _nucleo.TryVista(e2e, out transacao);

    public ContaId AbrirCliente(string numeroDaConta, Valor saldoInicial)
    {
        ContaId conta = Ledger.AbrirCliente(numeroDaConta);
        Ledger.AportarNoGenesis(conta, saldoInicial);
        return conta;
    }

    /// <summary>Seta 1. Percorre RECEBIDA, VALIDADA e ENVIADA_SPI, ou para em REJEITADA.</summary>
    public RespostaDePagamento Pagar(ComandoDePagamento comando)
    {
        ArgumentNullException.ThrowIfNull(comando);

        lock (_trava)
        {
            ContaId origem = ContaId.Cliente(Ledger.Id, comando.ContaDeOrigem);
            DateTimeOffset agora = _relogio.Agora;
            ImpressaoDoPedido impressao = ImpressaoDoPedido.De(origem, comando.ChaveDestino, comando.Valor);

            // A reivindicacao vem ANTES de qualquer consulta externa. Consultar o DICT primeiro e
            // reivindicar depois seria check-then-act: dois POSTs simultaneos fariam duas
            // resolucoes de chave antes de a colisao ser notada.
            if (!Idempotencia.TentarReivindicar(comando.E2E, impressao, out EntradaDeIdempotencia? original))
            {
                return new RespostaDePagamento(
                    comando.E2E,
                    original!.EstadoRespondido,
                    original.MotivoRespondido);
            }

            try
            {
                return Processar(comando, origem, impressao, agora);
            }
            catch (MotorPixException)
            {
                // Soltar a reivindicacao permite ao cliente reenviar, mas SO quando nada
                // irreversivel foi lancado: o ledger e append-only e nao volta atras junto. Com o
                // debito ja commitado, o E2E fica queimado de proposito e a transacao permanece
                // como registro para a conciliacao encontrar.
                if (!Ledger.HouveDebito(comando.E2E))
                {
                    Idempotencia.LiberarSeEmVoo(comando.E2E);
                    _nucleo.Esquecer(comando.E2E);
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Seta 6, lado do pagador — e, no gate 6, tambem o retorno da devolucao.
    /// </summary>
    public void Receber(Pacs002 confirmacao)
    {
        ArgumentNullException.ThrowIfNull(confirmacao);

        // O nucleo trata tudo o que for de uma transacao originada aqui: classificacao de
        // reentrega, contradicao, estorno e o pacs.002 atrasado sobre EXPIRADA.
        if (_nucleo.AplicarResultado(confirmacao))
        {
            return;
        }

        // E2E desconhecido com bloco de devolucao: e o ACSC de um pacs.004 originado pelo OUTRO
        // participante. Aqui o papel se inverte — este PSP e o creditor.
        if (confirmacao.Status == StatusPacs002.Acsc && confirmacao.Referencia?.E2eDevolvido is not null)
        {
            AplicarDevolucaoRecebida(confirmacao, confirmacao.Referencia);
            return;
        }

        throw new TransacaoDesconhecidaException(confirmacao.E2E);
    }

    /// <summary>
    /// Responde pela capacidade REAL de creditar.
    /// <para>
    /// A conta existir nao basta: este modulo so honra credito de devolucao, e a expectativa e uma
    /// transacao originada aqui que possa vir a ser devolvida. Responder <c>true</c> por qualquer
    /// conta de cliente reintroduziria o modo de falha que a ADR-009 criou esta guarda para
    /// impedir — o SPI liquidaria e a entrega lancaria, com o dinheiro ja movido.
    /// </para>
    /// </summary>
    public bool PodeCreditar(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);

        if (conta.Classe != ClasseConta.Cliente || !Ledger.ContemConta(conta))
        {
            return false;
        }

        // DEVOLVIDA entra junto de LIQUIDADA porque a reentrega do mesmo ACSC reconsulta esta
        // guarda depois de a original ja ter transicionado — e negar ali seria mentir sobre um
        // credito que ja aconteceu.
        return _nucleo.ExisteTransacao(t =>
            t.ContaOrigem.Equals(conta)
            && t.Estado is EstadoTransacao.Liquidada or EstadoTransacao.Devolvida);
    }

    private RespostaDePagamento Processar(
        ComandoDePagamento comando,
        ContaId origem,
        ImpressaoDoPedido impressao,
        DateTimeOffset agora)
    {
        Transacao transacao = Transacao.Iniciar(comando.E2E, origem, comando.ChaveDestino, comando.Valor, agora);
        _nucleo.Registrar(transacao);

        if (!Ledger.ContemConta(origem))
        {
            return Rejeitar(transacao, MotivoRejeicaoLocal.ContaDeOrigemDesconhecida, agora, impressao);
        }

        // Seta 2. Chave ausente e recusa de negocio, nao defeito: vira a aresta estados:5.
        if (!_dict.TryResolver(comando.ChaveDestino, out ResolucaoChave? destino))
        {
            return Rejeitar(transacao, MotivoRejeicaoLocal.ChaveNaoEncontrada, agora, impressao);
        }

        transacao.RegistrarDestino(destino.Ispb, destino.Conta);

        // "saldo do cliente ok" faz parte da aresta estados:4. Esta e a pre-guarda que produz a
        // rejeicao de NEGOCIO; a guarda que protege o invariante e a do ledger, no mesmo ato
        // atomico do debito.
        if (Ledger.SaldoDoCliente(origem).Centavos < comando.Valor.Centavos)
        {
            return Rejeitar(transacao, MotivoRejeicaoLocal.SaldoInsuficiente, agora, impressao);
        }

        // A mensagem e montada e validada AQUI, antes da transicao para VALIDADA: o diagrama nao
        // tem aresta VALIDADA -> REJEITADA, entao recusa descoberta depois nao teria para onde ir.
        Pacs008 ordem;

        try
        {
            ordem = new Pacs008(
                comando.E2E,
                Ispb,
                origem,
                destino.Ispb,
                destino.Conta,
                comando.Valor,
                comando.ChaveDestino);
        }
        catch (OrdemInvalidaException)
        {
            return Rejeitar(transacao, MotivoRejeicaoLocal.OrdemInvalida, agora, impressao);
        }

        transacao.Aplicar(TipoEvento.ValidacaoLocalOk, agora);

        // Seta 3. O saldo ja foi conferido sob este mesmo lock; se a guarda do ledger disparar
        // aqui, e violacao de invariante interna e a excecao sobe — mascarar seria inventar uma
        // saida de VALIDADA que o diagrama nao tem.
        Ledger.DebitarCliente(comando.E2E, origem, comando.Valor);

        transacao.Aplicar(TipoEvento.DebitoLancadoEPacs008Despachado, agora);

        // Seta 4, depois da transicao gravada e por enfileiramento, nunca por chamada direta.
        _barramento.Enfileirar(new EnvelopePacs008(ordem));

        return Responder(transacao, impressao, agora);
    }

    /// <summary>
    /// O dinheiro de volta: credita o cliente e marca a original como <c>DEVOLVIDA</c>.
    /// <para>
    /// Os lancamentos da original nao sao tocados — o credito entra com a chave de idempotencia da
    /// DEVOLUCAO. O que muda nela e so o campo Estado, pela aresta <c>estados:16</c>.
    /// </para>
    /// </summary>
    private void AplicarDevolucaoRecebida(Pacs002 confirmacao, OrgnlTxRef referencia)
    {
        EndToEndId e2eOriginal = referencia.E2eDevolvido!;

        if (!_nucleo.Conhece(e2eOriginal))
        {
            throw new TransacaoDesconhecidaException(e2eOriginal);
        }

        if (!PodeCreditar(referencia.ContaCreditor))
        {
            throw new ContaDesconhecidaException(referencia.ContaCreditor);
        }

        _nucleo.CreditarUmaVez(confirmacao.E2E, referencia.ContaCreditor, referencia.Valor);

        _nucleo.Com(e2eOriginal, original =>
        {
            // O vinculo e gravado mesmo quando a transicao ainda nao pode ser aplicada: sem ele, a
            // conciliacao ve uma original DEVOLVIDA e um credito sob um E2E que este PSP nunca
            // originou, sem nada em API publica que pareie os dois.
            original.MarcarDevolucao(confirmacao.E2E);

            switch (original.Estado)
            {
                case EstadoTransacao.Liquidada:
                    original.Aplicar(TipoEvento.DevolucaoLiquidada, _relogio.Agora);
                    break;

                case EstadoTransacao.Devolvida:
                    // Segunda devolucao da mesma original: DEVOLVIDA e terminal. No-op.
                    break;

                case EstadoTransacao.Expirada:
                    // A devolucao liquidou antes de a original fechar por consulta. A aresta
                    // estados:16 sai de LIQUIDADA, entao a marcacao acima e o que permite a
                    // consulta subsequente completar o quadro.
                    break;

                default:
                    _nucleo.RegistrarDivergencia(new DivergenciaPatrimonial(
                        original.E2E,
                        original.Estado.ToString(),
                        $"devolucao {confirmacao.E2E} liquidada"));
                    break;
            }

            return true;
        });
    }

    /// <summary>Recusa local: aresta estados:5, sempre a partir de <c>RECEBIDA</c>.</summary>
    private RespostaDePagamento Rejeitar(
        Transacao transacao,
        MotivoRejeicaoLocal motivo,
        DateTimeOffset agora,
        ImpressaoDoPedido impressao)
    {
        transacao.Aplicar(TipoEvento.ValidacaoLocalFalhou, agora);
        transacao.RegistrarMotivoDeRejeicao(motivo);
        return Responder(transacao, impressao, agora);
    }

    /// <summary>
    /// Congela a resposta no instante em que ela e produzida.
    /// O snapshot e o que um reenvio devolve, mesmo que a transacao avance depois: o diagrama diz
    /// "replay da resposta", nao "estado atual".
    /// </summary>
    private RespostaDePagamento Responder(Transacao transacao, ImpressaoDoPedido impressao, DateTimeOffset agora)
    {
        Idempotencia.Congelar(transacao.E2E, impressao, transacao.Estado, transacao.MotivoRejeicao, agora);
        return new RespostaDePagamento(transacao.E2E, transacao.Estado, transacao.MotivoRejeicao);
    }

    /// <summary>
    /// Aresta <c>MATCH -.-> SM_R</c>: reaplica um credito perdido, buscando a confirmacao no SPI.
    /// <para>
    /// Idempotente pela chave do ledger — reprocessar o que ja foi creditado e no-op. E o proprio
    /// <see cref="Receber"/> que aplica, entao o caminho e exatamente o mesmo da entrega normal.
    /// </para>
    /// </summary>
    public bool ReprocessarCredito(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        if (!_nucleo.TryConfirmacaoDoSpi(e2e, out Pacs002? confirmacao)
            || confirmacao is null
            || confirmacao.Status != StatusPacs002.Acsc)
        {
            return false;
        }

        if (Ledger.HouveCredito(e2e))
        {
            return false;
        }

        Receber(confirmacao);
        return Ledger.HouveCredito(e2e);
    }

    /// <summary>Historicos append-only das transacoes deste PSP, para o replay do gate 8.</summary>
    public IReadOnlyList<HistoricoDeTransacao> HistoricosParaReplay() => _nucleo.HistoricosParaReplay();
}
