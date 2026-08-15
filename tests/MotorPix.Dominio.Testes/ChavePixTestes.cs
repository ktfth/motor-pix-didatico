using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// Normalizacao dentro do value object.
/// <para>
/// O teste central desta suite e o primeiro: se <c>"529.982.247-25"</c> e <c>"52998224725"</c> nao
/// colapsarem na mesma chave, a resolucao no DICT fica intermitente e o sintoma aparece longe da
/// causa ("as vezes o DICT nao acha"). Todo o resto - idempotencia da normalizacao, digito
/// verificador, faixas - existe para sustentar essa igualdade.
/// </para>
/// <para>
/// Os CPFs e CNPJs usados aqui foram conferidos a mao pelo modulo 11 (as contas estao nos
/// comentarios de cada bloco), e os casos invalidos mutam <em>o digito verificador</em> de um valido,
/// mutacao que e recusada por construcao: o DV e funcao dos digitos que ficaram intactos.
/// </para>
/// <para>
/// A normalizacao remove <em>apenas</em> pontuacao conhecida (<c>.</c>, <c>-</c>, <c>/</c>,
/// parenteses e espaco) e exige digito no resto. E a diferenca entre colapsar grafias do mesmo
/// numero — o que se quer — e colapsar textos diferentes na mesma chave — o que manda dinheiro
/// para a conta errada.
/// </para>
/// </summary>
public sealed class ChavePixTestes
{
    // CPF 529.982.247-25 - DV1: 5*10+2*9+9*8+9*7+8*6+2*5+2*4+4*3+7*2 = 295; 295 % 11 = 9; 11-9 = 2.
    //                      DV2: 5*11+2*10+9*9+9*8+8*7+2*6+2*5+4*4+7*3+2*2 = 347; 347 % 11 = 6; 11-6 = 5.
    private const string CpfValido = "52998224725";
    private const string CpfValidoComPontuacao = "529.982.247-25";

    // CPF 111.444.777-35 - DV1: soma 162; 162 % 11 = 8; 11-8 = 3.  DV2: soma 204; 204 % 11 = 6; 11-6 = 5.
    private const string OutroCpfValido = "11144477735";

    // CNPJ 11.222.333/0001-81 - DV1: soma 102; 102 % 11 = 3; 11-3 = 8.  DV2: soma 120; 120 % 11 = 10; 11-10 = 1.
    private const string CnpjValido = "11222333000181";
    private const string CnpjValidoComPontuacao = "11.222.333/0001-81";

    // CNPJ 34.238.864/0001-68 - DV1: soma 247; 247 % 11 = 5; 11-5 = 6.  DV2: soma 234; 234 % 11 = 3; 11-3 = 8.
    private const string OutroCnpjValido = "34238864000168";

    private const string EmailNormalizado = "fulano@exemplo.com";
    private const string TelefoneNormalizado = "+5511999999999";

    // UUID de namespace do RFC 4122 (DNS): literal fixo, para nao depender de Guid.NewGuid.
    private const string GuidMaiusculo = "6BA7B810-9DAD-11D1-80B4-00C04FD430C8";
    private const string GuidMinusculo = "6ba7b810-9dad-11d1-80b4-00c04fd430c8";

    private const string MotivoDvCpf = "digito verificador de CPF invalido";
    private const string MotivoDvCnpj = "digito verificador de CNPJ invalido";

    // ---------------------------------------------------------------------------------------
    // O teste que impede o DICT intermitente
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Criar_CpfComEsemPontuacao_ProduzChavesIguais()
    {
        ChavePix comPontuacao = ChavePix.Criar(TipoChave.Cpf, CpfValidoComPontuacao);
        ChavePix semPontuacao = ChavePix.Criar(TipoChave.Cpf, CpfValido);

        // Instancias distintas: a igualdade abaixo e estrutural, nao um cache devolvendo o mesmo objeto.
        Assert.NotSame(comPontuacao, semPontuacao);

        Assert.Equal(CpfValido, comPontuacao.Texto);
        Assert.Equal(CpfValido, semPontuacao.Texto);
        Assert.Equal(comPontuacao, semPontuacao);

        bool iguaisPorEquals = comPontuacao.Equals(semPontuacao);
        Assert.True(iguaisPorEquals);

        // Hash igual e o que faz a chave funcionar como indice do DICT, nao so como comparacao.
        Assert.Equal(comPontuacao.GetHashCode(), semPontuacao.GetHashCode());
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(CpfValidoComPontuacao)]
    [InlineData(CpfValido)]
    [InlineData(" 52998224725 ")]
    [InlineData("529 982 247 25")]
    [InlineData("529-982-247.25")]
    public void Criar_CpfEmGrafiasDiferentes_ColapsaNaMesmaFormaCanonica(string bruto)
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Cpf, bruto);

        Assert.Equal(CpfValido, chave.Texto);
        Assert.Equal(ChavePix.Criar(TipoChave.Cpf, CpfValido), chave);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_CpfEnvolvidoPorLetras_ERecusadoPorCaractereInvalido()
    {
        // A normalizacao remove apenas pontuacao conhecida (ponto, hifen, barra, parenteses e
        // espaco) e exige digito no resto. Descartar *todo* nao-digito era a leniencia perigosa:
        // "cpf 529.982.247-25 ok" e "529.982.247-25" sao dois textos distintos que colapsavam na
        // mesma chave de enderecamento do DICT. Quem grava a chave suja e quem consulta a limpa
        // acham a mesma entrada, entao um payload corrompido resolve para o titular certo — ate o
        // dia em que a sujeira contem digitos e resolve para o titular errado, e nada no caminho
        // registrou que os dois textos nunca foram o mesmo.
        ChavePixInvalidaException excecao = Assert.Throws<ChavePixInvalidaException>(
            () => ChavePix.Criar(TipoChave.Cpf, "cpf 529.982.247-25 ok"));

        Assert.Contains("CPF com caractere invalido", excecao.Motivo, StringComparison.Ordinal);
        Assert.Equal("CPF com caractere invalido 'c'", excecao.Motivo);
        Assert.Equal("cpf 529.982.247-25 ok", excecao.Bruto);

        // O mesmo CPF, sem a sujeira, continua sendo aceito: o que a guarda recusa e o texto que
        // sobra depois da pontuacao conhecida, e nao a grafia pontuada.
        Assert.Equal(CpfValido, ChavePix.Criar(TipoChave.Cpf, CpfValidoComPontuacao).Texto);
    }

    // ---------------------------------------------------------------------------------------
    // Idempotencia da normalizacao: Criar(Criar(x).Valor) == Criar(x)
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(TipoChave.Cpf, CpfValidoComPontuacao, CpfValido)]
    [InlineData(TipoChave.Cnpj, CnpjValidoComPontuacao, CnpjValido)]
    [InlineData(TipoChave.Email, "  Fulano@Exemplo.COM ", EmailNormalizado)]
    [InlineData(TipoChave.Telefone, "+55 (11) 99999-9999", TelefoneNormalizado)]
    [InlineData(TipoChave.Aleatoria, GuidMaiusculo, GuidMinusculo)]
    public void Criar_ReaplicadoSobreOProprioValor_EhIdempotente(TipoChave tipo, string bruto, string canonico)
    {
        ChavePix primeira = ChavePix.Criar(tipo, bruto);
        Assert.Equal(canonico, primeira.Texto);

        ChavePix segunda = ChavePix.Criar(tipo, primeira.Texto);
        ChavePix terceira = ChavePix.Criar(tipo, segunda.Texto);

        Assert.Equal(canonico, segunda.Texto);
        Assert.Equal(canonico, terceira.Texto);
        Assert.Equal(primeira, segunda);
        Assert.Equal(primeira, terceira);
        Assert.Equal(primeira.GetHashCode(), segunda.GetHashCode());
    }

    // ---------------------------------------------------------------------------------------
    // CPF: digito verificador, sequencias repetidas e comprimento
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    // Mutacoes no proprio DV: o DV e funcao dos digitos anteriores, que ficaram intactos.
    [InlineData("52998224726")]
    [InlineData("52998224735")]
    [InlineData("11144477736")]
    [InlineData("11144477725")]
    // Mutacao no corpo: 5 -> 6 na posicao 0 leva a soma de 295 para 305; 305 % 11 = 8; 11-8 = 3 != 2.
    [InlineData("62998224725")]
    // Mesma mutacao, apresentada com pontuacao: a recusa nao depende da grafia.
    [InlineData("629.982.247-25")]
    public void Criar_CpfComDigitoVerificadorErrado_Lanca(string bruto)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Cpf, bruto));

        Assert.Equal("ChavePix", excecao.Tipo);
        Assert.Equal(bruto, excecao.Bruto);
        Assert.Equal(MotivoDvCpf, excecao.Motivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("00000000000")]
    [InlineData("11111111111")]
    [InlineData("22222222222")]
    [InlineData("33333333333")]
    [InlineData("44444444444")]
    [InlineData("55555555555")]
    [InlineData("66666666666")]
    [InlineData("77777777777")]
    [InlineData("88888888888")]
    [InlineData("99999999999")]
    public void Criar_CpfComTodosOsDigitosIguais_LancaAindaQueAAritmeticaAceite(string repetido)
    {
        // Oraculo independente: o modulo 11 puro, sem a convencao das sequencias repetidas.
        // Ele aceita os dez repdigits (para o digito d, DV1 = 54d mod 11 e DV2 = 65d mod 11 caem
        // exatamente em d), entao a recusa abaixo e convencao do cadastro, nao aritmetica.
        bool aritmeticaAceita = AritmeticaModulo11AceitaCpf(repetido);
        Assert.True(aritmeticaAceita);

        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Cpf, repetido));

        Assert.Equal(MotivoDvCpf, excecao.Motivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("5299822472", 10)]
    [InlineData("529.982.247-2", 10)]
    [InlineData("529982247250", 12)]
    [InlineData("529.982.247-250", 12)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Criar_CpfComQuantidadeDeDigitosForaDeOnze_Lanca(string bruto, int digitosEncontrados)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Cpf, bruto));

        Assert.Equal($"CPF com {digitosEncontrados} digitos, esperado 11", excecao.Motivo);
        Assert.Equal(bruto, excecao.Bruto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_CpfComUmDigitoTrocado_NemSempreEhRecusado_LimiteDoModulo11()
    {
        // Contra-exemplo para a propriedade "mutar um digito invalida" (PLANO.md, gate 2).
        // "12345678909" e valido: DV1 = soma 210, 210 % 11 = 1 -> resto < 2 -> DV 0.
        // "22345678909" tambem e: a troca no digito 0 leva a soma a 220, 220 % 11 = 0 -> DV 0 de novo
        // (restos 0 e 1 colapsam no mesmo DV), e no DV2 o primeiro digito pesa 11, que e 0 mod 11.
        // Sao duas chaves diferentes, ambas aceitas - o DV protege contra digitacao, nao contra fraude.
        ChavePix original = ChavePix.Criar(TipoChave.Cpf, "12345678909");
        ChavePix vizinho = ChavePix.Criar(TipoChave.Cpf, "22345678909");

        Assert.Equal("12345678909", original.Texto);
        Assert.Equal("22345678909", vizinho.Texto);
        Assert.NotEqual(original, vizinho);
    }

    // ---------------------------------------------------------------------------------------
    // CNPJ
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(CnpjValidoComPontuacao, CnpjValido)]
    [InlineData(CnpjValido, CnpjValido)]
    [InlineData(" 11.222.333/0001-81 ", CnpjValido)]
    [InlineData("34.238.864/0001-68", OutroCnpjValido)]
    [InlineData(OutroCnpjValido, OutroCnpjValido)]
    public void Criar_CnpjComPontuacao_NormalizaParaSoDigitos(string bruto, string canonico)
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Cnpj, bruto);

        Assert.Equal(canonico, chave.Texto);
        Assert.Equal(TipoChave.Cnpj, chave.Tipo);
        Assert.Equal(ChavePix.Criar(TipoChave.Cnpj, canonico), chave);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("11222333000182")]
    [InlineData("11222333000171")]
    [InlineData("34238864000169")]
    [InlineData("34238864000158")]
    [InlineData("11.222.333/0001-82")]
    public void Criar_CnpjComDigitoVerificadorErrado_Lanca(string bruto)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Cnpj, bruto));

        Assert.Equal(MotivoDvCnpj, excecao.Motivo);
        Assert.Equal(bruto, excecao.Bruto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_CnpjTodoZerado_LancaAindaQueAAritmeticaAceite()
    {
        // Somas zeradas dao resto 0 nos dois DVs, e resto < 2 vira DV 0 - que e o que esta escrito.
        // Dos catorze repdigits so este passa na aritmetica; os outros ja caem no proprio DV.
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Cnpj, "00000000000000"));

        Assert.Equal(MotivoDvCnpj, excecao.Motivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("1122233300018", 13)]
    [InlineData("112223330001811", 15)]
    [InlineData(CpfValido, 11)]
    [InlineData("", 0)]
    public void Criar_CnpjComQuantidadeDeDigitosForaDeQuatorze_Lanca(string bruto, int digitosEncontrados)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Cnpj, bruto));

        Assert.Equal($"CNPJ com {digitosEncontrados} digitos, esperado 14", excecao.Motivo);
    }

    // ---------------------------------------------------------------------------------------
    // E-mail
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Criar_EmailComEspacosAoRedorECaixaMista_NormalizaParaMinusculas()
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Email, "  Fulano@Exemplo.COM ");

        Assert.Equal(EmailNormalizado, chave.Texto);
        Assert.Equal(TipoChave.Email, chave.Tipo);
        Assert.Equal(ChavePix.Criar(TipoChave.Email, "FULANO@EXEMPLO.COM"), chave);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("fulano.exemplo.com", "forma de e-mail invalida")]
    [InlineData("fulano@@exemplo.com", "forma de e-mail invalida")]
    [InlineData("fulano@exemplo@com", "forma de e-mail invalida")]
    [InlineData("fulano@", "forma de e-mail invalida")]
    [InlineData("@exemplo.com", "forma de e-mail invalida")]
    [InlineData("ful ano@exemplo.com", "forma de e-mail invalida")]
    [InlineData("fulano@exe mplo.com", "forma de e-mail invalida")]
    [InlineData("fulano@exemplo", "dominio de e-mail invalido")]
    [InlineData("fulano@localhost", "dominio de e-mail invalido")]
    [InlineData("fulano@.com", "dominio de e-mail invalido")]
    [InlineData("fulano@exemplo.", "dominio de e-mail invalido")]
    public void Criar_EmailMalFormado_Lanca(string bruto, string motivoEsperado)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Email, bruto));

        Assert.Equal(motivoEsperado, excecao.Motivo);
        Assert.Equal(bruto, excecao.Bruto);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_EmailVazio_Lanca(string bruto)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Email, bruto));

        Assert.Equal("e-mail vazio", excecao.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_EmailComExatamente77Caracteres_EhAceito()
    {
        string email = new string('a', 65) + "@exemplo.com";
        Assert.Equal(77, email.Length);

        ChavePix chave = ChavePix.Criar(TipoChave.Email, email);

        Assert.Equal(email, chave.Texto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_EmailComMaisDe77Caracteres_Lanca()
    {
        string email = new string('a', 66) + "@exemplo.com";
        Assert.Equal(78, email.Length);

        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Email, email));

        Assert.Equal("e-mail acima de 77 caracteres", excecao.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_EmailQueSoCabeDepoisDoTrim_EhAceito()
    {
        // O limite de 77 e medido depois do Trim: espaco de borda nao consome cota.
        string email = new string('a', 65) + "@exemplo.com";
        string comEspacos = "  " + email + "  ";
        Assert.Equal(81, comEspacos.Length);

        ChavePix chave = ChavePix.Criar(TipoChave.Email, comEspacos);

        Assert.Equal(email, chave.Texto);
        Assert.Equal(77, chave.Texto.Length);
    }

    // ---------------------------------------------------------------------------------------
    // Telefone
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData("+55 (11) 99999-9999")]
    [InlineData("+5511999999999")]
    [InlineData("  +55 11 99999 9999  ")]
    [InlineData("+55-11-99999-9999")]
    public void Criar_TelefoneComMascara_NormalizaParaE164(string bruto)
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Telefone, bruto);

        Assert.Equal(TelefoneNormalizado, chave.Texto);
        Assert.Equal(TipoChave.Telefone, chave.Tipo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("5511999999999")]
    [InlineData("55 (11) 99999-9999")]
    [InlineData("(11) 99999-9999")]
    [InlineData("0055 11 99999 9999")]
    public void Criar_TelefoneSemMaisInicial_LancaPorqueODdiNaoEInferido(string bruto)
    {
        // Assumir +55 para um numero sem DDI seria inventar regra de negocio: o E.164 vem completo
        // do payload ou nao vem.
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Telefone, bruto));

        Assert.Equal("telefone deve estar em E.164 e comecar com '+'", excecao.Motivo);
        Assert.Equal(bruto, excecao.Bruto);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("+1234567", 7)]
    [InlineData("+1234567890123456", 16)]
    [InlineData("+", 0)]
    public void Criar_TelefoneComQuantidadeDeDigitosForaDaFaixa_Lanca(string bruto, int digitosEncontrados)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Telefone, bruto));

        Assert.Equal(
            $"telefone com {digitosEncontrados} digitos, esperado entre 8 e 15",
            excecao.Motivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("+12345678", 8)]
    [InlineData("+123456789012345", 15)]
    public void Criar_TelefoneNosExtremosDaFaixa_EhAceito(string bruto, int digitosEncontrados)
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Telefone, bruto);

        Assert.Equal(bruto, chave.Texto);

        // O '+' nao e digito: a faixa 8..15 conta so os digitos, e o canonico tem um caractere a mais.
        Assert.Equal(digitosEncontrados + 1, chave.Texto.Length);
    }

    // ---------------------------------------------------------------------------------------
    // Aleatoria (EVP)
    // ---------------------------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(GuidMaiusculo)]
    [InlineData(GuidMinusculo)]
    [InlineData("6Ba7B810-9dAd-11d1-80B4-00c04Fd430c8")]
    [InlineData("  6BA7B810-9DAD-11D1-80B4-00C04FD430C8  ")]
    public void Criar_AleatoriaEmQualquerCaixa_NormalizaParaGuidMinusculoFormatoD(string bruto)
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Aleatoria, bruto);

        Assert.Equal(GuidMinusculo, chave.Texto);
        Assert.Equal(TipoChave.Aleatoria, chave.Tipo);
        Assert.Equal(ChavePix.Criar(TipoChave.Aleatoria, GuidMaiusculo), chave);
    }

    [Theory]
    [Trait("gate", "1")]
    // Formato "N" (sem hifens): e o mesmo UUID, mas nao e a forma canonica exigida.
    [InlineData("6ba7b8109dad11d180b400c04fd430c8")]
    // Formato "B" e "P": delimitadores nao aceitos pelo TryParseExact com "D".
    [InlineData("{6ba7b810-9dad-11d1-80b4-00c04fd430c8}")]
    [InlineData("(6ba7b810-9dad-11d1-80b4-00c04fd430c8)")]
    [InlineData("nao-e-um-guid")]
    [InlineData("6ba7b810-9dad-11d1-80b4-00c04fd430c")]
    [InlineData("6ba7b810-9dad-11d1-80b4-00c04fd430c8x")]
    public void Criar_AleatoriaForaDoFormatoD_Lanca(string bruto)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Aleatoria, bruto));

        Assert.Equal("chave aleatoria nao e um GUID no formato 'D'", excecao.Motivo);
        Assert.Equal(bruto, excecao.Bruto);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_AleatoriaVazia_Lanca(string bruto)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Aleatoria, bruto));

        Assert.Equal("chave aleatoria vazia", excecao.Motivo);
    }

    // ---------------------------------------------------------------------------------------
    // O tipo vem de fora: nunca e inferido do texto
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Criar_MesmoTextoComoCpfEComoTelefone_TemResultadosDiferentes()
    {
        // "12345678909" e CPF valido (DV1: soma 210, resto 1 -> 0; DV2: soma 255, resto 2 -> 9)
        // e nao e telefone nenhum, por falta do '+'. Se o tipo fosse inferido do texto, seria preciso
        // escolher um dos dois - escolha que nenhum documento autoriza.
        const string ambiguo = "12345678909";

        ChavePix comoCpf = ChavePix.Criar(TipoChave.Cpf, ambiguo);
        Assert.Equal(ambiguo, comoCpf.Texto);
        Assert.Equal(TipoChave.Cpf, comoCpf.Tipo);

        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(TipoChave.Telefone, ambiguo));
        Assert.Equal("telefone deve estar em E.164 e comecar com '+'", excecao.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_TextoValidoEmUmTipo_ERecusadoNoOutro_PorqueOTipoNuncaEInferido()
    {
        // Mesma intencao do teste anterior, mais dura: "+52998224725" ja foi aceito pelos dois
        // tipos, normalizando diferente em cada um. Com a pontuacao restrita, as duas linguagens
        // ficaram disjuntas — o '+' e obrigatorio no telefone e proibido no CPF — e agora cada
        // texto e recusado pelo tipo a que nao pertence, em vez de produzir duas chaves distintas
        // em silencio. O que nao muda: nao existe "a chave deste texto", existe a chave deste
        // texto *neste tipo*, e quem chama e que decide qual.
        const string comMaisInicial = "+52998224725";

        ChavePix comoTelefone = ChavePix.Criar(TipoChave.Telefone, comMaisInicial);
        Assert.Equal(comMaisInicial, comoTelefone.Texto);
        Assert.Equal(TipoChave.Telefone, comoTelefone.Tipo);

        ChavePixInvalidaException recusadoComoCpf = Assert.Throws<ChavePixInvalidaException>(
            () => ChavePix.Criar(TipoChave.Cpf, comMaisInicial));
        Assert.Equal("CPF com caractere invalido '+'", recusadoComoCpf.Motivo);

        // E a volta: os mesmos onze digitos sem o '+' sao chave de CPF valida e nao sao telefone.
        ChavePix comoCpf = ChavePix.Criar(TipoChave.Cpf, CpfValido);
        Assert.Equal(CpfValido, comoCpf.Texto);
        Assert.NotEqual(comoCpf.Texto, comoTelefone.Texto);
        Assert.NotEqual(comoCpf, comoTelefone);

        ChavePixInvalidaException recusadoComoTelefone = Assert.Throws<ChavePixInvalidaException>(
            () => ChavePix.Criar(TipoChave.Telefone, CpfValido));
        Assert.Equal("telefone deve estar em E.164 e comecar com '+'", recusadoComoTelefone.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_TelefoneComLetraDepoisDoMais_ERecusadoEmVezDeNormalizarSilenciosamente()
    {
        // Descartar todo nao-digito fazia "+ab12345678" virar "+12345678": um numero que ninguem
        // digitou, com 8 digitos, dentro da faixa aceita e portanto gravavel no DICT. A chave de
        // enderecamento seria invencao da normalizacao, e o pagamento iria para quem por acaso
        // tivesse aquele numero.
        ChavePixInvalidaException excecao = Assert.Throws<ChavePixInvalidaException>(
            () => ChavePix.Criar(TipoChave.Telefone, "+ab12345678"));

        Assert.Contains("telefone com caractere invalido", excecao.Motivo, StringComparison.Ordinal);
        Assert.Equal("telefone com caractere invalido 'a'", excecao.Motivo);

        // O telefone valida o texto ja sem o '+' inicial, mas reporta o texto que o CHAMADOR passou:
        // um diagnostico que apontasse "ab12345678" mandaria procurar um valor que ninguem digitou.
        Assert.Equal("+ab12345678", excecao.Bruto);

        // Sem as letras, o mesmo numero e aceito — a recusa e do caractere, nao do comprimento.
        Assert.Equal("+12345678", ChavePix.Criar(TipoChave.Telefone, "+12345678").Texto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ChavesDeTiposDiferentes_NuncaSaoIguaisEntreSi()
    {
        ChavePix[] umaDeCadaTipo = new ChavePix[]
        {
            ChavePix.Criar(TipoChave.Cpf, CpfValidoComPontuacao),
            ChavePix.Criar(TipoChave.Cnpj, CnpjValidoComPontuacao),
            ChavePix.Criar(TipoChave.Email, "  Fulano@Exemplo.COM "),
            ChavePix.Criar(TipoChave.Telefone, "+55 (11) 99999-9999"),
            ChavePix.Criar(TipoChave.Aleatoria, GuidMaiusculo),
        };

        for (int i = 0; i < umaDeCadaTipo.Length; i++)
        {
            for (int j = i + 1; j < umaDeCadaTipo.Length; j++)
            {
                ChavePix esquerda = umaDeCadaTipo[i];
                ChavePix direita = umaDeCadaTipo[j];

                Assert.NotEqual(esquerda.Tipo, direita.Tipo);
                Assert.NotEqual(esquerda, direita);

                // As formas canonicas tambem sao disjuntas por construcao (11 e 14 digitos, '+' na
                // frente, '@' no meio, hifens do GUID), entao nem por coincidencia de texto duas
                // chaves de tipos diferentes se encontram.
                Assert.NotEqual(esquerda.Texto, direita.Texto);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Superficie da fabrica: subtipos, ToString, nulo, tipo desconhecido e TryCriar
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Criar_CadaTipo_ProduzOSubtipoFechadoCorrespondente()
    {
        Assert.IsType<ChaveCpf>(ChavePix.Criar(TipoChave.Cpf, CpfValidoComPontuacao));
        Assert.IsType<ChaveCnpj>(ChavePix.Criar(TipoChave.Cnpj, CnpjValidoComPontuacao));
        Assert.IsType<ChaveEmail>(ChavePix.Criar(TipoChave.Email, EmailNormalizado));
        Assert.IsType<ChaveTelefone>(ChavePix.Criar(TipoChave.Telefone, TelefoneNormalizado));
        Assert.IsType<ChaveAleatoria>(ChavePix.Criar(TipoChave.Aleatoria, GuidMaiusculo));
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(TipoChave.Cpf, CpfValidoComPontuacao, "Cpf:52998224725")]
    [InlineData(TipoChave.Cnpj, CnpjValidoComPontuacao, "Cnpj:11222333000181")]
    [InlineData(TipoChave.Email, "  Fulano@Exemplo.COM ", "Email:fulano@exemplo.com")]
    [InlineData(TipoChave.Telefone, "+55 (11) 99999-9999", "Telefone:+5511999999999")]
    [InlineData(TipoChave.Aleatoria, GuidMaiusculo, "Aleatoria:6ba7b810-9dad-11d1-80b4-00c04fd430c8")]
    public void ToString_QualquerTipo_ImprimeTipoEValorCanonico(TipoChave tipo, string bruto, string esperado)
    {
        ChavePix chave = ChavePix.Criar(tipo, bruto);

        Assert.Equal(esperado, chave.ToString());
        Assert.Equal(tipo, chave.Tipo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(TipoChave.Cpf)]
    [InlineData(TipoChave.Cnpj)]
    [InlineData(TipoChave.Email)]
    [InlineData(TipoChave.Telefone)]
    [InlineData(TipoChave.Aleatoria)]
    public void Criar_TextoNulo_LancaParaTodosOsTipos(TipoChave tipo)
    {
        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(tipo, null));

        Assert.Null(excecao.Bruto);
        Assert.Equal("ChavePix", excecao.Tipo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_TipoForaDoSumType_Lanca()
    {
        const TipoChave desconhecido = (TipoChave)99;

        ChavePixInvalidaException excecao =
            Assert.Throws<ChavePixInvalidaException>(() => ChavePix.Criar(desconhecido, CpfValido));

        Assert.Equal("tipo de chave desconhecido: 99", excecao.Motivo);
        Assert.Equal(CpfValido, excecao.Bruto);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(TipoChave.Cpf, "52998224726")]
    [InlineData(TipoChave.Cnpj, "11222333000182")]
    [InlineData(TipoChave.Email, "fulano-arroba-exemplo.com")]
    [InlineData(TipoChave.Telefone, "5511999999999")]
    [InlineData(TipoChave.Aleatoria, "6ba7b8109dad11d180b400c04fd430c8")]
    [InlineData((TipoChave)99, CpfValido)]
    public void TryCriar_EntradaInvalida_DevolveFalseSemLancar(TipoChave tipo, string bruto)
    {
        bool criou = ChavePix.TryCriar(tipo, bruto, out ChavePix? chave);

        Assert.False(criou);
        Assert.Null(chave);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(TipoChave.Cpf, CpfValidoComPontuacao, CpfValido)]
    [InlineData(TipoChave.Cnpj, CnpjValidoComPontuacao, CnpjValido)]
    [InlineData(TipoChave.Email, "  Fulano@Exemplo.COM ", EmailNormalizado)]
    [InlineData(TipoChave.Telefone, "+55 (11) 99999-9999", TelefoneNormalizado)]
    [InlineData(TipoChave.Aleatoria, GuidMaiusculo, GuidMinusculo)]
    public void TryCriar_EntradaValida_DevolveTrueComValorNormalizado(TipoChave tipo, string bruto, string canonico)
    {
        bool criou = ChavePix.TryCriar(tipo, bruto, out ChavePix? chave);

        Assert.True(criou);
        Assert.NotNull(chave);
        Assert.Equal(canonico, chave!.Texto);
        Assert.Equal(tipo, chave.Tipo);
        Assert.Equal(ChavePix.Criar(tipo, bruto), chave);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(TipoChave.Cpf)]
    [InlineData(TipoChave.Cnpj)]
    [InlineData(TipoChave.Email)]
    [InlineData(TipoChave.Telefone)]
    [InlineData(TipoChave.Aleatoria)]
    public void TryCriar_TextoNulo_DevolveFalseSemLancar(TipoChave tipo)
    {
        bool criou = ChavePix.TryCriar(tipo, null, out ChavePix? chave);

        Assert.False(criou);
        Assert.Null(chave);
    }

    // ---------------------------------------------------------------------------------------
    // Uso como indice: HashSet
    // ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void HashSet_UmaChaveDeCadaTipo_ConvivemSemColisao()
    {
        HashSet<ChavePix> indice = new HashSet<ChavePix>
        {
            ChavePix.Criar(TipoChave.Cpf, CpfValidoComPontuacao),
            ChavePix.Criar(TipoChave.Cnpj, CnpjValidoComPontuacao),
            ChavePix.Criar(TipoChave.Email, "  Fulano@Exemplo.COM "),
            ChavePix.Criar(TipoChave.Telefone, "+55 (11) 99999-9999"),
            ChavePix.Criar(TipoChave.Aleatoria, GuidMaiusculo),
        };

        Assert.Equal(5, indice.Count);

        // Uma segunda grafia da mesma chave nao cria entrada nova: e exatamente isto que o DICT
        // depende para nao ter duas linhas para o mesmo CPF.
        bool adicionouOutraGrafia = indice.Add(ChavePix.Criar(TipoChave.Cpf, CpfValido));
        Assert.False(adicionouOutraGrafia);
        Assert.Equal(5, indice.Count);

        // Chave de outro titular, mesmo tipo, entra normalmente.
        bool adicionouOutroCpf = indice.Add(ChavePix.Criar(TipoChave.Cpf, OutroCpfValido));
        Assert.True(adicionouOutroCpf);
        Assert.Equal(6, indice.Count);

        // Consulta pelo indice: a chave reconstruida de outra grafia acha a entrada gravada.
        bool achouEmailEmOutraCaixa = indice.Contains(ChavePix.Criar(TipoChave.Email, "FULANO@EXEMPLO.COM"));
        bool achouTelefoneSemMascara = indice.Contains(ChavePix.Criar(TipoChave.Telefone, "+5511999999999"));
        bool achouCnpjNuncaGravado = indice.Contains(ChavePix.Criar(TipoChave.Cnpj, OutroCnpjValido));

        Assert.True(achouEmailEmOutraCaixa);
        Assert.True(achouTelefoneSemMascara);
        Assert.False(achouCnpjNuncaGravado);
    }

    // ---------------------------------------------------------------------------------------
    // Oraculo independente do modulo 11 (sem a convencao das sequencias repetidas)
    // ---------------------------------------------------------------------------------------

    private static bool AritmeticaModulo11AceitaCpf(string digitos) =>
        digitos[9] - '0' == DigitoDeControle(digitos, 9)
        && digitos[10] - '0' == DigitoDeControle(digitos, 10);

    /// <summary>
    /// Peso do digito i e <c>quantidade + 1 - i</c>: 10..2 para o primeiro DV, 11..2 para o segundo.
    /// </summary>
    private static int DigitoDeControle(string digitos, int quantidade)
    {
        int soma = 0;
        for (int i = 0; i < quantidade; i++)
        {
            soma += (digitos[i] - '0') * (quantidade + 1 - i);
        }

        int resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
