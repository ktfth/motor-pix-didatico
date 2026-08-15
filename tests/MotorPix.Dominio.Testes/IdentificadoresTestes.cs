using System.Reflection;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;
using MotorPix.Testes.Comum;

namespace MotorPix.Dominio.Testes;

/// <summary>
/// Forma e identidade dos tres identificadores estruturais do kernel contabil: <see cref="Ispb"/>,
/// <see cref="LedgerId"/> e <see cref="ContaId"/>.
/// <para>
/// Dois testes aqui sustentam invariantes maiores: o ISPB nao-ASCII (que <c>char.IsDigit</c>
/// aceitaria) protege a concatenacao do EndToEndId, e a distincao de <see cref="ContaId"/> por
/// ledger e o que torna "lancamento entre ledgers" detectavel.
/// </para>
/// </summary>
public sealed class IdentificadoresTestes
{
    private const string TextoIspbPagador = "11111111";
    private const string TextoIspbRecebedor = "22222222";

    // ------------------------------------------------------------------ Ispb

    [Fact]
    [Trait("gate", "1")]
    public void Ispb_Criar_ComOitoZeros_EhValidoEPreservaOsZerosAEsquerda()
    {
        Ispb ispb = Ispb.Criar("00000000");

        Assert.Equal("00000000", ispb.Texto);
        Assert.Equal("00000000", ispb.ToString());
        Assert.Equal(8, ispb.Texto.Length);
        Assert.Equal(8, Ispb.Comprimento);
    }

    [Theory]
    [InlineData("1234567")] // 7 digitos
    [InlineData("123456789")] // 9 digitos
    [InlineData("")]
    [Trait("gate", "1")]
    public void Ispb_Criar_ComComprimentoDiferenteDeOito_Lanca(string bruto)
    {
        IspbInvalidoException erro = Assert.Throws<IspbInvalidoException>(() => Ispb.Criar(bruto));

        Assert.Equal("Ispb", erro.Tipo);
        Assert.Equal(bruto, erro.Bruto);
        Assert.Contains("comprimento", erro.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Ispb_Criar_ComTextoNulo_Lanca()
    {
        IspbInvalidoException erro = Assert.Throws<IspbInvalidoException>(() => Ispb.Criar(null));

        Assert.Equal("nulo", erro.Motivo);
        Assert.Null(erro.Bruto);
    }

    [Theory]
    [InlineData("1234567a", 7)]
    [InlineData("a1234567", 0)]
    [InlineData("1234-567", 4)]
    [Trait("gate", "1")]
    public void Ispb_Criar_ComCaractereNaoNumerico_Lanca(string bruto, int posicao)
    {
        IspbInvalidoException erro = Assert.Throws<IspbInvalidoException>(() => Ispb.Criar(bruto));

        Assert.Equal(8, bruto.Length);
        Assert.Equal(bruto, erro.Bruto);
        Assert.Contains($"posicao {posicao}", erro.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Ispb_Criar_ComDigitoArabicoIndico_Lanca()
    {
        const char digitoArabicoIndico = '\u0663'; // 3 em arabico-indico (U+0663)

        // Oraculo independente do dominio: o BCL classifica este caractere como digito. E por isso
        // que a validacao nao pode usar char.IsDigit - um ISPB assim quebraria a concatenacao do
        // EndToEndId e produziria chave de idempotencia que nenhum outro sistema reproduz.
        Assert.True(char.IsDigit(digitoArabicoIndico));

        string bruto = "1234567" + digitoArabicoIndico;
        Assert.Equal(8, bruto.Length);

        IspbInvalidoException erro = Assert.Throws<IspbInvalidoException>(() => Ispb.Criar(bruto));

        Assert.Contains("posicao 7", erro.Motivo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234567a")]
    [Trait("gate", "1")]
    public void Ispb_TryCriar_ComTextoInvalido_DevolveFalsoENuloSemLancar(string? bruto)
    {
        bool aceito = Ispb.TryCriar(bruto, out Ispb? ispb);

        Assert.False(aceito);
        Assert.Null(ispb);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Ispb_TryCriar_ComTextoValido_DevolveVerdadeiroEOIdentificador()
    {
        bool aceito = Ispb.TryCriar("00000000", out Ispb? ispb);

        Assert.True(aceito);
        Assert.NotNull(ispb);
        Assert.Equal("00000000", ispb?.Texto);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Ispb_Igualdade_PorTextoOrdinal_ColideNoDicionario()
    {
        Ispb primeiro = Ispb.Criar(TextoIspbPagador);
        Ispb segundo = Ispb.Criar(TextoIspbPagador);
        Ispb outro = Ispb.Criar(TextoIspbRecebedor);

        Assert.NotSame(primeiro, segundo);
        Assert.Equal(primeiro, segundo);
        Assert.Equal(primeiro.GetHashCode(), segundo.GetHashCode());
        Assert.NotEqual(primeiro, outro);

        Dictionary<Ispb, string> participantes = new() { [primeiro] = "pagador" };
        participantes[segundo] = "mesmo participante";
        participantes[outro] = "recebedor";

        Assert.Equal(2, participantes.Count);
        Assert.Equal("mesmo participante", participantes[primeiro]);
    }

    [Fact]
    [Trait("gate", "1")]
    public void Ispb_ComZerosAEsquerda_SobreviveAConcatenacaoNoEndToEndId()
    {
        Ispb ispb = Ispb.Criar("00000000");

        EndToEndId composto = EndToEndId.Compor(ispb, RelogioFake.Epoca, "ABCDEFGHIJK");

        // Se o ISPB fosse guardado como int, os zeros sumiriam e sobrariam 24 caracteres.
        Assert.Equal("E00000000202601011200ABCDEFGHIJK", composto.Texto);
        Assert.Equal(32, composto.Texto.Length);

        EndToEndId devolta = EndToEndId.Criar(composto.Texto);

        Assert.Equal("00000000", devolta.Ispb.Texto);
        Assert.Equal(ispb, devolta.Ispb);
    }

    // ------------------------------------------------------------- LedgerId

    [Fact]
    [Trait("gate", "1")]
    public void LedgerId_Spi_TemValorSpiEEhSpi()
    {
        Assert.Equal("Spi", LedgerId.Spi.Texto);
        Assert.Equal("Spi", LedgerId.Spi.ToString());
        Assert.True(LedgerId.Spi.EhSpi);
    }

    [Fact]
    [Trait("gate", "1")]
    public void LedgerId_Criar_ComTextoSpi_VoltaAoSingletonDoSpi()
    {
        LedgerId reconstruido = LedgerId.Criar("Spi");

        Assert.Same(LedgerId.Spi, reconstruido);
        Assert.Equal(LedgerId.Spi, reconstruido);
        Assert.True(reconstruido.EhSpi);
    }

    [Fact]
    [Trait("gate", "1")]
    public void LedgerId_Psp_ComIspb_ProduzValorComPrefixo()
    {
        LedgerId ledger = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));

        Assert.Equal("Psp:11111111", ledger.Texto);
        Assert.Equal("Psp:11111111", ledger.ToString());
        Assert.False(ledger.EhSpi);
    }

    [Fact]
    [Trait("gate", "1")]
    public void LedgerId_Criar_ComTextoDePsp_ReproduzOMesmoLedger()
    {
        LedgerId porFabrica = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));
        LedgerId porTexto = LedgerId.Criar("Psp:11111111");

        Assert.Equal(porFabrica, porTexto);
        Assert.Equal(porFabrica.GetHashCode(), porTexto.GetHashCode());
        Assert.False(porTexto.EhSpi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("spi")] // caixa errada
    [InlineData("SPI")]
    [InlineData(" Spi")]
    [InlineData("Psp")]
    [InlineData("Psp11111111")] // sem os dois pontos
    [InlineData("psp:11111111")] // prefixo em caixa errada
    [InlineData("Spi:11111111")]
    [Trait("gate", "1")]
    public void LedgerId_Criar_ComTextoInvalido_LancaLedgerIdInvalido(string? bruto)
    {
        LedgerIdInvalidoException erro =
            Assert.Throws<LedgerIdInvalidoException>(() => LedgerId.Criar(bruto));

        Assert.Equal("LedgerId", erro.Tipo);
        Assert.Equal(bruto, erro.Bruto);
    }

    [Theory]
    [InlineData("Psp:")]
    [InlineData("Psp:1234567")]
    [InlineData("Psp:1234567a")]
    [Trait("gate", "1")]
    public void LedgerId_Criar_ComIspbMalFormadoNoPsp_LancaLedgerIdInvalidoComOTextoInteiro(string bruto)
    {
        // Quem pediu um LedgerId tem de receber LedgerIdInvalidoException. Vazar a
        // IspbInvalidoException de dentro entregava ao chamador um Bruto que e so um pedaco do
        // texto que ele passou ("1234567a" em vez de "Psp:1234567a"): o log da falha nao permitia
        // reproduzir a entrada, e o catch por tipo pegava a excecao de outro identificador.
        LedgerIdInvalidoException erro =
            Assert.Throws<LedgerIdInvalidoException>(() => LedgerId.Criar(bruto));

        Assert.Equal("LedgerId", erro.Tipo);
        Assert.Equal("ISPB embutido invalido", erro.Motivo);
        Assert.Equal(bruto, erro.Bruto);

        // A familia continua uma so: quem quer apenas "identificador mal formado" segue com um catch.
        Assert.IsAssignableFrom<IdentificadorInvalidoException>(erro);
    }

    [Fact]
    [Trait("gate", "1")]
    public void LedgerId_DeDoisPspsDiferentes_NaoSaoIguais()
    {
        LedgerId pagador = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));
        LedgerId recebedor = LedgerId.Psp(Ispb.Criar(TextoIspbRecebedor));

        Assert.NotEqual(pagador, recebedor);
        Assert.NotEqual(pagador, LedgerId.Spi);
        Assert.False(pagador.EhSpi);
        Assert.False(recebedor.EhSpi);

        HashSet<LedgerId> ledgers = new();

        Assert.True(ledgers.Add(pagador));
        Assert.True(ledgers.Add(recebedor));
        Assert.True(ledgers.Add(LedgerId.Spi));
        Assert.Equal(3, ledgers.Count);
    }

    // -------------------------------------------------------------- ContaId

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_Cliente_CarregaOLedgerAClasseEAChave()
    {
        LedgerId ledger = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));

        ContaId conta = ContaId.Cliente(ledger, "0001");

        Assert.Equal(ledger, conta.Ledger);
        Assert.Same(ledger, conta.Ledger);
        Assert.Equal(ClasseConta.Cliente, conta.Classe);
        Assert.Equal("CLIENTE:0001", conta.Chave);
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_Fabricas_ProduzemAsChavesEAsClassesDoPlanoDeContas()
    {
        LedgerId ledger = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));

        Assert.Equal("CLIENTE:0001", ContaId.Cliente(ledger, "0001").Chave);
        Assert.Equal("ESPELHO_PI", ContaId.EspelhoPi(ledger).Chave);
        Assert.Equal("PI:11111111", ContaId.Pi(Ispb.Criar(TextoIspbPagador)).Chave);
        Assert.Equal("ABERTURA", ContaId.Abertura(ledger).Chave);

        // A classe viaja dentro do identificador, e nao como parametro de quem registra a conta:
        // e dela que a natureza contabil e derivada. Registrar a conta PI como ativo — erro
        // plausivel, ja que "PI" e intuitivamente o que o participante tem — inverteria a guarda
        // de nao-negatividade exatamente na conta em que o invariante 8 mora.
        Assert.Equal(ClasseConta.Cliente, ContaId.Cliente(ledger, "0001").Classe);
        Assert.Equal(ClasseConta.EspelhoPi, ContaId.EspelhoPi(ledger).Classe);
        Assert.Equal(ClasseConta.Pi, ContaId.Pi(Ispb.Criar(TextoIspbPagador)).Classe);
        Assert.Equal(ClasseConta.Abertura, ContaId.Abertura(ledger).Classe);

        Assert.Equal("ESPELHO_PI", ContaId.ChaveEspelhoPi);
        Assert.Equal("ABERTURA", ContaId.ChaveAbertura);
        Assert.Equal("CLIENTE:", ContaId.PrefixoCliente);
        Assert.Equal("PI:", ContaId.PrefixoPi);
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_NaoExpoeFabricaGenericaQuePermitaEscolherAClasse()
    {
        // A unica forma de obter um ContaId e por uma das quatro fabricas nomeadas. Uma
        // ContaId.Criar(ledger, chave) generica deixaria a classe — e portanto a natureza contabil
        // — a cargo de quem chama, que e exatamente a decisao que o tipo existe para tirar dele.
        IEnumerable<string> fabricas = typeof(ContaId)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(metodo => metodo.Name);

        Assert.DoesNotContain(fabricas, nome => string.Equals(nome, "Criar", StringComparison.Ordinal));

        foreach (string nomeada in new[] { "Cliente", "EspelhoPi", "Pi", "Abertura" })
        {
            Assert.Contains(fabricas, nome => string.Equals(nome, nomeada, StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_Pi_CaiSempreNoLedgerDoSpiMesmoPartindoDeUmPsp()
    {
        Ispb ispb = Ispb.Criar(TextoIspbPagador);
        LedgerId ledgerDoPsp = LedgerId.Psp(ispb);

        ContaId pi = ContaId.Pi(ispb);

        Assert.Equal(LedgerId.Spi, pi.Ledger);
        Assert.True(pi.Ledger.EhSpi);
        Assert.NotEqual(ledgerDoPsp, pi.Ledger);

        // A conta PI e do SPI; o que o PSP tem no proprio ledger e o espelho, outra conta.
        Assert.NotEqual(pi, ContaId.EspelhoPi(ledgerDoPsp));
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_MesmaChaveEmLedgersDiferentes_NaoSaoIguaisNemColidemNoDicionario()
    {
        LedgerId pagador = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));
        LedgerId recebedor = LedgerId.Psp(Ispb.Criar(TextoIspbRecebedor));

        ContaId clienteNoPagador = ContaId.Cliente(pagador, "0001");
        ContaId clienteNoRecebedor = ContaId.Cliente(recebedor, "0001");

        // Mesma chave, ledgers distintos: e o ledger que separa os dois espacos de contas.
        Assert.Equal(clienteNoPagador.Chave, clienteNoRecebedor.Chave);
        Assert.NotEqual(clienteNoPagador, clienteNoRecebedor);

        Dictionary<ContaId, string> saldos = new()
        {
            [clienteNoPagador] = "pagador",
            [clienteNoRecebedor] = "recebedor",
        };

        Assert.Equal(2, saldos.Count);
        Assert.Equal("pagador", saldos[clienteNoPagador]);
        Assert.Equal("recebedor", saldos[clienteNoRecebedor]);
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_EspelhoPiDeDoisPsps_SaoContasDiferentes()
    {
        LedgerId pagador = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));
        LedgerId recebedor = LedgerId.Psp(Ispb.Criar(TextoIspbRecebedor));

        ContaId espelhoDoPagador = ContaId.EspelhoPi(pagador);
        ContaId espelhoDoRecebedor = ContaId.EspelhoPi(recebedor);

        Assert.NotEqual(espelhoDoPagador, espelhoDoRecebedor);

        HashSet<ContaId> contas = new();

        Assert.True(contas.Add(espelhoDoPagador));
        Assert.True(contas.Add(espelhoDoRecebedor));
        Assert.Equal(2, contas.Count);
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_Igualdade_MesmoLedgerEMesmaChave_SaoIguaisEColidemNoDicionario()
    {
        LedgerId ledger = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));

        ContaId porFabrica = ContaId.Cliente(ledger, "0001");
        ContaId porTexto = ContaId.Cliente(LedgerId.Criar("Psp:11111111"), "0001");

        Assert.NotSame(porFabrica, porTexto);
        Assert.Equal(porFabrica, porTexto);
        Assert.Equal(porFabrica.GetHashCode(), porTexto.GetHashCode());

        Dictionary<ContaId, string> saldos = new() { [porFabrica] = "primeiro" };
        saldos[porTexto] = "segundo";

        Assert.Single(saldos);
        Assert.Equal("segundo", saldos[porFabrica]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [Trait("gate", "1")]
    public void ContaId_Cliente_ComNumeroDeContaNuloVazioOuEmBranco_Lanca(string? numeroDaConta)
    {
        // A validacao roda ANTES da concatenacao do prefixo. Se rodasse depois, "CLIENTE:" + ""
        // passaria e todo cliente sem identificacao compartilharia uma conta-fantasma unica — o
        // credito liquidaria com sucesso, na conta errada.
        ContaIdInvalidoException erro =
            Assert.Throws<ContaIdInvalidoException>(() => ContaId.Cliente(LedgerId.Spi, numeroDaConta));

        Assert.Equal("ContaId", erro.Tipo);
        Assert.Equal("numero de conta do cliente vazio", erro.Motivo);
        Assert.Equal(numeroDaConta, erro.Bruto);
    }

    [Theory]
    [InlineData(" 0001 ")]
    [InlineData(" 0001")]
    [InlineData("0001 ")]
    [InlineData("\t0001")]
    [InlineData("0001\t")]
    [Trait("gate", "1")]
    public void ContaId_Cliente_ComEspacoNasBordas_Lanca(string numeroDaConta)
    {
        // Sem esta validacao, "0001 " e "0001" viram duas contas distintas: as chaves resultantes
        // sao "CLIENTE:0001 " e "CLIENTE:0001", que nao colidem no dicionario de saldos. O extrato
        // do cliente ficaria partido em duas contas que um humano le como a mesma, e metade do
        // dinheiro moraria na que ninguem consulta. Recusar e a unica saida: normalizar em silencio
        // faria a conta gravada divergir do que o chamador pediu.
        ContaIdInvalidoException erro =
            Assert.Throws<ContaIdInvalidoException>(() => ContaId.Cliente(LedgerId.Spi, numeroDaConta));

        Assert.Equal("ContaId", erro.Tipo);
        Assert.Equal("numero de conta com espaco nas bordas", erro.Motivo);
        Assert.Equal(numeroDaConta, erro.Bruto);

        // A versao aparada e aceita, e e a unica forma valida daquele numero de conta.
        Assert.Equal("CLIENTE:0001", ContaId.Cliente(LedgerId.Spi, numeroDaConta.Trim()).Chave);
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_Criar_ComLedgerNulo_LancaArgumentNull()
    {
        ArgumentNullException erro =
            Assert.Throws<ArgumentNullException>(() => ContaId.Cliente(null!, "0001"));

        Assert.Equal("ledger", erro.ParamName);
    }

    [Fact]
    [Trait("gate", "1")]
    public void ContaId_ToString_IncluiLedgerEChave()
    {
        LedgerId ledger = LedgerId.Psp(Ispb.Criar(TextoIspbPagador));

        Assert.Equal("Psp:11111111/CLIENTE:0001", ContaId.Cliente(ledger, "0001").ToString());
        Assert.Equal("Psp:11111111/ESPELHO_PI", ContaId.EspelhoPi(ledger).ToString());
        Assert.Equal("Spi/PI:11111111", ContaId.Pi(Ispb.Criar(TextoIspbPagador)).ToString());
        Assert.Equal("Spi/ABERTURA", ContaId.Abertura(LedgerId.Spi).ToString());
    }
}
