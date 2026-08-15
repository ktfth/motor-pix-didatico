using MotorPix.Dominio.Excecoes;

namespace MotorPix.Dominio.Identificadores;

public enum TipoChave
{
    Cpf = 1,
    Cnpj = 2,
    Email = 3,
    Telefone = 4,
    Aleatoria = 5,
}

/// <summary>
/// Chave de enderecamento do DICT, como sum type fechado.
/// <para>
/// A normalizacao mora <em>dentro</em> do value object: se <c>"123.456.789-09"</c> e
/// <c>"12345678909"</c> nao colapsarem no mesmo valor canonico, a resolucao de chave fica intermitente e o
/// bug aparece como "as vezes o DICT nao acha".
/// </para>
/// <para>
/// O tipo vem explicito de quem construiu, nunca inferido do texto: <c>"12345678909"</c> e
/// ambiguo entre CPF e telefone, e adivinhar introduziria regra que nenhum documento autoriza.
/// </para>
/// </summary>
public abstract record ChavePix
{
    private protected ChavePix(string texto) => Texto = texto;

    /// <summary>Forma canonica. Duas chaves iguais tem exatamente o mesmo texto aqui.</summary>
    public string Texto { get; }

    public abstract TipoChave Tipo { get; }

    public static ChavePix Criar(TipoChave tipo, string? bruto) => tipo switch
    {
        TipoChave.Cpf => ChaveCpf.Criar(bruto),
        TipoChave.Cnpj => ChaveCnpj.Criar(bruto),
        TipoChave.Email => ChaveEmail.Criar(bruto),
        TipoChave.Telefone => ChaveTelefone.Criar(bruto),
        TipoChave.Aleatoria => ChaveAleatoria.Criar(bruto),
        _ => throw new ChavePixInvalidaException(bruto, $"tipo de chave desconhecido: {tipo}"),
    };

    public static bool TryCriar(TipoChave tipo, string? bruto, out ChavePix? chave)
    {
        try
        {
            chave = Criar(tipo, bruto);
            return true;
        }
        catch (ChavePixInvalidaException)
        {
            chave = null;
            return false;
        }
    }

    /// <summary>
    /// Selado: sem o <c>sealed</c>, cada record derivado sintetiza o proprio <c>ToString</c> e este
    /// formato canonico nunca chega a ser usado.
    /// </summary>
    public sealed override string ToString() => $"{Tipo}:{Texto}";

    /// <summary>
    /// Remove apenas pontuacao conhecida e exige que todo o resto seja digito.
    /// <para>
    /// Descartar qualquer nao-digito faria <c>"cpf 529.982.247-25 ok"</c> ser aceito e resolver
    /// para a mesma entrada do DICT que o CPF limpo — dois textos distintos colapsando na mesma
    /// chave de enderecamento, que e como dinheiro vai para a conta errada.
    /// </para>
    /// </summary>
    /// <param name="bruto">Trecho a normalizar.</param>
    /// <param name="rotulo">Nome do tipo, para a mensagem de erro.</param>
    /// <param name="original">
    /// Texto que o chamador passou, reportado na excecao. Existe porque o telefone normaliza
    /// <c>texto[1..]</c>, e sem este parametro a excecao diria "ab12345678" para quem escreveu
    /// "+ab12345678" — o diagnostico apontaria um valor que ninguem digitou.
    /// </param>
    private protected static string SomenteDigitos(string bruto, string rotulo, string? original = null)
    {
        Span<char> destino = bruto.Length <= 64 ? stackalloc char[bruto.Length] : new char[bruto.Length];
        int escritos = 0;

        foreach (char c in bruto)
        {
            if (TextoAscii.EhDigito(c))
            {
                destino[escritos++] = c;
            }
            else if (!EhPontuacaoAceita(c))
            {
                throw new ChavePixInvalidaException(original ?? bruto, $"{rotulo} com caractere invalido '{c}'");
            }
        }

        return new string(destino[..escritos]);
    }

    private static bool EhPontuacaoAceita(char c) => c is '.' or '-' or '/' or '(' or ')' or ' ';
}

public sealed record ChaveCpf : ChavePix
{
    private const int Digitos = 11;

    private ChaveCpf(string texto) : base(texto)
    {
    }

    public override TipoChave Tipo => TipoChave.Cpf;

    internal static ChaveCpf Criar(string? bruto)
    {
        if (bruto is null)
        {
            throw new ChavePixInvalidaException(bruto, "CPF nulo");
        }

        string digitos = SomenteDigitos(bruto, "CPF");
        if (digitos.Length != Digitos)
        {
            throw new ChavePixInvalidaException(bruto, $"CPF com {digitos.Length} digitos, esperado {Digitos}");
        }

        if (!Modulo11.CpfValido(digitos))
        {
            throw new ChavePixInvalidaException(bruto, "digito verificador de CPF invalido");
        }

        return new ChaveCpf(digitos);
    }
}

public sealed record ChaveCnpj : ChavePix
{
    private const int Digitos = 14;

    private ChaveCnpj(string texto) : base(texto)
    {
    }

    public override TipoChave Tipo => TipoChave.Cnpj;

    internal static ChaveCnpj Criar(string? bruto)
    {
        if (bruto is null)
        {
            throw new ChavePixInvalidaException(bruto, "CNPJ nulo");
        }

        string digitos = SomenteDigitos(bruto, "CNPJ");
        if (digitos.Length != Digitos)
        {
            throw new ChavePixInvalidaException(bruto, $"CNPJ com {digitos.Length} digitos, esperado {Digitos}");
        }

        if (!Modulo11.CnpjValido(digitos))
        {
            throw new ChavePixInvalidaException(bruto, "digito verificador de CNPJ invalido");
        }

        return new ChaveCnpj(digitos);
    }
}

public sealed record ChaveEmail : ChavePix
{
    private const int ComprimentoMaximo = 77;

    private ChaveEmail(string texto) : base(texto)
    {
    }

    public override TipoChave Tipo => TipoChave.Email;

    internal static ChaveEmail Criar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto))
        {
            throw new ChavePixInvalidaException(bruto, "e-mail vazio");
        }

        string normalizado = bruto.Trim().ToLowerInvariant();
        if (normalizado.Length > ComprimentoMaximo)
        {
            throw new ChavePixInvalidaException(bruto, $"e-mail acima de {ComprimentoMaximo} caracteres");
        }

        int arroba = normalizado.IndexOf('@', StringComparison.Ordinal);
        if (arroba <= 0
            || arroba != normalizado.LastIndexOf('@')
            || arroba == normalizado.Length - 1
            || normalizado.Contains(' ', StringComparison.Ordinal))
        {
            throw new ChavePixInvalidaException(bruto, "forma de e-mail invalida");
        }

        string dominio = normalizado[(arroba + 1)..];
        if (!dominio.Contains('.', StringComparison.Ordinal) || dominio.StartsWith('.') || dominio.EndsWith('.'))
        {
            throw new ChavePixInvalidaException(bruto, "dominio de e-mail invalido");
        }

        return new ChaveEmail(normalizado);
    }
}

public sealed record ChaveTelefone : ChavePix
{
    private const int DigitosMinimos = 8;
    private const int DigitosMaximos = 15;

    private ChaveTelefone(string texto) : base(texto)
    {
    }

    public override TipoChave Tipo => TipoChave.Telefone;

    /// <summary>
    /// Normaliza para E.164 (<c>+</c> seguido de digitos). O DDI nao e inferido: um numero sem
    /// <c>+</c> e recusado, porque assumir <c>+55</c> seria inventar regra de negocio.
    /// </summary>
    internal static ChaveTelefone Criar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto))
        {
            throw new ChavePixInvalidaException(bruto, "telefone vazio");
        }

        string texto = bruto.Trim();
        if (texto[0] != '+')
        {
            throw new ChavePixInvalidaException(bruto, "telefone deve estar em E.164 e comecar com '+'");
        }

        // O '+' inicial ja foi consumido acima; o resto tem de ser digito ou pontuacao conhecida.
        // Sem isso, "+ab12345678" normalizaria silenciosamente para "+12345678".
        string digitos = SomenteDigitos(texto[1..], "telefone", bruto);
        if (digitos.Length is < DigitosMinimos or > DigitosMaximos)
        {
            throw new ChavePixInvalidaException(
                bruto,
                $"telefone com {digitos.Length} digitos, esperado entre {DigitosMinimos} e {DigitosMaximos}");
        }

        return new ChaveTelefone("+" + digitos);
    }
}

public sealed record ChaveAleatoria : ChavePix
{
    private ChaveAleatoria(string texto) : base(texto)
    {
    }

    public override TipoChave Tipo => TipoChave.Aleatoria;

    internal static ChaveAleatoria Criar(string? bruto)
    {
        if (string.IsNullOrWhiteSpace(bruto))
        {
            throw new ChavePixInvalidaException(bruto, "chave aleatoria vazia");
        }

        if (!Guid.TryParseExact(bruto.Trim(), "D", out Guid identificador))
        {
            throw new ChavePixInvalidaException(bruto, "chave aleatoria nao e um GUID no formato 'D'");
        }

        return new ChaveAleatoria(identificador.ToString("D"));
    }
}
