using MotorPix.Contratos;
using MotorPix.Dominio.Excecoes;
using MotorPix.Dominio.Identificadores;

namespace MotorPix.Composicao;

/// <summary>
/// Barramento in-process com entrega diferida (outbox).
/// <para>
/// <see cref="Enfileirar"/> so registra; a entrega acontece em <see cref="Drenar"/>, chamado
/// explicitamente pelo host ou pelo teste. Nao existe scheduler: o diagrama nao tem esse no, e
/// inventar um <c>hosted service</c> seria criar um modulo ausente do desenho.
/// </para>
/// <para>
/// A entrega e <b>uma mensagem por vez</b>, e o envelope so sai da fila depois de entregue. A
/// versao anterior tirava a rodada inteira da fila antes de entregar: uma entrega que lancasse
/// levava junto todas as mensagens seguintes, em definitivo — o SPI liquidava, o credito do
/// recebedor nunca acontecia, <c>Pendentes</c> voltava a zero e a soma por ledger continuava
/// fechando, entao nenhuma propriedade contabil acusava a perda.
/// </para>
/// </summary>
public sealed class BarramentoEmMemoria : IBarramento
{
    private readonly object _trava = new();
    private readonly Queue<Envelope> _fila = new();
    private readonly Dictionary<Ispb, IParticipante> _participantes = [];

    private ISpi? _spi;

    /// <summary>
    /// Marcado por thread, e nao por instancia: a reentrancia que interessa e a da mesma pilha de
    /// chamadas. Um <c>bool</c> de instancia acusaria reentrancia falsa numa segunda thread que
    /// apenas drenasse em paralelo, e abortaria uma drenagem legitima.
    /// </summary>
    [ThreadStatic]
    private static bool _drenandoNestaThread;

    public int Pendentes
    {
        get
        {
            lock (_trava)
            {
                return _fila.Count;
            }
        }
    }

    /// <summary>Total entregue desde sempre. Existe para o teste conferir que nada se perdeu.</summary>
    public int TotalEntregue
    {
        get
        {
            lock (_trava)
            {
                return _totalEntregue;
            }
        }
    }

    private int _totalEntregue;

    public void RegistrarSpi(ISpi spi)
    {
        ArgumentNullException.ThrowIfNull(spi);
        _spi = spi;
    }

    public void RegistrarParticipante(IParticipante participante)
    {
        ArgumentNullException.ThrowIfNull(participante);

        lock (_trava)
        {
            _participantes[participante.Ispb] = participante;
        }
    }

    /// <summary>Participantes registrados. Fonte unica: o SPI consome este mesmo mapa.</summary>
    public IReadOnlyDictionary<Ispb, IParticipante> Participantes
    {
        get
        {
            lock (_trava)
            {
                return new Dictionary<Ispb, IParticipante>(_participantes);
            }
        }
    }

    public void Enfileirar(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        lock (_trava)
        {
            _fila.Enqueue(envelope);
        }
    }

    public int Drenar()
    {
        // Drenar de dentro de uma entrega reintroduziria a pilha reentrante que o outbox existe
        // para eliminar: o pacs.002 chegaria ao pagador antes de a transicao para ENVIADA_SPI ter
        // sido gravada, e o par (VALIDADA, pacs.002) — que o diagrama nao tem — apareceria no
        // caminho feliz.
        if (_drenandoNestaThread)
        {
            throw new MensageriaInvalidaException("drenagem reentrante do barramento");
        }

        _drenandoNestaThread = true;
        int entregues = 0;

        try
        {
            while (true)
            {
                Envelope envelope;

                lock (_trava)
                {
                    if (_fila.Count == 0)
                    {
                        break;
                    }

                    // Espiar sem remover: o envelope so sai da fila depois de entregue com
                    // sucesso, entao uma entrega que lance deixa a mensagem exatamente onde estava
                    // e as seguintes intactas.
                    envelope = _fila.Peek();
                }

                Entregar(envelope);

                lock (_trava)
                {
                    _fila.Dequeue();
                    _totalEntregue++;
                }

                entregues++;
            }
        }
        finally
        {
            _drenandoNestaThread = false;
        }

        return entregues;
    }

    private void Entregar(Envelope envelope)
    {
        switch (envelope)
        {
            case EnvelopePacs008 pacs008:
                if (_spi is null)
                {
                    throw new MensageriaInvalidaException("barramento sem SPI registrado");
                }

                _spi.ReceberPacs008(pacs008.Ordem);
                break;

            case EnvelopePacs004 pacs004:
                if (_spi is null)
                {
                    throw new MensageriaInvalidaException("barramento sem SPI registrado");
                }

                _spi.ReceberPacs004(pacs004.Devolucao);
                break;

            case EnvelopePacs002 pacs002:
                IParticipante? participante;

                lock (_trava)
                {
                    _participantes.TryGetValue(pacs002.Destinatario, out participante);
                }

                if (participante is null)
                {
                    throw new MensageriaInvalidaException(
                        $"barramento sem participante registrado para {pacs002.Destinatario}");
                }

                participante.Receber(pacs002.Confirmacao);
                break;

            default:
                throw new MensageriaInvalidaException($"envelope desconhecido: {envelope.GetType().Name}");
        }
    }
}
