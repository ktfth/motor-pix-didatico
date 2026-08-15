using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Identificadores;

/// <summary>
/// Classe da conta no plano de contas. E dela que a natureza contabil e a politica de emissao sao
/// derivadas — nunca de um parametro do chamador.
/// </summary>
public enum ClasseConta
{
    /// <summary>Conta do cliente no ledger de um PSP.</summary>
    Cliente = 1,

    /// <summary>Espelho da conta PI dentro do ledger do PSP.</summary>
    EspelhoPi = 2,

    /// <summary>Conta PI de um participante, no ledger do SPI.</summary>
    Pi = 3,

    /// <summary>Contra-conta de emissao. So pode ser tocada no genesis.</summary>
    Abertura = 4,
}

/// <summary>
/// Endereco de uma conta dentro de um ledger especifico.
/// <para>
/// Sao tres espacos de contas independentes, um por ledger. Carregar o <see cref="LedgerId"/> no
/// proprio identificador e o que torna "lancamento entre ledgers" um erro de dominio detectavel,
/// em vez de um lancamento formalmente valido que move dinheiro entre participantes sem passar
/// pelo SPI.
/// </para>
/// <para>
/// A <see cref="ClasseConta"/> viaja junto com o identificador. Sem isso, a natureza contabil
/// dependeria de quem registrou a conta, e registrar a conta PI como ativo inverteria a guarda de
/// nao-negatividade exatamente na conta em que o invariante 8 mora.
/// </para>
/// </summary>
public sealed record ContaId
{
    public const string ChaveEspelhoPi = "ESPELHO_PI";
    public const string ChaveAbertura = "ABERTURA";
    public const string PrefixoCliente = "CLIENTE:";
    public const string PrefixoPi = "PI:";

    private ContaId(LedgerId ledger, ClasseConta classe, string chave)
    {
        Ledger = ledger;
        Classe = classe;
        Chave = chave;
    }

    public LedgerId Ledger { get; }

    public ClasseConta Classe { get; }

    public string Chave { get; }

    /// <summary>Conta do cliente no ledger de um PSP. Passivo: o PSP deve este saldo ao cliente.</summary>
    public static ContaId Cliente(LedgerId ledger, string? conta)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        // Validar ANTES de concatenar. Se a validacao rodasse sobre a string ja prefixada,
        // "CLIENTE:" + "" passaria, e todo cliente sem identificacao compartilharia uma
        // conta-fantasma unica — o credito liquidaria com sucesso, na conta errada.
        if (string.IsNullOrWhiteSpace(conta))
        {
            throw new ContaIdInvalidoException(conta, "numero de conta do cliente vazio");
        }

        if (conta.Length != conta.Trim().Length)
        {
            throw new ContaIdInvalidoException(conta, "numero de conta com espaco nas bordas");
        }

        return new ContaId(ledger, ClasseConta.Cliente, PrefixoCliente + conta);
    }

    /// <summary>
    /// Espelho da conta PI dentro do ledger do PSP. Ativo: e o que o PSP entende ter no SPI.
    /// A defasagem entre este espelho e a conta PI real e a representacao contabil do dinheiro em
    /// transito, e e o insumo da conciliacao.
    /// </summary>
    public static ContaId EspelhoPi(LedgerId ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return new ContaId(ledger, ClasseConta.EspelhoPi, ChaveEspelhoPi);
    }

    /// <summary>Conta PI de um participante, sempre no ledger do SPI.</summary>
    public static ContaId Pi(Ispb ispb)
    {
        ArgumentNullException.ThrowIfNull(ispb);
        return new ContaId(LedgerId.Spi, ClasseConta.Pi, PrefixoPi + ispb.Texto);
    }

    /// <summary>
    /// Contra-conta de emissao. Debitada no genesis para fixar a constante do pool, e recusada pelo
    /// ledger em qualquer ato posterior.
    /// </summary>
    public static ContaId Abertura(LedgerId ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return new ContaId(ledger, ClasseConta.Abertura, ChaveAbertura);
    }

    public bool Equals(ContaId? outro) =>
        outro is not null
        && Ledger.Equals(outro.Ledger)
        && string.Equals(Chave, outro.Chave, StringComparison.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Ledger, StringComparer.Ordinal.GetHashCode(Chave));

    public override string ToString() => $"{Ledger.Texto}/{Chave}";
}
