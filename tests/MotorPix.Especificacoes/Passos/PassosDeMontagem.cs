using MotorPix.Especificacoes.Suporte;
using TechTalk.SpecFlow;

namespace MotorPix.Especificacoes.Passos;

/// <summary>
/// Os passos que montam o mundo antes do primeiro "Quando": participantes, fundo inicial e,
/// quando o cenario precisa, o limite de timeout.
/// </summary>
[Binding]
public sealed class PassosDeMontagem
{
    private readonly ContextoDoMotor _contexto;

    public PassosDeMontagem(ContextoDoMotor contexto) => _contexto = contexto;

    [Given(@"que o motor está montado com fundo inicial de ""([^""]*)"" para cada participante")]
    public void DadoMotorMontado(string fundo) =>
        _contexto.Montar(Dinheiro.EmCentavos(fundo));

    [Given(@"que o motor está montado com fundo inicial de ""([^""]*)"" e timeout de (\d+) segundos")]
    public void DadoMotorMontadoComTimeout(string fundo, int segundos) =>
        _contexto.Montar(Dinheiro.EmCentavos(fundo), TimeSpan.FromSeconds(segundos));

    [Given(@"que o relógio avançou (\d+) segundos")]
    [When(@"o relógio avança (\d+) segundos")]
    public void QuandoRelogioAvanca(int segundos) =>
        _contexto.Relogio.Avancar(TimeSpan.FromSeconds(segundos));
}
