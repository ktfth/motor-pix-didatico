using MotorPix.Especificacoes.Suporte;
using MotorPix.Psp.Nucleo;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// Asserções sobre a maquina de estados: em que estado a transacao esta e por quais transicoes ela
/// passou.
/// <para>
/// A tabela do historico e comparada na ordem e no tamanho exato. Conferir apenas o estado final
/// deixaria passar um caminho que chega ao mesmo lugar por uma aresta que o diagrama nao tem.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDeTransacao
{
    private readonly ContextoDoMotor _contexto;

    public PassosDeTransacao(ContextoDoMotor contexto) => _contexto = contexto;

    [Then(@"a transação está em ""([^""]*)""")]
    public void EntaoTransacaoEstaEm(string estado)
    {
        VistaDaTransacao transacao = _contexto.TransacaoDoPagador(_contexto.E2E);

        Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(estado), transacao.Estado);
    }

    [Then(@"o pagamento ""([^""]*)"" está em ""([^""]*)""")]
    public void EntaoPagamentoApelidadoEstaEm(string apelido, string estado)
    {
        VistaDaTransacao transacao = _contexto.TransacaoDoPagador(_contexto.E2eDe(apelido));

        Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(estado), transacao.Estado);
    }

    [Then(@"o histórico da transação é exatamente:")]
    public void EntaoHistoricoExatamente(Table tabela)
    {
        ArgumentNullException.ThrowIfNull(tabela);

        VistaDaTransacao transacao = _contexto.TransacaoDoPagador(_contexto.E2E);

        Assert.Equal(tabela.RowCount, transacao.Historico.Count);

        for (int i = 0; i < tabela.RowCount; i++)
        {
            TableRow linha = tabela.Rows[i];
            TransicaoAplicada aplicada = transacao.Historico[i];

            Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(linha["origem"]), aplicada.Origem);
            Assert.Equal(Vocabulario.Enumerado<TipoEvento>(linha["evento"]), aplicada.Evento);
            Assert.Equal(Vocabulario.Enumerado<EstadoTransacao>(linha["destino"]), aplicada.Destino);
        }
    }

    [Then(@"a transação não é conhecida pelo pagador")]
    public void EntaoNaoConhecida() =>
        Assert.False(_contexto.Motor.Pagador.TryTransacao(_contexto.E2E, out _));
}
