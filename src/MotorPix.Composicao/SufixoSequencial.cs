using MotorPix.Dominio.Tempo;

namespace MotorPix.Composicao;

/// <summary>
/// Fonte de sufixo padrao do composition root: um contador em base 36, sem nenhuma leitura ambiente.
/// <para>
/// Deliberadamente <b>nao</b> e aleatoria. O motor precisa de um sufixo que nao repita dentro do
/// mesmo minuto — porque o resto do <c>EndToEndId</c> tem precisao de minuto —, e um contador
/// entrega isso com a vantagem de ser reproduzivel. Aleatoriedade de verdade exigiria
/// <c>System.Random</c>, que esta banido justamente por tornar o replay por semente ilusorio.
/// </para>
/// <para>
/// Num sistema real, dois participantes gerando E2E com a mesma sequencia colidiriam; aqui nao ha
/// esse risco, porque o ISPB de quem origina ja entra no identificador. Se um dia houver, o lugar
/// de resolver e uma implementacao diferente desta interface — nao um <c>Random</c> escondido.
/// </para>
/// </summary>
public sealed class SufixoSequencial : IFonteAleatoria
{
    private const string Alfabeto = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int Comprimento = 11;

    private readonly object _trava = new();
    private long _proximo;

    public SufixoSequencial(long inicio = 1) => _proximo = inicio;

    public string SufixoDeE2e()
    {
        long valor;

        lock (_trava)
        {
            valor = _proximo++;
        }

        Span<char> sufixo = stackalloc char[Comprimento];
        long restante = valor;

        for (int i = Comprimento - 1; i >= 0; i--)
        {
            sufixo[i] = Alfabeto[(int)(restante % Alfabeto.Length)];
            restante /= Alfabeto.Length;
        }

        return new string(sufixo);
    }
}
