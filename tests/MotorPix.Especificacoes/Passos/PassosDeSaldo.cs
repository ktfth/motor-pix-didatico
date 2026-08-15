using MotorPix.Especificacoes.Suporte;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// As asserções de dinheiro. Todas leem centavos e comparam com o valor escrito no cenario,
/// convertido por <see cref="Dinheiro"/> — nenhum passo aqui recalcula o que o motor deveria ter
/// feito, porque comparar o motor com uma reimplementacao do motor nao prova nada.
/// </summary>
[Binding]
public sealed class PassosDeSaldo
{
    private readonly ContextoDoMotor _contexto;

    public PassosDeSaldo(ContextoDoMotor contexto) => _contexto = contexto;

    [Then(@"o saldo do cliente pagador é ""([^""]*)""")]
    public void EntaoSaldoPagador(string esperado) =>
        Conferir("saldo do cliente pagador", esperado, _contexto.SaldoDoClientePagador);

    [Then(@"o saldo do cliente recebedor é ""([^""]*)""")]
    public void EntaoSaldoRecebedor(string esperado) =>
        Conferir("saldo do cliente recebedor", esperado, _contexto.SaldoDoClienteRecebedor);

    [Then(@"a conta PI do pagador é ""([^""]*)""")]
    public void EntaoPiPagador(string esperado) =>
        Conferir("conta PI do pagador", esperado, _contexto.SaldoPiPagador);

    [Then(@"a conta PI do recebedor é ""([^""]*)""")]
    public void EntaoPiRecebedor(string esperado) =>
        Conferir("conta PI do recebedor", esperado, _contexto.SaldoPiRecebedor);

    [Then(@"o espelho do pagador é ""([^""]*)""")]
    public void EntaoEspelhoPagador(string esperado) =>
        Conferir("espelho do pagador", esperado, _contexto.SaldoEspelhoPagador);

    [Then(@"o espelho do recebedor é ""([^""]*)""")]
    public void EntaoEspelhoRecebedor(string esperado) =>
        Conferir("espelho do recebedor", esperado, _contexto.SaldoEspelhoRecebedor);

    [Then(@"o pool do SPI é ""([^""]*)""")]
    public void EntaoPool(string esperado) =>
        Conferir("pool do SPI", esperado, _contexto.PoolDoSpi);

    [Then(@"o espelho e a conta PI coincidem nos dois participantes")]
    public void EntaoQuiescencia()
    {
        Assert.Equal(_contexto.SaldoPiPagador, _contexto.SaldoEspelhoPagador);
        Assert.Equal(_contexto.SaldoPiRecebedor, _contexto.SaldoEspelhoRecebedor);
    }

    private static void Conferir(string oQue, string esperado, long observado)
    {
        long alvo = Dinheiro.EmCentavos(esperado);

        Assert.True(
            alvo == observado,
            $"{oQue}: esperado {Dinheiro.Formatar(alvo)}, observado {Dinheiro.Formatar(observado)}");
    }
}
