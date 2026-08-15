using System.Reflection;
using MotorPix.Dominio.Contabilidade;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Valores;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// P0, a propriedade estrutural: a <em>forma</em> do lancamento de partidas dobradas.
/// <para>
/// Nao ha ledger aqui de proposito. O que se testa e o que o tipo aceita construir — se um
/// lancamento malformado nascesse, toda guarda a jusante ja estaria defendendo o indefensavel.
/// </para>
/// </summary>
public sealed class LancamentoTestes
{
    private const string HistoricoPadrao = "debito do cliente pagador";

    private static readonly Ispb IspbPagador = Ispb.Criar("11111111");
    private static readonly Ispb IspbRecebedor = Ispb.Criar("22222222");
    private static readonly LedgerId LedgerDoPagador = LedgerId.Psp(IspbPagador);
    private static readonly LedgerId LedgerDoRecebedor = LedgerId.Psp(IspbRecebedor);
    private static readonly ContaId ContaCliente = ContaId.Cliente(LedgerDoPagador, "0001");
    private static readonly ContaId ContaEspelho = ContaId.EspelhoPi(LedgerDoPagador);
    private static readonly ChaveIdempotencia ChavePadrao = ChaveIdempotencia.Genesis(ContaCliente);

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComPernasValidas_ProduzExatamenteDuasPartidasDebitoDepoisCredito()
    {
        Valor valor = Valor.DeCentavos(12_34);

        Lancamento lancamento = Lancamento.Criar(ContaCliente, ContaEspelho, valor, ChavePadrao, HistoricoPadrao);

        Assert.Collection(
            lancamento.Partidas,
            partida =>
            {
                Assert.Equal(ContaCliente, partida.Conta);
                Assert.Equal(Lado.Debito, partida.Lado);
                Assert.Equal(valor, partida.Valor);
            },
            partida =>
            {
                Assert.Equal(ContaEspelho, partida.Conta);
                Assert.Equal(Lado.Credito, partida.Lado);
                Assert.Equal(valor, partida.Valor);
            });
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(12_34L)]
    [InlineData(1_000_00L)]
    [InlineData(long.MaxValue)]
    [Trait("gate", "1")]
    public void Criar_ComQualquerValorPositivo_TemUmaPernaPorLadoComOMesmoValorEContasDistintas(long centavos)
    {
        Valor valor = Valor.DeCentavos(centavos);

        Lancamento lancamento = Lancamento.Criar(ContaCliente, ContaEspelho, valor, ChavePadrao, HistoricoPadrao);

        Partida debito = Assert.Single(lancamento.Partidas, partida => partida.Lado == Lado.Debito);
        Partida credito = Assert.Single(lancamento.Partidas, partida => partida.Lado == Lado.Credito);

        Assert.Equal(valor, debito.Valor);
        Assert.Equal(valor, credito.Valor);
        Assert.Equal(debito.Valor, credito.Valor);
        Assert.NotEqual(debito.Conta, credito.Conta);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComPernasValidas_PreservaValorChaveEHistorico()
    {
        Valor valor = Valor.DeReais(150, 25);

        Lancamento lancamento = Lancamento.Criar(ContaCliente, ContaEspelho, valor, ChavePadrao, HistoricoPadrao);

        Assert.Equal(ContaCliente, lancamento.Debito);
        Assert.Equal(ContaEspelho, lancamento.Credito);
        Assert.Equal(valor, lancamento.Valor);
        Assert.Equal(ChavePadrao, lancamento.Chave);
        Assert.Equal(HistoricoPadrao, lancamento.Historico);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComPernasValidas_UsaOLedgerDasContasComoLedgerDoLancamento()
    {
        Lancamento lancamento = Lancamento.Criar(
            ContaCliente,
            ContaEspelho,
            Valor.DeCentavos(1_00),
            ChavePadrao,
            HistoricoPadrao);

        Assert.Equal(LedgerDoPagador, lancamento.Ledger);
        Assert.Equal(lancamento.Debito.Ledger, lancamento.Ledger);
        Assert.Equal(lancamento.Credito.Ledger, lancamento.Ledger);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComValorDefault_LancaValorInvalidoException()
    {
        // 'default(Valor)' vale zero centavos e a struct nao consegue impedir a propria construcao
        // default: quem barra e a fronteira que a consome. Zero passaria por "balanceado" — as duas
        // pernas seriam iguais — e poluiria o log com um ato sem efeito patrimonial.
        Valor valorDefault = default;

        ValorInvalidoException excecao = Assert.Throws<ValorInvalidoException>(
            () => Lancamento.Criar(ContaCliente, ContaEspelho, valorDefault, ChavePadrao, HistoricoPadrao));

        Assert.Equal(0L, excecao.Centavos);
        Assert.False(valorDefault.EhPositivo);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComDebitoECreditoNaMesmaConta_LancaLancamentoDesbalanceadoException()
    {
        LancamentoDesbalanceadoException excecao = Assert.Throws<LancamentoDesbalanceadoException>(
            () => Lancamento.Criar(
                ContaCliente,
                ContaCliente,
                Valor.DeCentavos(10_00),
                ChavePadrao,
                HistoricoPadrao));

        Assert.Contains(ContaCliente.ToString(), excecao.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComPernasEmLedgersDiferentes_LancaLancamentoEntreLedgersException()
    {
        // Por que esta guarda existe, se a contabilidade "fecha": debito e credito batem pelo mesmo
        // valor e a soma global do sistema continua exatamente constante. A propriedade de soma
        // sozinha, portanto, NAO pegaria este caso — seria dinheiro atravessando a fronteira de um
        // participante sem passar pelo SPI. So o ledger carregado dentro do ContaId torna o defeito
        // detectavel.
        ContaId clienteDoRecebedor = ContaId.Cliente(LedgerDoRecebedor, "0002");

        LancamentoEntreLedgersException excecao = Assert.Throws<LancamentoEntreLedgersException>(
            () => Lancamento.Criar(
                ContaCliente,
                clienteDoRecebedor,
                Valor.DeCentavos(10_00),
                ChavePadrao,
                HistoricoPadrao));

        Assert.Equal(ContaCliente, excecao.Debito);
        Assert.Equal(clienteDoRecebedor, excecao.Credito);
        Assert.NotEqual(excecao.Debito.Ledger, excecao.Credito.Ledger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    [Trait("gate", "1")]
    public void Criar_ComHistoricoAusenteOuEmBranco_LancaLancamentoDesbalanceadoException(string? historico)
    {
        Assert.Throws<LancamentoDesbalanceadoException>(
            () => Lancamento.Criar(
                ContaCliente,
                ContaEspelho,
                Valor.DeCentavos(10_00),
                ChavePadrao,
                historico!));
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComDebitoNulo_LancaArgumentNullException()
    {
        ContaId? debito = null;

        ArgumentNullException excecao = Assert.Throws<ArgumentNullException>(
            () => Lancamento.Criar(debito!, ContaEspelho, Valor.DeCentavos(10_00), ChavePadrao, HistoricoPadrao));

        Assert.Equal("debito", excecao.ParamName);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComCreditoNulo_LancaArgumentNullException()
    {
        ContaId? credito = null;

        ArgumentNullException excecao = Assert.Throws<ArgumentNullException>(
            () => Lancamento.Criar(ContaCliente, credito!, Valor.DeCentavos(10_00), ChavePadrao, HistoricoPadrao));

        Assert.Equal("credito", excecao.ParamName);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Criar_ComChaveNula_LancaArgumentNullException()
    {
        ChaveIdempotencia? chave = null;

        ArgumentNullException excecao = Assert.Throws<ArgumentNullException>(
            () => Lancamento.Criar(ContaCliente, ContaEspelho, Valor.DeCentavos(10_00), chave!, HistoricoPadrao));

        Assert.Equal("chave", excecao.ParamName);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Lancamento_NaoExpoeNenhumaPropriedadeComSetterPublicoNemInit()
    {
        // Nao existe API para alterar nem remover um lancamento (invariante 2): correcao e estorno
        // sao lancamentos novos. Para a reflexao, 'init' tambem e um set publico — barrar SetMethod
        // publico cobre 'set' e 'init' de uma vez, e com eles o 'with { Valor = ... }' que seria
        // um UPDATE disfarcado de expressao imutavel.
        PropertyInfo[] propriedades = typeof(Lancamento).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        Assert.NotEmpty(propriedades);
        Assert.DoesNotContain(propriedades, propriedade => propriedade.SetMethod is not null && propriedade.SetMethod.IsPublic);
    }

    [Theory]
    [InlineData("Alterar")]
    [InlineData("Atualizar")]
    [InlineData("Remover")]
    [InlineData("Excluir")]
    [InlineData("Update")]
    [InlineData("Delete")]
    [Trait("gate", "1")]
    public void Lancamento_NaoExpoeMetodoDeAlteracaoOuRemocao(string proibido)
    {
        IEnumerable<string> nomes = typeof(Lancamento)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(metodo => metodo.Name);

        Assert.DoesNotContain(nomes, nome => nome.Contains(proibido, StringComparison.OrdinalIgnoreCase));
    }
}
