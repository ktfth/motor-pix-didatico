using System.Globalization;

namespace MotorPix.Especificacoes.Suporte;

/// <summary>
/// Converte o dinheiro escrito no texto do cenario ("R$ 1.000,00") para centavos.
/// <para>
/// Existe por causa do invariante 1. O caminho obvio seria declarar o parametro do passo como
/// <c>decimal</c> e deixar o SpecFlow converter — e isso introduziria <c>decimal</c> no projeto
/// pela porta dos fundos, num assembly onde o <c>BannedApiAnalyzers</c> o proibe. A conversao aqui
/// vai de <see cref="string"/> direto para <see cref="long"/>, sem tipo de ponto flutuante no meio:
/// o texto e lido digito a digito, nao interpretado como numero real e depois arredondado.
/// </para>
/// </summary>
public static class Dinheiro
{
    /// <summary>
    /// Aceita "1.000,00", "1000,00", "R$ 250,00" e "250". Exige exatamente dois digitos de centavos
    /// quando ha virgula: "250,5" e recusado, porque "cinco centavos" e "cinquenta centavos" nao
    /// podem depender de quem le.
    /// </summary>
    public static long EmCentavos(string texto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);

        string limpo = texto.Trim()
            .Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        bool negativo = limpo.StartsWith('-');
        if (negativo)
        {
            limpo = limpo[1..];
        }

        string[] partes = limpo.Split(',');

        if (partes.Length > 2)
        {
            throw new FormatException($"valor monetario invalido: '{texto}'");
        }

        string inteiros = partes[0];
        string centavos = partes.Length == 2 ? partes[1] : "00";

        if (inteiros.Length == 0 || !inteiros.All(char.IsAsciiDigit))
        {
            throw new FormatException($"valor monetario invalido: '{texto}'");
        }

        if (centavos.Length != 2 || !centavos.All(char.IsAsciiDigit))
        {
            throw new FormatException(
                $"'{texto}' precisa de exatamente dois digitos de centavos");
        }

        // checked: o Directory.Build.props liga CheckForOverflowUnderflow, e um cenario com um
        // valor absurdo tem de estourar em vez de dar a volta no long silenciosamente.
        long total = checked((long.Parse(inteiros, CultureInfo.InvariantCulture) * 100)
            + long.Parse(centavos, CultureInfo.InvariantCulture));

        return negativo ? -total : total;
    }

    /// <summary>
    /// Formata centavos como "1.000,00", para mensagens de falha legiveis.
    /// <para>
    /// A cultura e fixada em invariante de proposito. Com <c>N0</c> na cultura corrente, o separador
    /// de milhar muda com a maquina que roda a suite — em pt-BR sai "2.000", em en-US "2,000", em
    /// fr-FR "2 000" com espaco. A mensagem de falha de um cenario nao pode depender do runner.
    /// </para>
    /// </summary>
    public static string Formatar(long centavos)
    {
        long absoluto = Math.Abs(centavos);
        string sinal = centavos < 0 ? "-" : string.Empty;

        string inteiros = (absoluto / 100)
            .ToString("N0", CultureInfo.InvariantCulture)
            .Replace(",", ".", StringComparison.Ordinal);

        return $"{sinal}{inteiros},{absoluto % 100:D2}";
    }
}
