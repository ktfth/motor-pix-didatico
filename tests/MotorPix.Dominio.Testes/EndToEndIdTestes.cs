using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// Forma do identificador fim-a-fim: 'E' + ISPB (8 digitos) + yyyyMMddHHmm (12) + 11 alfanumericos
/// ASCII, 32 caracteres ao todo.
/// <para>
/// Os identificadores validos sao montados por <see cref="EndToEndId.Compor"/> e as variacoes
/// invalidas derivam de um valido por substituicao de caractere - assim cada teste negativo isola
/// exatamente um defeito, e o comprimento continua 32 quando o defeito nao e de comprimento.
/// </para>
/// <para>
/// O oraculo do texto esperado e escrito a mao (constante <c>E2eDaEpoca</c>), nunca derivado da
/// propria fabrica: comparar <c>Compor</c> com <c>Compor</c> nao provaria nada sobre o layout.
/// </para>
/// </summary>
public sealed class EndToEndIdTestes
{
    private const string TextoIspb = "11111111";
    private const string SufixoPadrao = "ABCDEFGHIJK";

    /// <summary>
    /// Conferido posicao a posicao: 'E' (1) + "11111111" (8) + "202601011200" (12) +
    /// "ABCDEFGHIJK" (11) = 32. O instante e o de <see cref="RelogioFake.Epoca"/>.
    /// </summary>
    private const string E2eDaEpoca = "E11111111202601011200ABCDEFGHIJK";

    private const int ComprimentoInstante = 12;
    private const int InicioInstante = 1 + Ispb.Comprimento;
    private const int InicioSufixo = InicioInstante + ComprimentoInstante;

    private static readonly Ispb IspbPadrao = Ispb.Criar(TextoIspb);

    [Fact]
    [Trait("gate", "1")]
    public void Compor_ComIspbInstanteESufixo_ProduzOsTrintaEDoisCaracteresNaOrdemDoLayout()
    {
        EndToEndId id = EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, SufixoPadrao);

        Assert.Equal(E2eDaEpoca, id.Texto);
        Assert.Equal(32, id.Texto.Length);
        Assert.Equal(32, EndToEndId.Comprimento);
        Assert.Equal(E2eDaEpoca, id.ToString());
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComE2eBemFormado_ExpoeIspbInstanteDeclaradoESufixo()
    {
        EndToEndId id = EndToEndId.Criar(E2eDaEpoca);

        Assert.Equal(E2eDaEpoca, id.Texto);
        Assert.Equal(TextoIspb, id.Ispb.Texto);
        Assert.Equal(SufixoPadrao, id.Sufixo);
        Assert.Equal(RelogioFake.Epoca, id.InstanteDeclarado);
    }

    [Fact]
    [Trait("gate", "1")]
    public void InstanteDeclarado_DeE2eValido_EhUtcECorrespondeAoInstanteEmbutido()
    {
        // "202603091245" escrito a mao no meio do identificador; nenhum campo vem de fabrica.
        EndToEndId id = EndToEndId.Criar("E11111111202603091245ABCDEFGHIJK");

        Assert.Equal(TimeSpan.Zero, id.InstanteDeclarado.Offset);
        Assert.Equal(2026, id.InstanteDeclarado.Year);
        Assert.Equal(3, id.InstanteDeclarado.Month);
        Assert.Equal(9, id.InstanteDeclarado.Day);
        Assert.Equal(12, id.InstanteDeclarado.Hour);
        Assert.Equal(45, id.InstanteDeclarado.Minute);
        Assert.Equal(0, id.InstanteDeclarado.Second);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Compor_ComRelogioAvancado_GravaOInstanteDoRelogioNoIdentificador()
    {
        RelogioFake relogio = new();

        // 2026-01-01 + 67 dias = 2026-03-09 (2026 nao e bissexto: 31 de janeiro + 28 de fevereiro).
        relogio.Avancar(TimeSpan.FromDays(67));
        relogio.Avancar(TimeSpan.FromMinutes(45));

        EndToEndId id = EndToEndId.Compor(IspbPadrao, relogio.Agora, SufixoPadrao);

        Assert.Equal("E11111111202603091245ABCDEFGHIJK", id.Texto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Compor_ComInstanteEmOutroFuso_ConverteParaUtcAntesDeConcatenar()
    {
        RelogioFake relogio = new();
        DateTimeOffset mesmoInstanteEmBrasilia = relogio.Agora.ToOffset(TimeSpan.FromHours(-3));

        EndToEndId id = EndToEndId.Compor(IspbPadrao, mesmoInstanteEmBrasilia, SufixoPadrao);

        // 09:00-03:00 e o mesmo instante que 12:00Z: o texto embutido nao pode mudar com o fuso.
        Assert.Equal(9, mesmoInstanteEmBrasilia.Hour);
        Assert.Equal(E2eDaEpoca, id.Texto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Compor_ComInstanteContendoSegundos_TruncaOInstanteDeclaradoParaOMinuto()
    {
        RelogioFake relogio = new();
        relogio.AvancarSegundos(59);

        EndToEndId id = EndToEndId.Compor(IspbPadrao, relogio.Agora, SufixoPadrao);

        Assert.Equal(59, relogio.Agora.Second);
        Assert.Equal(E2eDaEpoca, id.Texto);
        Assert.Equal(0, id.InstanteDeclarado.Second);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    [Trait("gate", "1")]
    public void Criar_ComComprimentoDiferenteDeTrintaEDois_Lanca(int comprimento)
    {
        string valido = ValidoDaEpoca();
        string bruto = comprimento < valido.Length
            ? valido[..comprimento]
            : valido + new string('Z', comprimento - valido.Length);

        Assert.Equal(comprimento, bruto.Length);

        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(bruto));

        Assert.Equal("EndToEndId", erro.Tipo);
        Assert.Equal(bruto, erro.Bruto);
        Assert.Contains("comprimento", erro.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComTextoNulo_Lanca()
    {
        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(null));

        Assert.Equal("nulo", erro.Motivo);
        Assert.Null(erro.Bruto);
        Assert.Contains("<nulo>", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComPrimeiroCaractereMinusculo_Lanca()
    {
        string bruto = SubstituirCaractere(ValidoDaEpoca(), 0, 'e');

        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(bruto));

        // Comprimento intacto: o unico defeito e a caixa do prefixo.
        Assert.Equal(32, bruto.Length);
        Assert.Contains("maiusculo", erro.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComLetraNoIspbEmbutido_Lanca()
    {
        string bruto = SubstituirCaractere(ValidoDaEpoca(), 4, 'A');

        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(bruto));

        Assert.Equal(32, bruto.Length);
        Assert.Equal("ISPB embutido invalido", erro.Motivo);
        Assert.Equal(bruto, erro.Bruto);
    }

    [Theory]
    [InlineData("202613011200")] // mes 13
    [InlineData("202602301200")] // 30 de fevereiro
    [InlineData("202601012500")] // hora 25
    [InlineData("202601011260")] // minuto 60
    [Trait("gate", "1")]
    public void Criar_ComInstanteEmbutidoImpossivel_Lanca(string instante)
    {
        string bruto = ComInstante(ValidoDaEpoca(), instante);

        Assert.Equal(32, bruto.Length);

        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(bruto));

        Assert.Equal("instante embutido nao e uma data valida", erro.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComInstanteEmbutidoNaoNumerico_Lanca()
    {
        string bruto = ComInstante(ValidoDaEpoca(), "2026010112OO");

        Assert.Equal(32, bruto.Length);

        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(bruto));

        Assert.Equal("instante embutido nao numerico", erro.Motivo);
    }

    // Codigos em vez de literais para manter o fonte em ASCII puro.
    [Theory]
    [InlineData(0x2D)] // '-'
    [InlineData(0x20)] // ' '
    [InlineData(0xC1)] // 'A' com acento agudo: alfanumerico em Unicode, fora do ASCII
    [Trait("gate", "1")]
    public void Criar_ComSufixoNaoAlfanumericoAscii_Lanca(int codigoDoIntruso)
    {
        char intruso = (char)codigoDoIntruso;
        string bruto = SubstituirCaractere(ValidoDaEpoca(), EndToEndId.Comprimento - 1, intruso);

        Assert.Equal(32, bruto.Length);

        EndToEndIdInvalidoException erro =
            Assert.Throws<EndToEndIdInvalidoException>(() => EndToEndId.Criar(bruto));

        Assert.Equal("sufixo com caractere nao alfanumerico ASCII", erro.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComSufixosQueDiferemSoNaCaixa_ProduzTransacoesDistintas()
    {
        EndToEndId maiusculo = EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, "ABCDEFGHIJK");
        EndToEndId minusculo = EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, "abcdefghijk");

        Assert.Equal("ABCDEFGHIJK", maiusculo.Sufixo);
        Assert.Equal("abcdefghijk", minusculo.Sufixo);
        Assert.NotEqual(maiusculo, minusculo);
        Assert.NotEqual(maiusculo.Texto, minusculo.Texto);

        HashSet<EndToEndId> vistos = new();

        Assert.True(vistos.Add(maiusculo));

        // Se a comparacao fosse case-insensitive, este Add devolveria false e o motor trataria
        // uma transacao nova como reenvio: falso replay, pagamento engolido.
        Assert.True(vistos.Add(minusculo));

        // Reenvio de verdade - mesmo texto - continua colapsando.
        Assert.False(vistos.Add(EndToEndId.Criar(maiusculo.Texto)));
        Assert.Equal(2, vistos.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("E11111111202601011200ABCDEFGHIJ")] // 31 caracteres
    [InlineData("E11111111202601011200ABCDEFGHIJKL")] // 33 caracteres
    [InlineData("e11111111202601011200ABCDEFGHIJK")] // 'e' minusculo
    [InlineData("E1111111A202601011200ABCDEFGHIJK")] // letra no ISPB
    [InlineData("E11111111202613011200ABCDEFGHIJK")] // mes 13
    [InlineData("E11111111202601011200ABCDEFGHI-K")] // hifen no sufixo
    [Trait("gate", "1")]
    public void TryCriar_ComTextoInvalido_DevolveFalsoENuloSemLancar(string? bruto)
    {
        bool aceito = EndToEndId.TryCriar(bruto, out EndToEndId? id);

        Assert.False(aceito);
        Assert.Null(id);
    }

    [Fact]
    [Trait("gate", "1")]
    public void TryCriar_ComTextoValido_DevolveVerdadeiroEOIdentificador()
    {
        bool aceito = EndToEndId.TryCriar(E2eDaEpoca, out EndToEndId? id);

        Assert.True(aceito);
        Assert.NotNull(id);
        Assert.Equal(E2eDaEpoca, id?.Texto);
        Assert.Equal(SufixoPadrao, id?.Sufixo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJ")] // 10
    [InlineData("ABCDEFGHIJKL")] // 12
    [Trait("gate", "1")]
    public void Compor_ComSufixoDeTamanhoDiferenteDeOnze_Lanca(string sufixo)
    {
        EndToEndIdInvalidoException erro = Assert.Throws<EndToEndIdInvalidoException>(
            () => EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, sufixo));

        Assert.Equal(sufixo, erro.Bruto);
        Assert.Contains("11 caracteres", erro.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Compor_ComSufixoDeOnzeCaracteresNaoAlfanumericos_Lanca()
    {
        EndToEndIdInvalidoException erro = Assert.Throws<EndToEndIdInvalidoException>(
            () => EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, "ABCDEFGHI K"));

        // Compor nao tem validacao propria de conteudo: delega a Criar, e o bruto reportado ja e o
        // identificador inteiro.
        Assert.Equal("sufixo com caractere nao alfanumerico ASCII", erro.Motivo);
        Assert.Equal("E11111111202601011200ABCDEFGHI K", erro.Bruto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Igualdade_EntreInstanciasDistintasComOMesmoTexto_SaoIguaisEColidemNoDicionario()
    {
        EndToEndId porFabrica = EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, SufixoPadrao);
        EndToEndId porTexto = EndToEndId.Criar(E2eDaEpoca);

        Assert.NotSame(porFabrica, porTexto);
        Assert.Equal(porFabrica, porTexto);
        Assert.Equal(porFabrica.GetHashCode(), porTexto.GetHashCode());

        Dictionary<EndToEndId, string> respostas = new() { [porFabrica] = "primeira" };
        respostas[porTexto] = "segunda";

        Assert.Single(respostas);
        Assert.Equal("segunda", respostas[porFabrica]);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Igualdade_ComIspbOuInstanteDiferente_ProduzIdentificadoresDistintos()
    {
        EndToEndId original = EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, SufixoPadrao);
        EndToEndId outroIspb = EndToEndId.Compor(Ispb.Criar("22222222"), RelogioFake.Epoca, SufixoPadrao);

        RelogioFake relogio = new();
        relogio.Avancar(TimeSpan.FromMinutes(1));
        EndToEndId outroInstante = EndToEndId.Compor(IspbPadrao, relogio.Agora, SufixoPadrao);

        Assert.NotEqual(original, outroIspb);
        Assert.NotEqual(original, outroInstante);

        HashSet<EndToEndId> vistos = new() { original, outroIspb, outroInstante };
        Assert.Equal(3, vistos.Count);
    }

    private static string ValidoDaEpoca() =>
        EndToEndId.Compor(IspbPadrao, RelogioFake.Epoca, SufixoPadrao).Texto;

    /// <summary>Troca um caractere preservando o comprimento - o defeito injetado e sempre um so.</summary>
    private static string SubstituirCaractere(string bruto, int indice, char novo)
    {
        char[] caracteres = bruto.ToCharArray();
        caracteres[indice] = novo;
        return new string(caracteres);
    }

    /// <summary>Troca os 12 digitos de instante, mantendo prefixo, ISPB e sufixo intactos.</summary>
    private static string ComInstante(string bruto, string instante) =>
        string.Concat(bruto[..InicioInstante], instante, bruto[InicioSufixo..]);
}
