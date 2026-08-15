#language: pt
@gate4
Funcionalidade: Idempotência por EndToEndId
  Como cliente pagador que não sabe se o primeiro POST chegou
  Quero reenviar o mesmo pedido sob o mesmo EndToEndId
  Para que a repetição me devolva a resposta original em vez de me cobrar duas vezes

  Invariante 5: reenviar o mesmo EndToEndId devolve a resposta ORIGINAL e não gera lançamento
  novo. O defeito que a regra evita é o pior modo de falha de um motor de pagamentos — o cliente
  que reenvia por não ter visto a resposta e acaba pagando duas vezes — e o seu simétrico: o
  replay cego, que responderia "aceito" a um pedido diferente do que foi executado.

  São três camadas independentes, e cada uma tem cenário próprio aqui: a idempotência da API do
  PSP (indexada pelo E2E do pedido do cliente), a deduplicação do SPI (indexada pelo E2E da
  mensagem) e a chave de idempotência do ledger (indexada pelo par transação/etapa).

  Os valores esperados são escritos à mão. Derivá-los das mesmas funções que o motor usa
  provaria apenas que o motor é determinístico.

  Contexto:
    Dado que o motor está montado com fundo inicial de "1.000,00" para cada participante

  Cenário: o reenvio devolve a resposta original e não move dinheiro nenhum
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o cliente guarda a resposta recebida
    E o cliente pagador reenvia o mesmo pagamento de "250,00"
    Então a resposta é idêntica à que o cliente guardou
    E a resposta tem estado "ENVIADA_SPI"
    E a resposta não traz motivo de rejeição
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o pagador lançou exatamente 1 débito para essa transação
    E o registro de idempotência conhece exatamente 1 pedido
    E o registro de idempotência guarda exatamente 1 resposta
    E há 1 mensagem pendente no barramento
    E o histórico da transação é exatamente:
      | origem   | evento                              | destino     |
      | RECEBIDA | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |

  Esquema do Cenário: reenviar não duplica o pagamento, qualquer que seja o valor
    Quando o cliente pagador paga "<valor>" para a chave do recebedor
    E o cliente pagador reenvia o mesmo pagamento de "<valor>"
    Então há 1 mensagem pendente no barramento
    E o barramento entrega 3 mensagens ao ser drenado
    E o saldo do cliente pagador é "<sobra>"
    E o saldo do cliente recebedor é "<destino>"
    E o pool do SPI é "2.000,00"
    E o pagador lançou exatamente 1 débito para essa transação
    E o recebedor lançou exatamente 1 crédito para essa transação

    Exemplos:
      | valor    | sobra  | destino  |
      | 0,01     | 999,99 | 1.000,01 |
      | 1,00     | 999,00 | 1.001,00 |
      | 250,00   | 750,00 | 1.250,00 |
      | 1.000,00 | 0,00   | 2.000,00 |

  Cenário: a resposta congelada é a da primeira vez, e não o estado atual da transação
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o cliente guarda a resposta recebida
    E o barramento é drenado
    Então a transação está em "LIQUIDADA"
    Quando o cliente pagador reenvia o mesmo pagamento de "250,00"
    Então a resposta tem estado "ENVIADA_SPI"
    E a resposta não traz motivo de rejeição
    E a resposta é idêntica à que o cliente guardou
    E a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.250,00"
    E o recebedor lançou exatamente 1 crédito para essa transação
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                        | LIQUIDADA   |

  Cenário: o reenvio de um pedido rejeitado devolve a mesma rejeição, e continua sem lançar nada
    Quando o cliente pagador paga "1.000,01" para a chave do recebedor
    E o cliente guarda a resposta recebida
    E o cliente pagador reenvia o mesmo pagamento de "1.000,01"
    Então a resposta tem estado "REJEITADA"
    E a resposta traz motivo "SALDO_INSUFICIENTE"
    E a resposta é idêntica à que o cliente guardou
    E o saldo do cliente pagador é "1.000,00"
    E o pagador lançou exatamente 0 débitos para essa transação
    E não há mensagem pendente no barramento
    E o registro de idempotência guarda exatamente 1 resposta
    E o histórico da transação é exatamente:
      | origem   | evento                 | destino   |
      | RECEBIDA | VALIDACAO_LOCAL_FALHOU | REJEITADA |

  Esquema do Cenário: o mesmo E2E com outro pedido é conflito, nunca replay cego
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o cliente guarda a resposta recebida
    E o cliente pagador reenvia o mesmo E2E com valor "<divergente>"
    Então o reenvio é recusado por conflito de E2E
    E o conflito mostra o pedido original de "250,00" e o pedido recusado de "<divergente>"
    E o saldo do cliente pagador é "750,00"
    E o pagador lançou exatamente 1 débito para essa transação
    E há 1 mensagem pendente no barramento
    E o registro de idempotência conhece exatamente 1 pedido
    Quando o cliente pagador reenvia o mesmo pagamento de "250,00"
    Então a resposta é idêntica à que o cliente guardou

    Exemplos:
      | divergente |
      | 500,00     |
      | 250,01     |
      | 249,99     |
      | 1.000,00   |

  Esquema do Cenário: outra grafia da mesma chave é o mesmo pedido, e não um conflito
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o cliente guarda a resposta recebida
    E o cliente pagador reenvia o mesmo pagamento de "250,00" digitando a chave como "<grafia>"
    Então a resposta é idêntica à que o cliente guardou
    E o saldo do cliente pagador é "750,00"
    E o pagador lançou exatamente 1 débito para essa transação
    E há 1 mensagem pendente no barramento

    Exemplos:
      | grafia                |
      | Recebedor@Exemplo.COM |
      | RECEBEDOR@EXEMPLO.COM |

  Cenário: a mesma ordem entregue duas vezes ao SPI liquida uma vez só
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E o mesmo pacs.008 de "250,00" é reentregue ao SPI
    Então o barramento entrega 3 mensagens ao ser drenado
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.250,00"
    E o pool do SPI é "2.000,00"
    E o recebedor lançou exatamente 1 crédito para essa transação
    E o log de decisões do SPI tem exatamente 1 decisão
    E a transação está em "LIQUIDADA"
    E o registro de idempotência guarda exatamente 1 resposta
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | PACS002_ACSC                        | LIQUIDADA   |

  Esquema do Cenário: o ledger recusa o segundo débito do mesmo E2E, mesmo vindo por fora da API
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o mesmo débito de "<valor>" é lançado outra vez direto no ledger do pagador
    Então o ledger recusa o lançamento duplicado
    E o saldo do cliente pagador é "750,00"
    E o pagador lançou exatamente 1 débito para essa transação
    E há 1 mensagem pendente no barramento

    Exemplos:
      | valor  |
      | 250,00 |
      | 500,00 |

  Cenário: trinta e duas postagens simultâneas do mesmo pedido produzem um único pagamento
    Quando 32 postagens do mesmo pagamento de "1,00" chegam ao mesmo tempo
    Então as 32 respostas são idênticas entre si
    E a resposta tem estado "ENVIADA_SPI"
    E o registro de idempotência conhece exatamente 1 pedido
    E o registro de idempotência guarda exatamente 1 resposta
    E o pagador lançou exatamente 1 débito para essa transação
    E o saldo do cliente pagador é "999,00"
    E há 1 mensagem pendente no barramento

  Cenário: entre postagens simultâneas do mesmo E2E com pedidos diferentes, só uma vence
    Quando estas postagens do mesmo E2E chegam ao mesmo tempo:
      | valor |
      | 1,00  |
      | 2,00  |
      | 4,00  |
      | 8,00  |
    Então apenas uma postagem foi aceita
    E as outras 3 postagens foram recusadas por conflito de E2E
    E o registro de idempotência conhece exatamente 1 pedido
    E o registro de idempotência guarda exatamente 1 resposta
    E o pagador lançou exatamente 1 débito para essa transação
    E há 1 mensagem pendente no barramento
