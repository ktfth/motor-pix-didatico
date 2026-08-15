using MotorPix.Dominio.Identificadores;
using MotorPix.Dominio.Tempo;

namespace MotorPix.Psp.Nucleo;

/// <summary>
/// Produz o <c>EndToEndId</c> das transacoes que o proprio motor origina — hoje, so a devolucao.
/// <para>
/// O pagamento normal <b>nao</b> passa por aqui: o E2E dele vem de fora, com o cliente, pela seta 1.
/// A devolucao e diferente porque e transacao nova originada pelo PSP, e alguem tem de escolher o
/// identificador.
/// </para>
/// </summary>
public interface IGeradorEndToEndId
{
    EndToEndId Gerar(Ispb ispbDoOriginador);
}

/// <summary>
/// Compoe o identificador a partir das duas fontes ambientais injetadas: o instante vem do
/// <see cref="IClock"/> e o sufixo da <see cref="IFonteAleatoria"/>.
/// <para>
/// Nao ha nenhuma leitura ambiente aqui dentro — e por isso que uma execucao com relogio fake e
/// fonte deterministica reproduz identificador por identificador.
/// </para>
/// </summary>
public sealed class GeradorDeE2e(IClock relogio, IFonteAleatoria aleatoria) : IGeradorEndToEndId
{
    private readonly IClock _relogio = relogio ?? throw new ArgumentNullException(nameof(relogio));

    private readonly IFonteAleatoria _aleatoria = aleatoria ?? throw new ArgumentNullException(nameof(aleatoria));

    public EndToEndId Gerar(Ispb ispbDoOriginador)
    {
        ArgumentNullException.ThrowIfNull(ispbDoOriginador);

        return EndToEndId.Compor(ispbDoOriginador, _relogio.Agora, _aleatoria.SufixoDeE2e());
    }
}
