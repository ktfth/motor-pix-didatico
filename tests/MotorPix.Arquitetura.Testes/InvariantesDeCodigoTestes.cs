using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Tempo;

namespace MotorPix.Arquitetura.Testes;

/// <summary>
/// Invariantes que valem para o codigo inteiro, e nao para um tipo em particular: dinheiro sem
/// ponto flutuante (invariante 1), tempo so por <c>IClock</c> (invariante 9), teste sem espera
/// de relogio de parede e hierarquia unica de excecoes.
/// </summary>
public sealed class InvariantesDeCodigoTestes
{
    private const BindingFlags TodosOsMembros =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    // O teste que caca ponto flutuante precisa nomear ponto flutuante. Esta e a unica dispensa do
    // banimento em todo o repositorio, e e deliberadamente estreita: uma linha, so typeof.
#pragma warning disable RS0030
    private static readonly Type[] TiposDePontoFlutuante = { typeof(decimal), typeof(double), typeof(float) };
#pragma warning restore RS0030

    /// <summary>
    /// Grafias que declaram ponto flutuante: as palavras-chave e os nomes CLR equivalentes.
    /// </summary>
    private static readonly string[] TokensDePontoFlutuante =
    {
        "decimal", "double", "float", "System.Decimal", "System.Double", "System.Single",
    };

    private static readonly string[] TokensDeRelogioAmbiente =
    {
        "DateTime.UtcNow", "DateTime.Now", "DateTimeOffset.UtcNow", "DateTimeOffset.Now",
    };

    private static readonly string[] TokensDeEspera = { "Thread.Sleep", "Task.Delay" };

    private static readonly string[] SimbolosQueDevemEstarBanidos =
    {
        "System.DateTime.UtcNow",
        "System.DateTime.Now",
        "System.DateTimeOffset.UtcNow",
        "System.DateTimeOffset.Now",
        "System.Random",
        "System.Guid.NewGuid",
    };

    /// <summary>
    /// Invariante 1 pela superficie compilada: varre todos os tipos do assembly de dominio,
    /// inclusive os nao-publicos, e recusa <c>decimal</c>, <c>double</c> e <c>float</c> em campo,
    /// propriedade, parametro ou retorno — incluindo as formas <c>Nullable</c>, array e argumento
    /// de generico, que uma busca textual ingenua deixaria passar.
    /// Todas as violacoes sao coletadas e reportadas juntas: consertar uma de cada vez, com um
    /// ciclo de build por vez, e o caminho mais lento para a mesma correcao.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void Invariante1_NenhumMembroDoDominio_ExpoeDecimalDoubleOuFloat()
    {
        var violacoes = new List<string>();

        foreach (Type tipo in TiposDoDominio())
        {
            string nome = tipo.FullName ?? tipo.Name;

            foreach (FieldInfo campo in tipo.GetFields(TodosOsMembros))
            {
                Registrar(violacoes, campo.FieldType, nome, $"campo '{campo.Name}'");
            }

            foreach (PropertyInfo propriedade in tipo.GetProperties(TodosOsMembros))
            {
                Registrar(violacoes, propriedade.PropertyType, nome, $"propriedade '{propriedade.Name}'");

                foreach (ParameterInfo indice in propriedade.GetIndexParameters())
                {
                    Registrar(violacoes, indice.ParameterType, nome, $"parametro '{indice.Name}' do indexador '{propriedade.Name}'");
                }
            }

            foreach (MethodInfo metodo in tipo.GetMethods(TodosOsMembros))
            {
                Registrar(violacoes, metodo.ReturnType, nome, $"retorno de '{metodo.Name}'");

                foreach (ParameterInfo parametro in metodo.GetParameters())
                {
                    Registrar(violacoes, parametro.ParameterType, nome, $"parametro '{parametro.Name}' de '{metodo.Name}'");
                }
            }

            foreach (ConstructorInfo construtor in tipo.GetConstructors(TodosOsMembros))
            {
                foreach (ParameterInfo parametro in construtor.GetParameters())
                {
                    Registrar(violacoes, parametro.ParameterType, nome, $"parametro '{parametro.Name}' do construtor");
                }
            }
        }

        Assert.True(
            violacoes.Count == 0,
            "Invariante 1: dinheiro e long em centavos, ponto flutuante nao entra no dominio."
                + Environment.NewLine
                + string.Join(Environment.NewLine, violacoes));
    }

    /// <summary>
    /// Invariante 1 pelo texto do fonte.
    /// <para>
    /// Este teste existe porque o <c>BannedApiAnalyzers</c> nao cobre o caso. Verificado
    /// empiricamente: <c>T:System.Decimal</c> em <c>BannedSymbols.txt</c> faz RS0030 disparar em
    /// uso de MEMBRO (por exemplo <c>decimal.Parse</c>), mas nao dispara em declaracao de campo,
    /// de parametro, de retorno, de variavel local, nem em literal. Sem esta varredura, o
    /// invariante 1 simplesmente nao esta imposto para declaracoes — e o teste por reflexao
    /// acima, que cobre a superficie de tipos, nao enxerga variavel local nem literal.
    /// </para>
    /// <para>
    /// Comentario e literal de texto sao removidos antes da busca: o proprio dominio cita
    /// "decimal" em comentario para explicar por que nao usa decimal, e uma busca crua acusaria
    /// justamente a documentacao da regra.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void Invariante1_NenhumFonteDeSrc_DeclaraDecimalDoubleOuFloat()
    {
        IReadOnlyList<string> ocorrencias = Procurar(ArquivosDoRepositorio.FontesDeSrc(), TokensDePontoFlutuante);

        Assert.True(
            ocorrencias.Count == 0,
            "Invariante 1: ponto flutuante citado como tipo no fonte de src/."
                + Environment.NewLine
                + string.Join(Environment.NewLine, ocorrencias));
    }

    /// <summary>
    /// Invariante 9: tempo so entra por <c>IClock</c> injetado. A implementacao que le o relogio
    /// do sistema vive no composition root, fora de src/ do ponto de vista desta regra — e
    /// quando ela existir, entrara na lista de excecoes deste teste de forma explicita.
    /// A varredura tambem ignora comentario, senao o proprio XML doc de <c>IClock</c>, que cita
    /// <c>DateTime.UtcNow</c> para proibi-lo, derrubaria o teste.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void Invariante9_NenhumFonteDeSrc_LeORelogioDoSistema()
    {
        // MotorPix.Composicao e o composition root: e o unico projeto que nao carrega
        // BannedSymbols.txt e o unico lugar em que RelogioSistema pode ler o relogio de verdade.
        // A dispensa e nominal e estreita — um projeto, e o teste abaixo confere que ela nao virou
        // um cheque em branco.
        IReadOnlyList<string> fontes = ArquivosDoRepositorio.FontesDeSrc()
            .Where(caminho => !caminho.Replace('\\', '/').Contains("/MotorPix.Composicao/", StringComparison.Ordinal))
            .ToList();

        IReadOnlyList<string> ocorrencias = Procurar(fontes, TokensDeRelogioAmbiente);

        Assert.True(
            ocorrencias.Count == 0,
            "Invariante 9: tempo so entra via IClock injetado."
                + Environment.NewLine
                + string.Join(Environment.NewLine, ocorrencias));
    }

    /// <summary>
    /// Teste que espera relogio de parede e teste intermitente: passa na maquina rapida, falha na
    /// CI carregada, e o timeout do gate 5 tem de ser determinado pelo relogio fake.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void SemEspera_NenhumFonteDeTests_UsaThreadSleepOuTaskDelay()
    {
        IReadOnlyList<string> ocorrencias = Procurar(ArquivosDoRepositorio.FontesDeTests(), TokensDeEspera);

        Assert.True(
            ocorrencias.Count == 0,
            "Espera de relogio de parede em teste: use RelogioFake."
                + Environment.NewLine
                + string.Join(Environment.NewLine, ocorrencias));
    }

    /// <summary>
    /// Toda excecao do dominio desce de <c>MotorPixException</c>: e isso que permite a um
    /// chamador distinguir "regra de negocio recusou" de "bug de programacao" num unico
    /// <c>catch</c>. Uma excecao solta na hierarquia de <c>System.Exception</c> desfaz a
    /// distincao sem que nenhum teste de comportamento perceba.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void Excecoes_DoDominio_DescendemDeMotorPixException()
    {
        Type[] excecoes = ExcecoesDoDominio();

        string[] desviantes = excecoes
            .Where(t => t != typeof(MotorPixException) && !typeof(MotorPixException).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(excecoes);
        Assert.True(
            desviantes.Length == 0,
            "Excecoes do dominio fora da raiz MotorPixException:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, desviantes));
    }

    /// <summary>
    /// A dispensa do invariante 9 vale para <b>um unico arquivo</b> do composition root.
    /// <para>
    /// Sem este teste, "Composicao esta fora da varredura" viraria carta branca para o projeto
    /// inteiro, e o proximo tipo que precisasse do relogio nasceria la sem que ninguem notasse.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "3")]
    public void Invariante9_NoComposicao_SoORelogioDoSistemaLeORelogioAmbiente()
    {
        List<string> arquivosComRelogio = [];

        foreach (string caminho in ArquivosDoRepositorio.FontesDeSrc())
        {
            if (!caminho.Replace('\\', '/').Contains("/MotorPix.Composicao/", StringComparison.Ordinal))
            {
                continue;
            }

            string limpo = FonteCsharp.SemComentariosELiterais(File.ReadAllText(caminho));

            if (TokensDeRelogioAmbiente.Any(token => limpo.Contains(token, StringComparison.Ordinal)))
            {
                arquivosComRelogio.Add(Path.GetFileName(caminho));
            }
        }

        Assert.True(
            arquivosComRelogio.Count == 1 && arquivosComRelogio[0] == "RelogioSistema.cs",
            "So RelogioSistema.cs pode ler o relogio ambiente; encontrados: "
                + string.Join(", ", arquivosComRelogio));
    }

    /// <summary>
    /// A raiz e abstrata e assenta direto em <c>System.Exception</c>: se fosse concreta, lancar
    /// <c>MotorPixException</c> generica voltaria a ser possivel, que e o que o prompt proibe.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void MotorPixException_EhAbstrataEDesceDiretamenteDeException()
    {
        Assert.True(typeof(MotorPixException).IsAbstract, "MotorPixException tem de ser abstract.");
        Assert.True(
            typeof(MotorPixException).BaseType == typeof(Exception),
            $"MotorPixException deveria descer de System.Exception, e desce de '{typeof(MotorPixException).BaseType}'.");
    }

    /// <summary>
    /// Excecao abstrata sem filha concreta e hierarquia que ninguem lanca: peso morto que
    /// aparenta cobrir um caso e nao cobre nenhum.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void Excecoes_AbstratasDoDominio_TemPeloMenosUmaFilhaConcreta()
    {
        Type[] excecoes = ExcecoesDoDominio();
        Type[] abstratas = excecoes.Where(t => t.IsAbstract).ToArray();
        var orfas = new List<string>();

        foreach (Type abstrata in abstratas)
        {
            bool temFilhaConcreta = false;

            foreach (Type candidata in excecoes)
            {
                if (!candidata.IsAbstract && candidata != abstrata && abstrata.IsAssignableFrom(candidata))
                {
                    temFilhaConcreta = true;
                    break;
                }
            }

            if (!temFilhaConcreta)
            {
                orfas.Add(abstrata.FullName ?? abstrata.Name);
            }
        }

        Assert.True(abstratas.Length > 0, "Nenhuma excecao abstrata encontrada: a raiz MotorPixException deveria estar aqui.");
        Assert.True(
            orfas.Count == 0,
            "Excecoes abstratas sem nenhuma filha concreta:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, orfas));
    }

    /// <summary>
    /// O arquivo de simbolos banidos e a metade da imposicao que roda no compilador. Se ele
    /// sumir ou for esvaziado, o build continua verde e as proibicoes evaporam em silencio.
    /// </summary>
    [Theory]
    [Trait("gate", "1")]
    [InlineData("System.DateTime.UtcNow")]
    [InlineData("System.DateTime.Now")]
    [InlineData("System.DateTimeOffset.UtcNow")]
    [InlineData("System.DateTimeOffset.Now")]
    [InlineData("System.Random")]
    [InlineData("System.Guid.NewGuid")]
    public void BannedSymbols_ContemAEntradaProibida(string simbolo)
    {
        IReadOnlySet<string> banidos = SimbolosBanidos();
        bool presente = banidos.Contains(simbolo);

        Assert.True(
            presente,
            $"'{simbolo}' nao esta em '{RaizDoRepositorio.Relativo(CaminhoDoBannedSymbols())}'. "
                + $"Entradas encontradas: [{string.Join(", ", banidos.OrderBy(s => s, StringComparer.Ordinal))}].");
    }

    /// <summary>
    /// Complementa o teorema acima: o arquivo existe, tem conteudo e esta de fato ligado a
    /// compilacao como <c>AdditionalFiles</c>. Um <c>BannedSymbols.txt</c> que o build nao le e
    /// documentacao, nao regra.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void BannedSymbols_ExisteEEstaLigadoAoBuildComoAdditionalFiles()
    {
        string caminho = CaminhoDoBannedSymbols();
        Assert.True(File.Exists(caminho), $"Arquivo de simbolos banidos ausente: '{RaizDoRepositorio.Relativo(caminho)}'.");

        IReadOnlySet<string> banidos = SimbolosBanidos();
        Assert.True(
            banidos.Count >= SimbolosQueDevemEstarBanidos.Length,
            $"Esperava ao menos {SimbolosQueDevemEstarBanidos.Length} simbolos banidos e encontrei {banidos.Count}.");

        string props = Path.Combine(RaizDoRepositorio.Src, "Directory.Build.props");
        Assert.True(File.Exists(props), $"Arquivo ausente: '{RaizDoRepositorio.Relativo(props)}'.");

        bool registrado = XDocument.Load(props)
            .Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "AdditionalFiles", StringComparison.Ordinal))
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Any(v => v.EndsWith("BannedSymbols.txt", StringComparison.OrdinalIgnoreCase));

        Assert.True(registrado, $"'{RaizDoRepositorio.Relativo(props)}' nao registra BannedSymbols.txt como AdditionalFiles.");
    }

    private static Type[] TiposDoDominio()
    {
        Assembly dominio = typeof(IClock).Assembly;

        try
        {
            return dominio.GetTypes();
        }
        catch (ReflectionTypeLoadException excecao)
        {
            return excecao.Types.Where(t => t is not null).Select(t => t!).ToArray();
        }
    }

    private static Type[] ExcecoesDoDominio()
        => TiposDoDominio().Where(typeof(Exception).IsAssignableFrom).ToArray();

    private static void Registrar(List<string> violacoes, Type tipo, string nomeDoTipo, string onde)
    {
        Type? proibido = PontoFlutuanteEm(tipo, profundidade: 0);

        if (proibido is not null)
        {
            violacoes.Add($"{nomeDoTipo}: {onde} usa '{proibido.Name}'.");
        }
    }

    /// <summary>
    /// Procura ponto flutuante atravessando <c>Nullable</c>, array, ponteiro, <c>ref</c> e
    /// argumentos de generico. A profundidade e limitada por seguranca contra tipo ciclico.
    /// </summary>
    private static Type? PontoFlutuanteEm(Type tipo, int profundidade)
    {
        if (profundidade > 8 || tipo.IsGenericParameter)
        {
            return null;
        }

        if (tipo.IsByRef || tipo.IsPointer || tipo.IsArray)
        {
            Type? elemento = tipo.GetElementType();
            return elemento is null ? null : PontoFlutuanteEm(elemento, profundidade + 1);
        }

        Type? subjacente = Nullable.GetUnderlyingType(tipo);
        if (subjacente is not null)
        {
            return PontoFlutuanteEm(subjacente, profundidade + 1);
        }

        if (TiposDePontoFlutuante.Contains(tipo))
        {
            return tipo;
        }

        if (tipo.IsGenericType)
        {
            foreach (Type argumento in tipo.GetGenericArguments())
            {
                Type? achado = PontoFlutuanteEm(argumento, profundidade + 1);
                if (achado is not null)
                {
                    return achado;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Procura cada token como palavra inteira no fonte ja sem comentario e sem literal.
    /// O indice do casamento vale para o texto original porque a limpeza troca cada caractere
    /// removido por um espaco (ou pela propria quebra de linha), preservando posicoes.
    /// </summary>
    /// <summary>
    /// Nenhum fonte de <c>src/</c> declara excecao herdando direto de <see cref="Exception"/>.
    /// <para>
    /// A checagem por reflexao acima cobre o assembly de dominio e so ele — o projeto de testes de
    /// arquitetura nao referencia os modulos, entao os DLLs deles nem chegam ao diretorio de saida.
    /// Verificado empiricamente: uma excecao fora da hierarquia plantada em <c>MotorPix.Dict</c>
    /// passou verde por aquele caminho.
    /// </para>
    /// <para>
    /// Sem esta varredura textual, o gate 3 poderia declarar
    /// <c>TransicaoInvalidaException : Exception</c> dentro de um modulo e o invariante 4 ficaria
    /// sem rede: ninguem mais distinguiria "a regra recusou" de "o motor quebrou" num unico
    /// <c>catch</c>. Ler o fonte tambem evita carregar dois <c>MotorPixException</c> de assemblies
    /// diferentes, que e o que aconteceria ao varrer os <c>bin/</c> de cada modulo.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "2")]
    public void HierarquiaDeExcecoes_NenhumFonteDeSrc_HerdaDiretoDeException()
    {
        Regex padrao = new(
            @"\bclass\s+(?<nome>\w+)\s*:\s*(?:System\.)?Exception\b",
            RegexOptions.CultureInvariant);

        List<string> violacoes = [];

        foreach (string arquivo in ArquivosDoRepositorio.FontesDeSrc())
        {
            string bruto = File.ReadAllText(arquivo);
            string limpo = FonteCsharp.SemComentariosELiterais(bruto);

            foreach (Match ocorrencia in padrao.Matches(limpo))
            {
                string nome = ocorrencia.Groups["nome"].Value;

                // A raiz da hierarquia e a unica que pode herdar direto de Exception.
                if (string.Equals(nome, nameof(MotorPixException), StringComparison.Ordinal))
                {
                    continue;
                }

                violacoes.Add(
                    $"{RaizDoRepositorio.Relativo(arquivo)}:{LinhaDe(bruto, ocorrencia.Index)} "
                    + $"declara '{nome}' herdando direto de Exception.");
            }
        }

        Assert.True(
            violacoes.Count == 0,
            "Toda excecao de regra de negocio desce de MotorPixException (prompt:32)."
                + Environment.NewLine
                + string.Join(Environment.NewLine, violacoes));
    }

    private static IReadOnlyList<string> Procurar(IReadOnlyList<string> arquivos, IReadOnlyList<string> tokens)
    {
        Regex[] padroes = tokens
            .Select(t => new Regex($@"\b{Regex.Escape(t)}\b", RegexOptions.CultureInvariant))
            .ToArray();

        var achados = new List<string>();

        foreach (string arquivo in arquivos)
        {
            string bruto = File.ReadAllText(arquivo);
            string limpo = FonteCsharp.SemComentariosELiterais(bruto);

            for (int i = 0; i < padroes.Length; i++)
            {
                foreach (Match ocorrencia in padroes[i].Matches(limpo))
                {
                    achados.Add($"{RaizDoRepositorio.Relativo(arquivo)}:{LinhaDe(bruto, ocorrencia.Index)} usa '{tokens[i]}'.");
                }
            }
        }

        return achados;
    }

    private static int LinhaDe(string texto, int indice)
    {
        int linha = 1;

        for (int i = 0; i < indice && i < texto.Length; i++)
        {
            if (texto[i] == '\n')
            {
                linha++;
            }
        }

        return linha;
    }

    private static string CaminhoDoBannedSymbols() => Path.Combine(RaizDoRepositorio.Src, "BannedSymbols.txt");

    /// <summary>
    /// Le os identificadores de documentacao do arquivo de banimento, sem o prefixo de especie
    /// (<c>T:</c>, <c>P:</c>, <c>M:</c>) e sem a mensagem que vem depois do ponto-e-virgula.
    /// </summary>
    private static IReadOnlySet<string> SimbolosBanidos()
    {
        var simbolos = new HashSet<string>(StringComparer.Ordinal);
        string caminho = CaminhoDoBannedSymbols();

        if (!File.Exists(caminho))
        {
            return simbolos;
        }

        foreach (string linha in File.ReadAllLines(caminho))
        {
            string texto = linha.Trim();

            if (texto.Length == 0 || texto.StartsWith('#'))
            {
                continue;
            }

            int separador = texto.IndexOf(';');
            string identificador = (separador < 0 ? texto : texto[..separador]).Trim();

            if (identificador.Length > 2 && identificador[1] == ':')
            {
                identificador = identificador[2..];
            }

            if (identificador.Length > 0)
            {
                simbolos.Add(identificador);
            }
        }

        return simbolos;
    }
}

/// <summary>
/// Limpeza lexica de fonte C#: remove comentario de linha, de bloco e XML, literal de texto
/// (comum, verbatim e interpolado) e literal de caractere, trocando cada caractere removido por
/// espaco e preservando as quebras de linha.
/// <para>
/// Preservar o comprimento e proposital: assim o indice de um casamento no texto limpo aponta
/// para a mesma posicao no texto original, e a mensagem de falha pode citar a linha certa.
/// </para>
/// <para>
/// O conteudo dos buracos de interpolacao e mantido, ja que ali existe codigo de verdade —
/// so o texto ao redor e apagado.
/// </para>
/// </summary>
internal static class FonteCsharp
{
    internal static string SemComentariosELiterais(string fonte)
    {
        var saida = new StringBuilder(fonte.Length);
        int i = 0;

        while (i < fonte.Length)
        {
            char atual = fonte[i];

            if (atual == '/' && Espia(fonte, i + 1) == '/')
            {
                i = ApagarComentarioDeLinha(fonte, i, saida);
                continue;
            }

            if (atual == '/' && Espia(fonte, i + 1) == '*')
            {
                i = ApagarComentarioDeBloco(fonte, i, saida);
                continue;
            }

            if (atual == '\'')
            {
                i = ApagarCaractere(fonte, i, saida);
                continue;
            }

            if (EhInicioDeTexto(fonte, i, out bool interpolado, out bool verbatim, out int aspas))
            {
                i = ApagarTexto(fonte, i, aspas, interpolado, verbatim, saida);
                continue;
            }

            saida.Append(atual);
            i++;
        }

        return saida.ToString();
    }

    private static char Espia(string fonte, int indice) => indice < fonte.Length ? fonte[indice] : '\0';

    private static void Apagar(StringBuilder saida, char caractere) => saida.Append(caractere == '\n' ? '\n' : ' ');

    private static int ApagarComentarioDeLinha(string fonte, int i, StringBuilder saida)
    {
        while (i < fonte.Length && fonte[i] != '\n')
        {
            Apagar(saida, fonte[i]);
            i++;
        }

        return i;
    }

    private static int ApagarComentarioDeBloco(string fonte, int i, StringBuilder saida)
    {
        Apagar(saida, fonte[i]);
        Apagar(saida, fonte[i + 1]);
        i += 2;

        while (i < fonte.Length)
        {
            if (fonte[i] == '*' && Espia(fonte, i + 1) == '/')
            {
                Apagar(saida, fonte[i]);
                Apagar(saida, fonte[i + 1]);
                return i + 2;
            }

            Apagar(saida, fonte[i]);
            i++;
        }

        return i;
    }

    private static int ApagarCaractere(string fonte, int i, StringBuilder saida)
    {
        Apagar(saida, fonte[i]);
        i++;

        while (i < fonte.Length)
        {
            char atual = fonte[i];

            if (atual == '\\' && i + 1 < fonte.Length)
            {
                Apagar(saida, atual);
                Apagar(saida, fonte[i + 1]);
                i += 2;
                continue;
            }

            Apagar(saida, atual);
            i++;

            if (atual == '\'' || atual == '\n')
            {
                return i;
            }
        }

        return i;
    }

    /// <summary>
    /// Reconhece o inicio de um literal de texto, com os prefixos <c>$</c> e <c>@</c> em
    /// qualquer ordem. Devolve o indice das aspas de abertura.
    /// </summary>
    private static bool EhInicioDeTexto(string fonte, int i, out bool interpolado, out bool verbatim, out int aspas)
    {
        interpolado = false;
        verbatim = false;
        aspas = i;

        if (fonte[i] != '"' && fonte[i] != '$' && fonte[i] != '@')
        {
            return false;
        }

        int j = i;

        while (j < fonte.Length && (fonte[j] == '$' || fonte[j] == '@'))
        {
            interpolado |= fonte[j] == '$';
            verbatim |= fonte[j] == '@';
            j++;
        }

        if (j < fonte.Length && fonte[j] == '"')
        {
            aspas = j;
            return true;
        }

        return false;
    }

    private static int ApagarTexto(string fonte, int i, int aspas, bool interpolado, bool verbatim, StringBuilder saida)
    {
        for (; i <= aspas; i++)
        {
            Apagar(saida, fonte[i]);
        }

        while (i < fonte.Length)
        {
            char atual = fonte[i];

            if (!verbatim && atual == '\\' && i + 1 < fonte.Length)
            {
                Apagar(saida, atual);
                Apagar(saida, fonte[i + 1]);
                i += 2;
                continue;
            }

            if (verbatim && atual == '"' && Espia(fonte, i + 1) == '"')
            {
                Apagar(saida, atual);
                Apagar(saida, fonte[i + 1]);
                i += 2;
                continue;
            }

            if (atual == '"')
            {
                Apagar(saida, atual);
                return i + 1;
            }

            if (interpolado && atual == '{')
            {
                if (Espia(fonte, i + 1) == '{')
                {
                    Apagar(saida, atual);
                    Apagar(saida, fonte[i + 1]);
                    i += 2;
                    continue;
                }

                i = CopiarBuraco(fonte, i, saida);
                continue;
            }

            // Literal comum nao atravessa linha; parar aqui evita que um fonte mal formado
            // engula o resto do arquivo.
            if (!verbatim && atual == '\n')
            {
                saida.Append('\n');
                return i + 1;
            }

            Apagar(saida, atual);
            i++;
        }

        return i;
    }

    /// <summary>
    /// Copia o codigo de um buraco de interpolacao, respeitando chaves aninhadas e apagando os
    /// literais que aparecam la dentro.
    /// </summary>
    private static int CopiarBuraco(string fonte, int i, StringBuilder saida)
    {
        saida.Append(fonte[i]);
        i++;
        int profundidade = 1;

        while (i < fonte.Length && profundidade > 0)
        {
            char atual = fonte[i];

            if (atual == '\'')
            {
                i = ApagarCaractere(fonte, i, saida);
                continue;
            }

            if (EhInicioDeTexto(fonte, i, out bool interpolado, out bool verbatim, out int aspas))
            {
                i = ApagarTexto(fonte, i, aspas, interpolado, verbatim, saida);
                continue;
            }

            if (atual == '{')
            {
                profundidade++;
            }
            else if (atual == '}')
            {
                profundidade--;
            }

            saida.Append(atual);
            i++;
        }

        return i;
    }
}
