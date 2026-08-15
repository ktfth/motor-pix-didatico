using MotorPix.Dominio.Tempo;

namespace MotorPix.Composicao;

/// <summary>
/// A unica implementacao que le o relogio do sistema.
/// <para>
/// Este arquivo mora no composition root justamente porque e o unico projeto que nao carrega
/// <c>BannedSymbols.txt</c>. Em qualquer outro lugar do motor, tocar <c>DateTimeOffset.UtcNow</c>
/// e erro de compilacao — nao convencao de revisao.
/// </para>
/// </summary>
public sealed class RelogioSistema : IClock
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}
