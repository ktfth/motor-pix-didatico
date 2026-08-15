#language: pt
@gate3
Funcionalidade: Caminho feliz do pagamento Pix
  Como cliente pagador
  Quero que meu pagamento chegue à conta do recebedor
  Para que o dinheiro saia da minha conta e entre na dele, sem sumir no meio

  As setas 1 a 7 do diagrama de arquitetura, escritas na linguagem de quem usa o motor.
  Os saldos esperados são escritos à mão de propósito: derivá-los das funções que o motor
  usa provaria apenas que as funções são determinísticas.

  Contexto:
    Dado que o motor está montado com fundo inicial de "1.000,00" para cada participante

  Cenário: o dinheiro chega ao recebedor e o pool do SPI não muda
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    Então a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.250,00"
    E a conta PI do pagador é "750,00"
    E a conta PI do recebedor é "1.250,00"
    E o pool do SPI é "2.000,00"

  Cenário: antes de drenar, o débito já aconteceu e a liquidação ainda não
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    Então a resposta tem estado "ENVIADA_SPI"
    E a resposta não traz motivo de rejeição
    E há 1 mensagem pendente no barramento
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.000,00"
    E a conta PI do pagador é "1.000,00"

  Cenário: o espelho reflete o débito antes da liquidação, e isso é o dinheiro em trânsito
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    Então o espelho do pagador é "750,00"
    E a conta PI do pagador é "1.000,00"
    Quando o barramento é drenado
    Então o espelho do pagador é "750,00"
    E a conta PI do pagador é "750,00"

  Cenário: em quiescência não sobra nada em trânsito
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    Então o espelho e a conta PI coincidem nos dois participantes
    E não há mensagem pendente no barramento

  Cenário: o histórico registra exatamente as três transições do diagrama
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    Então o histórico da transação é exatamente:
      | origem      | evento                            | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                      | LIQUIDADA   |

  Cenário: o drenar entrega o pacs.008 e os dois pacs.002
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    Então o barramento entrega 3 mensagens ao ser drenado

  Esquema do Cenário: qualquer valor dentro do saldo move exatamente aquele valor
    Quando o cliente pagador paga "<valor>" para a chave do recebedor
    E o barramento é drenado
    Então o saldo do cliente pagador é "<sobra>"
    E o saldo do cliente recebedor é "<destino>"
    E o pool do SPI é "2.000,00"

    Exemplos:
      | valor    | sobra    | destino  |
      | 0,01     | 999,99   | 1.000,01 |
      | 1,00     | 999,00   | 1.001,00 |
      | 250,00   | 750,00   | 1.250,00 |
      | 999,99   | 0,01     | 1.999,99 |
      | 1.000,00 | 0,00     | 2.000,00 |
