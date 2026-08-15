using MotorPix.Dominio.Identificadores;

namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Mesmo <see cref="EndToEndId"/>, pedido diferente.
/// <para>
/// Desvio declarado da letra do prompt, que manda "reenvio retorna a resposta original". Replay
/// cego significaria responder "aceito" a um pagamento que nao foi o executado: quem reenviasse o
/// mesmo E2E com valor dez vezes maior receberia a confirmacao do pagamento antigo e teria toda
/// razao de acreditar que o novo passou. Num motor de pagamentos, esse e o pior modo de falha
/// disponivel.
/// </para>
/// <para>
/// A impressao digital comparada e canonica — conta de origem, chave normalizada e centavos —,
/// entao duas grafias da mesma chave nao produzem conflito falso.
/// </para>
/// </summary>
public sealed class E2eConflitanteException : MotorPixException
{
    public E2eConflitanteException(EndToEndId e2e, string impressaoNova, string impressaoOriginal)
        : base($"EndToEndId {e2e} ja foi usado para outro pedido. Recebido '{impressaoNova}', original '{impressaoOriginal}'.")
    {
        E2E = e2e;
        ImpressaoNova = impressaoNova;
        ImpressaoOriginal = impressaoOriginal;
    }

    public EndToEndId E2E { get; }

    public string ImpressaoNova { get; }

    public string ImpressaoOriginal { get; }
}
