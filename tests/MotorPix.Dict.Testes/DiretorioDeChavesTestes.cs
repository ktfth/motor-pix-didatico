using MotorPix.Contratos;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dict.Testes;

/// <summary>
/// Gate 2 — seta 2 do diagrama de arquitetura: "resolver chave" (chave para ISPB mais conta).
/// <para>
/// O teste central desta suite e o primeiro: grafias diferentes da mesma chave resolvem para a
/// mesma entrada. A normalizacao <em>nao</em> acontece aqui — ela mora dentro de
/// <see cref="ChavePix"/>, fechada no gate 1 — e e exatamente isso que faz o indice funcionar sem
/// que o diretorio saiba o que e um CPF, um e-mail ou um E.164. O diretorio so sabe usar uma chave
/// como chave de dicionario; quem garante que "529.982.247-25" e "52998224725" sao a mesma chave e
/// o value object.
/// </para>
/// <para>
/// A consequencia pratica: se amanha o formato canonico do telefone mudar, nenhuma linha deste
/// modulo muda. E, na direcao contraria, se a normalizacao regredir, o sintoma aparece aqui como
/// "o DICT as vezes nao acha" — longe da causa. Por isso as duas suites cobrem a mesma igualdade
/// de angulos diferentes.
/// </para>
/// </summary>
public sealed class DiretorioDeChavesTestes
{
    // CPF 529.982.247-25 — DV1: 5*10+2*9+9*8+9*7+8*6+2*5+2*4+4*3+7*2 = 295; 295 % 11 = 9; 11-9 = 2.
    //                      DV2: 5*11+2*10+9*9+9*8+8*7+2*6+2*5+4*4+7*3+2*2 = 347; 347 % 11 = 6; 11-6 = 5.
    private const string CpfCanonico = "52998224725";
    private const string CpfPontuado = "529.982.247-25";
    private const string CpfComEspacos = " 52998224725 ";

    // CPF 111.444.777-35 — DV1: soma 162; 162 % 11 = 8; 11-8 = 3.  DV2: soma 204; 204 % 11 = 6; 11-6 = 5.
    private const string OutroCpfCanonico = "11144477735";

    // CNPJ 11.222.333/0001-81 — DV1: soma 102; 102 % 11 = 3; 11-3 = 8.  DV2: soma 120; 120 % 11 = 10; 11-10 = 1.
    private const string CnpjCanonico = "11222333000181";
    private const string CnpjPontuado = "11.222.333/0001-81";

    private const string EmailCanonico = "fulano@exemplo.com";
    private const string EmailCaixaAlta = "  FULANO@Exemplo.COM ";

    private const string TelefoneCanonico = "+5511999999999";
    private const string TelefonePontuado = "+55 (11) 99999-9999";

    // UUID de namespace do RFC 4122 (DNS): literal fixo, porque Guid.NewGuid e simbolo banido.
    private const string GuidMinusculo = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";
    private const string GuidMaiusculo = "6BA7B810-9DAD-11D1-80B4-00C04FD430C8";
    private const string GuidComEspacos = "  6ba7b810-9dad-11d1-80b4-00c04fd430c8  ";

    private const string IspbTitularTexto = "11111111";
    private const string IspbOutroTexto = "22222222";

    private const string LedgerTitularTexto = "Psp:11111111";
    private const string LedgerOutroTexto = "Psp:22222222";

    private const string MotivoEspelhoPi = "chave so pode enderecar conta de cliente, e esta e EspelhoPi";
    private const string MotivoAbertura = "chave so pode enderecar conta de cliente, e esta e Abertura";
    private const string MotivoPi = "chave so pode enderecar conta de cliente, e esta e Pi";

    // A ordem de declaracao e a ordem de inicializacao: Ispb antes de LedgerId antes de ContaId.
    private static readonly Ispb IspbTitular = Ispb.Criar(IspbTitularTexto);
    private static readonly Ispb IspbOutro = Ispb.Criar(IspbOutroTexto);
    private static readonly LedgerId LedgerTitular = LedgerId.Psp(IspbTitular);
    private static readonly LedgerId LedgerOutro = LedgerId.Psp(IspbOutro);
    private static readonly ContaId ContaTitular = ContaId.Cliente(LedgerTitular, "0001");
    private static readonly ContaId ContaOutro = ContaId.Cliente(LedgerOutro, "0002");

    // ---------------------------------------------------------------------------------------
    // O teste central: grafias diferentes resolvem a mesma entrada
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Resolver_CpfEmTresGrafias_DevolveExatamenteAMesmaResolucao()
    {
        // Vinculo gravado UMA vez, na forma canonica.
        DiretorioDeChavesEmMemoria diretorio = new();
        ResolucaoChave gravado = new(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular);
        diretorio.Vincular(gravado);

        // Tres textos distintos que o chamador poderia ter digitado. Nenhum deles passa pelo
        // diretorio como texto: viram ChavePix antes, e a ChavePix ja colapsou os tres no mesmo
        // valor canonico. O diretorio nunca ve um ponto, um hifen ou um espaco de borda.
        ResolucaoChave porPontuado = diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfPontuado));
        ResolucaoChave porCanonico = diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfCanonico));
        ResolucaoChave porComEspacos = diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfComEspacos));

        // Mesma instancia, e nao apenas registros estruturalmente iguais: as tres consultas caem na
        // MESMA linha do indice. Se a normalizacao regredisse, duas delas nao achariam entrada
        // nenhuma (excecao) em vez de acharem entradas diferentes — o modo de falha e "as vezes o
        // DICT nao acha", nunca "achou dois titulares".
        Assert.Same(gravado, porPontuado);
        Assert.Same(gravado, porCanonico);
        Assert.Same(gravado, porComEspacos);

        // A entrada devolvida carrega a chave na forma canonica, nao a grafia da consulta.
        Assert.Equal(CpfCanonico, porPontuado.Chave.Texto);
        Assert.Equal(TipoChave.Cpf, porPontuado.Chave.Tipo);
        Assert.Equal(IspbTitular, porPontuado.Ispb);
        Assert.Equal(ContaTitular, porPontuado.Conta);

        // Tres consultas, uma unica entrada gravada.
        Assert.Equal(1, diretorio.Quantidade);
    }

    [Theory]
    [Trait("gate", "2")]
    // CPF: pontuacao e espaco de borda, nos dois sentidos (grava pontuado, consulta limpo e vice-versa).
    [InlineData(TipoChave.Cpf, CpfCanonico, CpfPontuado, CpfCanonico)]
    [InlineData(TipoChave.Cpf, CpfPontuado, CpfCanonico, CpfCanonico)]
    [InlineData(TipoChave.Cpf, CpfCanonico, CpfComEspacos, CpfCanonico)]
    // CNPJ: mesma historia, com a barra do formato.
    [InlineData(TipoChave.Cnpj, CnpjCanonico, CnpjPontuado, CnpjCanonico)]
    [InlineData(TipoChave.Cnpj, CnpjPontuado, CnpjCanonico, CnpjCanonico)]
    // E-mail: caixa alta e espaco de borda.
    [InlineData(TipoChave.Email, EmailCanonico, EmailCaixaAlta, EmailCanonico)]
    [InlineData(TipoChave.Email, "FULANO@EXEMPLO.COM", EmailCanonico, EmailCanonico)]
    // Telefone: mascara de discagem sobre o mesmo E.164.
    [InlineData(TipoChave.Telefone, TelefoneCanonico, TelefonePontuado, TelefoneCanonico)]
    [InlineData(TipoChave.Telefone, "+55-11-99999-9999", TelefoneCanonico, TelefoneCanonico)]
    // Aleatoria: GUID em caixa alta e com espaco de borda.
    [InlineData(TipoChave.Aleatoria, GuidMinusculo, GuidMaiusculo, GuidMinusculo)]
    [InlineData(TipoChave.Aleatoria, GuidMaiusculo, GuidComEspacos, GuidMinusculo)]
    public void Resolver_ChaveGravadaEConsultadaEmGrafiasDiferentes_AchaAMesmaEntrada(
        TipoChave tipo,
        string grafiaGravada,
        string grafiaConsultada,
        string canonicoEsperado)
    {
        DiretorioDeChavesEmMemoria diretorio = new();
        diretorio.Vincular(ChavePix.Criar(tipo, grafiaGravada), IspbTitular, ContaTitular);

        ResolucaoChave resolucao = diretorio.Resolver(ChavePix.Criar(tipo, grafiaConsultada));

        // A forma canonica e uma so, independente de qual das duas grafias entrou primeiro.
        Assert.Equal(canonicoEsperado, resolucao.Chave.Texto);
        Assert.Equal(tipo, resolucao.Chave.Tipo);
        Assert.Equal(IspbTitular, resolucao.Ispb);
        Assert.Equal(ContaTitular, resolucao.Conta);

        // TryResolver caminha pelo mesmo indice: nao existe um segundo caminho de busca que pudesse
        // divergir do primeiro.
        bool achou = diretorio.TryResolver(ChavePix.Criar(tipo, grafiaConsultada), out ResolucaoChave? porTry);
        Assert.True(achou);
        Assert.Same(resolucao, porTry);

        Assert.Equal(1, diretorio.Quantidade);
    }

    // ---------------------------------------------------------------------------------------
    // Resolucao ausente: resultado normal do negocio, nao defeito
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Resolver_ChaveNaoVinculada_LancaChaveNaoEncontradaComAChaveConsultada()
    {
        // Chave ausente e a aresta "validacao local falha" da maquina de estados: o pagamento e
        // recusado antes de virar pacs.008, sem lancamento nenhum. Nao e bug do diretorio — e o
        // caso em que a chave que o pagador digitou simplesmente nao existe no DICT.
        DiretorioDeChavesEmMemoria diretorio = new();
        diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular);

        ChavePix ausente = ChavePix.Criar(TipoChave.Cpf, OutroCpfCanonico);

        ChaveNaoEncontradaNoDictException excecao =
            Assert.Throws<ChaveNaoEncontradaNoDictException>(() => diretorio.Resolver(ausente));

        // A excecao carrega a chave consultada — sem ela, o diagnostico do chamador seria "alguma
        // chave nao existe", e a resolucao acontece dentro de um fluxo com varias chaves em jogo.
        Assert.Equal(ausente, excecao.Chave);
        Assert.Equal(OutroCpfCanonico, excecao.Chave.Texto);
        Assert.Equal(TipoChave.Cpf, excecao.Chave.Tipo);
        Assert.Contains(OutroCpfCanonico, excecao.Message, StringComparison.Ordinal);

        // Toda excecao de regra de negocio desce de MotorPixException (o prompt proibe Exception e
        // InvalidOperationException genericas).
        Assert.IsAssignableFrom<MotorPixException>(excecao);

        // A consulta que falhou nao criou entrada: consultar nao e registrar.
        Assert.Equal(1, diretorio.Quantidade);
    }

    [Theory]
    [Trait("gate", "2")]
    [InlineData(TipoChave.Cpf, CpfCanonico)]
    [InlineData(TipoChave.Cnpj, CnpjCanonico)]
    [InlineData(TipoChave.Email, EmailCanonico)]
    [InlineData(TipoChave.Telefone, TelefoneCanonico)]
    [InlineData(TipoChave.Aleatoria, GuidMinusculo)]
    public void TryResolver_ChaveNaoVinculada_DevolveFalseENuloSemLancar(TipoChave tipo, string bruto)
    {
        // O caminho sem excecao existe porque ausencia e esperada: quem esta validando uma chave
        // digitada nao deveria pagar o custo (nem escrever o try/catch) de um fluxo excepcional.
        DiretorioDeChavesEmMemoria diretorio = new();

        bool achou = diretorio.TryResolver(ChavePix.Criar(tipo, bruto), out ResolucaoChave? resolucao);

        Assert.False(achou);
        Assert.Null(resolucao);
        Assert.Equal(0, diretorio.Quantidade);
    }

    [Fact]
    [Trait("gate", "2")]
    public void TryResolver_ChaveDeOutroTipoComOMesmoTitular_NaoVazaAEntradaVizinha()
    {
        // O titular tem CPF gravado; a consulta pelo CNPJ dele (nunca gravado) e ausencia, nao
        // "acha por aproximacao". O indice e por chave, nao por titular.
        DiretorioDeChavesEmMemoria diretorio = new();
        diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular);

        bool achouCnpj = diretorio.TryResolver(ChavePix.Criar(TipoChave.Cnpj, CnpjCanonico), out ResolucaoChave? porCnpj);
        bool achouCpf = diretorio.TryResolver(ChavePix.Criar(TipoChave.Cpf, CpfPontuado), out ResolucaoChave? porCpf);

        Assert.False(achouCnpj);
        Assert.Null(porCnpj);
        Assert.True(achouCpf);
        Assert.NotNull(porCpf);
        Assert.Equal(ContaTitular, porCpf!.Conta);
    }

    // ---------------------------------------------------------------------------------------
    // Vinculo duplicado: o teste que impede o rebind silencioso
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_MesmaChaveDuasVezes_LancaChaveJaVinculadaComOTitularAtual()
    {
        DiretorioDeChavesEmMemoria diretorio = new();
        ResolucaoChave original = new(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular);
        diretorio.Vincular(original);

        ChaveJaVinculadaException excecao = Assert.Throws<ChaveJaVinculadaException>(
            () => diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular));

        // TitularAtual e quem JA detem a chave — o dado que o chamador precisa para decidir se e
        // reenvio dele mesmo ou disputa com outro participante.
        Assert.Equal(IspbTitular, excecao.TitularAtual);
        Assert.Equal(CpfCanonico, excecao.Chave.Texto);
        Assert.IsAssignableFrom<MotorPixException>(excecao);

        Assert.Equal(1, diretorio.Quantidade);
        Assert.Same(original, diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfCanonico)));
    }

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_MesmaChaveEmOutraGrafiaComOutroIspb_RecusaORebindEPreservaAEntradaOriginal()
    {
        // Este e o teste que impede o rebind silencioso, e ele so tem valor com a segunda tentativa
        // vindo em OUTRA GRAFIA: se o diretorio indexasse pelo texto cru, "529.982.247-25" seria uma
        // segunda linha, a chave passaria a resolver para dois titulares conforme a grafia da
        // consulta, e o pagamento cairia em quem chegou por ultimo.
        DiretorioDeChavesEmMemoria diretorio = new();
        ResolucaoChave original = new(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular);
        diretorio.Vincular(original);

        ChaveJaVinculadaException excecao = Assert.Throws<ChaveJaVinculadaException>(
            () => diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfPontuado), IspbOutro, ContaOutro));

        // O titular reportado e o incumbente, nao o invasor.
        Assert.Equal(IspbTitular, excecao.TitularAtual);
        Assert.NotEqual(IspbOutro, excecao.TitularAtual);

        // A chave da excecao ja vem canonica: quem le o log ve a chave do indice, nao a grafia
        // que a segunda tentativa usou.
        Assert.Equal(CpfCanonico, excecao.Chave.Texto);
        Assert.Contains(IspbTitularTexto, excecao.Message, StringComparison.Ordinal);

        // Entrada original intacta, e nao apenas "ainda existe": mesma instancia, mesmo ISPB,
        // mesma conta. Uma recusa que tivesse sobrescrito antes de lancar deixaria a excecao
        // certa e o estado errado.
        ResolucaoChave depois = diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfComEspacos));
        Assert.Same(original, depois);
        Assert.Equal(IspbTitular, depois.Ispb);
        Assert.Equal(ContaTitular, depois.Conta);
        Assert.Equal(LedgerTitularTexto, depois.Conta.Ledger.Texto);

        // Nenhuma linha nova: a recusa nao e "grava e desfaz", e "nao grava".
        Assert.Equal(1, diretorio.Quantidade);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_MesmaChaveComOMesmoTitularEOutraConta_TambemERecusado()
    {
        // Rebind dentro do mesmo participante tambem e rebind: a chave passaria a apontar para
        // outra conta do mesmo PSP, e o credito da seta 7 cairia no cliente errado — com partidas
        // dobradas batendo e nada quebrando na hora.
        DiretorioDeChavesEmMemoria diretorio = new();
        diretorio.Vincular(ChavePix.Criar(TipoChave.Email, EmailCanonico), IspbTitular, ContaTitular);

        ContaId outraContaDoMesmoPsp = ContaId.Cliente(LedgerTitular, "0009");

        ChaveJaVinculadaException excecao = Assert.Throws<ChaveJaVinculadaException>(
            () => diretorio.Vincular(ChavePix.Criar(TipoChave.Email, EmailCaixaAlta), IspbTitular, outraContaDoMesmoPsp));

        Assert.Equal(IspbTitular, excecao.TitularAtual);
        Assert.Equal(EmailCanonico, excecao.Chave.Texto);

        Assert.Equal(ContaTitular, diretorio.Resolver(ChavePix.Criar(TipoChave.Email, EmailCanonico)).Conta);
        Assert.Equal(1, diretorio.Quantidade);
    }

    // ---------------------------------------------------------------------------------------
    // Vinculo incoerente: conta fora do ledger do ISPB, ou conta que nao e de cliente
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_ContaDeUmLedgerQueNaoEODoIspb_LancaVinculoInconsistenteCitandoOsDoisLedgers()
    {
        // Por que isto importa: com o vinculo aceito, o diretorio devolveria o ISPB de um
        // participante junto com uma conta que vive no ledger de outro. O credito da seta 7 seria
        // lancado no ledger errado e NADA quebraria na hora — partidas dobradas continuam batendo,
        // a soma global continua constante, o pagamento "liquida com sucesso". O erro so apareceria
        // depois, na conciliacao, como divergencia orfa: um credito sem contraparte do lado certo e
        // um saldo a mais do lado errado, sem nenhum registro dizendo que os dois sao o mesmo
        // dinheiro. E o tipo de defeito que custa auditoria manual, nao stack trace.
        DiretorioDeChavesEmMemoria diretorio = new();

        VinculoInconsistenteException excecao = Assert.Throws<VinculoInconsistenteException>(
            () => diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaOutro));

        // O Motivo cita os DOIS ledgers: o que veio e o que era esperado. Citar so um deixaria
        // quem le sem saber qual dos dois lados esta errado.
        Assert.Equal($"a conta pertence ao ledger '{LedgerOutroTexto}', esperado '{LedgerTitularTexto}'", excecao.Motivo);
        Assert.Contains(LedgerOutroTexto, excecao.Motivo, StringComparison.Ordinal);
        Assert.Contains(LedgerTitularTexto, excecao.Motivo, StringComparison.Ordinal);

        Assert.Equal(CpfCanonico, excecao.Chave.Texto);
        Assert.Equal(IspbTitular, excecao.Ispb);
        Assert.Equal(ContaOutro, excecao.Conta);
        Assert.IsAssignableFrom<MotorPixException>(excecao);

        Assert.Equal(0, diretorio.Quantidade);
    }

    [Theory]
    [Trait("gate", "2")]
    [InlineData(ClasseConta.EspelhoPi, MotivoEspelhoPi)]
    [InlineData(ClasseConta.Abertura, MotivoAbertura)]
    // Pi merece atencao: ContaId.Pi() fixa LedgerId.Spi por construcao, entao uma conta PI tambem
    // falharia na guarda de ledger. A guarda de CLASSE roda primeiro justamente por isso — dizer
    // "isto nem e conta de cliente" e um diagnostico mais util do que "ledger errado", que mandaria
    // procurar o defeito no participante em vez de no vinculo.
    [InlineData(ClasseConta.Pi, MotivoPi)]
    public void Vincular_ContaQueNaoEDeCliente_LancaVinculoInconsistenteCitandoAClasse(
        ClasseConta classe,
        string motivoEsperado)
    {
        // Uma chave so pode enderecar conta de cliente. Apontar para ESPELHO_PI, PI ou ABERTURA
        // faria o credito da seta 7 cair numa conta estrutural do motor em vez de na conta de
        // alguem: o dinheiro sairia do pagador e ficaria parado numa conta que ninguem saca.
        DiretorioDeChavesEmMemoria diretorio = new();
        ContaId conta = ContaEstrutural(classe);
        Assert.Equal(classe, conta.Classe);

        VinculoInconsistenteException excecao = Assert.Throws<VinculoInconsistenteException>(
            () => diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, conta));

        Assert.Equal(motivoEsperado, excecao.Motivo);
        Assert.Equal(conta, excecao.Conta);
        Assert.Equal(IspbTitular, excecao.Ispb);
        Assert.Equal(CpfCanonico, excecao.Chave.Texto);

        Assert.Equal(0, diretorio.Quantidade);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_QualquerRecusa_NaoDeixaResiduoNoDiretorio()
    {
        // Todas as formas de recusa, uma atras da outra, no MESMO diretorio: a soma delas continua
        // sendo diretorio vazio. Uma guarda que validasse depois de inserir passaria nos testes
        // individuais acima (a excecao certa e lancada) e falharia aqui.
        DiretorioDeChavesEmMemoria diretorio = new();
        ChavePix chave = ChavePix.Criar(TipoChave.Cpf, CpfCanonico);

        Assert.Throws<VinculoInconsistenteException>(() => diretorio.Vincular(chave, IspbTitular, ContaOutro));
        Assert.Throws<VinculoInconsistenteException>(
            () => diretorio.Vincular(chave, IspbTitular, ContaId.EspelhoPi(LedgerTitular)));
        Assert.Throws<VinculoInconsistenteException>(
            () => diretorio.Vincular(chave, IspbTitular, ContaId.Abertura(LedgerTitular)));
        Assert.Throws<VinculoInconsistenteException>(
            () => diretorio.Vincular(chave, IspbTitular, ContaId.Pi(IspbTitular)));

        Assert.Equal(0, diretorio.Quantidade);

        // E a chave recusada continua ausente pelos dois caminhos de consulta.
        Assert.Throws<ChaveNaoEncontradaNoDictException>(() => diretorio.Resolver(chave));

        bool achou = diretorio.TryResolver(ChavePix.Criar(TipoChave.Cpf, CpfPontuado), out ResolucaoChave? resolucao);
        Assert.False(achou);
        Assert.Null(resolucao);

        // Depois das recusas, um vinculo coerente continua sendo aceito: o diretorio nao ficou
        // "envenenado" pelas tentativas invalidas.
        diretorio.Vincular(chave, IspbTitular, ContaTitular);
        Assert.Equal(1, diretorio.Quantidade);
        Assert.Equal(ContaTitular, diretorio.Resolver(chave).Conta);
    }

    // ---------------------------------------------------------------------------------------
    // Titular com varias chaves (o inverso — uma chave para dois titulares — e o rebind acima)
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_VariasChavesDeTiposDiferentesParaOMesmoTitular_TodasResolvemParaEle()
    {
        // A relacao e N chaves para 1 conta. O DICT nao impede que a mesma pessoa tenha CPF,
        // e-mail, telefone e EVP apontando para a mesma conta — impede o contrario.
        DiretorioDeChavesEmMemoria diretorio = new();

        ChavePix[] chaves =
        [
            ChavePix.Criar(TipoChave.Cpf, CpfPontuado),
            ChavePix.Criar(TipoChave.Cnpj, CnpjPontuado),
            ChavePix.Criar(TipoChave.Email, EmailCaixaAlta),
            ChavePix.Criar(TipoChave.Telefone, TelefonePontuado),
            ChavePix.Criar(TipoChave.Aleatoria, GuidMaiusculo),
        ];

        foreach (ChavePix chave in chaves)
        {
            diretorio.Vincular(chave, IspbTitular, ContaTitular);
        }

        Assert.Equal(5, diretorio.Quantidade);

        foreach (ChavePix chave in chaves)
        {
            ResolucaoChave resolucao = diretorio.Resolver(chave);

            Assert.Equal(IspbTitular, resolucao.Ispb);
            Assert.Equal(ContaTitular, resolucao.Conta);
            Assert.Equal(chave, resolucao.Chave);
        }

        // E a conta de outro participante continua alcancavel por chave propria: o diretorio nao
        // virou "tudo aponta para o primeiro titular".
        diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, OutroCpfCanonico), IspbOutro, ContaOutro);

        Assert.Equal(6, diretorio.Quantidade);
        Assert.Equal(ContaOutro, diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, OutroCpfCanonico)).Conta);
        Assert.Equal(ContaTitular, diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfCanonico)).Conta);
    }

    // ---------------------------------------------------------------------------------------
    // Construtor que recebe a colecao de vinculos
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Construtor_ComColecaoDeVinculos_VinculaTodosERespondeEmQualquerGrafia()
    {
        ResolucaoChave[] vinculos =
        [
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular),
            new ResolucaoChave(ChavePix.Criar(TipoChave.Email, EmailCanonico), IspbTitular, ContaTitular),
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cpf, OutroCpfCanonico), IspbOutro, ContaOutro),
        ];

        DiretorioDeChavesEmMemoria diretorio = new(vinculos);

        Assert.Equal(3, diretorio.Quantidade);

        // O construtor passa pelo mesmo Vincular: nada entra por uma porta lateral que pule as
        // guardas de coerencia ou o indice canonico.
        Assert.Same(vinculos[0], diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, CpfComEspacos)));
        Assert.Same(vinculos[1], diretorio.Resolver(ChavePix.Criar(TipoChave.Email, EmailCaixaAlta)));
        Assert.Same(vinculos[2], diretorio.Resolver(ChavePix.Criar(TipoChave.Cpf, OutroCpfCanonico)));
    }

    [Fact]
    [Trait("gate", "2")]
    public void Construtor_ComChaveRepetidaNaColecao_LancaEOVinculoIncoerenteNemChegaAExistir()
    {
        // Um vinculo incoerente nao pode mais chegar ao construtor do diretorio: a coerencia e
        // validada no construtor de ResolucaoChave, entao a instancia incoerente simplesmente nao
        // existe. Estado ilegal irrepresentavel — a guarda saiu de dentro de uma implementacao
        // especifica e passou a valer para qualquer IDiretorioChaves.
        VinculoInconsistenteException incoerente = Assert.Throws<VinculoInconsistenteException>(
            () => new ResolucaoChave(ChavePix.Criar(TipoChave.Email, EmailCanonico), IspbTitular, ContaOutro));

        Assert.Equal($"a conta pertence ao ledger '{LedgerOutroTexto}', esperado '{LedgerTitularTexto}'", incoerente.Motivo);

        // Sobra ao construtor do diretorio recusar o que so ele pode saber: a mesma chave duas
        // vezes na carga em lote. A excecao ESCAPA do construtor, e o objeto parcialmente
        // preenchido nao chega a ser publicado — `new` que lanca nao devolve referencia. Nao ha
        // estado observavel para assertar depois da falha, e assertar "quantos entraram antes de
        // quebrar" fixaria um detalhe de ordem de iteracao que nenhum contrato promete.
        ResolucaoChave[] comRepetida =
        [
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular),
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cnpj, CnpjCanonico), IspbTitular, ContaTitular),
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cpf, CpfComEspacos), IspbTitular, ContaTitular),
        ];

        ChaveJaVinculadaException duplicada =
            Assert.Throws<ChaveJaVinculadaException>(() => new DiretorioDeChavesEmMemoria(comRepetida));

        Assert.Equal(CpfCanonico, duplicada.Chave.Texto);
        Assert.Equal(IspbTitular, duplicada.TitularAtual);

        // Sem a repetida, a carga em lote funciona: o defeito era o item, nao a forma de carregar.
        DiretorioDeChavesEmMemoria valido = new(new[] { comRepetida[0], comRepetida[1] });
        Assert.Equal(2, valido.Quantidade);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Construtor_ComAMesmaChaveRepetidaNaColecao_Lanca()
    {
        // Carga em lote com a chave duplicada (em grafias diferentes, para valer a pena) e a mesma
        // recusa do rebind: um seed de teste com duas linhas para o mesmo CPF explodiria no
        // construtor em vez de produzir um diretorio cujo conteudo depende da ordem do array.
        ResolucaoChave[] vinculos =
        [
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), IspbTitular, ContaTitular),
            new ResolucaoChave(ChavePix.Criar(TipoChave.Cpf, CpfPontuado), IspbOutro, ContaOutro),
        ];

        ChaveJaVinculadaException excecao =
            Assert.Throws<ChaveJaVinculadaException>(() => new DiretorioDeChavesEmMemoria(vinculos));

        Assert.Equal(IspbTitular, excecao.TitularAtual);
        Assert.Equal(CpfCanonico, excecao.Chave.Texto);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Construtor_ComColecaoVazia_ProduzDiretorioVazioEquivalenteAoSemArgumento()
    {
        DiretorioDeChavesEmMemoria porColecao = new(Array.Empty<ResolucaoChave>());
        DiretorioDeChavesEmMemoria semArgumento = new();

        Assert.Equal(0, porColecao.Quantidade);
        Assert.Equal(0, semArgumento.Quantidade);

        ChavePix chave = ChavePix.Criar(TipoChave.Cpf, CpfCanonico);
        Assert.Throws<ChaveNaoEncontradaNoDictException>(() => porColecao.Resolver(chave));
        Assert.Throws<ChaveNaoEncontradaNoDictException>(() => semArgumento.Resolver(chave));
    }

    // ---------------------------------------------------------------------------------------
    // Chaves de tipos diferentes com os mesmos digitos nunca colidem no indice
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_CpfETelefoneComOsMesmosDigitos_SaoEntradasDistintas()
    {
        // As formas canonicas dos cinco tipos sao disjuntas por construcao — 11 digitos, 14
        // digitos, '+' na frente, '@' no meio, hifens do GUID —, entao nem por coincidencia de
        // texto duas chaves de tipos diferentes se encontram. Ainda assim a igualdade de ChavePix
        // nao depende disso: o EqualityContract do record ja separa ChaveCpf de ChaveTelefone.
        // Sao duas defesas independentes, e este teste exercita a mais fraca das duas condicoes:
        // os MESMOS digitos, um em cada tipo, em titulares diferentes.
        ChavePix cpf = ChavePix.Criar(TipoChave.Cpf, CpfCanonico);
        ChavePix telefone = ChavePix.Criar(TipoChave.Telefone, "+" + CpfCanonico);

        // Digito a digito identicos; canonicos diferentes por causa do '+' obrigatorio do E.164.
        Assert.Equal(cpf.Texto, telefone.Texto[1..]);
        Assert.NotEqual(cpf.Texto, telefone.Texto);
        Assert.NotEqual(cpf.Tipo, telefone.Tipo);
        Assert.NotEqual(cpf, telefone);

        DiretorioDeChavesEmMemoria diretorio = new();
        diretorio.Vincular(cpf, IspbTitular, ContaTitular);

        // Se colidissem no indice, esta segunda chamada lancaria ChaveJaVinculada.
        diretorio.Vincular(telefone, IspbOutro, ContaOutro);

        Assert.Equal(2, diretorio.Quantidade);
        Assert.Equal(ContaTitular, diretorio.Resolver(cpf).Conta);
        Assert.Equal(ContaOutro, diretorio.Resolver(telefone).Conta);
        Assert.Equal(IspbTitular, diretorio.Resolver(cpf).Ispb);
        Assert.Equal(IspbOutro, diretorio.Resolver(telefone).Ispb);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_CpfECnpjComPrefixoComum_SaoEntradasDistintas()
    {
        // Segunda variacao do mesmo risco, agora entre dois tipos que sao ambos "so digitos": o
        // CNPJ 11222333000181 e o CPF 11144477735 comecam igual e tem comprimentos diferentes. Um
        // indice por prefixo, ou por digitos truncados, encontraria um no lugar do outro.
        ChavePix cpf = ChavePix.Criar(TipoChave.Cpf, OutroCpfCanonico);
        ChavePix cnpj = ChavePix.Criar(TipoChave.Cnpj, CnpjCanonico);

        Assert.NotEqual(cpf, cnpj);

        DiretorioDeChavesEmMemoria diretorio = new();
        diretorio.Vincular(cpf, IspbTitular, ContaTitular);
        diretorio.Vincular(cnpj, IspbOutro, ContaOutro);

        Assert.Equal(2, diretorio.Quantidade);
        Assert.Equal(IspbTitular, diretorio.Resolver(cpf).Ispb);
        Assert.Equal(IspbOutro, diretorio.Resolver(cnpj).Ispb);
    }

    // ---------------------------------------------------------------------------------------
    // Argumentos nulos
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_VinculoNulo_LancaArgumentNullComParamName()
    {
        DiretorioDeChavesEmMemoria diretorio = new();

        ArgumentNullException excecao =
            Assert.Throws<ArgumentNullException>(() => diretorio.Vincular((ResolucaoChave)null!));

        Assert.Equal("vinculo", excecao.ParamName);
        Assert.Equal(0, diretorio.Quantidade);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Resolver_ChaveNula_LancaArgumentNullComParamName()
    {
        DiretorioDeChavesEmMemoria diretorio = new();

        ArgumentNullException excecao =
            Assert.Throws<ArgumentNullException>(() => diretorio.Resolver(null!));

        // ArgumentNullException, e nao ChaveNaoEncontradaNoDict: chave nula e defeito do chamador,
        // e nao "a chave nao esta cadastrada". Confundir os dois esconderia um bug de montagem do
        // payload atras de uma recusa de negocio perfeitamente normal.
        Assert.Equal("chave", excecao.ParamName);
    }

    [Fact]
    [Trait("gate", "2")]
    public void TryResolver_ChaveNula_LancaArgumentNullEmVezDeDevolverFalse()
    {
        DiretorioDeChavesEmMemoria diretorio = new();

        ArgumentNullException excecao = Assert.Throws<ArgumentNullException>(
            () => { diretorio.TryResolver(null!, out _); });

        // TryResolver so absorve o caso de negocio (chave ausente). Argumento nulo continua sendo
        // excecao: devolver false aqui converteria um defeito de programacao em "chave nao existe".
        Assert.Equal("chave", excecao.ParamName);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Vincular_IspbNulo_LancaArgumentNullComParamName()
    {
        DiretorioDeChavesEmMemoria diretorio = new();

        ArgumentNullException excecao = Assert.Throws<ArgumentNullException>(
            () => diretorio.Vincular(ChavePix.Criar(TipoChave.Cpf, CpfCanonico), null!, ContaTitular));

        // A guarda e transitiva: quem recusa e LedgerId.Psp, chamado pela verificacao de coerencia.
        Assert.Equal("ispb", excecao.ParamName);
        Assert.Equal(0, diretorio.Quantidade);
    }

    [Fact]
    [Trait("gate", "2")]
    public void Construtor_ComColecaoNula_LancaArgumentNullComParamName()
    {
        ArgumentNullException excecao =
            Assert.Throws<ArgumentNullException>(() => new DiretorioDeChavesEmMemoria(null!));

        Assert.Equal("vinculos", excecao.ParamName);
    }

    // ---------------------------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Contas estruturais do titular, uma por classe que NAO e cliente.
    /// <see cref="ContaId.Pi(Ispb)"/> nao aceita ledger: por construcao a conta PI vive no ledger
    /// do SPI, e e por isso que o caso Pi cai na guarda de ledger antes da guarda de classe.
    /// </summary>
    private static ContaId ContaEstrutural(ClasseConta classe) => classe switch
    {
        ClasseConta.EspelhoPi => ContaId.EspelhoPi(LedgerTitular),
        ClasseConta.Pi => ContaId.Pi(IspbTitular),
        ClasseConta.Abertura => ContaId.Abertura(LedgerTitular),
        _ => throw new ArgumentOutOfRangeException(nameof(classe), classe, "classe sem conta estrutural"),
    };
}
