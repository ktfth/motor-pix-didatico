using MotorPix.Contratos;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;
using MotorPix.Especificacoes.Suporte;
using MotorPix.PspPagador;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// O que so o tema do timeout precisa: contar quantas transacoes a varredura alcancou, ler a
/// resposta da consulta de status, e o unico arranjo que os passos existentes nao produzem — um
/// destino que o SPI recusa DEPOIS de o debito ja ter acontecido.
/// <para>
/// Os passos de acao ja existentes bastam para o resto: "o pacs.002 que nunca chega" e a AUSENCIA
/// de "o barramento é drenado" no cenario, e nao um mock. E a entrega da ordem na mao do SPI ja
/// mora em <c>PassosDeConciliacao</c>, entao este arquivo a reusa em vez de dar um segundo nome a
/// mesma acao.
/// </para>
/// </summary>
[Binding]
public sealed class PassosDeTimeoutEConsulta
{
    /// <summary>A mesma conta que <c>ContextoDoMotor.Montar</c> abre no pagador.</summary>
    private const string NumeroDaContaPagador = "0001";

    /// <summary>Numero que o recebedor nunca abriu: o DICT resolve, o ledger dele nao conhece.</summary>
    private const string NumeroDaContaFantasma = "0009";

    private const string EmailDaChaveFantasma = "fantasma@exemplo.com";

    private readonly ContextoDoMotor _contexto;

    public PassosDeTimeoutEConsulta(ContextoDoMotor contexto) => _contexto = contexto;

    /// <summary>
    /// Vinculo bem formado — conta de cliente, no ledger do recebedor — apontando para uma conta que
    /// o recebedor nunca abriu.
    /// <para>
    /// E o jeito de produzir um RJCT do SPI sem tocar em nenhuma guarda local do pagador: o DICT
    /// resolve, o debito acontece, o pacs.008 e despachado, e a recusa so aparece no SPI, que
    /// pergunta ao recebedor antes de liquidar. Sem isso, um cenario de rejeicao pararia em
    /// RECEBIDA -> REJEITADA, sem nunca chegar a ENVIADA_SPI — e portanto sem nunca poder expirar.
    /// </para>
    /// </summary>
    [When(@"o cliente pagador paga ""([^""]*)"" para uma chave cuja conta de destino o recebedor nunca abriu")]
    public void QuandoPagaParaContaQueNaoExiste(string valor)
    {
        ChavePix chave = ChavePix.Criar(TipoChave.Email, EmailDaChaveFantasma);
        ContaId fantasma = ContaId.Cliente(_contexto.Motor.Recebedor.Ledger.Id, NumeroDaContaFantasma);

        // Direto no DICT, e nao por RegistrarChave: o recebedor confere que a conta existe no
        // ledger dele antes de vincular, que e exatamente a guarda que este cenario precisa burlar.
        _contexto.Motor.Dict.Vincular(chave, ContextoDoMotor.IspbRecebedor, fantasma);

        _contexto.UltimaResposta = _contexto.Motor.Pagador.Pagar(new ComandoDePagamento(
            _contexto.E2E,
            NumeroDaContaPagador,
            chave,
            Valor.DeCentavos(Dinheiro.EmCentavos(valor))));
    }

    /// <summary>
    /// Varre e afirma quantas alcancou, no mesmo passo — como "o barramento entrega N mensagens ao
    /// ser drenado".
    /// <para>
    /// A contagem e o que torna a varredura afirmavel como idempotente: se a segunda passada
    /// contasse de novo, "quantas expiraram" viraria funcao de quantas vezes o host varreu.
    /// </para>
    /// </summary>
    // Grupo NAO-capturante no plural: um grupo a mais aqui viraria parametro a mais no metodo, e o
    // passo pararia de casar por aridade.
    [Then(@"a varredura expira (\d+) transaç(?:ão|ões)")]
    public void EntaoAVarreduraExpira(int quantas)
    {
        int alcancadas = _contexto.Motor.Pagador.ExpirarVencidos().Count;

        Assert.True(
            quantas == alcancadas,
            $"varredura de vencidos: esperado {quantas} transacao(oes) expirada(s), observado {alcancadas}");
    }

    /// <summary>
    /// Consulta o SPI e afirma o que ele respondeu. Consultar e o unico jeito de sair de EXPIRADA,
    /// entao o passo tem de ser o mesmo ato que produz o efeito — separar pergunta de resposta faria
    /// o cenario consultar duas vezes e esconder qual das duas transicionou.
    /// </summary>
    [Then(@"a consulta de status responde ""([^""]*)""")]
    public void EntaoAConsultaResponde(string esperado)
    {
        ResultadoConsulta alvo = Vocabulario.Enumerado<ResultadoConsulta>(esperado);
        ResultadoConsulta observado = _contexto.Motor.Pagador.ConsultarStatus(_contexto.E2E);

        Assert.True(
            alvo == observado,
            $"consulta de status: esperado {alvo}, observado {observado}");
    }
}
