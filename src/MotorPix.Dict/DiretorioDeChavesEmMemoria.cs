using System.Diagnostics.CodeAnalysis;
using MotorPix.Contratos;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dict;

/// <summary>
/// Diretorio de chaves in-memory.
/// <para>
/// A normalizacao nao acontece aqui: ela mora dentro de <see cref="ChavePix"/>, e este diretorio
/// indexa pela chave ja canonica. E o que faz <c>"529.982.247-25"</c> e <c>"52998224725"</c>
/// encontrarem a mesma entrada sem que o diretorio precise saber o que e um CPF.
/// </para>
/// <para>
/// A coerencia do vinculo (conta de cliente, no ledger do participante declarado) e garantida pelo
/// construtor de <see cref="ResolucaoChave"/>, nao por este tipo: assim ela vale para qualquer
/// implementacao de <see cref="IDiretorioChaves"/>, e nao so para esta.
/// </para>
/// <para>
/// Nao ha ordem de insercao observavel nem iteracao publica: o diretorio responde por chave e mais
/// nada. Qualquer consulta que dependesse de varredura seria uma porta para o modulo vazar sua
/// estrutura interna.
/// </para>
/// </summary>
public sealed class DiretorioDeChavesEmMemoria : IDiretorioChaves, IRegistroDeChaves
{
    private readonly object _trava = new();
    private readonly Dictionary<ChavePix, ResolucaoChave> _entradas = [];

    public DiretorioDeChavesEmMemoria()
    {
    }

    public DiretorioDeChavesEmMemoria(IEnumerable<ResolucaoChave> vinculos)
    {
        ArgumentNullException.ThrowIfNull(vinculos);

        foreach (ResolucaoChave vinculo in vinculos)
        {
            Vincular(vinculo);
        }
    }

    /// <summary>Quantidade de chaves vinculadas. Existe para teste e diagnostico, nao para varredura.</summary>
    public int Quantidade
    {
        get
        {
            lock (_trava)
            {
                return _entradas.Count;
            }
        }
    }

    /// <summary>
    /// Vincula uma chave a um participante e conta.
    /// <para>
    /// Recusa rebind: uma chave ja vinculada nao muda de titular por um segundo registro. Permitir
    /// silenciosamente faria a mesma chave resolver para contas diferentes conforme a ordem de
    /// registro, e o pagamento cairia em quem chegou por ultimo.
    /// </para>
    /// </summary>
    public void Vincular(ResolucaoChave vinculo)
    {
        ArgumentNullException.ThrowIfNull(vinculo);

        lock (_trava)
        {
            if (_entradas.TryGetValue(vinculo.Chave, out ResolucaoChave? existente))
            {
                throw new ChaveJaVinculadaException(vinculo.Chave, existente.Ispb);
            }

            _entradas.Add(vinculo.Chave, vinculo);
        }
    }

    /// <summary>
    /// As guardas de nulo ficam aqui, e nao so no construtor de <see cref="ResolucaoChave"/>: sem
    /// elas, uma conta nula produziria <c>NullReferenceException</c> saindo de API publica, e uma
    /// chave nula viraria um <c>ArgumentNullException</c> com <c>ParamName</c> "key", vazando o
    /// dicionario interno no diagnostico.
    /// </summary>
    public void Vincular(ChavePix chave, Ispb ispb, ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(chave);
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(conta);

        Vincular(new ResolucaoChave(chave, ispb, conta));
    }

    public ResolucaoChave Resolver(ChavePix chave)
    {
        ArgumentNullException.ThrowIfNull(chave);

        lock (_trava)
        {
            return _entradas.TryGetValue(chave, out ResolucaoChave? resolucao)
                ? resolucao
                : throw new ChaveNaoEncontradaNoDictException(chave);
        }
    }

    public bool TryResolver(ChavePix chave, [NotNullWhen(true)] out ResolucaoChave? resolucao)
    {
        ArgumentNullException.ThrowIfNull(chave);

        lock (_trava)
        {
            return _entradas.TryGetValue(chave, out resolucao);
        }
    }
}
