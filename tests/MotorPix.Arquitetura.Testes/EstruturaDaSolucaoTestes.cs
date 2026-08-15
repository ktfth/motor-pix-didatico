using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using MotorPix.Dominio.Tempo;

namespace MotorPix.Arquitetura.Testes;

/// <summary>
/// Guardas estruturais da solucao: grafo de referencias entre projetos, isolamento de
/// <c>internal</c> e completude do arquivo de solucao.
/// <para>
/// Todas as leituras sao feitas no disco, a partir da raiz do repositorio localizada em tempo de
/// execucao. Nenhum caminho absoluto aparece aqui: a suite tem de rodar igual na maquina de quem
/// escreveu e na CI.
/// </para>
/// </summary>
public sealed class EstruturaDaSolucaoTestes
{
    private const string NomeDoDominio = "MotorPix.Dominio";

    private const string SufixoDeAssemblyDeTeste = ".Testes";

    /// <summary>
    /// Whitelist escrita a mao: a unica fonte de verdade sobre quem pode depender de quem.
    /// Acrescentar um projeto ou uma aresta exige editar este mapa, e editar este mapa e uma
    /// decisao de arquitetura visivel no diff.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> GrafoAutorizado =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // Nucleo: nao conhece modulo nenhum.
            { "MotorPix.Dominio", Array.Empty<string>() },
            { "MotorPix.Mensagens", new[] { "MotorPix.Dominio" } },

            // Contratos e o assembly neutro onde as setas do diagrama viram interfaces. Nenhum
            // modulo publica tipo proprio aqui.
            { "MotorPix.Contratos", new[] { "MotorPix.Dominio", "MotorPix.Mensagens" } },

            // Kernel de PAPEL, nao um quinto modulo: a maquina de estados e o ledger de PSP, usados
            // pelos dois PSPs. Duplica-los seria dois lugares para o mesmo bug, e no gate 6 o
            // recebedor tambem origina transacao.
            {
                "MotorPix.Psp.Nucleo",
                new[] { "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos" }
            },

            // Modulos: dependem do nucleo e dos contratos, NUNCA uns dos outros. E esta ausencia de
            // aresta modulo -> modulo que o teste existe para defender.
            { "MotorPix.Dict", new[] { "MotorPix.Dominio", "MotorPix.Contratos" } },
            { "MotorPix.Spi", new[] { "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos" } },

            // A conciliacao le os tres ledgers por IConsultaLedger e fala com os PSPs por
            // IPspConciliavel. Nao referencia nenhum modulo — se referenciasse, teria acesso a
            // portas de escrita que o desenho lhe nega de proposito.
            { "MotorPix.Conciliacao", new[] { "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos" } },
            {
                "MotorPix.PspPagador",
                new[] { "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos", "MotorPix.Psp.Nucleo" }
            },
            {
                "MotorPix.PspRecebedor",
                new[] { "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos", "MotorPix.Psp.Nucleo" }
            },

            // Composition root: o unico projeto autorizado a conhecer todos, e o unico lugar em que
            // o ciclo das setas 4 e 6 e resolvido — por registro tardio no barramento.
            {
                "MotorPix.Composicao",
                new[]
                {
                    "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos", "MotorPix.Dict",
                    "MotorPix.Conciliacao", "MotorPix.Spi", "MotorPix.Psp.Nucleo", "MotorPix.PspPagador",
                    "MotorPix.PspRecebedor",
                }
            },

            { "MotorPix.Testes.Comum", new[] { "MotorPix.Dominio" } },
            { "MotorPix.Dominio.Testes", new[] { "MotorPix.Dominio", "MotorPix.Testes.Comum" } },
            {
                "MotorPix.Dict.Testes",
                new[] { "MotorPix.Dominio", "MotorPix.Contratos", "MotorPix.Dict", "MotorPix.Testes.Comum" }
            },
            {
                "MotorPix.Psp.Nucleo.Testes",
                new[]
                {
                    "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos", "MotorPix.Psp.Nucleo",
                    "MotorPix.Testes.Comum",
                }
            },

            // Os testes de fluxo consomem o motor pelo composition root, como um host faria: nenhuma
            // referencia a modulo individual, para que nenhum teste alcance tipo interno de modulo.
            {
                "MotorPix.Fluxos.Testes",
                new[]
                {
                    "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos", "MotorPix.Psp.Nucleo",
                    "MotorPix.Composicao", "MotorPix.Testes.Comum",
                }
            },
            // As especificacoes executaveis (SpecFlow) entram pela mesma porta que os testes de
            // fluxo: composition root, nenhum modulo individual. Um cenario escrito em linguagem de
            // negocio nao ganha permissao que um [Fact] nao tem.
            {
                "MotorPix.Especificacoes",
                new[]
                {
                    "MotorPix.Dominio", "MotorPix.Mensagens", "MotorPix.Contratos", "MotorPix.Psp.Nucleo",
                    "MotorPix.Composicao", "MotorPix.Testes.Comum",
                }
            },
            { "MotorPix.Arquitetura.Testes", new[] { "MotorPix.Dominio" } },
        };

    /// <summary>
    /// Modulos que se comunicam exclusivamente por interface publica, in-process. Nenhum deles pode
    /// referenciar outro: a comunicacao passa pelas interfaces de <c>MotorPix.Contratos</c>.
    /// <para>
    /// <c>MotorPix.Psp.Nucleo</c> nao entra na lista de proposito — e kernel de papel compartilhado
    /// pelos dois PSPs, nao um modulo do diagrama.
    /// </para>
    /// </summary>
    private static readonly string[] Modulos =
    [
        "MotorPix.Dict",
        "MotorPix.Spi",
        "MotorPix.PspPagador",
        "MotorPix.PspRecebedor",
        "MotorPix.Conciliacao",
    ];

    /// <summary>
    /// Le o XML dos <c>.csproj</c> e compara as arestas declaradas com a whitelist.
    /// <para>
    /// A leitura do arquivo de projeto e deliberada, e nao preguica de nao usar reflexao:
    /// <c>Assembly.GetReferencedAssemblies()</c> so enxerga as referencias que o compilador
    /// gravou no manifesto, ou seja, as que ja foram efetivamente usadas por algum tipo. Uma
    /// referencia declarada e ainda nao exercida — exatamente o instante em que a violacao de
    /// arquitetura nasce, antes de qualquer codigo depender dela — nao apareceria ali.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void GrafoDeReferencias_LidoDosCsproj_CoincideComAWhitelist()
    {
        IReadOnlyDictionary<string, HashSet<string>> noDisco = ReferenciasPorProjeto();
        var problemas = new List<string>();

        foreach (string projeto in noDisco.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!GrafoAutorizado.ContainsKey(projeto))
            {
                problemas.Add($"projeto '{projeto}' existe no disco e nao esta na whitelist deste teste.");
            }
        }

        foreach (string projeto in GrafoAutorizado.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!noDisco.TryGetValue(projeto, out HashSet<string>? declaradas))
            {
                problemas.Add($"projeto '{projeto}' esta na whitelist e nao foi encontrado no disco.");
                continue;
            }

            var esperadas = new HashSet<string>(GrafoAutorizado[projeto], StringComparer.Ordinal);

            foreach (string alvo in declaradas.Where(a => !esperadas.Contains(a)).OrderBy(a => a, StringComparer.Ordinal))
            {
                problemas.Add($"aresta a mais: '{projeto}' -> '{alvo}' nao esta autorizada.");
            }

            foreach (string alvo in esperadas.Where(a => !declaradas.Contains(a)).OrderBy(a => a, StringComparer.Ordinal))
            {
                problemas.Add($"aresta a menos: '{projeto}' -> '{alvo}' e esperada e nao foi declarada.");
            }
        }

        Assert.True(
            problemas.Count == 0,
            "Grafo de ProjectReference divergiu da whitelist:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problemas));
    }

    /// <summary>
    /// O kernel contabil nao depende de nada: nem de outro projeto, nem de pacote NuGet.
    /// Os analisadores (<c>BannedApiAnalyzers</c>) entram por <c>src/Directory.Build.props</c> e
    /// nao pelo csproj — sao ferramenta de compilacao herdada, nao dependencia do dominio.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void Dominio_NoProprioCsproj_NaoDeclaraProjectReferenceNemPackageReference()
    {
        XDocument csproj = XDocument.Load(CaminhoDoProjeto(NomeDoDominio));

        string[] projetos = Itens(csproj, "ProjectReference")
            .Select(NomeDoProjetoReferenciado)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] pacotes = Itens(csproj, "PackageReference")
            .Select(Include)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            projetos.Length == 0 && pacotes.Length == 0,
            $"{NomeDoDominio} tem de ser folha do grafo. ProjectReference: [{string.Join(", ", projetos)}]. "
                + $"PackageReference: [{string.Join(", ", pacotes)}].");
    }

    /// <summary>
    /// <c>InternalsVisibleTo</c> e a valvula de escape obvia do isolamento por assembly:
    /// expor internals para um assembly de producao dissolve a fronteira que os projetos
    /// separados existem para impor. So assembly de teste pode receber.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void InternalsVisibleTo_DeclaradoEmSrc_SoApontaParaAssemblyDeTeste()
    {
        var problemas = new List<string>();

        foreach (string caminho in ArquivosDoRepositorio.Projetos().Where(EstaEmSrc))
        {
            string projeto = Path.GetFileNameWithoutExtension(caminho);

            foreach (string alvo in InternalsVisibleToDeclarados(XDocument.Load(caminho)))
            {
                if (!alvo.EndsWith(SufixoDeAssemblyDeTeste, StringComparison.Ordinal))
                {
                    problemas.Add($"'{projeto}' expoe internals para '{alvo}', que nao termina em '{SufixoDeAssemblyDeTeste}'.");
                }
            }
        }

        Assert.True(
            problemas.Count == 0,
            "InternalsVisibleTo para assembly que nao e de teste:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problemas));
    }

    /// <summary>
    /// Confere o mesmo isolamento pelo outro lado: o que o assembly compilado realmente carrega.
    /// O csproj declara e o build aplica; comparar as duas pontas pega tanto uma exposicao
    /// acrescentada por atributo direto no codigo quanto um item de csproj que nao surtiu efeito.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void InternalsVisibleTo_DoDominio_EmRuntimeBateComOCsprojESoExpoeParaTestes()
    {
        string[] emRuntime = typeof(IClock).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => NomeSimplesDoAssembly(a.AssemblyName))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] noCsproj = InternalsVisibleToDeclarados(XDocument.Load(CaminhoDoProjeto(NomeDoDominio)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(string.Join(", ", noCsproj), string.Join(", ", emRuntime));

        string[] forasteiros = emRuntime
            .Where(n => !n.EndsWith(SufixoDeAssemblyDeTeste, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            forasteiros.Length == 0,
            $"{NomeDoDominio} expoe internals em runtime para: [{string.Join(", ", forasteiros)}].");
    }

    /// <summary>
    /// Projeto fora do arquivo de solucao nao compila no build da solucao, nao roda na CI e
    /// vira codigo morto que ninguem verifica — inclusive escapando destes proprios testes.
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void ArquivoDaSolucao_ListaExatamenteOsProjetosPresentesNoDisco()
    {
        var naSolucao = new HashSet<string>(
            Itens(XDocument.Load(RaizDoRepositorio.ArquivoDaSolucao), "Project")
                .Select(e => e.Attribute("Path")?.Value ?? string.Empty)
                .Where(p => p.Length > 0)
                .Select(p => p.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        var noDisco = new HashSet<string>(
            ArquivosDoRepositorio.Projetos().Select(RaizDoRepositorio.Relativo),
            StringComparer.OrdinalIgnoreCase);

        var problemas = new List<string>();

        foreach (string caminho in noDisco.Where(c => !naSolucao.Contains(c)).OrderBy(c => c, StringComparer.Ordinal))
        {
            problemas.Add($"projeto '{caminho}' existe no disco e nao esta em {RaizDoRepositorio.NomeDoArquivoDaSolucao}.");
        }

        foreach (string caminho in naSolucao.Where(c => !noDisco.Contains(c)).OrderBy(c => c, StringComparer.Ordinal))
        {
            problemas.Add($"projeto '{caminho}' esta em {RaizDoRepositorio.NomeDoArquivoDaSolucao} e nao existe no disco.");
        }

        Assert.True(
            problemas.Count == 0,
            "Arquivo de solucao divergiu do disco:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problemas));
    }

    /// <summary>
    /// A localizacao da raiz e infraestrutura de todos os outros testes deste projeto: se ela
    /// parar num diretorio errado, os demais passam por vacuidade (varrendo zero arquivo).
    /// </summary>
    [Fact]
    [Trait("gate", "1")]
    public void RaizDoRepositorio_EhAncestralDoDiretorioDeExecucao_EContemSrcETests()
    {
        string raiz = RaizDoRepositorio.Caminho;

        Assert.StartsWith(raiz, Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(RaizDoRepositorio.Src), $"Nao existe diretorio 'src' em '{raiz}'.");
        Assert.True(Directory.Exists(RaizDoRepositorio.Tests), $"Nao existe diretorio 'tests' em '{raiz}'.");
        Assert.True(ArquivosDoRepositorio.Projetos().Count > 0, $"Nenhum .csproj encontrado sob 'src' e 'tests' de '{raiz}'.");
    }

    /// <summary>
    /// Nenhum modulo referencia outro modulo.
    /// <para>
    /// A whitelist ja proibiria a aresta, mas este teste declara a regra em vez de deixa-la
    /// implicita numa tabela: quem acrescentar o proximo modulo ve a intencao e a lista para
    /// alimentar, em vez de descobrir a restricao por uma linha vermelha em outro teste.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("gate", "2")]
    public void Modulos_NaoReferenciamUnsAosOutros_ComunicamSePorContratos()
    {
        IReadOnlyDictionary<string, HashSet<string>> referencias = ReferenciasPorProjeto();
        List<string> violacoes = [];

        foreach (string modulo in Modulos)
        {
            Assert.True(referencias.ContainsKey(modulo), $"modulo '{modulo}' nao existe no disco");

            foreach (string outro in Modulos)
            {
                if (!string.Equals(modulo, outro, StringComparison.Ordinal)
                    && referencias[modulo].Contains(outro))
                {
                    violacoes.Add($"{modulo} -> {outro}");
                }
            }
        }

        Assert.True(
            violacoes.Count == 0,
            "modulos so podem conversar por interface publica declarada em MotorPix.Contratos; "
            + $"arestas proibidas encontradas: {string.Join(", ", violacoes)}");
    }

    private static IReadOnlyDictionary<string, HashSet<string>> ReferenciasPorProjeto()
    {
        var mapa = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string caminho in ArquivosDoRepositorio.Projetos())
        {
            string nome = Path.GetFileNameWithoutExtension(caminho);
            var referencias = new HashSet<string>(
                Itens(XDocument.Load(caminho), "ProjectReference").Select(NomeDoProjetoReferenciado),
                StringComparer.Ordinal);

            mapa[nome] = referencias;
        }

        return mapa;
    }

    private static string CaminhoDoProjeto(string nome)
    {
        foreach (string caminho in ArquivosDoRepositorio.Projetos())
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(caminho), nome, StringComparison.Ordinal))
            {
                return caminho;
            }
        }

        throw new InvalidOperationException($"Projeto '{nome}' nao foi encontrado sob 'src' e 'tests'.");
    }

    private static bool EstaEmSrc(string caminho)
        => caminho.StartsWith(RaizDoRepositorio.Src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> InternalsVisibleToDeclarados(XDocument csproj)
        => Itens(csproj, "InternalsVisibleTo").Select(Include).Select(NomeSimplesDoAssembly);

    /// <summary>
    /// Os csproj deste repositorio nao usam o namespace antigo do MSBuild, mas comparar por
    /// <c>LocalName</c> deixa o teste imune a isso.
    /// </summary>
    private static IEnumerable<XElement> Itens(XDocument documento, string nome)
        => documento.Descendants().Where(e => string.Equals(e.Name.LocalName, nome, StringComparison.Ordinal));

    private static string Include(XElement item) => item.Attribute("Include")?.Value ?? string.Empty;

    private static string NomeDoProjetoReferenciado(XElement item)
        => Path.GetFileNameWithoutExtension(Include(item).Replace('\\', '/'));

    /// <summary>
    /// O atributo pode carregar a chave publica ("Assembly, PublicKey=..."); so o nome simples
    /// interessa para a regra de sufixo.
    /// </summary>
    private static string NomeSimplesDoAssembly(string nomeCompleto)
    {
        int virgula = nomeCompleto.IndexOf(',');
        return (virgula < 0 ? nomeCompleto : nomeCompleto[..virgula]).Trim();
    }
}

/// <summary>
/// Raiz do repositorio localizada em tempo de execucao, subindo a partir do diretorio de saida
/// da suite ate achar o arquivo de solucao. Caminho absoluto fixo no codigo quebraria em
/// qualquer maquina que nao seja a de quem escreveu.
/// </summary>
internal static class RaizDoRepositorio
{
    internal const string NomeDoArquivoDaSolucao = "MotorPix.slnx";

    private static readonly Lazy<string> Localizada = new(Localizar);

    internal static string Caminho => Localizada.Value;

    internal static string Src => Path.Combine(Caminho, "src");

    internal static string Tests => Path.Combine(Caminho, "tests");

    internal static string ArquivoDaSolucao => Path.Combine(Caminho, NomeDoArquivoDaSolucao);

    /// <summary>Caminho relativo a raiz, sempre com barra normal, para mensagem de falha estavel.</summary>
    internal static string Relativo(string caminhoAbsoluto)
        => Path.GetRelativePath(Caminho, caminhoAbsoluto).Replace('\\', '/');

    private static string Localizar()
    {
        string inicio = Path.GetFullPath(AppContext.BaseDirectory);
        var visitados = new List<string>();
        DirectoryInfo? atual = new(inicio);

        while (atual is not null)
        {
            visitados.Add(atual.FullName);

            if (File.Exists(Path.Combine(atual.FullName, NomeDoArquivoDaSolucao)))
            {
                return atual.FullName;
            }

            atual = atual.Parent;
        }

        throw new InvalidOperationException(
            $"Nao encontrei '{NomeDoArquivoDaSolucao}' subindo a partir de '{inicio}'. "
                + $"Diretorios visitados: {string.Join(" -> ", visitados)}.");
    }
}

/// <summary>
/// Enumeracao dos arquivos do repositorio que sao codigo escrito por gente. Tudo sob
/// <c>obj</c> e <c>bin</c> fica de fora: sao artefatos de build (AssemblyInfo gerado, por
/// exemplo) e varre-los faria as varreduras textuais julgarem codigo que ninguem escreveu.
/// </summary>
internal static class ArquivosDoRepositorio
{
    internal static IReadOnlyList<string> Projetos()
        => Buscar("*.csproj", RaizDoRepositorio.Src, RaizDoRepositorio.Tests);

    internal static IReadOnlyList<string> FontesDeSrc() => Buscar("*.cs", RaizDoRepositorio.Src);

    internal static IReadOnlyList<string> FontesDeTests() => Buscar("*.cs", RaizDoRepositorio.Tests);

    private static IReadOnlyList<string> Buscar(string padrao, params string[] diretorios)
        => diretorios
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, padrao, SearchOption.AllDirectories))
            .Where(EhCodigoDeGente)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

    private static bool EhCodigoDeGente(string caminho)
    {
        foreach (string parte in caminho.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.Equals(parte, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parte, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
