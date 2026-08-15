#language: pt
@gate6
Funcionalidade: Devolução de um pagamento Pix
  Como PSP recebedor
  Quero devolver ao pagador um pagamento que já foi creditado ao meu cliente
  Para que o dinheiro volte inteiro sem que a liquidação original seja desfeita

  A seta pontilhada SM_R -.-> VAL do diagrama de arquitetura, e a aresta estados:16 do diagrama
  de estados. A devolução é uma transação NOVA, com EndToEndId próprio, que apenas referencia a
  original: ela percorre a mesma máquina normativa desde RECEBIDA e termina em LIQUIDADA.
  Quem termina em DEVOLVIDA é a original — e só quando a devolução liquida.

  Os saldos esperados são escritos à mão. O erro que estes cenários existem para tornar visível é
  a troca dos papéis: marcar a devolução como DEVOLVIDA, ou reabrir a original em vez de criar
  transação nova, deixaria todas as somas por participante fechando em zero mesmo assim.

  Contexto:
    Dado que o motor está montado com fundo inicial de "1.000,00" para cada participante

  Cenário: a devolução liquida como transação própria e a original termina em DEVOLVIDA
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    Então a transação está em "LIQUIDADA"
    E o saldo do cliente recebedor é "1.250,00"
    Quando o recebedor solicita a devolução de "250,00"
    Então a devolução está em "ENVIADA_SPI"
    E a devolução é uma transação nova, com E2E próprio emitido pelo recebedor
    E a original não virou devolução
    E há 1 mensagem pendente no barramento
    E o barramento entrega 3 mensagens ao ser drenado
    E a devolução está em "LIQUIDADA"
    E a transação está em "DEVOLVIDA"

  Cenário: o dinheiro volta inteiro ao cliente pagador e o pool do SPI não muda
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor solicita a devolução de "250,00"
    E o barramento é drenado
    Então o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o espelho do pagador é "1.000,00"
    E o espelho do recebedor é "1.000,00"
    E a conta PI do pagador é "1.000,00"
    E a conta PI do recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"
    E o espelho e a conta PI coincidem nos dois participantes
    E não há mensagem pendente no barramento

  Cenário: a devolução percorre a mesma máquina do pagamento, e a original só ganha a aresta estados:16
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor solicita a devolução de "250,00"
    E o barramento é drenado
    Então o histórico da devolução é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                        | LIQUIDADA   |
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                        | LIQUIDADA   |
      | LIQUIDADA   | DEVOLUCAO_LIQUIDADA                 | DEVOLVIDA   |
    E a original não virou devolução

  Esquema do Cenário: devolução de valor diferente do crédito recebido é recusada, e não queima a original
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor tenta devolver "<pedido>"
    Então o pedido de devolução é recusado porque devolução parcial está fora de escopo
    E a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.250,00"
    E não há mensagem pendente no barramento
    Quando o recebedor solicita a devolução de "250,00"
    E o barramento é drenado
    Então a devolução está em "LIQUIDADA"
    E a transação está em "DEVOLVIDA"
    E o saldo do cliente pagador é "1.000,00"

    Exemplos:
      | pedido |
      | 250,01 |
      | 500,00 |
      | 249,99 |
      | 0,01   |

  Cenário: não se devolve pagamento que ainda não foi creditado
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o recebedor tenta devolver "250,00"
    Então o pedido de devolução é recusado porque não há crédito recebido para esse E2E
    E a transação está em "ENVIADA_SPI"
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.000,00"
    E há 1 mensagem pendente no barramento

  Cenário: a segunda devolução da mesma original é recusada, antes e depois de a primeira liquidar
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor solicita a devolução de "250,00"
    E o recebedor tenta devolver "250,00"
    Então o pedido de devolução é recusado porque a original já foi devolvida
    E a transação está em "LIQUIDADA"
    E há 1 mensagem pendente no barramento
    Quando o barramento é drenado
    Então a transação está em "DEVOLVIDA"
    Quando o recebedor tenta devolver "250,00"
    Então o pedido de devolução é recusado porque a original já foi devolvida
    E a transação está em "DEVOLVIDA"
    E o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"

  Cenário: devolução de devolução é recusada, porque devolução não é crédito recebido
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor solicita a devolução de "250,00"
    E o barramento é drenado
    Então a devolução está em "LIQUIDADA"
    Quando o recebedor tenta devolver a devolução de "250,00"
    Então o pedido de devolução é recusado porque não há crédito recebido para esse E2E
    E o pagador não conhece a devolução
    E a transação está em "DEVOLVIDA"
    E o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"

  Cenário: devolução despachada e sem resposta do SPI deixa a original em LIQUIDADA
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor solicita a devolução de "250,00"
    Então há 1 mensagem pendente no barramento
    Quando o relógio avança 300 segundos
    E o pagador varre os vencidos
    Então a devolução está em "ENVIADA_SPI"
    E a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "750,00"
    E a conta PI do pagador é "750,00"
    E a conta PI do recebedor é "1.250,00"
    E o pool do SPI é "2.000,00"

  Cenário: devolução recusada pelo SPI deixa a original em LIQUIDADA e estorna quem devolveu
    O SPI só liquida a devolução que espelha exatamente a original. Para chegar a uma devolução
    recusada é preciso partir de livros já divergentes: o recebedor credita "5.000,00" por um E2E
    cujo lastro no SPI será de "250,00". O que importa aqui é o gatilho da aresta estados:16 ser
    "pacs.004 liquidada" — devolução que não liquida não devolve nada.

    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o recebedor credita "5.000,00" pelo pagamento sem lastro no SPI
    E o barramento é drenado
    Então a transação está em "LIQUIDADA"
    E o saldo do cliente recebedor é "6.000,00"
    Quando o recebedor solicita a devolução de "5.000,00"
    Então a devolução está em "ENVIADA_SPI"
    E o saldo do cliente recebedor é "1.000,00"
    Quando o barramento é drenado
    Então o SPI respondeu "ORDEM_INVALIDA" à devolução
    E a devolução está em "REJEITADA"
    E a transação está em "LIQUIDADA"
    E o saldo do cliente recebedor é "6.000,00"
    E o saldo do cliente pagador é "750,00"
    E a conta PI do pagador é "750,00"
    E a conta PI do recebedor é "1.250,00"

  Cenário: reentrega do pacs.002 da devolução não credita duas vezes nem transiciona a original de novo
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o recebedor solicita a devolução de "250,00"
    E o barramento é drenado
    Então a transação está em "DEVOLVIDA"
    E o saldo do cliente pagador é "1.000,00"
    Quando o pagador recebe novamente o pacs.002 da devolução
    E o pagador recebe novamente o pacs.002 da devolução
    Então a transação está em "DEVOLVIDA"
    E o saldo do cliente pagador é "1.000,00"
    E o espelho do pagador é "1.000,00"
    E o pool do SPI é "2.000,00"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                        | LIQUIDADA   |
      | LIQUIDADA   | DEVOLUCAO_LIQUIDADA                 | DEVOLVIDA   |
