namespace MotorPix.Dominio.Identificadores;

/// <summary>
/// Digito verificador de CPF e CNPJ (modulo 11).
/// Entrada e sempre a string ja normalizada para digitos ASCII.
/// </summary>
internal static class Modulo11
{
    internal static bool CpfValido(string digitos)
    {
        if (digitos.Length != 11 || TodosIguais(digitos))
        {
            return false;
        }

        int primeiro = DigitoVerificador(digitos, 9, pesoInicial: 10);
        int segundo = DigitoVerificador(digitos, 10, pesoInicial: 11);

        return digitos[9] - '0' == primeiro && digitos[10] - '0' == segundo;
    }

    internal static bool CnpjValido(string digitos)
    {
        if (digitos.Length != 14 || TodosIguais(digitos))
        {
            return false;
        }

        int primeiro = DigitoVerificadorCnpj(digitos, 12);
        int segundo = DigitoVerificadorCnpj(digitos, 13);

        return digitos[12] - '0' == primeiro && digitos[13] - '0' == segundo;
    }

    /// <summary>
    /// Sequencias como 11111111111 passam na aritmetica do modulo 11 e sao invalidas por convencao.
    /// </summary>
    private static bool TodosIguais(string digitos)
    {
        for (int i = 1; i < digitos.Length; i++)
        {
            if (digitos[i] != digitos[0])
            {
                return false;
            }
        }

        return true;
    }

    private static int DigitoVerificador(string digitos, int quantidade, int pesoInicial)
    {
        int soma = 0;
        int peso = pesoInicial;
        for (int i = 0; i < quantidade; i++)
        {
            soma += (digitos[i] - '0') * peso;
            peso--;
        }

        int resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static int DigitoVerificadorCnpj(string digitos, int quantidade)
    {
        int soma = 0;
        int peso = quantidade == 12 ? 5 : 6;
        for (int i = 0; i < quantidade; i++)
        {
            soma += (digitos[i] - '0') * peso;
            peso = peso == 2 ? 9 : peso - 1;
        }

        int resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
