namespace MotorPix.Dominio.Tempo;

/// <summary>
/// A segunda fonte ambiental do sistema, ao lado de <see cref="IClock"/>.
/// <para>
/// O invariante 9 cobre o tempo e nada mais, mas os 11 caracteres finais do <c>EndToEndId</c> sao
/// tao ambientais quanto o relogio: sem injeta-los, a devolucao gera um identificador diferente a
/// cada execucao, o contraexemplo encontrado por semente nao reproduz e o replay do gate 8 nao
/// reconstroi os mesmos identificadores.
/// </para>
/// <para>
/// A interface e minima de proposito — devolve um sufixo pronto, e nao bytes ou um inteiro. Assim
/// o alfabeto e o comprimento ficam do lado de quem sabe o que e um E2E, e nao de quem sabe sortear.
/// </para>
/// </summary>
public interface IFonteAleatoria
{
    /// <summary>Sufixo de 11 alfanumericos ASCII para compor um <c>EndToEndId</c>.</summary>
    string SufixoDeE2e();
}
