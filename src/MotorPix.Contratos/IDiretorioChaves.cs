using System.Diagnostics.CodeAnalysis;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Contratos;

/// <summary>
/// Resultado da resolucao de uma chave: o participante que a detem e a conta de destino dentro do
/// ledger dele.
/// <para>
/// A <see cref="Conta"/> ja vem enderecada ao ledger do participante — e por isso que
/// <see cref="ContaId"/> carrega o <c>LedgerId</c>. Devolver so o numero da conta obrigaria o
/// chamador a montar o endereco, e montar errado significa creditar no ledger de outro PSP.
/// </para>
/// <para>
/// As invariantes sao validadas <em>aqui</em>, e nao dentro de uma implementacao especifica de
/// <see cref="IDiretorioChaves"/>: se morassem no diretorio in-memory, qualquer outra implementacao
/// poderia devolver uma resolucao incoerente sem passar por guarda nenhuma, e quem consome a
/// interface nao tem como saber de qual implementacao veio o objeto.
/// </para>
/// </summary>
public sealed record ResolucaoChave
{
    public ResolucaoChave(ChavePix chave, Ispb ispb, ContaId conta)
    {
        ArgumentNullException.ThrowIfNull(chave);
        ArgumentNullException.ThrowIfNull(ispb);
        ArgumentNullException.ThrowIfNull(conta);

        // A classe e conferida antes do ledger porque da o diagnostico mais preciso: uma conta PI
        // vive no ledger do SPI por construcao, entao a guarda de ledger dispararia primeiro e
        // diria "ledger errado" quando o defeito real e "isso nem e conta de cliente".
        if (conta.Classe != ClasseConta.Cliente)
        {
            throw new VinculoInconsistenteException(
                chave,
                ispb,
                conta,
                $"chave so pode enderecar conta de cliente, e esta e {conta.Classe}");
        }

        LedgerId esperado = LedgerId.Psp(ispb);

        if (!conta.Ledger.Equals(esperado))
        {
            throw new VinculoInconsistenteException(
                chave,
                ispb,
                conta,
                $"a conta pertence ao ledger '{conta.Ledger}', esperado '{esperado}'");
        }

        Chave = chave;
        Ispb = ispb;
        Conta = conta;
    }

    public ChavePix Chave { get; }

    public Ispb Ispb { get; }

    public ContaId Conta { get; }
}

/// <summary>
/// Diretorio de chaves: chave para ISPB mais conta (seta 2 do diagrama de arquitetura).
/// <para>
/// Esta interface e a unica coisa que o PSP pagador conhece do modulo Dict. Nenhum tipo interno do
/// Dict atravessa esta fronteira: o que trafega sao value objects do dominio.
/// </para>
/// </summary>
public interface IDiretorioChaves
{
    /// <summary>
    /// Resolve a chave ou lanca <see cref="ChaveNaoEncontradaNoDictException"/>.
    /// <para>
    /// Chave ausente e um resultado normal do negocio — a aresta "validacao local falha" da maquina
    /// de estados —, nao um defeito. O caminho sem excecao e <see cref="TryResolver"/>; este existe
    /// para quem ja sabe que a chave deveria estar la.
    /// </para>
    /// </summary>
    ResolucaoChave Resolver(ChavePix chave);

    /// <summary>
    /// Tenta resolver sem lancar.
    /// <para>
    /// O <see cref="NotNullWhenAttribute"/> nao e enfeite: sem ele, o consumidor que faz
    /// <c>if (TryResolver(...)) { ... resolucao.Ispb ... }</c> recebe CS8602, que vira <em>erro</em>
    /// porque a solucao compila com <c>TreatWarningsAsErrors</c>. O autor entao silencia com
    /// <c>!</c>, e a supressao passa a esconder tambem o caso em que uma implementacao devolva
    /// <c>true</c> com <c>null</c>.
    /// </para>
    /// </summary>
    bool TryResolver(ChavePix chave, [NotNullWhen(true)] out ResolucaoChave? resolucao);
}
