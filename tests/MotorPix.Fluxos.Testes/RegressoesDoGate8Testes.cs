using MotorPix.Composicao;
using MotorPix.Contratos;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using MotorPix.Spi;
using MotorPix.Testes.Comum;

namespace MotorPix.Fluxos.Testes;

/// <summary>
/// Regressoes dos defeitos que a revisao adversarial do gate 8 encontrou.
/// <para>
/// Os dois tem o mesmo tema, que e o tema do gate: estado que o motor mantem em memoria e que o log
/// nao reconstroi — ou reconstroi diferente — nao e projecao, e uma segunda verdade.
/// </para>
/// </summary>
public sealed class RegressoesDoGate8Testes
{
    private const long Fundo = 1_000_00;
    private const long Pagamento = 100_00;

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    // ---------------------------------------------------------------------------------------
    // A1 — o indice vivo e o replay desempatavam ao contrario
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "8")]
    public void Congelar_DuasVezesOMesmoE2e_MantemAPrimeiraRespostaEmAmbosOsLados()
    {
        // Congelar sobrescrevia: o indice vivo desempatava por "ultima vence" e a projecao por
        // "primeira vence". Os dois divergiam sobre o MESMO log — exatamente o que este gate existe
        // para impedir —, e o indice apagava a resposta que ja tinha saido pela API.
        RegistroDeIdempotencia registro = new();
        EndToEndId e2e = E2eDe(1);
        ImpressaoDoPedido impressao = ImpressaoDoPedido.De(
            ContaId.Cliente(LedgerId.Psp(IspbPagador), "0001"),
            ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com"),
            Valor.DeCentavos(Pagamento));

        Assert.True(registro.TentarReivindicar(e2e, impressao, out _));

        registro.Congelar(e2e, impressao, EstadoTransacao.EnviadaSpi, null, RelogioFake.Epoca);
        registro.Congelar(e2e, impressao, EstadoTransacao.Liquidada, null, RelogioFake.Epoca);

        // O indice vivo mantem a primeira.
        Assert.True(registro.TryEntrada(e2e, out EntradaDeIdempotencia? viva));
        Assert.NotNull(viva);
        Assert.Equal(EstadoTransacao.EnviadaSpi, viva.EstadoRespondido);

        // E o replay concorda com ele.
        ProjecaoRespostas reconstruida = ProjecaoRespostas.Reconstruir(registro.Log());
        Assert.Equal(EstadoTransacao.EnviadaSpi, reconstruida.Resposta(e2e).EstadoRespondido);
        Assert.Equal(viva, reconstruida.Resposta(e2e));

        // O log tambem nao ganhou entrada nova: congelar de novo e no-op, nao append.
        Assert.Single(registro.Log());
    }

    // ---------------------------------------------------------------------------------------
    // C2 — a decisao do SPI tem de sobreviver a um replay
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "8")]
    public void ProjecaoDecisoes_ReconstruidaDoLog_RespondeAConsultaIgualAoSpiVivo()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, "0002");
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        motor.Recebedor.RegistrarChave(chave, contaRecebedor);

        EndToEndId liquidado = E2eDe(1);
        motor.Pagador.Pagar(new ComandoDePagamento(liquidado, "0001", chave, Valor.DeCentavos(Pagamento)));
        motor.Barramento.Drenar();

        // Uma recusa que NAO deixa lancamento nenhum no ledger: e o caso que provava a lacuna.
        // Reconstruindo so dos tres ledgers, esta decisao seria invisivel.
        EndToEndId recusado = E2eDe(2);
        Ispb forasteiro = Ispb.Criar("99999999");

        motor.Spi.ReceberPacs008(new MotorPix.Mensagens.Pacs008(
            recusado,
            IspbPagador,
            ContaId.Cliente(motor.Pagador.Ledger.Id, "0001"),
            forasteiro,
            ContaId.Cliente(LedgerId.Psp(forasteiro), "0003"),
            Valor.DeCentavos(Pagamento),
            chave));

        int commitsDoSpi = motor.Spi.Consulta.Log().Count;

        ProjecaoDecisoes reconstruida = ProjecaoDecisoes.Reconstruir(motor.Spi.LogDeDecisoes());

        // A decisao de recusa existe no log e NAO deixou rastro no ledger: e por isso que o ledger
        // sozinho nao bastava.
        Assert.Equal(ResultadoConsulta.Rejeitada, reconstruida.ConsultarStatus(recusado));
        Assert.Equal(ResultadoConsulta.Rejeitada, motor.Spi.ConsultarStatus(recusado));
        Assert.Equal(commitsDoSpi, motor.Spi.Consulta.Log().Count);

        // E o caso liquidado tambem bate.
        Assert.Equal(ResultadoConsulta.Liquidada, reconstruida.ConsultarStatus(liquidado));
        Assert.Equal(ResultadoConsulta.Liquidada, motor.Spi.ConsultarStatus(liquidado));

        // E2E que ninguem viu continua indeterminado nos dois lados — a resposta honesta.
        Assert.Equal(ResultadoConsulta.Indeterminada, reconstruida.ConsultarStatus(E2eDe(9)));
        Assert.Equal(ResultadoConsulta.Indeterminada, motor.Spi.ConsultarStatus(E2eDe(9)));
    }

    [Fact]
    [Trait("gate", "8")]
    public void ProjecaoDecisoes_ReconstruidaDuasVezes_EhDeterministicaESensivelAOrdem()
    {
        MotorMontado motor = MontarMotor();
        ContaId contaRecebedor = ContaId.Cliente(motor.Recebedor.Ledger.Id, "0002");
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");
        motor.Recebedor.RegistrarChave(chave, contaRecebedor);

        for (long i = 1; i <= 3; i++)
        {
            motor.Pagador.Pagar(new ComandoDePagamento(E2eDe(i), "0001", chave, Valor.DeCentavos(Pagamento)));
            motor.Barramento.Drenar();
        }

        IReadOnlyList<DecisaoDoSpi> log = motor.Spi.LogDeDecisoes();
        Assert.Equal(3, log.Count);

        // O1: determinismo.
        Assert.True(ProjecaoDecisoes.Reconstruir(log).EhIgualA(ProjecaoDecisoes.Reconstruir(log)));

        // O4: sensibilidade a ordem. O log carrega numero de sequencia, entao embaralhar nao produz
        // outro resultado — produz RECUSA, que e a forma mais forte de sensibilidade: o replay se
        // recusa a acreditar num log que nao e o log.
        Assert.Throws<MotorPix.Dominio.Excecoes.AtoContabilInvalidoException>(
            () => ProjecaoDecisoes.Reconstruir(log.Reverse().ToList()));
    }

    private static MotorMontado MontarMotor() =>
        MotorMontado.Montar(new RelogioFake(), IspbPagador, IspbRecebedor, Valor.DeCentavos(Fundo));

    private static EndToEndId E2eDe(long indice) =>
        new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).EmSequencia(indice);
}
