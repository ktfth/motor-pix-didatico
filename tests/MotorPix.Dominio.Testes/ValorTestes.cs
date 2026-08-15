using System.Reflection;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// Invariante 1: dinheiro e <c>long</c> em centavos, encapsulado num value object cuja unica
/// forma de nascer valido e a fabrica. <see cref="Valor"/> e o tipo da <em>perna de lancamento</em>,
/// entao e estritamente positivo — zero e negativo sao erro de dominio, nao caso de borda.
/// </summary>
public sealed class ValorTestes
{
    // ---------------------------------------------------------------------
    // DeCentavos
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void DeCentavos_ComZero_LancaValorInvalidoComMotivoZero()
    {
        ValorInvalidoException erro =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(0L));

        Assert.Equal("zero", erro.Motivo);
        Assert.Equal(0L, erro.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(-1L)]
    [InlineData(-99L)]
    [InlineData(-100L)]
    [InlineData(-1234L)]
    [InlineData(long.MinValue)]
    public void DeCentavos_ComNegativo_LancaValorInvalidoComMotivoNegativo(long centavos)
    {
        ValorInvalidoException erro =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(centavos));

        // O motivo distingue as duas rejeicoes: quem le o log precisa saber se recebeu
        // "nada" (zero) ou "dinheiro andando para tras" (negativo).
        Assert.Equal("negativo", erro.Motivo);
        Assert.Equal(centavos, erro.Centavos);
    }

    [Fact]
    [Trait("gate", "1")]
    public void DeCentavos_ParaZeroENegativo_UsaMotivosDiferentes()
    {
        ValorInvalidoException erroZero =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(0L));
        ValorInvalidoException erroNegativo =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(-1L));

        Assert.NotEqual(erroZero.Motivo, erroNegativo.Motivo);
        Assert.Contains("zero", erroZero.Message, StringComparison.Ordinal);
        Assert.Contains("negativo", erroNegativo.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(5L)]
    [InlineData(99L)]
    [InlineData(100L)]
    [InlineData(1234L)]
    [InlineData(long.MaxValue)]
    public void DeCentavos_ComPositivo_PreservaOsCentavosEEhPositivo(long centavos)
    {
        Valor valor = Valor.DeCentavos(centavos);

        Assert.Equal(centavos, valor.Centavos);
        Assert.True(valor.EhPositivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Default_TemZeroCentavosENaoEhPositivo_DocumentandoOBuracoDaStruct()
    {
        // Uma struct nao consegue proibir a propria construcao default: `default(Valor)` existe,
        // vale zero e escapa da fabrica. Este teste documenta o buraco — quem consome um Valor
        // vindo de fora do dominio (Partida, Lancamento) tem de consultar EhPositivo antes de usar.
        Valor buraco = default;

        Assert.Equal(0L, buraco.Centavos);
        Assert.False(buraco.EhPositivo);

        // O mesmo numero, se passasse pela fabrica, seria recusado.
        Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(buraco.Centavos));
    }

    // ---------------------------------------------------------------------
    // DeReais
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(10L, 50L, 1050L)]
    [InlineData(12L, 34L, 1234L)]
    [InlineData(1L, 0L, 100L)]
    [InlineData(0L, 1L, 1L)]
    [InlineData(0L, 99L, 99L)]
    [InlineData(7L, 5L, 705L)]
    [InlineData(1000L, 1L, 100001L)]
    public void DeReais_ComReaisECentavos_ConverteParaCentavos(long reais, long centavos, long esperado)
    {
        Valor valor = Valor.DeReais(reais, centavos);

        Assert.Equal(esperado, valor.Centavos);
    }

    [Fact]
    [Trait("gate", "1")]
    public void DeReais_ComCentavosOmitidos_AssumeZero()
    {
        Assert.Equal(2500L, Valor.DeReais(25L).Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(-1L)]
    [InlineData(-50L)]
    [InlineData(100L)]
    [InlineData(1000L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void DeReais_ComCentavosForaDeZeroA99_LancaValorInvalido(long centavos)
    {
        ValorInvalidoException erro =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeReais(10L, centavos));

        Assert.Equal("centavos fora de 0..99", erro.Motivo);
        Assert.Equal(centavos, erro.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(-1L)]
    [InlineData(-1000L)]
    [InlineData(long.MinValue)]
    public void DeReais_ComReaisNegativos_LancaValorInvalido(long reais)
    {
        ValorInvalidoException erro =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeReais(reais, 50L));

        Assert.Equal("reais negativos", erro.Motivo);

        // Quirk deliberadamente registrado: a propriedade chama-se Centavos, mas nesta rejeicao
        // ela carrega a quantidade de *reais* recebida.
        Assert.Equal(reais, erro.Centavos);
    }

    [Fact]
    [Trait("gate", "1")]
    public void DeReais_ComZeroReaisEZeroCentavos_LancaValorInvalidoPorSerZero()
    {
        ValorInvalidoException erro =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeReais(0L, 0L));

        Assert.Equal("zero", erro.Motivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MaxValue, 0L)]
    [InlineData(92233720368547759L, 0L)]
    [InlineData(92233720368547758L, 99L)]
    [InlineData(92233720368547758L, 8L)]
    public void DeReais_ComReaisGigantes_LancaEstouroDeValorENaoOverflowException(long reais, long centavos)
    {
        // Assert.Throws exige tipo exato: se a implementacao deixasse vazar OverflowException,
        // este teste falharia — que e exatamente o ponto (prompt:32 exige excecao de dominio).
        EstouroDeValorException erro =
            Assert.Throws<EstouroDeValorException>(() => Valor.DeReais(reais, centavos));

        Assert.Equal("DeReais", erro.Operacao);
        Assert.Equal(reais, erro.Esquerda);
        Assert.Equal(centavos, erro.Direita);
        Assert.IsAssignableFrom<MotorPixException>(erro);
    }

    [Fact]
    [Trait("gate", "1")]
    public void DeReais_NoLimiteExatoDoLong_NaoLanca()
    {
        // 92233720368547758 reais + 7 centavos = long.MaxValue centavos, o ultimo valor representavel.
        Valor limite = Valor.DeReais(92233720368547758L, 7L);

        Assert.Equal(long.MaxValue, limite.Centavos);
    }

    // ---------------------------------------------------------------------
    // operator +
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L, 1L, 2L)]
    [InlineData(1050L, 950L, 2000L)]
    [InlineData(1L, 99L, 100L)]
    [InlineData(1234L, 1L, 1235L)]
    [InlineData(long.MaxValue - 1L, 1L, long.MaxValue)]
    public void Soma_DeValores_SomaOsCentavos(long esquerda, long direita, long esperado)
    {
        Valor resultado = Valor.DeCentavos(esquerda) + Valor.DeCentavos(direita);

        Assert.Equal(esperado, resultado.Centavos);
        Assert.True(resultado.EhPositivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(long.MaxValue, long.MaxValue)]
    [InlineData(long.MaxValue - 1L, 2L)]
    [InlineData(5000000000000000000L, 5000000000000000000L)]
    public void Soma_ComEstouro_LancaEstouroDeValorComOperacaoDeMais(long esquerda, long direita)
    {
        EstouroDeValorException erro = Assert.Throws<EstouroDeValorException>(
            () => Valor.DeCentavos(esquerda) + Valor.DeCentavos(direita));

        Assert.Equal("+", erro.Operacao);
        Assert.Equal(esquerda, erro.Esquerda);
        Assert.Equal(direita, erro.Direita);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Soma_ComDefaultDosDoisLados_LancaValorInvalidoComMotivoZero()
    {
        // `+` revalida pela fabrica, entao o buraco da struct nao consegue mais produzir um Valor
        // de zero centavos por dentro do proprio tipo — um valor invalido que nasceria sem nunca
        // ter passado por DeCentavos. `-` sempre recusou; agora as duas pontas recusam pelo mesmo
        // criterio e a assimetria deixou de existir.
        Valor esquerda = default;
        Valor direita = default;

        ValorInvalidoException erroDaSoma =
            Assert.Throws<ValorInvalidoException>(() => esquerda + direita);
        ValorInvalidoException erroDaSubtracao =
            Assert.Throws<ValorInvalidoException>(() => esquerda - direita);

        Assert.Equal("zero", erroDaSoma.Motivo);
        Assert.Equal(0L, erroDaSoma.Centavos);
        Assert.Equal("subtracao produziria valor nao positivo", erroDaSubtracao.Motivo);
        Assert.Equal(0L, erroDaSubtracao.Centavos);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Soma_ComUmDefaultDeUmLadoSo_PreservaOOutroOperandoEContinuaPositiva()
    {
        // O default so e recusado quando o resultado nao e positivo: somar zero a um valor valido
        // continua valendo, e e por isso que a revalidacao da soma nao pode ser um "if EhPositivo"
        // nos operandos.
        Valor valido = Valor.DeCentavos(1_234L);

        Assert.Equal(1_234L, (valido + default(Valor)).Centavos);
        Assert.Equal(1_234L, (default(Valor) + valido).Centavos);
    }

    // ---------------------------------------------------------------------
    // operator -
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(2000L, 950L, 1050L)]
    [InlineData(100L, 99L, 1L)]
    [InlineData(long.MaxValue, 1L, long.MaxValue - 1L)]
    [InlineData(1235L, 1234L, 1L)]
    public void Subtracao_ComEsquerdaMaior_DevolveADiferenca(long esquerda, long direita, long esperado)
    {
        Valor resultado = Valor.DeCentavos(esquerda) - Valor.DeCentavos(direita);

        Assert.Equal(esperado, resultado.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(1234L)]
    [InlineData(long.MaxValue)]
    public void Subtracao_DeValoresIguais_LancaValorInvalidoPorResultadoZero(long centavos)
    {
        Valor esquerda = Valor.DeCentavos(centavos);
        Valor direita = Valor.DeCentavos(centavos);

        ValorInvalidoException erro =
            Assert.Throws<ValorInvalidoException>(() => esquerda - direita);

        Assert.Equal("subtracao produziria valor nao positivo", erro.Motivo);
        Assert.Equal(0L, erro.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L, 2L, -1L)]
    [InlineData(950L, 2000L, -1050L)]
    [InlineData(1L, long.MaxValue, -9223372036854775806L)]
    public void Subtracao_ComDireitaMaior_LancaValorInvalidoComOResultadoNegativo(
        long esquerda,
        long direita,
        long resultadoEsperado)
    {
        ValorInvalidoException erro = Assert.Throws<ValorInvalidoException>(
            () => Valor.DeCentavos(esquerda) - Valor.DeCentavos(direita));

        Assert.Equal("subtracao produziria valor nao positivo", erro.Motivo);
        Assert.Equal(resultadoEsperado, erro.Centavos);
    }

    // ---------------------------------------------------------------------
    // Ordem
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L, 2L, -1)]
    [InlineData(2L, 1L, 1)]
    [InlineData(5L, 5L, 0)]
    [InlineData(99L, 100L, -1)]
    [InlineData(100L, 99L, 1)]
    [InlineData(1L, long.MaxValue, -1)]
    [InlineData(long.MaxValue, 1L, 1)]
    [InlineData(long.MaxValue, long.MaxValue, 0)]
    [InlineData(1234L, 1233L, 1)]
    public void Comparacoes_SeguemAOrdemDosCentavos(long esquerda, long direita, int sinalEsperado)
    {
        Valor a = Valor.DeCentavos(esquerda);
        Valor b = Valor.DeCentavos(direita);

        Assert.Equal(sinalEsperado, Math.Sign(a.CompareTo(b)));
        Assert.Equal(sinalEsperado < 0, a < b);
        Assert.Equal(sinalEsperado > 0, a > b);
        Assert.Equal(sinalEsperado <= 0, a <= b);
        Assert.Equal(sinalEsperado >= 0, a >= b);
        Assert.Equal(sinalEsperado == 0, a.Equals(b));
    }

    [Fact]
    [Trait("gate", "1")]
    public void CompareTo_EAntissimetrico()
    {
        Valor menor = Valor.DeCentavos(1L);
        Valor maior = Valor.DeCentavos(long.MaxValue);

        Assert.Equal(-Math.Sign(menor.CompareTo(maior)), Math.Sign(maior.CompareTo(menor)));
        Assert.Equal(0, Math.Sign(menor.CompareTo(Valor.DeCentavos(1L))));
    }

    // ---------------------------------------------------------------------
    // ToString
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1234L, "R$ 12,34")]
    [InlineData(5L, "R$ 0,05")]
    [InlineData(100L, "R$ 1,00")]
    [InlineData(1L, "R$ 0,01")]
    [InlineData(10L, "R$ 0,10")]
    [InlineData(99L, "R$ 0,99")]
    [InlineData(1000L, "R$ 10,00")]
    [InlineData(100000L, "R$ 1000,00")]
    [InlineData(long.MaxValue, "R$ 92233720368547758,07")]
    public void ToString_FormataEmReaisComDuasCasas(long centavos, string esperado)
    {
        Assert.Equal(esperado, Valor.DeCentavos(centavos).ToString());
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L, ",01")]
    [InlineData(5L, ",05")]
    [InlineData(9L, ",09")]
    [InlineData(101L, ",01")]
    [InlineData(1009L, ",09")]
    [InlineData(100000009L, ",09")]
    public void ToString_ComCentavosMenoresQueDez_PreservaOZeroAEsquerda(long centavos, string sufixoEsperado)
    {
        // O bug classico e imprimir "R$ 10,9" para 1009 centavos: a parte fracionaria tem
        // sempre exatamente dois digitos, e o zero a esquerda nao pode cair.
        string texto = Valor.DeCentavos(centavos).ToString();

        Assert.Matches(@"^R\$ \d+,\d{2}$", texto);
        Assert.EndsWith(sufixoEsperado, texto, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Igualdade estrutural
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(1234L)]
    [InlineData(100L)]
    [InlineData(long.MaxValue)]
    public void Igualdade_DeValoresComMesmosCentavos_SaoIguaisEComMesmoHash(long centavos)
    {
        Valor a = Valor.DeCentavos(centavos);
        Valor b = Valor.DeCentavos(centavos);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L, 2L)]
    [InlineData(1234L, 1235L)]
    [InlineData(1L, long.MaxValue)]
    public void Igualdade_DeValoresComCentavosDiferentes_NaoSaoIguais(long esquerda, long direita)
    {
        Valor a = Valor.DeCentavos(esquerda);
        Valor b = Valor.DeCentavos(direita);

        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    // ---------------------------------------------------------------------
    // TryDeCentavos
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-1234L)]
    [InlineData(long.MinValue)]
    public void TryDeCentavos_ComEntradaNaoPositiva_DevolveFalsoEDefault(long centavos)
    {
        bool sucesso = Valor.TryDeCentavos(centavos, out Valor valor);

        Assert.False(sucesso);
        Assert.Equal(default(Valor), valor);
        Assert.Equal(0L, valor.Centavos);
        Assert.False(valor.EhPositivo);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1L)]
    [InlineData(1234L)]
    [InlineData(long.MaxValue)]
    public void TryDeCentavos_ComEntradaPositiva_DevolveVerdadeiroEOValor(long centavos)
    {
        bool sucesso = Valor.TryDeCentavos(centavos, out Valor valor);

        Assert.True(sucesso);
        Assert.Equal(centavos, valor.Centavos);
        Assert.True(valor.EhPositivo);
        Assert.Equal(Valor.DeCentavos(centavos), valor);
    }

    // ---------------------------------------------------------------------
    // Propriedades estruturais do tipo
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void CentavosPorReal_ValeCemEEAEscalaUsadaPorDeReais()
    {
        Assert.Equal(100L, Valor.CentavosPorReal);
        Assert.Equal(Valor.CentavosPorReal, Valor.DeReais(1L).Centavos);
        Assert.Equal("R$ 1,00", Valor.DeCentavos(Valor.CentavosPorReal).ToString());
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData("op_Multiply")]
    [InlineData("op_Division")]
    [InlineData("op_Modulus")]
    public void Valor_NaoExpoeMultiplicacaoNemDivisao(string nomeDoOperador)
    {
        // Nenhum dos documentos tem tarifa, juros, cambio ou rateio; divisao e a porta pela
        // qual o arredondamento entra e quebra o invariante 1.
        MethodInfo? operador = typeof(Valor).GetMethod(
            nomeDoOperador,
            BindingFlags.Public | BindingFlags.Static);

        Assert.Null(operador);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Valor_NaoTemConversaoImplicitaDeLong()
    {
        // Sem isto, `long` cru vazaria para dentro do dominio sem passar pela fabrica.
        MethodInfo? conversao = typeof(Valor).GetMethod(
            "op_Implicit",
            BindingFlags.Public | BindingFlags.Static);

        Assert.Null(conversao);
    }
}
