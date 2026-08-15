namespace MotorPix.Especificacoes.Suporte;

/// <summary>
/// Traduz os nomes que aparecem nos diagramas normativos ("ENVIADA_SPI", "PACS002_ACSC") para os
/// membros de enum correspondentes.
/// <para>
/// A traducao e por nome, com underscores removidos e comparacao sem caixa — nunca por valor
/// numerico. Casar por posicao faria um cenario continuar verde depois de alguem reordenar o enum,
/// e o texto do cenario passaria a afirmar outra coisa sem que uma linha dele mudasse.
/// </para>
/// <para>
/// Nome desconhecido lanca com a lista do que era aceito. Um cenario com estado escrito errado tem
/// de morrer como erro de vocabulario, nao ser silenciosamente ignorado.
/// </para>
/// </summary>
public static class Vocabulario
{
    public static T Enumerado<T>(string nome)
        where T : struct, Enum
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);

        string alvo = Normalizar(nome);

        foreach (T candidato in Enum.GetValues<T>())
        {
            if (Normalizar(candidato.ToString()!).Equals(alvo, StringComparison.Ordinal))
            {
                return candidato;
            }
        }

        string aceitos = string.Join(", ", Enum.GetNames<T>());

        throw new FormatException(
            $"'{nome}' nao e um {typeof(T).Name} conhecido. Aceitos: {aceitos}");
    }

    private static string Normalizar(string texto) =>
        texto.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
}
