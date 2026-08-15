using System.Globalization;
using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Identificadores;

/// <summary>
/// Identificador fim-a-fim da transacao: <c>E</c> + ISPB (8 digitos) + <c>yyyyMMddHHmm</c> (12) +
/// 11 alfanumericos, totalizando 32 caracteres.
/// <para>
/// Vem de fora: a seta 1 do diagrama de arquitetura parte do cliente e a maquina de estados fala
/// em "POST pagamento com E2E inedito". Por isso este value object valida <em>forma</em> e nada
/// mais — conferir o ISPB embutido contra o participante pagador, ou o instante embutido contra o
/// <c>IClock</c>, seria inventar comportamento que nenhum documento autoriza.
/// </para>
/// <para>
/// Comparacao ordinal e case-sensitive. Dois E2E que diferem apenas por caixa nos 11 finais sao
/// duas transacoes distintas, nunca um reenvio.
/// </para>
/// </summary>
public sealed record EndToEndId
{
    public const int Comprimento = 32;
    private const int ComprimentoInstante = 12;
    private const int ComprimentoSufixo = 11;
    private const string FormatoInstante = "yyyyMMddHHmm";

    private EndToEndId(string texto, Ispb ispb, DateTimeOffset instanteDeclarado, string sufixo)
    {
        Texto = texto;
        Ispb = ispb;
        InstanteDeclarado = instanteDeclarado;
        Sufixo = sufixo;
    }

    public string Texto { get; }

    public Ispb Ispb { get; }

    /// <summary>
    /// Instante que o emissor gravou no identificador.
    /// <para>
    /// E metadado opaco, nunca fonte de tempo: usa-lo para decidir timeout violaria o invariante 9
    /// e deixaria o vencimento a cargo de quem originou a mensagem.
    /// </para>
    /// </summary>
    public DateTimeOffset InstanteDeclarado { get; }

    public string Sufixo { get; }

    public static EndToEndId Criar(string? bruto)
    {
        if (bruto is null)
        {
            throw new EndToEndIdInvalidoException(bruto, "nulo");
        }

        if (bruto.Length != Comprimento)
        {
            throw new EndToEndIdInvalidoException(bruto, $"comprimento {bruto.Length}, esperado {Comprimento}");
        }

        if (bruto[0] != 'E')
        {
            throw new EndToEndIdInvalidoException(bruto, "nao comeca com 'E' maiusculo");
        }

        string textoIspb = bruto.Substring(1, Ispb.Comprimento);
        if (!Ispb.TryCriar(textoIspb, out Ispb? ispb) || ispb is null)
        {
            throw new EndToEndIdInvalidoException(bruto, "ISPB embutido invalido");
        }

        string textoInstante = bruto.Substring(1 + Ispb.Comprimento, ComprimentoInstante);
        if (!TextoAscii.TodosDigitos(textoInstante))
        {
            throw new EndToEndIdInvalidoException(bruto, "instante embutido nao numerico");
        }

        // AssumeUniversal | AdjustToUniversal: sem isso o fuso da maquina entra na conversao e o
        // mesmo identificador produz instantes diferentes em maquinas diferentes.
        if (!DateTimeOffset.TryParseExact(
                textoInstante,
                FormatoInstante,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset instante))
        {
            throw new EndToEndIdInvalidoException(bruto, "instante embutido nao e uma data valida");
        }

        string sufixo = bruto.Substring(1 + Ispb.Comprimento + ComprimentoInstante, ComprimentoSufixo);
        if (!TextoAscii.TodosAlfanumericos(sufixo))
        {
            throw new EndToEndIdInvalidoException(bruto, "sufixo com caractere nao alfanumerico ASCII");
        }

        return new EndToEndId(bruto, ispb, instante, sufixo);
    }

    public static bool TryCriar(string? bruto, out EndToEndId? id)
    {
        try
        {
            id = Criar(bruto);
            return true;
        }
        catch (EndToEndIdInvalidoException)
        {
            id = null;
            return false;
        }
    }

    public static EndToEndId Compor(Ispb ispb, DateTimeOffset instante, string sufixo)
    {
        ArgumentNullException.ThrowIfNull(ispb);

        if (sufixo is null || sufixo.Length != ComprimentoSufixo)
        {
            throw new EndToEndIdInvalidoException(sufixo, $"sufixo deve ter {ComprimentoSufixo} caracteres");
        }

        string textoInstante = instante.UtcDateTime.ToString(FormatoInstante, CultureInfo.InvariantCulture);
        return Criar($"E{ispb.Texto}{textoInstante}{sufixo}");
    }

    public bool Equals(EndToEndId? outro) =>
        outro is not null && string.Equals(Texto, outro.Texto, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Texto);

    public override string ToString() => Texto;
}
