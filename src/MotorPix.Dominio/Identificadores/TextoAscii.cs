namespace MotorPix.Dominio.Identificadores;

/// <summary>
/// Predicados de caractere restritos a ASCII.
/// <para>
/// Existe porque <see cref="char.IsDigit(char)"/> e <see cref="char.IsLetterOrDigit(char)"/>
/// aceitam digitos e letras Unicode: <c>char.IsDigit('٣')</c> — digito arabico-indico — e
/// verdadeiro. Um ISPB com esses caracteres passaria na validacao, quebraria a concatenacao do
/// EndToEndId e produziria uma chave de idempotencia que nenhum outro sistema reproduz.
/// </para>
/// </summary>
internal static class TextoAscii
{
    internal static bool EhDigito(char c) => c is >= '0' and <= '9';

    internal static bool EhLetra(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    internal static bool EhAlfanumerico(char c) => EhDigito(c) || EhLetra(c);

    internal static bool TodosDigitos(ReadOnlySpan<char> texto)
    {
        foreach (char c in texto)
        {
            if (!EhDigito(c))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TodosAlfanumericos(ReadOnlySpan<char> texto)
    {
        foreach (char c in texto)
        {
            if (!EhAlfanumerico(c))
            {
                return false;
            }
        }

        return true;
    }
}
