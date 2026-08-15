using MotorPix.Composicao;
using MotorPix.Contratos;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Regressoes dos defeitos que a revisao adversarial do gate 5 encontrou. Os tres tem em comum o
/// mesmo tema: a API do PSP nao entregava o que a conciliacao do gate 7 vai precisar, e entregava
/// o que ela nao deveria ter.
/// </summary>
public sealed class RegressoesDoGate5Testes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;

    private static readonly TimeSpan Limite = new(0, 0, 20);
    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // ---------------------------------------------------------------------------------------
    // A1 — a entidade viva nao pode vazar: Aplicar e publico
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void TryTransacao_DevolveVistaImutavel_ParaQueNinguemFecheExpiradaSemConsultar()
    {
        // Com a entidade viva na mao, uma unica linha fecharia o caso: t.Aplicar(
        // ConsultaConfirmaRejeicao, agora). Sem consultar o SPI, sem estornar o debito — o estorno
        // mora no PSP, nao na entidade — e fora do lock que serializa o agregado. Seria a opcao que
        // o invariante 6 proibe, e a conciliacao do gate 7 e o primeiro consumidor tentado a usa-la.
        (RelogioFake relogio, MotorMontado motor) = Montar();
        EndToEndId e2e = PagarEExpirar(relogio, motor);

        Assert.True(motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? vista));
        Assert.NotNull(vista);
        Assert.Equal(EstadoTransacao.Expirada, vista.Estado);

        // A vista nao tem como mover a transacao: nao existe Aplicar nela.
        Assert.Null(typeof(VistaDaTransacao).GetMethod(nameof(Transacao.Aplicar)));

        // E ela e uma copia: mexer no que se leu nao mexe no agregado.
        Assert.NotSame(vista.Historico, motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? outra) ? outra!.Historico : null);
        Assert.Equal(EstadoTransacao.Expirada, motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? depois) ? depois!.Estado : default);
    }

    // ---------------------------------------------------------------------------------------
    // A2 — o ciclo expirar-entao-consultar tem de ser expressavel pela API publica
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void ExpirarVencidos_DevolveQuaisExpiraram_ParaQueOCicloDeConciliacaoSejaExecutavel()
    {
        // Devolver so a contagem tornava o criterio de aceite do gate 7 — "nenhuma transacao
        // permanece em EXPIRADA" — inexecutavel: quem recebesse "3" nao teria como saber quem
        // consultar, e teria de manter por fora o indice que o PSP ja tem.
        (RelogioFake relogio, MotorMontado motor) = Montar();
        ContaId destino = PrepararDestino(motor);
        Assert.NotNull(destino);

        EndToEndId primeiro = E2eDe(1);
        EndToEndId segundo = E2eDe(2);
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

        motor.Pagador.Pagar(new ComandoDePagamento(primeiro, "0001", chave, Valor.DeCentavos(Pagamento)));
        motor.Pagador.Pagar(new ComandoDePagamento(segundo, "0001", chave, Valor.DeCentavos(Pagamento)));

        relogio.Avancar(Limite);
        IReadOnlyCollection<EndToEndId> expiradas = motor.Pagador.ExpirarVencidos();

        Assert.Equal(2, expiradas.Count);
        Assert.Contains(primeiro, expiradas);
        Assert.Contains(segundo, expiradas);

        // E a lista das que continuam em EXPIRADA, que e a que a conciliacao precisa zerar.
        Assert.Equal(2, motor.Pagador.Expiradas.Count);

        // O ciclo completo, escrito como o host escreveria: expirar, consultar cada uma, e a lista
        // esvazia. Sem essas portas, isto nao seria escrevivel.
        foreach (EndToEndId e2e in expiradas)
        {
            motor.Pagador.ConsultarStatus(e2e);
        }

        // O SPI nunca viu as ordens (nada foi drenado), entao as duas seguem indeterminadas — e
        // continuam em EXPIRADA, esperando a conciliacao. O ponto aqui e que o ciclo roda.
        Assert.Equal(2, motor.Pagador.Expiradas.Count);

        // Agora as ordens chegam ao SPI e a consulta fecha as duas.
        motor.Barramento.Drenar();

        foreach (EndToEndId e2e in motor.Pagador.Expiradas.ToArray())
        {
            motor.Pagador.ConsultarStatus(e2e);
        }

        Assert.Empty(motor.Pagador.Expiradas);
    }

    // ---------------------------------------------------------------------------------------
    // M1 — a worklist de pacs.002 atrasados tem de encolher
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "5")]
    public void Pacs002Atrasados_ApagaOE2eQuandoAConsultaOFecha_ParaQueOLacoDoHostTermine()
    {
        // O laco obvio do host e "consultar enquanto houver atrasados". Enquanto a lista so
        // crescia, a primeira volta fechava tudo e a segunda reconsultava os mesmos E2E — agora ja
        // liquidados, portanto no-op — e a condicao de parada nunca se tornava falsa: laco infinito.
        // Pior, um E2E resolvido continuava respondendo "pendente" para quem lesse a lista.
        (RelogioFake relogio, MotorMontado motor) = Montar();
        PrepararDestino(motor);

        EndToEndId e2e = E2eDe(1);
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        motor.Pagador.Pagar(new ComandoDePagamento(e2e, "0001", chave, Valor.DeCentavos(Pagamento)));

        relogio.Avancar(Limite);
        Assert.Single(motor.Pagador.ExpirarVencidos());

        // So agora o pacs.002 e entregue, sobre uma transacao ja EXPIRADA.
        motor.Barramento.Drenar();

        Assert.Contains(e2e, motor.Pagador.Pacs002Atrasados);
        Assert.Equal(EstadoTransacao.Expirada, motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? antes) ? antes!.Estado : default);

        // O laco do host termina.
        int voltas = 0;

        while (motor.Pagador.Pacs002Atrasados.Count > 0)
        {
            if (++voltas > 5)
            {
                Assert.Fail("a worklist de pacs.002 atrasados nao esvazia: o laco do host nunca terminaria");
            }

            foreach (EndToEndId pendente in motor.Pagador.Pacs002Atrasados.ToArray())
            {
                motor.Pagador.ConsultarStatus(pendente);
            }
        }

        Assert.Equal(1, voltas);
        Assert.Empty(motor.Pagador.Pacs002Atrasados);
        Assert.Equal(EstadoTransacao.Liquidada, motor.Pagador.TryTransacao(e2e, out VistaDaTransacao? depois) ? depois!.Estado : default);
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    private static (RelogioFake Relogio, MotorMontado Motor) Montar()
    {
        RelogioFake relogio = new();

        MotorMontado motor = MotorMontado.Montar(
            relogio,
            IspbPagador,
            IspbRecebedor,
            Valor.DeCentavos(Fundo),
            politica: new PoliticaDeTimeout(Limite));

        return (relogio, motor);
    }

    private static ContaId PrepararDestino(MotorMontado motor)
    {
        ContaId conta = ContaId.Cliente(motor.Recebedor.Ledger.Id, "0002");
        motor.Recebedor.RegistrarChave(ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com"), conta);
        return conta;
    }

    private static EndToEndId PagarEExpirar(RelogioFake relogio, MotorMontado motor)
    {
        PrepararDestino(motor);

        EndToEndId e2e = E2eDe(1);
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        motor.Pagador.Pagar(new ComandoDePagamento(e2e, "0001", chave, Valor.DeCentavos(Pagamento)));

        relogio.Avancar(Limite);
        motor.Pagador.ExpirarVencidos();

        return e2e;
    }

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);
}
