#language: pt
@gate5
Funcionalidade: Timeout e consulta de status
  Como responsável pelo PSP pagador
  Quero que a falta de resposta do SPI leve a transação a EXPIRADA e a nada mais
  Para que nenhum pagamento seja confirmado nem estornado por suposição

  Invariante 6: timeout não é falha. O pacs.008 pode ter chegado ao SPI e liquidado; o que
  faltou foi a RESPOSTA. Por isso a expiração não estorna — devolver o dinheiro aqui poderia
  criar dinheiro, já que ele talvez esteja na conta do recebedor. De EXPIRADA só se sai por
  consulta de status, e consulta indeterminada é resposta honesta, não transição.

  "O pacs.002 que nunca chega" não é uma espera que falhou: é uma drenagem que não aconteceu.
  Com o outbox explícito, o cenário canônico se escreve simplesmente não drenando o barramento.
  Todos os saldos abaixo são escritos à mão: derivá-los das funções que o motor usa provaria
  apenas que as funções são determinísticas.

  Contexto:
    Dado que o motor está montado com fundo inicial de "1.000,00" e timeout de 20 segundos

  Cenário: sem pacs.002 dentro do prazo, a varredura leva a transação a EXPIRADA
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    Então a transação está em "ENVIADA_SPI"
    E há 1 mensagem pendente no barramento
    Quando o relógio avança 20 segundos
    E o pagador varre os vencidos
    Então a transação está em "EXPIRADA"
    E há 1 mensagem pendente no barramento
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |

  Esquema do Cenário: a fronteira do prazo é fechada — exatamente no limite já venceu
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o relógio avança <segundos> segundos
    E o pagador varre os vencidos
    Então a transação está em "<estado>"

    Exemplos:
      | segundos | estado      |
      | 19       | ENVIADA_SPI |
      | 20       | EXPIRADA    |
      | 21       | EXPIRADA    |

  Cenário: expirar não é rejeitar — o débito fica de pé e nada é estornado
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o relógio avança 20 segundos
    E o pagador varre os vencidos
    Então a transação está em "EXPIRADA"
    E o saldo do cliente pagador é "900,00"
    E o espelho do pagador é "900,00"
    E o saldo do cliente recebedor é "1.000,00"
    E a conta PI do pagador é "1.000,00"
    E a conta PI do recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: o prazo vencido não expira nada — quem expira é a varredura
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o relógio avança 60 segundos
    Então a transação está em "ENVIADA_SPI"
    Quando o barramento é drenado
    Então a transação está em "LIQUIDADA"
    E a varredura expira 0 transações
    E o saldo do cliente recebedor é "1.100,00"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                        | LIQUIDADA   |

  Cenário: varrer duas vezes não expira de novo
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o relógio avança 20 segundos
    Então a varredura expira 1 transação
    E a varredura expira 0 transações
    E a transação está em "EXPIRADA"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |

  Cenário: o SPI nunca viu a ordem, então a consulta é indeterminada e não move nada
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o relógio avança 20 segundos
    E o pagador varre os vencidos
    Então a consulta de status responde "INDETERMINADA"
    E a transação está em "EXPIRADA"
    E o saldo do cliente pagador é "900,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |

  Cenário: o SPI liquidou e a resposta se perdeu — a consulta fecha o pagador sem creditar o recebedor
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o SPI recebe a ordem de "100,00" na mão, sem que a resposta seja entregue
    Então a conta PI do pagador é "900,00"
    E a conta PI do recebedor é "1.100,00"
    E o saldo do cliente recebedor é "1.000,00"
    E há 3 mensagens pendentes no barramento
    Quando o relógio avança 20 segundos
    E o pagador varre os vencidos
    Então a transação está em "EXPIRADA"
    E a consulta de status responde "LIQUIDADA"
    E a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "900,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |
      | EXPIRADA    | CONSULTA_CONFIRMA_LIQUIDACAO        | LIQUIDADA   |

  Cenário: o pacs.002 ACSC atrasado é registrado e não transiciona — a consulta é que fecha
    Quando o cliente pagador paga "100,00" para a chave do recebedor
    E o relógio avança 20 segundos
    E o pagador varre os vencidos
    Então a transação está em "EXPIRADA"
    E o barramento entrega 3 mensagens ao ser drenado
    E a transação está em "EXPIRADA"
    E o saldo do cliente pagador é "900,00"
    E o saldo do cliente recebedor é "1.100,00"
    E a consulta de status responde "LIQUIDADA"
    E a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "900,00"
    E o espelho e a conta PI coincidem nos dois participantes
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |
      | EXPIRADA    | CONSULTA_CONFIRMA_LIQUIDACAO        | LIQUIDADA   |

  Cenário: o pacs.002 RJCT atrasado não estorna — a consulta é que estorna
    Quando o cliente pagador paga "100,00" para uma chave cuja conta de destino o recebedor nunca abriu
    Então a resposta tem estado "ENVIADA_SPI"
    E o saldo do cliente pagador é "900,00"
    Quando o relógio avança 20 segundos
    E o pagador varre os vencidos
    Então a transação está em "EXPIRADA"
    E o barramento entrega 2 mensagens ao ser drenado
    E a transação está em "EXPIRADA"
    E o saldo do cliente pagador é "900,00"
    E a consulta de status responde "REJEITADA"
    E a transação está em "REJEITADA"
    E o saldo do cliente pagador é "1.000,00"
    E o espelho do pagador é "1.000,00"
    E a conta PI do pagador é "1.000,00"
    E o pool do SPI é "2.000,00"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |
      | EXPIRADA    | CONSULTA_CONFIRMA_REJEICAO          | REJEITADA   |
