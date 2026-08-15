using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// <see cref="Saldo"/> e o tipo da projecao: quantia <em>com sinal</em>, em centavos.
/// Admite zero e negativo justamente porque a guarda de nao-negatividade precisa poder calcular
/// um saldo negativo <em>antes</em> de recusa-lo — coisa que <see cref="Valor"/>, sendo a perna
/// de lancamento, nao pode representar. Nenhuma conta do sistema chega a <em>ficar</em> negativa;
/// o negativo existe como resultado intermediario que o ledger avalia e recusa.
/// </summary>
public sealed class SaldoTestes
{
    // ---------------------------------------------------------------------
    // Zero, EhZero, EhNegativo
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Zero_TemCentavosZeroEEquivaleAoDefault()
    {
        Assert.Equal(0L, Saldo.Zero.Centavos);
        Assert.True(Saldo.Zero.EhZero);
        Assert.False(Saldo.Zero.EhNegativo);

        // default(Saldo) valendo zero e semanticamente correto para conta sem movimento —
        // ao contrario de default(Valor), que e um buraco.
        Assert.Equal(Saldo.Zero, default(Saldo));
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MinValue, false, true)]
    [InlineData(-1234L, false, true)]
    [InlineData(-1L, false, true)]
    [InlineData(0L, true, false)]
    [InlineData(1L, false, false)]
    [InlineData(1234L, false, false)]
    [InlineData(long.MaxValue, false, false)]
    public void EhZeroEEhNegativo_RefletemOSinalDosCentavos(
        long centavos,
        bool esperadoEhZero,
        bool esperadoEhNegativo)
    {
        Saldo saldo = new(centavos);

        Assert.Equal(centavos, saldo.Centavos);
        Assert.Equal(esperadoEhZero, saldo.EhZero);
        Assert.Equal(esperadoEhNegativo, saldo.EhNegativo);
    }

    // ---------------------------------------------------------------------
    // Os quatro operadores
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(0L, 1L, 1L)]
    [InlineData(1000L, 250L, 1250L)]
    [InlineData(-500L, 200L, -300L)]
    [InlineData(-1L, 1L, 0L)]
    [InlineData(-1000L, 2000L, 1000L)]
    [InlineData(long.MinValue, 1L, -9223372036854775807L)]
    public void SomaComValor_SomaOsCentavosEAceitaResultadoNegativo(long inicial, long delta, long esperado)
    {
        Saldo resultado = new Saldo(inicial) + Valor.DeCentavos(delta);

        Assert.Equal(esperado, resultado.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1000L, 250L, 750L)]
    [InlineData(1000L, 1000L, 0L)]
    [InlineData(100L, 250L, -150L)]
    [InlineData(0L, 1L, -1L)]
    [InlineData(-500L, 200L, -700L)]
    [InlineData(long.MaxValue, 1L, 9223372036854775806L)]
    public void SubtracaoComValor_SubtraiOsCentavosEAceitaResultadoNegativo(
        long inicial,
        long delta,
        long esperado)
    {
        // Saldo negativo aqui e legal: e o insumo da guarda de nao-negatividade, que so depois
        // decide se recusa o lancamento.
        Saldo resultado = new Saldo(inicial) - Valor.DeCentavos(delta);

        Assert.Equal(esperado, resultado.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(0L, 0L, 0L)]
    [InlineData(1000L, 250L, 1250L)]
    [InlineData(-100L, -200L, -300L)]
    [InlineData(100L, -100L, 0L)]
    [InlineData(-100L, 250L, 150L)]
    [InlineData(long.MinValue, long.MaxValue, -1L)]
    public void SomaDeSaldos_SomaOsCentavos(long esquerda, long direita, long esperado)
    {
        Saldo resultado = new Saldo(esquerda) + new Saldo(direita);

        Assert.Equal(esperado, resultado.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(0L, 0L, 0L)]
    [InlineData(1250L, 250L, 1000L)]
    [InlineData(-100L, 200L, -300L)]
    [InlineData(-100L, -200L, 100L)]
    [InlineData(0L, long.MaxValue, -9223372036854775807L)]
    [InlineData(1000L, 1000L, 0L)]
    public void SubtracaoDeSaldos_SubtraiOsCentavos(long esquerda, long direita, long esperado)
    {
        Saldo resultado = new Saldo(esquerda) - new Saldo(direita);

        Assert.Equal(esperado, resultado.Centavos);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(0L, 1L)]
    [InlineData(-500L, 250L)]
    [InlineData(1000L, 999L)]
    [InlineData(long.MinValue + 1L, 1L)]
    [InlineData(long.MaxValue - 1L, 1L)]
    public void SomaESubtracaoComValor_SaoOperacoesInversas(long inicial, long delta)
    {
        Saldo saldo = new(inicial);
        Valor valor = Valor.DeCentavos(delta);

        Assert.Equal(saldo, saldo + valor - valor);
        Assert.Equal(saldo, saldo - valor + valor);
    }

    // ---------------------------------------------------------------------
    // Estouro em cada um dos quatro operadores
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(long.MaxValue - 5L, 10L)]
    [InlineData(5000000000000000000L, 5000000000000000000L)]
    public void SomaComValor_ComEstouro_LancaEstouroDeValor(long inicial, long delta)
    {
        EstouroDeValorException erro = Assert.Throws<EstouroDeValorException>(
            () => new Saldo(inicial) + Valor.DeCentavos(delta));

        Assert.Equal("Saldo +", erro.Operacao);
        Assert.Equal(inicial, erro.Esquerda);
        Assert.Equal(delta, erro.Direita);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MinValue, 1L)]
    [InlineData(long.MinValue + 5L, 10L)]
    [InlineData(-5000000000000000000L, 5000000000000000000L)]
    public void SubtracaoComValor_ComEstouro_LancaEstouroDeValor(long inicial, long delta)
    {
        EstouroDeValorException erro = Assert.Throws<EstouroDeValorException>(
            () => new Saldo(inicial) - Valor.DeCentavos(delta));

        Assert.Equal("Saldo -", erro.Operacao);
        Assert.Equal(inicial, erro.Esquerda);
        Assert.Equal(delta, erro.Direita);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(long.MinValue, -1L)]
    [InlineData(long.MinValue, long.MinValue)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void SomaDeSaldos_ComEstouro_LancaEstouroDeValor(long esquerda, long direita)
    {
        EstouroDeValorException erro = Assert.Throws<EstouroDeValorException>(
            () => new Saldo(esquerda) + new Saldo(direita));

        Assert.Equal("Saldo + Saldo", erro.Operacao);
        Assert.Equal(esquerda, erro.Esquerda);
        Assert.Equal(direita, erro.Direita);
    }

    [Theory]
    [Trait("gate", "1")]
    [InlineData(long.MinValue, 1L)]
    [InlineData(long.MaxValue, -1L)]
    [InlineData(long.MinValue, long.MaxValue)]
    [InlineData(-5000000000000000000L, 5000000000000000000L)]
    public void SubtracaoDeSaldos_ComEstouro_LancaEstouroDeValor(long esquerda, long direita)
    {
        EstouroDeValorException erro = Assert.Throws<EstouroDeValorException>(
            () => new Saldo(esquerda) - new Saldo(direita));

        Assert.Equal("Saldo - Saldo", erro.Operacao);
        Assert.Equal(esquerda, erro.Esquerda);
        Assert.Equal(direita, erro.Direita);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Estouro_EmQualquerOperador_EExcecaoDeDominioENaoOverflowException()
    {
        // O pior modo de falha do projeto seria wrap silencioso: a soma de saldos quebraria
        // e o teste que a confere tambem daria wrap, escondendo o rombo.
        EstouroDeValorException erro = Assert.Throws<EstouroDeValorException>(
            () => new Saldo(long.MaxValue) + Valor.DeCentavos(1L));

        Assert.IsAssignableFrom<MotorPixException>(erro);
        Assert.IsNotType<OverflowException>(erro);
    }

    // ---------------------------------------------------------------------
    // ToString com sinal
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(1234L, "R$ 12,34")]
    [InlineData(-1234L, "-R$ 12,34")]
    [InlineData(-5L, "-R$ 0,05")]
    [InlineData(5L, "R$ 0,05")]
    [InlineData(0L, "R$ 0,00")]
    [InlineData(-1L, "-R$ 0,01")]
    [InlineData(100L, "R$ 1,00")]
    [InlineData(-100L, "-R$ 1,00")]
    [InlineData(-99L, "-R$ 0,99")]
    [InlineData(-1000L, "-R$ 10,00")]
    [InlineData(long.MaxValue, "R$ 92233720368547758,07")]
    public void ToString_ImprimeOSinalAntesDoSimboloDeMoeda(long centavos, string esperado)
    {
        Assert.Equal(esperado, new Saldo(centavos).ToString());
    }

    [Fact]
    [Trait("gate", "1")]
    public void ToString_ComCentavosNoMinimoDoLong_ImprimeAMagnitudeExata()
    {
        // O valor exato importa porque esta string nao e enfeite: ela aparece na mensagem de
        // SaldoNegativoException e nas mensagens de falha dos testes de propriedade. Trocar
        // long.MinValue por long.MaxValue para fugir de Math.Abs(long.MinValue) imprimiria ",07"
        // e faria o diagnostico mentir por um centavo justamente no extremo em que alguem vai
        // conferir a conta a mao. A magnitude e calculada em ulong, entao termina em ",08".
        string texto = new Saldo(long.MinValue).ToString();

        Assert.Equal("-R$ 92233720368547758,08", texto);
        Assert.NotEqual("-R$ 92233720368547758,07", texto);

        // O negativo extremo e o simetrico do positivo extremo mais um centavo: os dois textos
        // diferem so no ultimo digito, e e esse digito que a saturacao perdia.
        Assert.Equal("R$ 92233720368547758,07", new Saldo(long.MaxValue).ToString());
    }

    // ---------------------------------------------------------------------
    // Ordem
    // ---------------------------------------------------------------------

    [Theory]
    [Trait("gate", "1")]
    [InlineData(0L, 0L, 0)]
    [InlineData(-1L, 0L, -1)]
    [InlineData(0L, -1L, 1)]
    [InlineData(-5L, -5L, 0)]
    [InlineData(-200L, -100L, -1)]
    [InlineData(-100L, -200L, 1)]
    [InlineData(long.MinValue, long.MaxValue, -1)]
    [InlineData(long.MaxValue, long.MinValue, 1)]
    [InlineData(long.MaxValue, 0L, 1)]
    [InlineData(1234L, 1233L, 1)]
    public void Comparacoes_SeguemAOrdemDosCentavosComSinal(long esquerda, long direita, int sinalEsperado)
    {
        Saldo a = new(esquerda);
        Saldo b = new(direita);

        Assert.Equal(sinalEsperado, Math.Sign(a.CompareTo(b)));
        Assert.Equal(sinalEsperado < 0, a < b);
        Assert.Equal(sinalEsperado > 0, a > b);
        Assert.Equal(sinalEsperado <= 0, a <= b);
        Assert.Equal(sinalEsperado >= 0, a >= b);
        Assert.Equal(sinalEsperado == 0, a.Equals(b));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Comparacoes_ContraZero_ConcordamComEhNegativoEEhZero()
    {
        Saldo negativo = new(-1L);
        Saldo positivo = new(1L);

        Assert.True(negativo < Saldo.Zero);
        Assert.True(negativo.EhNegativo);
        Assert.False(negativo >= Saldo.Zero);

        Assert.True(positivo > Saldo.Zero);
        Assert.False(positivo.EhNegativo);
        Assert.False(positivo <= Saldo.Zero);

        Saldo outroZero = new(0L);
        Assert.True(Saldo.Zero <= outroZero);
        Assert.True(Saldo.Zero >= outroZero);
        Assert.False(Saldo.Zero < outroZero);
        Assert.False(Saldo.Zero > outroZero);
        Assert.True(outroZero.EhZero);
    }

    // ---------------------------------------------------------------------
    // Por que Saldo e separado de Valor
    // ---------------------------------------------------------------------

    [Fact]
    [Trait("gate", "1")]
    public void Saldo_AdmiteZeroENegativo_ExatamenteOQueValorRecusa()
    {
        Saldo zero = new(0L);
        Saldo negativo = new(-250L);

        Assert.True(zero.EhZero);
        Assert.True(negativo.EhNegativo);
        Assert.Equal(-250L, negativo.Centavos);

        // Os mesmos dois numeros, se tivessem de virar perna de lancamento, seriam recusados.
        ValorInvalidoException erroZero =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(zero.Centavos));
        ValorInvalidoException erroNegativo =
            Assert.Throws<ValorInvalidoException>(() => Valor.DeCentavos(negativo.Centavos));

        Assert.Equal("zero", erroZero.Motivo);
        Assert.Equal("negativo", erroNegativo.Motivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void SubtracaoAbaixoDeZero_EhLegalEmSaldoEIlegalEmValor()
    {
        Valor cem = Valor.DeCentavos(100L);
        Valor duzentos = Valor.DeCentavos(200L);

        // Saldo desce abaixo de zero sem reclamar: e o fold do replay e o insumo da guarda.
        Saldo resultado = new Saldo(100L) - duzentos;
        Assert.Equal(-100L, resultado.Centavos);
        Assert.True(resultado.EhNegativo);

        // Valor, no mesmo caso, nao tem como representar o resultado.
        Assert.Throws<ValorInvalidoException>(() => cem - duzentos);
        Assert.Throws<ValorInvalidoException>(() => cem - Valor.DeCentavos(100L));
    }
}
