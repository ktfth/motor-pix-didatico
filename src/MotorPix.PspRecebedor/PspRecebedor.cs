using System.Diagnostics.CodeAnalysis;
using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;
using MotorPix.Psp.Nucleo;

namespace MotorPix.PspRecebedor;

/// <summary>
/// O PSP recebedor: handler do <c>pacs.002</c>, credito do cliente (seta 7) e origem da devolucao.
/// <para>
/// Compartilha com o pagador o mesmo <see cref="NucleoDePsp"/>. Ate o gate 6 so o pagador tinha
/// varredura de vencidos e consulta de status, e por isso uma devolucao sem <c>pacs.002</c> ficava
/// em <c>ENVIADA_SPI</c> sem caminho de recuperacao — e o criterio de aceite da conciliacao era
/// vacuamente verdadeiro para devolucoes, porque nenhuma conseguia chegar a <c>EXPIRADA</c>.
/// </para>
/// </summary>
public sealed class PspRecebedor : IParticipante, IPspConciliavel
{
    private readonly object _trava = new();
    private readonly NucleoDePsp _nucleo;
    private readonly IRegistroDeChaves _registro;
    private readonly IClock _relogio;
    private readonly IBarramento _barramento;
    private readonly IGeradorEndToEndId _gerador;

    /// <summary>Creditos aplicados, indexados pelo E2E que os produziu. Insumo da devolucao.</summary>
    private readonly Dictionary<EndToEndId, CreditoRecebido> _creditosRecebidos = [];

    private readonly Dictionary<EndToEndId, EndToEndId> _devolucoesPorOriginal = [];

    /// <summary>
    /// A devolucao nao e enderecada por chave: o destino vem da ordem original. Esta constante
    /// ocupa o campo que o tipo exige sem fingir ser endereco de ninguem.
    /// </summary>
    private static readonly ChavePix ChaveDeDevolucao =
        ChavePix.Criar(TipoChave.Email, "devolucao@interno.motorpix");

    public PspRecebedor(
        Ispb ispb,
        IClock relogio,
        IRegistroDeChaves registro,
        IBarramento barramento,
        IGeradorEndToEndId gerador,
        PoliticaDeTimeout? politica = null)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(barramento);
        ArgumentNullException.ThrowIfNull(gerador);

        Ispb = ispb;
        _relogio = relogio;
        _registro = registro;
        _barramento = barramento;
        _gerador = gerador;
        _nucleo = new NucleoDePsp(ispb, relogio, politica);
    }

    public Ispb Ispb { get; }

    public LedgerPsp Ledger => _nucleo.Ledger;

    /// <summary>Porta de leitura do ledger para a conciliacao. Sem nenhum direito de escrever.</summary>
    public IConsultaLedger Consulta => _nucleo.Ledger.Consulta;

    public PoliticaDeTimeout Politica => _nucleo.Politica;

    public IReadOnlyList<DivergenciaPatrimonial> Divergencias => _nucleo.Divergencias;

    public IReadOnlyCollection<EndToEndId> Pacs002Atrasados => _nucleo.Pacs002Atrasados;

    public IReadOnlyCollection<EndToEndId> Expiradas => _nucleo.Expiradas;

    /// <summary>Instante do ultimo credito aplicado. Existe para o teste conferir que o tempo veio do IClock.</summary>
    public DateTimeOffset? UltimoCreditoEm { get; private set; }

    public void RegistrarSpi(ISpi spi) => _nucleo.RegistrarSpi(spi);

    public IReadOnlyCollection<EndToEndId> ExpirarVencidos() => _nucleo.ExpirarVencidos();

    public ResultadoConsulta ConsultarStatus(EndToEndId e2e) => _nucleo.ConsultarStatus(e2e);

    public bool TryDevolucao(EndToEndId e2eDaDevolucao, [NotNullWhen(true)] out VistaDaTransacao? devolucao) =>
        _nucleo.TryVista(e2eDaDevolucao, out devolucao);

    public ContaId AbrirCliente(string numeroDaConta, Valor saldoInicial)
    {
        ContaId conta = Ledger.AbrirCliente(numeroDaConta);
        Ledger.AportarNoGenesis(conta, saldoInicial);
        return conta;
    }

    /// <summary>
    /// Vincula uma chave a uma conta deste PSP, conferindo primeiro que a conta existe aqui.
    /// <para>
    /// A garantia de que a conta de destino existe vem do <b>registro</b>, nao da consulta: e o
    /// unico ponto em que alguem sabe, ao mesmo tempo, qual e a chave e qual e o ledger.
    /// </para>
    /// </summary>
    public void RegistrarChave(ChavePix chave, ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(chave);
        ArgumentNullException.ThrowIfNull(conta);

        if (!Ledger.ContemConta(conta))
        {
            throw new ContaDesconhecidaException(conta);
        }

        _registro.Vincular(new ResolucaoChave(chave, Ispb, conta));
    }

    /// <summary>
    /// Seta 6, lado do recebedor, e seta 7 — mais o retorno da devolucao que ele proprio originou.
    /// </summary>
    public void Receber(Pacs002 confirmacao)
    {
        ArgumentNullException.ThrowIfNull(confirmacao);

        // Despacho por PAPEL, antes de qualquer outra coisa. Quando o pacs.002 e da devolucao que
        // este PSP originou, ele e o DEBTOR: a conta do OrgnlTxRef pertence ao ledger do outro
        // participante, e tratar isso como ordem de creditar fazia o metodo lancar dentro da
        // drenagem — com o envelope preso na fila para sempre, bloqueando todas as outras.
        if (_nucleo.AplicarResultado(confirmacao))
        {
            SoltarIndiceSeRejeitada(confirmacao);
            return;
        }

        if (confirmacao.Status != StatusPacs002.Acsc)
        {
            return;
        }

        OrgnlTxRef referencia = confirmacao.Referencia
            ?? throw new OrdemInvalidaException(confirmacao.E2E, "pacs.002 ACSC sem bloco de referencia da ordem");

        lock (_trava)
        {
            // Confere a conta de destino aqui dentro: a invariante do recebedor nao pode depender
            // da correcao de quem enviou. ESPELHO_PI e um ContaId perfeitamente valido.
            if (!PodeCreditar(referencia.ContaCreditor))
            {
                throw new ContaDesconhecidaException(referencia.ContaCreditor);
            }

            if (!_nucleo.CreditarUmaVez(confirmacao.E2E, referencia.ContaCreditor, referencia.Valor))
            {
                return;
            }

            UltimoCreditoEm = _relogio.Agora;

            // Credito vindo de uma DEVOLUCAO nao vira insumo de outra devolucao: e o discriminador
            // que corta a recursao.
            if (referencia.E2eDevolvido is null)
            {
                _creditosRecebidos[confirmacao.E2E] = new CreditoRecebido(
                    referencia.ContaCreditor,
                    referencia.Valor,
                    referencia.IspbDebtor,
                    referencia.ContaDebtor);
            }
        }
    }

    public bool PodeCreditar(ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        return conta.Classe == ClasseConta.Cliente && Ledger.ContemConta(conta);
    }

    /// <summary>
    /// Solicita a devolucao de uma transacao ja creditada aqui (seta pontilhada <c>SM_R -.-&gt; VAL</c>).
    /// <para>
    /// Transacao <b>nova</b>, com E2E proprio, que apenas referencia a original. Percorre a mesma
    /// maquina desde <c>RECEBIDA</c> e termina em <c>LIQUIDADA</c>; quem termina em
    /// <c>DEVOLVIDA</c> e a original, e so quando esta aqui liquidar.
    /// </para>
    /// </summary>
    public VistaDaTransacao SolicitarDevolucao(EndToEndId e2eOriginal, Valor valor)
    {
        ArgumentNullException.ThrowIfNull(e2eOriginal);

        lock (_trava)
        {
            if (!_creditosRecebidos.TryGetValue(e2eOriginal, out CreditoRecebido? original))
            {
                throw new DevolucaoInvalidaException(e2eOriginal, "nao ha credito recebido para este E2E");
            }

            if (valor.Centavos != original.Valor.Centavos)
            {
                throw new DevolucaoInvalidaException(
                    e2eOriginal,
                    $"devolucao parcial esta fora de escopo: recebido {original.Valor.Centavos}, pedido {valor.Centavos}");
            }

            if (_devolucoesPorOriginal.ContainsKey(e2eOriginal))
            {
                throw new DevolucaoInvalidaException(e2eOriginal, "esta transacao ja foi devolvida");
            }

            DateTimeOffset agora = _relogio.Agora;
            EndToEndId e2eDevolucao = _gerador.Gerar(Ispb);

            Transacao devolucao = Transacao.Iniciar(
                e2eDevolucao,
                original.ContaCreditada,
                ChaveDeDevolucao,
                valor,
                agora,
                TipoTransacao.Devolucao,
                e2eOriginal);

            // A mensagem e montada antes de qualquer lancamento, pelo mesmo motivo do pagamento: o
            // diagrama nao tem aresta saindo de VALIDADA para REJEITADA.
            Pacs004 mensagem = new(
                e2eDevolucao,
                e2eOriginal,
                Ispb,
                original.ContaCreditada,
                original.IspbPagador,
                original.ContaPagadora,
                valor);

            _nucleo.Registrar(devolucao);
            _devolucoesPorOriginal[e2eOriginal] = e2eDevolucao;

            devolucao.Aplicar(TipoEvento.ValidacaoLocalOk, agora);

            // O dinheiro sai de quem devolve: D CLIENTE / C ESPELHO_PI, como a seta 3.
            Ledger.DebitarCliente(e2eDevolucao, original.ContaCreditada, valor);

            devolucao.Aplicar(TipoEvento.DebitoLancadoEPacs008Despachado, agora);
            _barramento.Enfileirar(new EnvelopePacs004(mensagem));

            return VistaDaTransacao.De(devolucao);
        }
    }

    /// <summary>
    /// Devolucao recusada nao consome a chance de devolver: sem soltar o indice, a original ficaria
    /// permanentemente inelegivel por causa de uma tentativa que nao moveu dinheiro.
    /// </summary>
    private void SoltarIndiceSeRejeitada(Pacs002 confirmacao)
    {
        if (confirmacao.Status != StatusPacs002.Rjct)
        {
            return;
        }

        if (!_nucleo.TryVista(confirmacao.E2E, out VistaDaTransacao? devolucao)
            || devolucao!.E2eOriginal is null)
        {
            return;
        }

        lock (_trava)
        {
            _devolucoesPorOriginal.Remove(devolucao.E2eOriginal);
        }
    }

    /// <summary>O credito que este PSP aplicou por um E2E, com os dados para uma eventual devolucao.</summary>
    private sealed record CreditoRecebido(
        ContaId ContaCreditada,
        Valor Valor,
        Ispb IspbPagador,
        ContaId ContaPagadora);

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
