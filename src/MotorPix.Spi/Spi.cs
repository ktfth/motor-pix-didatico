using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;
using MotorPix.Dominio.Valores;
using MotorPix.Mensagens;

namespace MotorPix.Spi;

/// <summary>
/// O SPI: validacao do <c>pacs.008</c>, deduplicacao por E2E e liquidacao atomica entre contas PI.
/// <para>
/// Nao conhece nenhum modulo: fala com os participantes pela interface
/// <see cref="IParticipante"/>, roteada por ISPB, e responde pelo barramento. E isso que quebra o
/// ciclo <c>PspPagador -&gt; Spi -&gt; PspPagador</c> das setas 4 e 6 sem nenhuma aresta de projeto.
/// </para>
/// </summary>
public sealed class SpiEmMemoria : ISpi
{
    private readonly object _trava = new();
    private readonly Ledger _ledger;
    private readonly IBarramento _barramento;
    private readonly IReadOnlyDictionary<Ispb, IParticipante> _participantes;

    /// <summary>
    /// Dedup por E2E da mensagem: um <c>pacs.008</c> reentregue devolve o MESMO <c>pacs.002</c> ja
    /// emitido, sem segunda liquidacao.
    /// <para>
    /// Guarda tambem a impressao da ordem original. Sem ela, a reentrega seria replay <em>cego</em>:
    /// um <c>pacs.008</c> com o mesmo E2E e outro creditor receberia de volta um ACSC cujo
    /// <c>OrgnlTxRef</c> descreve uma ordem diferente — o SPI afirmando "liquidado" para algo que
    /// nunca tocou o ledger. E exatamente a falha que a decisao D3 proibiu na API do PSP, e nao
    /// havia razao para a camada de baixo ser mais permissiva que a de cima.
    /// </para>
    /// </summary>
    private readonly Dictionary<EndToEndId, (Pacs002 Resposta, string Impressao)> _respostas = [];

    /// <summary>
    /// Log append-only das decisoes emitidas, na ordem de emissao.
    /// <para>
    /// A decisao do SPI e estado do qual dependem os invariantes 5 e 6 — e dela que saem o dedup de
    /// mensagem e a resposta da consulta de status. Sem log, ela nao seria reconstruivel: uma ordem
    /// recusada por participante desconhecido nem chega a lancar no ledger, entao um motor
    /// reconstruido a partir dos tres ledgers responderia "indeterminada" onde o vivo responde
    /// "rejeitada", e a transacao em EXPIRADA perderia sua unica saida legitima.
    /// </para>
    /// </summary>
    private readonly List<DecisaoDoSpi> _decisoes = [];

    public SpiEmMemoria(
        IClock relogio,
        IBarramento barramento,
        IReadOnlyDictionary<Ispb, IParticipante> participantes)
    {
        ArgumentNullException.ThrowIfNull(relogio);
        ArgumentNullException.ThrowIfNull(barramento);
        ArgumentNullException.ThrowIfNull(participantes);

        _barramento = barramento;
        _participantes = participantes;
        _ledger = new Ledger(LedgerId.Spi, relogio);

        _ledger.Abrir(PlanoDeContas.AberturaSpi());
    }

    /// <summary>
    /// A devolucao nao passa pelo DICT — o destino vem da ordem original, nao de uma chave. Como o
    /// tipo <c>Pacs008</c> exige uma, esta constante ocupa o campo sem fingir ser endereco de
    /// ninguem. E o unico ponto do motor em que uma ChavePix nao veio de um titular.
    /// </summary>
    private static readonly ChavePix ChavePixDeDevolucao =
        ChavePix.Criar(TipoChave.Email, "devolucao@interno.motorpix");

    public IConsultaLedger Consulta => _ledger;

    public IInspecaoLedger Inspecao => _ledger;

    /// <summary>Abre a conta PI de um participante. Bootstrap, antes de qualquer operacao.</summary>
    public ContaId AbrirContaPi(Ispb ispb)
    {
        ArgumentNullException.ThrowIfNull(ispb);

        _ledger.Abrir(PlanoDeContas.Pi(ispb));
        return ContaId.Pi(ispb);
    }

    /// <summary>Genesis: <c>D ABERTURA / C PI</c>. Fixa a constante do pool.</summary>
    public Commit AportarNoGenesis(Ispb ispb, Valor valor)
    {
        ArgumentNullException.ThrowIfNull(ispb);

        ContaId pi = ContaId.Pi(ispb);

        return _ledger.Lancar(Lancamento.Criar(
            ContaId.Abertura(LedgerId.Spi),
            pi,
            valor,
            ChaveIdempotencia.Genesis(pi),
            $"genesis da conta PI de {ispb}"));
    }

    /// <summary>
    /// Recebe uma devolucao. E a mesma mecanica do <c>pacs.008</c>: mesma validacao, mesmo dedup por
    /// E2E proprio, mesma liquidacao atomica entre contas PI.
    /// <para>
    /// Do ponto de vista do SPI a devolucao nao tem nada de especial — e uma ordem com papeis
    /// invertidos. O que ela carrega a mais, <c>E2eOriginal</c>, viaja no <c>OrgnlTxRef</c> da
    /// confirmacao para que o participante que recebe de volta saiba qual transacao marcar.
    /// </para>
    /// </summary>
    public void ReceberPacs004(Pacs004 devolucao)
    {
        ArgumentNullException.ThrowIfNull(devolucao);

        Pacs008 comoOrdem;

        try
        {
            comoOrdem = new Pacs008(
                devolucao.E2E,
                devolucao.IspbDebtor,
                devolucao.ContaDebtor,
                devolucao.IspbCreditor,
                devolucao.ContaCreditor,
                devolucao.Valor,
                ChavePixDeDevolucao);
        }
        catch (MotorPixException)
        {
            // Nenhuma excecao sai daqui. A conversao so falha se uma validacao nova de Pacs008 nao
            // tiver equivalente em Pacs004 — e isso nao pode envenenar a fila.
            Responder(devolucao.IspbDebtor, devolucao.IspbCreditor, RecusaDeOrdem(devolucao.E2E));
            return;
        }

        if (!DevolucaoEspelhaOriginal(devolucao))
        {
            Processar(comoOrdem, devolucao.E2eOriginal, recusaAntecipada: RecusaDeOrdem(devolucao.E2E));
            return;
        }

        Processar(comoOrdem, devolucao.E2eOriginal);
    }

    private static Pacs002 RecusaDeOrdem(EndToEndId e2e) => Pacs002.Rejeitado(e2e, MotivoRejeicao.OrdemInvalida);

    /// <summary>
    /// A devolucao tem de espelhar exatamente a original que referencia: mesma dupla de
    /// participantes com os papeis trocados, mesmas contas trocadas e mesmo valor.
    /// <para>
    /// Este e o unico ponto em que o SPI confiava cegamente em quem enviou. Duas linhas abaixo ele
    /// reconsulta <c>PodeCreditar</c> justamente porque "a invariante nao pode depender da correcao
    /// de quem enviou" — a devolucao merece o mesmo tratamento. Sem isso, um pacs.004 apontando
    /// para um E2E que o pagador nao conhece liquidaria entre contas PI e so entao estouraria na
    /// entrega, com o dinheiro ja movido.
    /// </para>
    /// </summary>
    private bool DevolucaoEspelhaOriginal(Pacs004 devolucao)
    {
        lock (_trava)
        {
            if (!_respostas.TryGetValue(devolucao.E2eOriginal, out (Pacs002 Resposta, string Impressao) original))
            {
                return false;
            }

            if (original.Resposta.Status != StatusPacs002.Acsc)
            {
                return false;
            }

            string espelho = Impressao(
                TipoDeOrdem.Pagamento,
                devolucao.IspbCreditor,
                devolucao.ContaCreditor,
                devolucao.IspbDebtor,
                devolucao.ContaDebtor,
                devolucao.Valor);

            return string.Equals(original.Impressao, espelho, StringComparison.Ordinal);
        }
    }

    public void ReceberPacs008(Pacs008 ordem)
    {
        ArgumentNullException.ThrowIfNull(ordem);
        Processar(ordem, e2eDevolvido: null);
    }

    private void Processar(Pacs008 ordem, EndToEndId? e2eDevolvido, Pacs002? recusaAntecipada = null)
    {
        Pacs002 resposta;
        string impressao = ImpressaoDaOrdem(ordem, e2eDevolvido);

        lock (_trava)
        {
            // Dedup por E2E: reentrega devolve a resposta original, sem liquidar de novo. E a
            // guarda do lado do SPI; a do lado do PSP e outra, com semantica propria.
            if (_respostas.TryGetValue(ordem.E2E, out (Pacs002 Resposta, string Impressao) jaEmitida))
            {
                // Mesmo E2E, ordem diferente: recusar. O SPI nao pode lancar — nenhuma excecao
                // sai daqui —, entao a recusa vira status.
                resposta = string.Equals(jaEmitida.Impressao, impressao, StringComparison.Ordinal)
                    ? jaEmitida.Resposta
                    : Pacs002.Rejeitado(ordem.E2E, MotivoRejeicao.OrdemInvalida);
            }
            else
            {
                resposta = recusaAntecipada ?? Liquidar(ordem, e2eDevolvido);
                _respostas.Add(ordem.E2E, (resposta, impressao));
                _decisoes.Add(new DecisaoDoSpi(ordem.E2E, resposta, impressao, _decisoes.Count + 1));
            }
        }

        Responder(ordem, resposta);
    }

    /// <summary>
    /// Consulta de status. A verdade vem do que o SPI efetivamente decidiu, e nada mais.
    /// <para>
    /// Um E2E que o SPI nunca viu devolve <see cref="ResultadoConsulta.Indeterminada"/>, e nao
    /// "rejeitada". A diferenca importa: o pacs.008 pode simplesmente nao ter chegado — e e
    /// exatamente esse o caso que produz EXPIRADA. Responder "rejeitada" convidaria o pagador a
    /// estornar um pagamento que ainda pode liquidar.
    /// </para>
    /// </summary>
    public ResultadoConsulta ConsultarStatus(EndToEndId e2e)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            if (!_respostas.TryGetValue(e2e, out (Pacs002 Resposta, string Impressao) entrada))
            {
                return ResultadoConsulta.Indeterminada;
            }

            return entrada.Resposta.Status == StatusPacs002.Acsc
                ? ResultadoConsulta.Liquidada
                : ResultadoConsulta.Rejeitada;
        }
    }

    public bool TryConfirmacao(EndToEndId e2e, out Pacs002? confirmacao) => TryRespostaDe(e2e, out confirmacao);

    /// <summary>Log append-only das decisoes, para replay. E a verdade; _respostas e projecao.</summary>
    public IReadOnlyList<DecisaoDoSpi> LogDeDecisoes()
    {
        lock (_trava)
        {
            return [.. _decisoes];
        }
    }

    /// <summary>Resposta ja emitida para um E2E, se houver. Base da consulta de status.</summary>
    public bool TryRespostaDe(EndToEndId e2e, out Pacs002? resposta)
    {
        ArgumentNullException.ThrowIfNull(e2e);

        lock (_trava)
        {
            if (_respostas.TryGetValue(e2e, out (Pacs002 Resposta, string Impressao) entrada))
            {
                resposta = entrada.Resposta;
                return true;
            }

            resposta = null;
            return false;
        }
    }

    /// <summary>
    /// Forma canonica da ordem, para reconhecer reentrega da MESMA ordem.
    /// Inclui os dois participantes, as duas contas e o valor — tudo o que a liquidacao usa.
    /// </summary>
    private enum TipoDeOrdem
    {
        Pagamento = 1,
        Devolucao = 2,
    }

    /// <summary>
    /// Forma canonica da ordem, para reconhecer reentrega da MESMA ordem.
    /// <para>
    /// Inclui o TIPO da mensagem. Sem esse discriminador, o indice de respostas e um espaco unico
    /// por E2E: um pagamento cujo identificador colidisse com o de uma devolucao futura queimaria o
    /// E2E dela, e o SPI responderia recusa a uma devolucao bem formada — com o cliente de quem
    /// devolve ja debitado.
    /// </para>
    /// </summary>
    private static string ImpressaoDaOrdem(Pacs008 ordem, EndToEndId? e2eDevolvido) =>
        Impressao(
            e2eDevolvido is null ? TipoDeOrdem.Pagamento : TipoDeOrdem.Devolucao,
            ordem.IspbDebtor,
            ordem.ContaDebtor,
            ordem.IspbCreditor,
            ordem.ContaCreditor,
            ordem.Valor);

    private static string Impressao(
        TipoDeOrdem tipo,
        Ispb ispbDebtor,
        ContaId contaDebtor,
        Ispb ispbCreditor,
        ContaId contaCreditor,
        Valor valor) =>
        $"{tipo}|{ispbDebtor}|{contaDebtor}|{ispbCreditor}|{contaCreditor}|{valor.Centavos}";

    private Pacs002 Liquidar(Pacs008 ordem, EndToEndId? e2eDevolvido)
    {
        if (!_participantes.ContainsKey(ordem.IspbDebtor) || !_participantes.ContainsKey(ordem.IspbCreditor))
        {
            return Pacs002.Rejeitado(ordem.E2E, MotivoRejeicao.ParticipanteDesconhecido);
        }

        if (!_ledger.ContemConta(ContaId.Pi(ordem.IspbDebtor)) || !_ledger.ContemConta(ContaId.Pi(ordem.IspbCreditor)))
        {
            return Pacs002.Rejeitado(ordem.E2E, MotivoRejeicao.ParticipanteDesconhecido);
        }

        // Perguntar ANTES de liquidar. Se a conta de destino nao aceitar credito, liquidar aqui
        // deixaria dinheiro movido no SPI sem contrapartida no recebedor — e a soma por ledger
        // continuaria fechando em zero, entao nenhuma propriedade contabil acusaria.
        if (!_participantes[ordem.IspbCreditor].PodeCreditar(ordem.ContaCreditor))
        {
            return Pacs002.Rejeitado(ordem.E2E, MotivoRejeicao.ContaDeDestinoIndisponivel);
        }

        try
        {
            // Seta 5: lancamento atomico, debito da PI pagadora e credito da PI recebedora.
            _ledger.Lancar(Lancamento.Criar(
                ContaId.Pi(ordem.IspbDebtor),
                ContaId.Pi(ordem.IspbCreditor),
                ordem.Valor,
                ChaveIdempotencia.De(ordem.E2E, EtapaLancamento.Liquidacao),
                $"liquidacao {ordem.E2E}"));
        }
        catch (SaldoNegativoException)
        {
            return Pacs002.Rejeitado(ordem.E2E, MotivoRejeicao.SaldoInsuficienteNaContaPi);
        }
        catch (MotorPixException)
        {
            // Nenhuma excecao pode sair de ReceberPacs008. Se saisse, o E2E ficaria sem resposta
            // registrada — nem a reentrega produziria pacs.002 —, o pagador ficaria em ENVIADA_SPI
            // com o cliente ja debitado, e a entrega das outras mensagens seria interrompida.
            // O caso concreto e o pagamento intra-participante: as duas pernas cairiam na mesma
            // conta PI e Lancamento.Criar recusa, com razao.
            return Pacs002.Rejeitado(ordem.E2E, MotivoRejeicao.OrdemInvalida);
        }

        return Pacs002.Aceito(
            ordem.E2E,
            new OrgnlTxRef(
                ordem.IspbDebtor,
                ordem.ContaDebtor,
                ordem.IspbCreditor,
                ordem.ContaCreditor,
                ordem.Valor)
            {
                E2eDevolvido = e2eDevolvido,
            });
    }

    /// <summary>
    /// Seta 6. O ACSC vai para os dois participantes; o RJCT so para o pagador — o recebedor nunca
    /// soube da ordem, e avisa-lo de algo que nao aconteceu nao tem efeito nenhum no ledger dele.
    /// </summary>
    private void Responder(Pacs008 ordem, Pacs002 resposta) =>
        Responder(ordem.IspbDebtor, ordem.IspbCreditor, resposta);

    private void Responder(Ispb debtor, Ispb creditor, Pacs002 resposta)
    {
        _barramento.Enfileirar(new EnvelopePacs002(debtor, resposta));

        if (resposta.Status == StatusPacs002.Acsc)
        {
            _barramento.Enfileirar(new EnvelopePacs002(creditor, resposta));
        }
    }
}
