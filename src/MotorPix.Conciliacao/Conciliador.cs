using MotorPix.Contratos;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Conciliacao;

/// <summary>
/// Ledgers PSP contra ledger SPI: acha as divergencias, atribui cada uma a um E2E, e devolve o que
/// nao tem explicacao como alarme.
/// <para>
/// A conciliacao <b>detecta e manda perguntar</b>; quem decide continua sendo o SPI (ADR-013).
/// </para>
/// <para>
/// Precisao que o comentario anterior nao tinha: <see cref="IPspConciliavel.ConsultarStatus"/>
/// <em>transiciona</em>, como efeito da resposta do SPI. O que a conciliacao nao consegue e escolher
/// o desfecho — ela nao tem porta que aplique um evento diretamente, e o resultado vem de quem tem
/// a verdade. Decidir a partir de evidencia indireta e local e que o invariante 6 proibe.
/// </para>
/// <para>
/// A leitura dos saldos vem de <see cref="ProjecaoSaldos"/>, o fold puro do log. A conciliacao
/// consome o mesmo replay que o gate 8 verifica: se as projecoes materializadas divergirem do log,
/// e o log que a conciliacao acredita.
/// </para>
/// </summary>
public sealed class Conciliador
{
    private readonly IConsultaLedger _spi;
    private readonly IReadOnlyDictionary<Ispb, IPspConciliavel> _psps;

    public Conciliador(IConsultaLedger spi, IReadOnlyDictionary<Ispb, IPspConciliavel> psps)
    {
        ArgumentNullException.ThrowIfNull(spi);
        ArgumentNullException.ThrowIfNull(psps);

        _spi = spi;
        _psps = psps;
    }

    public RelatorioDeConciliacao Conciliar()
    {
        ProjecaoSaldos projecaoSpi = ProjecaoSaldos.Reconstruir(_spi.Log());
        HashSet<EndToEndId> liquidados = EtapasPorE2e(_spi.Log(), EtapaLancamento.Liquidacao);

        List<DivergenciaPorE2E> divergencias = [];
        Dictionary<Ispb, Saldo> diferencas = [];
        List<EndToEndId> expiradas = [];

        foreach ((Ispb ispb, IPspConciliavel psp) in _psps)
        {
            IReadOnlyList<Commit> log = psp.Consulta.Log();
            ProjecaoSaldos projecaoPsp = ProjecaoSaldos.Reconstruir(log);

            ContaId contaPi = ContaId.Pi(ispb);
            ContaId espelho = ContaId.EspelhoPi(LedgerId.Psp(ispb));

            long saldoPi = projecaoSpi.Contas.Contains(contaPi)
                ? projecaoSpi.SaldoNatural(contaPi).Centavos
                : 0L;

            long saldoEspelho = projecaoPsp.Contas.Contains(espelho)
                ? projecaoPsp.SaldoNatural(espelho).Centavos
                : 0L;

            // A identidade que o genesis estabelece e que toda operacao completa restaura:
            // PI menos espelho e exatamente o que esta em transito para este participante.
            diferencas[ispb] = new Saldo(saldoPi - saldoEspelho);

            expiradas.AddRange(psp.Expiradas);

            divergencias.AddRange(DivergenciasDoParticipante(ispb, log, liquidados));
        }

        // Uma diferenca que as divergencias por E2E nao somam e dinheiro sem dono: nenhum
        // lancamento a explica. Isso e criacao ou destruicao de dinheiro, e vira alarme.
        divergencias.AddRange(OrfasResiduais(diferencas, divergencias));

        return new RelatorioDeConciliacao(divergencias, diferencas, expiradas);
    }

    /// <summary>
    /// Fecha o que for fechavel, pela unica via legitima: perguntando ao SPI.
    /// <para>
    /// Uma consulta que responda indeterminado deixa a divergencia aberta — e correto. A nota do
    /// diagrama diz "o que a consulta nao fechar, a conciliacao fecha", e a conciliacao fecha
    /// reapresentando a pergunta, nao respondendo por conta propria.
    /// </para>
    /// </summary>
    /// <returns>Quantas consultas foram emitidas.</returns>
    public int Fechar(RelatorioDeConciliacao relatorio)
    {
        ArgumentNullException.ThrowIfNull(relatorio);

        int acoes = 0;

        // Aresta MATCH -.-> VAL: perguntar ao SPI sobre o que saiu do pagador e nao se sabe se chegou.
        foreach (EndToEndId e2e in relatorio.AConsultar)
        {
            foreach (IPspConciliavel psp in _psps.Values)
            {
                if (!psp.Expiradas.Contains(e2e) && !ConheceOE2e(psp, e2e))
                {
                    continue;
                }

                psp.ConsultarStatus(e2e);
                acoes++;
                break;
            }
        }

        // Aresta MATCH -.-> SM_R: reaplicar o credito que ficou pelo caminho.
        //
        // Sem esta metade, o cenario canonico do gate 5 — pacs.002 ACSC perdido — nunca alcanca
        // quiescencia: a consulta fecha o pagador em LIQUIDADA, o recebedor continua com a
        // pendencia, AConsultar fica vazia, Fechar devolve zero e o laco para com o dinheiro
        // parado na conta PI do recebedor, sem nunca chegar ao cliente dele.
        foreach (EndToEndId e2e in relatorio.ACreditar)
        {
            foreach ((Ispb ispb, IPspConciliavel psp) in _psps)
            {
                if (!relatorio.Divergencias.Any(d =>
                        d.Classe == ClasseDeDivergencia.PendenciaDoRecebedor
                        && d.Participante.Equals(ispb)
                        && e2e.Equals(d.E2E)))
                {
                    continue;
                }

                if (psp.ReprocessarCredito(e2e))
                {
                    acoes++;
                }

                break;
            }
        }

        return acoes;
    }

    /// <summary>
    /// Roda conciliacao e fechamento ate o quadro parar de mudar.
    /// <para>
    /// O limite de rodadas nao e otimizacao: e a diferenca entre "estabilizou" e "esta oscilando".
    /// Sem ele, uma divergencia que a consulta nao fecha faria o laco girar para sempre.
    /// </para>
    /// </summary>
    public RelatorioDeConciliacao ConciliarAteEstabilizar(int maximoDeRodadas = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximoDeRodadas);

        RelatorioDeConciliacao relatorio = Conciliar();

        for (int rodada = 0; rodada < maximoDeRodadas && !relatorio.EmQuiescencia; rodada++)
        {
            if (Fechar(relatorio) == 0)
            {
                break;
            }

            relatorio = Conciliar();
        }

        return relatorio;
    }

    private bool ConheceOE2e(IPspConciliavel psp, EndToEndId e2e) =>
        EtapasPorE2e(psp.Consulta.Log(), EtapaLancamento.DebitoPagador).Contains(e2e);

    private IEnumerable<DivergenciaPorE2E> DivergenciasDoParticipante(
        Ispb ispb,
        IReadOnlyList<Commit> log,
        HashSet<EndToEndId> liquidados)
    {
        Dictionary<EndToEndId, Valor> debitados = ValoresPorE2e(log, EtapaLancamento.DebitoPagador);
        HashSet<EndToEndId> estornados = EtapasPorE2e(log, EtapaLancamento.EstornoPagador);
        HashSet<EndToEndId> creditados = EtapasPorE2e(log, EtapaLancamento.CreditoRecebedor);

        foreach ((EndToEndId e2e, Valor valor) in debitados)
        {
            if (estornados.Contains(e2e))
            {
                // Debitado e estornado: efeito liquido zero, nada em transito.
                continue;
            }

            if (!liquidados.Contains(e2e))
            {
                yield return new DivergenciaPorE2E(
                    e2e,
                    ClasseDeDivergencia.PendenciaDoPagador,
                    ispb,
                    valor,
                    "debitado no PSP e sem liquidacao correspondente no SPI");
            }
        }

        // Do outro lado: o SPI liquidou a favor deste participante e o credito ao cliente nao
        // aconteceu. O valor vem do proprio log do SPI.
        foreach ((EndToEndId e2e, Valor valor) in CreditosDevidos(ispb))
        {
            if (!creditados.Contains(e2e))
            {
                yield return new DivergenciaPorE2E(
                    e2e,
                    ClasseDeDivergencia.PendenciaDoRecebedor,
                    ispb,
                    valor,
                    "liquidado no SPI a favor deste participante e sem credito ao cliente");
            }
        }
    }

    private IEnumerable<(EndToEndId E2E, Valor Valor)> CreditosDevidos(Ispb ispb)
    {
        ContaId contaPi = ContaId.Pi(ispb);

        foreach (Commit commit in _spi.Log())
        {
            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                if (lancamento.Chave.Etapa == EtapaLancamento.Liquidacao
                    && lancamento.Credito.Equals(contaPi)
                    && lancamento.Chave.E2E is not null)
                {
                    yield return (lancamento.Chave.E2E, lancamento.Valor);
                }
            }
        }
    }

    /// <summary>
    /// Sobra que os E2E nao explicam. Se a diferenca de um participante nao bate com a soma das
    /// divergencias atribuidas a ele, o resto e orfao.
    /// </summary>
    private static IEnumerable<DivergenciaPorE2E> OrfasResiduais(
        IReadOnlyDictionary<Ispb, Saldo> diferencas,
        IReadOnlyList<DivergenciaPorE2E> explicadas)
    {
        foreach ((Ispb ispb, Saldo diferenca) in diferencas)
        {
            // As DUAS classes de pendencia contribuem com o MESMO sinal, e conferir isso com
            // numeros e obrigatorio porque a intuicao erra:
            //
            //   pendencia do pagador   — o espelho ja caiu (seta 3) e a conta PI ainda nao (seta 5),
            //                            logo PI - espelho = +V;
            //   pendencia do recebedor — a conta PI ja subiu (seta 5) e o espelho ainda nao
            //                            (seta 7), logo PI - espelho = +V.
            //
            // Nos dois casos o SPI segura mais do que o espelho do PSP reflete. A primeira versao
            // deste codigo somava a pendencia do pagador com sinal negativo, e o residuo saia 2V:
            // toda pendencia legitima do pagador virava uma orfa do dobro do valor — alarme falso
            // que treina o operador a ignorar o alarme.
            long explicado = explicadas
                .Where(d => d.Participante.Equals(ispb) && d.Classe != ClasseDeDivergencia.Orfa)
                .Sum(d => d.Valor.Centavos);

            long residuo = diferenca.Centavos - explicado;

            if (residuo != 0)
            {
                yield return new DivergenciaPorE2E(
                    E2E: null,
                    ClasseDeDivergencia.Orfa,
                    ispb,
                    Valor.DeCentavos(Math.Abs(residuo)),
                    $"diferenca de {residuo} centavos entre conta PI e espelho sem E2E que a explique");
            }
        }
    }

    private static Dictionary<EndToEndId, Valor> ValoresPorE2e(IReadOnlyList<Commit> log, EtapaLancamento etapa)
    {
        Dictionary<EndToEndId, Valor> encontrados = [];

        foreach (Commit commit in log)
        {
            foreach (Lancamento lancamento in commit.Lancamentos)
            {
                if (lancamento.Chave.Etapa == etapa && lancamento.Chave.E2E is not null)
                {
                    encontrados[lancamento.Chave.E2E] = lancamento.Valor;
                }
            }
        }

        return encontrados;
    }

    private static HashSet<EndToEndId> EtapasPorE2e(IReadOnlyList<Commit> log, EtapaLancamento etapa) =>
        [.. ValoresPorE2e(log, etapa).Keys];
}
