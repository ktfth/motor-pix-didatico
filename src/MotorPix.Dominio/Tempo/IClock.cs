namespace MotorPix.Dominio.Tempo;

/// <summary>
/// Unica fonte de tempo do sistema (invariante 9).
/// <para>
/// Expoe <see cref="DateTimeOffset"/> e nao <see cref="DateTime"/> de proposito: <c>DateTime</c>
/// carrega um <c>Kind</c> que se perde em serializacao e comparacao, e um instante com offset
/// ambiguo e bug silencioso num motor de pagamentos.
/// </para>
/// <para>
/// A implementacao que le o relogio do sistema mora em MotorPix.Composicao, o unico projeto que
/// nao carrega <c>BannedSymbols.txt</c>. Em qualquer outro lugar, tocar <c>DateTime.UtcNow</c>
/// e erro de compilacao.
/// </para>
/// </summary>
public interface IClock
{
    DateTimeOffset Agora { get; }
}
