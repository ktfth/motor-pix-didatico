using MotorPix.Especificacoes.Suporte;
using MotorPix.Psp.Nucleo;
using MotorPix.PspPagador;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// As acoes do cenario: pagar, drenar o barramento, expirar vencidos, consultar status.
/// <para>
/// Drenar e um passo explicito porque o outbox e explicito (ADR-008). Isso e o que permite escrever
/// "o pacs.002 que nunca chega" como a ausencia de uma linha no cenario, em vez de um mock.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDePagamento
{
    private readonly ContextoDoMotor _contexto;

    public PassosDePagamento(ContextoDoMotor contexto) => _contexto = contexto;

    [When(@"o cliente pagador paga ""([^""]*)"" para a chave do recebedor")]
    public void QuandoPaga(string valor) =>
        _contexto.Pagar(Dinheiro.EmCentavos(valor));

    [When(@"o cliente pagador paga ""([^""]*)"" para a chave do recebedor, como pagamento ""([^""]*)""")]
    public void QuandoPagaApelidado(string valor, string apelido) =>
        _contexto.Pagar(_contexto.ProximoE2e(apelido), Dinheiro.EmCentavos(valor));

    [When(@"o cliente pagador reenvia o mesmo pagamento de ""([^""]*)""")]
    public void QuandoReenvia(string valor) =>
        _contexto.Pagar(_contexto.E2E, Dinheiro.EmCentavos(valor));

    [When(@"o cliente pagador reenvia o mesmo E2E com valor ""([^""]*)""")]
    public void QuandoReenviaComOutroValor(string valor)
    {
        try
        {
            _contexto.Pagar(_contexto.E2E, Dinheiro.EmCentavos(valor));
        }
        catch (Exception excecao)
        {
            _contexto.UltimaExcecao = excecao;
        }
    }

    [When(@"o barramento é drenado")]
    public void QuandoDrena() => _contexto.Motor.Barramento.Drenar();

    [When(@"o pagador varre os vencidos")]
    public void QuandoVarreVencidos() => _contexto.Motor.Pagador.ExpirarVencidos();

    [When(@"o pagador consulta o status da transação")]
    public void QuandoConsultaStatus() => _contexto.Motor.Pagador.ConsultarStatus(_contexto.E2E);

    [Then(@"a resposta tem estado ""([^""]*)""")]
    public void EntaoRespostaTemEstado(string estado)
    {
        RespostaDePagamento resposta = _contexto.UltimaResposta
            ?? throw new InvalidOperationException("nenhum pagamento foi feito neste cenário");

        Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(estado), resposta.Estado);
    }

    [Then(@"a resposta não traz motivo de rejeição")]
    public void EntaoRespostaSemMotivo()
    {
        RespostaDePagamento resposta = _contexto.UltimaResposta
            ?? throw new InvalidOperationException("nenhum pagamento foi feito neste cenário");

        Assert.Null(resposta.Motivo);
    }

    [Then(@"a resposta traz motivo ""([^""]*)""")]
    public void EntaoRespostaComMotivo(string motivo)
    {
        RespostaDePagamento resposta = _contexto.UltimaResposta
            ?? throw new InvalidOperationException("nenhum pagamento foi feito neste cenário");

        Assert.Equal(Vocabulario.Enumerado<MotivoRejeicaoLocal>(motivo), resposta.Motivo);
    }

    // "mensagem" -> "mensagens": o plural troca o m final por ns, entao 'mensagens?' nao casa o
    // singular. Grupo nao-capturante de proposito — um grupo a mais aqui viraria um parametro a
    // mais no metodo, e o passo pararia de casar por aridade.
    [Then(@"há (\d+) mensage(?:m|ns) pendentes? no barramento")]
    public void EntaoPendentes(int quantas) =>
        Assert.Equal(quantas, _contexto.Motor.Barramento.Pendentes);

    [Then(@"não há mensagem pendente no barramento")]
    public void EntaoSemPendentes() =>
        Assert.Equal(0, _contexto.Motor.Barramento.Pendentes);

    [Then(@"o barramento entrega (\d+) mensagens ao ser drenado")]
    public void EntaoEntrega(int quantas) =>
        Assert.Equal(quantas, _contexto.Motor.Barramento.Drenar());
}
