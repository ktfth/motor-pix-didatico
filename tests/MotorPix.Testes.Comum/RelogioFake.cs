using MotorPix.Dominio.Tempo;

namespace MotorPix.Testes.Comum;

/// <summary>
/// Relogio controlado pelo teste.
/// <para>
/// Escrito a mao, sem dependencia: sao poucas linhas e o controle do instante de avaliacao e
/// total. Nenhum teste do motor usa <c>Task.Delay</c> ou <c>Thread.Sleep</c> — tempo aqui e um
/// parametro, nao uma espera.
/// </para>
/// </summary>
public sealed class RelogioFake : IClock
{
    /// <summary>
    /// Instante inicial arbitrario mas fixo. Ser fixo e o ponto: qualquer teste que dependa do
    /// valor concreto falha do mesmo jeito em qualquer maquina.
    /// </summary>
    public static readonly DateTimeOffset Epoca = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _agora;

    public RelogioFake()
        : this(Epoca)
    {
    }

    public RelogioFake(DateTimeOffset inicio) => _agora = inicio;

    public DateTimeOffset Agora => _agora;

    public void Avancar(TimeSpan intervalo)
    {
        if (intervalo < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalo), "O relogio do motor nao anda para tras.");
        }

        _agora += intervalo;
    }

    public void AvancarSegundos(int segundos) => Avancar(TimeSpan.FromSeconds(segundos));

    public void Posicionar(DateTimeOffset instante)
    {
        if (instante < _agora)
        {
            throw new ArgumentOutOfRangeException(nameof(instante), "O relogio do motor nao anda para tras.");
        }

        _agora = instante;
    }
}
