using System.Reflection;
using System.Runtime.CompilerServices;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Testes.Comum;

namespace MotorPix.Psp.Nucleo.Testes;

/// <summary>
/// O agregado <see cref="Transacao"/> visto de fora: nascimento, carimbos de tempo, historico
/// append-only, guardas de construcao e a superficie que o invariante 4 exige (estado sem setter
/// publico).
/// <para>
/// As transicoes em si sao cobertas celula a celula em <see cref="MatrizDeTransicoesTestes"/>.
/// Aqui interessa o que a maquina de estados nao ve: quem escreve, quando escreve, e o que o
/// chamador consegue mexer.
/// </para>
/// </summary>
public sealed class TransacaoTestes
{
    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");

    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");

    private static readonly ContaId ContaDeOrigem = ContaId.Cliente(LedgerId.Psp(IspbPagador), "0001");

    private static readonly ChavePix ChaveDeDestino = ChavePix.Criar(TipoChave.Email, "recebedor@exemplo.com");

    private static readonly Valor ValorDoPagamento = Valor.DeCentavos(250_00);

    /// <summary>
    /// <c>[*] --&gt; RECEBIDA</c>: o unico caminho de entrada. Nasce sem historico porque nascer nao
    /// e transicao — se a construcao gravasse uma entrada, o historico deixaria de ser a lista de
    /// eventos aplicados e viraria a lista de eventos aplicados mais um.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Iniciar_NoInstanteInformado_NasceEmRecebidaComHistoricoVazioECarimbosIguais()
    {
        EndToEndId e2e = NovoE2e();

        Transacao transacao = Transacao.Iniciar(
            e2e,
            ContaDeOrigem,
            ChaveDeDestino,
            ValorDoPagamento,
            RelogioFake.Epoca);

        Assert.Equal(EstadoTransacao.Recebida, transacao.Estado);
        Assert.Empty(transacao.Historico);

        Assert.Equal(RelogioFake.Epoca, transacao.CriadaEm);
        Assert.Equal(RelogioFake.Epoca, transacao.AtualizadaEm);
        Assert.Equal(transacao.CriadaEm, transacao.AtualizadaEm);

        Assert.Equal(e2e, transacao.E2E);
        Assert.Equal(TipoTransacao.Pagamento, transacao.Tipo);
        Assert.Equal(ContaDeOrigem, transacao.ContaOrigem);
        Assert.Equal(ChaveDeDestino, transacao.ChaveDestino);
        Assert.Equal(ValorDoPagamento, transacao.Valor);

        // Nada do que so a validacao local descobre pode ja estar preenchido.
        Assert.Null(transacao.E2eOriginal);
        Assert.Null(transacao.IspbDestino);
        Assert.Null(transacao.ContaDestino);
        Assert.Null(transacao.MotivoRejeicao);
    }

    /// <summary>
    /// A transicao valida move <c>AtualizadaEm</c> para o instante recebido e acrescenta
    /// <b>exatamente uma</b> entrada. <c>CriadaEm</c> nao anda: e dela que o gate 4 precisa para
    /// responder um reenvio com a resposta original.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Aplicar_TransicaoValida_EmpurraAtualizadaEmEAcrescentaUmaUnicaEntradaAoHistorico()
    {
        RelogioFake relogio = new();
        Transacao transacao = NovaTransacao(relogio);

        relogio.AvancarSegundos(30);
        DateTimeOffset instante = relogio.Agora;

        EstadoTransacao destino = transacao.Aplicar(TipoEvento.ValidacaoLocalOk, instante);

        Assert.Equal(EstadoTransacao.Validada, destino);
        Assert.Equal(EstadoTransacao.Validada, transacao.Estado);
        Assert.Equal(instante, transacao.AtualizadaEm);
        Assert.Equal(RelogioFake.Epoca, transacao.CriadaEm);
        Assert.NotEqual(transacao.CriadaEm, transacao.AtualizadaEm);

        TransicaoAplicada unica = Assert.Single(transacao.Historico);
        Assert.Equal(
            new TransicaoAplicada(
                EstadoTransacao.Recebida,
                TipoEvento.ValidacaoLocalOk,
                EstadoTransacao.Validada,
                instante),
            unica);
    }

    /// <summary>
    /// O historico e append-only pelo mesmo motivo que o ledger: quem detem a lista de fora nao
    /// pode acrescentar nada.
    /// <para>
    /// Conferir so o tipo estatico (<c>IReadOnlyList</c>) nao bastaria — devolver a propria
    /// <c>List</c> satisfaz o tipo estatico e um <c>cast</c> para <c>ICollection</c> recupera o
    /// <c>Add</c>. Por isso o teste vai atras do objeto em tempo de execucao.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Historico_DevolvidoAoChamador_NaoAceitaAcrescimoDeFora()
    {
        RelogioFake relogio = new();
        Transacao transacao = NovaTransacao(relogio);
        relogio.AvancarSegundos(1);
        transacao.Aplicar(TipoEvento.ValidacaoLocalOk, relogio.Agora);

        IReadOnlyList<TransicaoAplicada> historico = transacao.Historico;

        Assert.IsNotType<List<TransicaoAplicada>>(historico);
        Assert.False(
            historico is ICollection<TransicaoAplicada> { IsReadOnly: false },
            "Historico devolveu uma colecao mutavel: um cast recupera o Add e o log deixa de ser append-only.");

        // Se nem ICollection for, melhor ainda — nao existe Add nenhum para chamar; e quando for,
        // o Add tem de recusar em tempo de execucao, e nao apenas estar escondido pelo tipo estatico.
        if (historico is ICollection<TransicaoAplicada> colecao)
        {
            Assert.Throws<NotSupportedException>(
                () => colecao.Add(new TransicaoAplicada(
                    EstadoTransacao.Recebida,
                    TipoEvento.ValidacaoLocalFalhou,
                    EstadoTransacao.Rejeitada,
                    RelogioFake.Epoca)));
        }

        Assert.Single(transacao.Historico);

        PropertyInfo propriedade = PropriedadePublica(nameof(Transacao.Historico));
        Assert.Equal(typeof(IReadOnlyList<TransicaoAplicada>), propriedade.PropertyType);
        Assert.Null(propriedade.SetMethod);
    }

    /// <summary>
    /// Invariante 4, metade "estado nao tem setter publico", conferida na superficie compilada.
    /// <para>
    /// A varredura vale para todas as propriedades publicas do agregado, e nao so para
    /// <c>Estado</c>: um setter publico em <c>AtualizadaEm</c> permitiria reescrever o carimbo que
    /// o gate 5 usa para contar o timeout, sem passar por <c>Aplicar</c>.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Estado_NaSuperficieCompilada_NaoTemSetterPublicoNemInit()
    {
        PropertyInfo estado = PropriedadePublica(nameof(Transacao.Estado));

        Assert.Equal(typeof(EstadoTransacao), estado.PropertyType);
        Assert.NotNull(estado.GetMethod);
        Assert.True(estado.GetMethod!.IsPublic, "Estado precisa ser legivel de fora.");

        Assert.False(
            estado.SetMethod is { IsPublic: true },
            "Estado tem setter publico: o invariante 4 exige que o unico mutador seja Aplicar.");

        Assert.False(
            EhInit(estado.SetMethod),
            "Estado tem setter 'init': um 'with' trocando Estado seria setter publico disfarcado.");

        List<string> comSetterAberto = [];

        foreach (PropertyInfo propriedade in typeof(Transacao).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (propriedade.SetMethod is { IsPublic: true })
            {
                comSetterAberto.Add($"{propriedade.Name} (setter publico)");
            }
            else if (EhInit(propriedade.SetMethod))
            {
                comSetterAberto.Add($"{propriedade.Name} (init)");
            }
        }

        Assert.True(
            comSetterAberto.Count == 0,
            "Propriedades publicas de Transacao gravaveis de fora: " + string.Join(", ", comSetterAberto));

        // Campo publico contornaria a propriedade inteira.
        FieldInfo[] campos = typeof(Transacao).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.True(
            campos.Length == 0,
            $"Transacao expoe campos publicos: [{string.Join(", ", campos.Select(c => c.Name))}].");
    }

    /// <summary>
    /// <see cref="Transacao"/> e <c>sealed class</c> e nao <c>record</c>.
    /// <para>
    /// Num record, <c>transacao with { Estado = EstadoTransacao.Liquidada }</c> e um setter publico
    /// disfarcado: o compilador sintetiza o clone e o construtor de copia, e o estado passa a ser
    /// gravavel de fora sem nunca consultar a tabela de transicoes. O teste procura exatamente as
    /// duas marcas sintetizadas.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Transacao_NaoEhRecord_PorqueUmWithTrocandoEstadoSeriaSetterPublicoDisfarcado()
    {
        Type tipo = typeof(Transacao);

        Assert.True(tipo.IsClass, "Transacao tem identidade e ciclo de vida: e classe.");
        Assert.True(tipo.IsSealed, "Transacao e sealed: herdar dela abriria um caminho paralelo de mutacao.");

        Assert.Null(MetodoDeClonagem(tipo));
        Assert.Null(tipo.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance));

        ConstructorInfo[] construtoresDeCopia = tipo
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(construtor =>
            {
                ParameterInfo[] parametros = construtor.GetParameters();
                return parametros.Length == 1 && parametros[0].ParameterType == tipo;
            })
            .ToArray();

        Assert.True(
            construtoresDeCopia.Length == 0,
            "Transacao tem construtor de copia, que e a outra marca de record.");

        // Controle da sonda: TransicaoAplicada E record, e a mesma busca tem de achar a clonagem
        // la. Sem este par, um nome errado de metodo faria o teste acima passar por vacuidade.
        Assert.NotNull(MetodoDeClonagem(typeof(TransicaoAplicada)));
    }

    /// <summary>
    /// <c>default(Valor)</c> vale zero centavos e nao e um valor valido; a struct nao consegue
    /// impedir a propria construcao default, entao quem consome tem de recusar na fronteira.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Iniciar_ComValorDefault_LancaValorInvalidoException()
    {
        ValorInvalidoException excecao = Assert.Throws<ValorInvalidoException>(
            () => Transacao.Iniciar(NovoE2e(), ContaDeOrigem, ChaveDeDestino, default, RelogioFake.Epoca));

        Assert.Equal(0, excecao.Centavos);
    }

    /// <summary>
    /// Argumento nulo e erro de programacao, nao recusa de negocio: <see cref="ArgumentNullException"/>,
    /// e nao uma excecao de dominio. O <c>ParamName</c> e assertado porque sem ele o teste passaria
    /// com a guarda errada disparando.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Iniciar_ComArgumentoNulo_LancaArgumentNullExceptionNomeandoOParametro()
    {
        ArgumentNullException semE2e = Assert.Throws<ArgumentNullException>(
            () => Transacao.Iniciar(null!, ContaDeOrigem, ChaveDeDestino, ValorDoPagamento, RelogioFake.Epoca));
        Assert.Equal("e2e", semE2e.ParamName);

        ArgumentNullException semConta = Assert.Throws<ArgumentNullException>(
            () => Transacao.Iniciar(NovoE2e(), null!, ChaveDeDestino, ValorDoPagamento, RelogioFake.Epoca));
        Assert.Equal("contaOrigem", semConta.ParamName);

        ArgumentNullException semChave = Assert.Throws<ArgumentNullException>(
            () => Transacao.Iniciar(NovoE2e(), ContaDeOrigem, null!, ValorDoPagamento, RelogioFake.Epoca));
        Assert.Equal("chaveDestino", semChave.ParamName);
    }

    /// <summary>
    /// Invariante 7: devolucao e transacao propria, com E2E proprio, referenciando a original. Sem
    /// a referencia ela nao e devolucao de nada.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Iniciar_DevolucaoSemE2eOriginal_LancaOrdemInvalidaException()
    {
        EndToEndId e2e = NovoE2e();

        OrdemInvalidaException excecao = Assert.Throws<OrdemInvalidaException>(
            () => Transacao.Iniciar(
                e2e,
                ContaDeOrigem,
                ChaveDeDestino,
                ValorDoPagamento,
                RelogioFake.Epoca,
                TipoTransacao.Devolucao));

        Assert.Equal(e2e, excecao.E2E);
    }

    /// <summary>
    /// O outro lado do mesmo discriminador: pagamento nao referencia original.
    /// <para>
    /// E este lado que corta a recursao no gate 6. Se um pagamento pudesse carregar
    /// <c>E2eOriginal</c>, "devolucao" viraria um adjetivo opcional e a devolucao de uma devolucao
    /// deixaria de ser detectavel pelo tipo — o mesmo dinheiro voltaria de ida e volta indefinidamente,
    /// com lancamentos validos em todos os saltos.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Iniciar_PagamentoComE2eOriginal_LancaOrdemInvalidaException()
    {
        GeradorE2eDeterministico gerador = new(IspbPagador, RelogioFake.Epoca);
        EndToEndId original = gerador.Proximo();
        EndToEndId novo = gerador.Proximo();

        OrdemInvalidaException excecao = Assert.Throws<OrdemInvalidaException>(
            () => Transacao.Iniciar(
                novo,
                ContaDeOrigem,
                ChaveDeDestino,
                ValorDoPagamento,
                RelogioFake.Epoca,
                TipoTransacao.Pagamento,
                original));

        Assert.Equal(novo, excecao.E2E);
    }

    /// <summary>
    /// A devolucao bem formada nasce como qualquer outra transacao: em RECEBIDA, com E2E proprio,
    /// guardando a referencia a original. Nada na original e tocado — nem podia ser, ela nem entra
    /// aqui.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Iniciar_DevolucaoComE2eOriginal_NasceEmRecebidaGuardandoAReferencia()
    {
        GeradorE2eDeterministico gerador = new(IspbPagador, RelogioFake.Epoca);
        EndToEndId original = gerador.Proximo();
        EndToEndId devolucao = gerador.Proximo();
        Assert.NotEqual(original, devolucao);

        Transacao transacao = Transacao.Iniciar(
            devolucao,
            ContaDeOrigem,
            ChaveDeDestino,
            ValorDoPagamento,
            RelogioFake.Epoca,
            TipoTransacao.Devolucao,
            original);

        Assert.Equal(EstadoTransacao.Recebida, transacao.Estado);
        Assert.Equal(TipoTransacao.Devolucao, transacao.Tipo);
        Assert.Equal(devolucao, transacao.E2E);
        Assert.Equal(original, transacao.E2eOriginal);
        Assert.Empty(transacao.Historico);
    }

    /// <summary>Resolucao do DICT guardada como veio, sem reinterpretacao.</summary>
    [Fact]
    [Trait("gate", "3")]
    public void RegistrarDestino_ComAResolucaoDoDict_GuardaIspbEConta()
    {
        Transacao transacao = NovaTransacao(new RelogioFake());
        ContaId contaDoRecebedor = ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0002");

        transacao.RegistrarDestino(IspbRecebedor, contaDoRecebedor);

        Assert.Equal(IspbRecebedor, transacao.IspbDestino);
        Assert.Equal(contaDoRecebedor, transacao.ContaDestino);
    }

    /// <summary>
    /// Registro parcial e pior que registro nenhum: com ISPB gravado e conta ausente, o
    /// <c>pacs.008</c> sairia enderecado ao participante certo e a conta nenhuma.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void RegistrarDestino_ComArgumentoNulo_LancaSemGravarMetadeDaResolucao()
    {
        Transacao transacao = NovaTransacao(new RelogioFake());
        ContaId contaDoRecebedor = ContaId.Cliente(LedgerId.Psp(IspbRecebedor), "0002");

        ArgumentNullException semIspb = Assert.Throws<ArgumentNullException>(
            () => transacao.RegistrarDestino(null!, contaDoRecebedor));
        Assert.Equal("ispb", semIspb.ParamName);

        ArgumentNullException semConta = Assert.Throws<ArgumentNullException>(
            () => transacao.RegistrarDestino(IspbRecebedor, null!));
        Assert.Equal("conta", semConta.ParamName);

        Assert.Null(transacao.IspbDestino);
        Assert.Null(transacao.ContaDestino);
    }

    /// <summary>Todo motivo do enum e aceito e devolvido igual.</summary>
    [Fact]
    [Trait("gate", "3")]
    public void RegistrarMotivoDeRejeicao_ComQualquerMotivoDoEnum_GuardaOQueRecebeu()
    {
        foreach (MotivoRejeicaoLocal motivo in Enum.GetValues<MotivoRejeicaoLocal>())
        {
            Transacao transacao = NovaTransacao(new RelogioFake());
            Assert.Null(transacao.MotivoRejeicao);

            transacao.RegistrarMotivoDeRejeicao(motivo);

            Assert.Equal(motivo, transacao.MotivoRejeicao);
        }
    }

    /// <summary>
    /// Um <c>enum</c> em C# aceita qualquer valor do tipo subjacente por conversao explicita. Sem a
    /// guarda, um motivo inexistente chegaria a resposta da API como numero solto, e o
    /// <c>switch</c> que o traduzir cairia no caso default de alguem.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void RegistrarMotivoDeRejeicao_ComValorForaDoEnum_LancaESemGuardarNada()
    {
        Transacao transacao = NovaTransacao(new RelogioFake());

        ArgumentOutOfRangeException excecao = Assert.Throws<ArgumentOutOfRangeException>(
            () => transacao.RegistrarMotivoDeRejeicao((MotivoRejeicaoLocal)999));

        Assert.Equal("motivo", excecao.ParamName);
        Assert.Null(transacao.MotivoRejeicao);
    }

    /// <summary>
    /// O caminho feliz da maquina, do ponto de vista do historico: tres entradas encadeadas, em que
    /// o destino de cada uma e a origem da seguinte, e a ultima concorda com o estado corrente. E
    /// esse encadeamento que torna o historico reproduzivel no replay do gate 8 — uma sequencia com
    /// buraco produziria projecao diferente do agregado sem que nenhum estado final denunciasse.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void CaminhoCompleto_DeRecebidaAteLiquidada_EncadeiaTresTransicoesNoHistorico()
    {
        RelogioFake relogio = new();
        Transacao transacao = NovaTransacao(relogio);

        TipoEvento[] eventos =
        [
            TipoEvento.ValidacaoLocalOk,
            TipoEvento.DebitoLancadoEPacs008Despachado,
            TipoEvento.Pacs002Acsc,
        ];

        DateTimeOffset[] instantes = new DateTimeOffset[eventos.Length];

        for (int i = 0; i < eventos.Length; i++)
        {
            relogio.AvancarSegundos(5);
            instantes[i] = relogio.Agora;
            transacao.Aplicar(eventos[i], instantes[i]);
        }

        Assert.Equal(EstadoTransacao.Liquidada, transacao.Estado);

        Assert.Collection(
            transacao.Historico,
            passo => Assert.Equal(
                new TransicaoAplicada(
                    EstadoTransacao.Recebida,
                    TipoEvento.ValidacaoLocalOk,
                    EstadoTransacao.Validada,
                    instantes[0]),
                passo),
            passo => Assert.Equal(
                new TransicaoAplicada(
                    EstadoTransacao.Validada,
                    TipoEvento.DebitoLancadoEPacs008Despachado,
                    EstadoTransacao.EnviadaSpi,
                    instantes[1]),
                passo),
            passo => Assert.Equal(
                new TransicaoAplicada(
                    EstadoTransacao.EnviadaSpi,
                    TipoEvento.Pacs002Acsc,
                    EstadoTransacao.Liquidada,
                    instantes[2]),
                passo));

        IReadOnlyList<TransicaoAplicada> historico = transacao.Historico;

        for (int i = 1; i < historico.Count; i++)
        {
            Assert.Equal(historico[i - 1].Destino, historico[i].Origem);
        }

        Assert.Equal(transacao.Estado, historico[^1].Destino);
        Assert.Equal(transacao.AtualizadaEm, historico[^1].Em);
        Assert.Equal(RelogioFake.Epoca, transacao.CriadaEm);
    }

    private static EndToEndId NovoE2e() => new GeradorE2eDeterministico(IspbPagador, RelogioFake.Epoca).Proximo();

    private static Transacao NovaTransacao(RelogioFake relogio) =>
        Transacao.Iniciar(NovoE2e(), ContaDeOrigem, ChaveDeDestino, ValorDoPagamento, relogio.Agora);

    private static PropertyInfo PropriedadePublica(string nome)
    {
        PropertyInfo? propriedade = typeof(Transacao).GetProperty(nome, BindingFlags.Public | BindingFlags.Instance);

        Assert.True(
            propriedade is not null,
            $"Transacao nao expoe a propriedade publica '{nome}'; publicas: "
                + $"[{string.Join(", ", typeof(Transacao).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name))}].");

        return propriedade!;
    }

    /// <summary>
    /// O clone sintetizado dos records tem nome impronunciavel em C# e so aparece por reflexao.
    /// Encontra-lo e a prova de que o tipo e record.
    /// </summary>
    private static MethodInfo? MetodoDeClonagem(Type tipo) =>
        tipo.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Setter <c>init</c> nao se distingue de um setter comum por <c>IsPublic</c>: a diferenca esta
    /// no modificador obrigatorio <c>IsExternalInit</c> gravado no retorno do acessor.
    /// </summary>
    private static bool EhInit(MethodInfo? setter) =>
        setter is not null
        && setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
}
