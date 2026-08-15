namespace MotorPix.Dominio.Excecoes;

/// <summary>
/// Raiz de toda excecao de regra de negocio do motor.
/// O prompt proibe <c>Exception</c> e <c>InvalidOperationException</c> genericas para regra de
/// negocio; um teste de arquitetura garante que nenhuma excecao do dominio escape desta raiz.
/// </summary>
public abstract class MotorPixException : Exception
{
    protected MotorPixException(string mensagem)
        : base(mensagem)
    {
    }

    protected MotorPixException(string mensagem, Exception excecaoInterna)
        : base(mensagem, excecaoInterna)
    {
    }
}
