using MotorPix.Psp.Nucleo;

namespace MotorPix.Especificacoes.Suporte;

/// <summary>
/// Quem vigia o vigia. <see cref="Dinheiro"/> e <see cref="Vocabulario"/> traduzem o texto de todo
/// cenario: se eles estiverem errados, os cenarios continuam verdes afirmando outra coisa.
/// <para>
/// Sao <c>[Fact]</c> e nao cenarios Gherkin de proposito — escrever a especificacao da ferramenta
/// que le a especificacao, na propria linguagem que ela le, seria circular.
/// </para>
/// </summary>
public sealed class FerramentasDaEspecificacaoTestes
{
    [Theory]
    [Trait("gate", "3")]
    [InlineData("0,00", 0L)]
    [InlineData("0,01", 1L)]
    [InlineData("1,00", 100L)]
    [InlineData("250,00", 25_000L)]
    [InlineData("999,99", 99_999L)]
    [InlineData("1.000,00", 100_000L)]
    [InlineData("1.234.567,89", 123_456_789L)]
    [InlineData("R$ 250,00", 25_000L)]
    [InlineData("r$ 1.000,00", 100_000L)]
    [InlineData("250", 25_000L)]
    [InlineData("-250,00", -25_000L)]
    public void EmCentavos_ConverteOTextoDoCenario_SemPassarPorPontoFlutuante(string texto, long esperado) =>
        Assert.Equal(esperado, Dinheiro.EmCentavos(texto));

    /// <summary>
    /// "250,5" e recusado porque nao da para saber se sao cinco ou cinquenta centavos, e um cenario
    /// nao pode depender de quem le. Aceitar e arredondar seria pior: o texto afirmaria um valor e o
    /// motor receberia outro.
    /// </summary>
    [Theory]
    [Trait("gate", "3")]
    [InlineData("250,5")]
    [InlineData("250,555")]
    [InlineData("250,")]
    [InlineData("abc")]
    [InlineData("1,00,00")]
    [InlineData(",50")]
    public void EmCentavos_RecusaTextoAmbiguoOuInvalido(string texto) =>
        Assert.Throws<FormatException>(() => Dinheiro.EmCentavos(texto));

    [Fact]
    [Trait("gate", "3")]
    public void EmCentavos_ERoundTripComFormatar()
    {
        long[] valores = [0L, 1L, 99L, 100L, 25_000L, 99_999L, 100_000L, 123_456_789L];

        foreach (long centavos in valores)
        {
            string texto = Dinheiro.Formatar(centavos);

            Assert.True(
                centavos == Dinheiro.EmCentavos(texto),
                $"{centavos} formatou como '{texto}' e voltou como {Dinheiro.EmCentavos(texto)}");
        }
    }

    [Theory]
    [Trait("gate", "3")]
    [InlineData("ENVIADA_SPI", EstadoTransacao.EnviadaSpi)]
    [InlineData("enviada_spi", EstadoTransacao.EnviadaSpi)]
    [InlineData("LIQUIDADA", EstadoTransacao.Liquidada)]
    [InlineData("EXPIRADA", EstadoTransacao.Expirada)]
    [InlineData("DEVOLVIDA", EstadoTransacao.Devolvida)]
    public void Vocabulario_TraduzONomeDoDiagramaParaOEnum(string nome, EstadoTransacao esperado) =>
        Assert.Equal(esperado, Vocabulario.Enumerado<EstadoTransacao>(nome));

    [Theory]
    [Trait("gate", "3")]
    [InlineData("PACS002_ACSC", TipoEvento.Pacs002Acsc)]
    [InlineData("pacs.002_acsc", TipoEvento.Pacs002Acsc)]
    [InlineData("TIMEOUT_DETECTADO", TipoEvento.TimeoutDetectado)]
    public void Vocabulario_IgnoraPontuacaoDoNomeDeEvento(string nome, TipoEvento esperado) =>
        Assert.Equal(esperado, Vocabulario.Enumerado<TipoEvento>(nome));

    /// <summary>
    /// Um estado escrito errado tem de matar o cenario. Se caisse em silencio — devolvendo o
    /// primeiro membro, ou default — o cenario passaria afirmando um estado que ninguem checou.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Vocabulario_RecusaNomeDesconhecido_EListaOQueEraAceito()
    {
        FormatException erro = Assert.Throws<FormatException>(
            () => Vocabulario.Enumerado<EstadoTransacao>("QUASE_LIQUIDADA"));

        Assert.Contains("QUASE_LIQUIDADA", erro.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(EstadoTransacao.Liquidada), erro.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A traducao e por NOME. Este teste morre se alguem trocar a traducao por casamento de valor
    /// numerico: "1" nao e nome de nenhum membro, e nao pode virar o membro de valor 1.
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Vocabulario_NaoCasaPorValorNumerico() =>
        Assert.Throws<FormatException>(() => Vocabulario.Enumerado<EstadoTransacao>("1"));
}
