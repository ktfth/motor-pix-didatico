using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Contabilidade;

/// <summary>
/// Plano de contas dos tres ledgers.
/// <para>
/// Os rotulos "cliente para espelho conta PI" no pagador e "espelho conta PI para cliente" no
/// recebedor nao descrevem estruturas diferentes: sao os dois polos do mesmo ledger em ordem
/// debito para credito, invertida porque o dinheiro anda em sentidos opostos nos dois papeis. Um
/// unico tipo de ledger de PSP atende os dois — sem isso, a devolucao nao teria onde lancar.
/// </para>
/// <para>
/// Natureza e politica de abertura vem da <see cref="ClasseConta"/> embutida no
/// <see cref="ContaId"/>; estas fabricas apenas nomeiam as contas.
/// </para>
/// </summary>
public static class PlanoDeContas
{
    /// <summary>Passivo: o PSP deve este saldo ao cliente.</summary>
    public static Conta Cliente(LedgerId ledger, string conta) => Conta.De(ContaId.Cliente(ledger, conta));

    /// <summary>Ativo: o que o PSP entende ter na sua conta PI.</summary>
    public static Conta EspelhoPi(LedgerId ledger) => Conta.De(ContaId.EspelhoPi(ledger));

    /// <summary>
    /// Conta PI de um participante no ledger do SPI. Passivo do SPI perante o participante,
    /// e sem descoberto: e aqui que o invariante 8 vive.
    /// </summary>
    public static Conta Pi(Ispb ispb) => Conta.De(ContaId.Pi(ispb));

    /// <summary>
    /// Contra-conta de abertura do SPI. Ativo, debitada no genesis para fixar a constante do pool,
    /// e recusada pelo ledger em qualquer ato posterior.
    /// </summary>
    public static Conta AberturaSpi() => Conta.De(ContaId.Abertura(LedgerId.Spi));
}
