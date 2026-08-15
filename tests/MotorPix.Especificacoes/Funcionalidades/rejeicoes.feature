#language: pt
@gate3
Funcionalidade: Rejeições do pagamento Pix
  Como cliente pagador
  Quero que um pagamento recusado deixe minha conta exatamente como ela estava
  Para que uma recusa nunca me custe dinheiro nem invente dinheiro para ninguém

  As duas arestas de rejeição do diagrama: uma sai de RECEBIDA, antes de qualquer lançamento; a
  outra sai de ENVIADA_SPI, depois de o débito já ter acontecido. As duas terminam no mesmo
  REJEITADA e têm consequências contábeis opostas — por isso nenhum cenário aqui se contenta com
  o estado final: todos conferem o dinheiro nos três ledgers e o pool do SPI.

  Os saldos esperados são escritos à mão. Derivá-los das mesmas contas que o motor faz provaria
  apenas que o motor é determinístico, e não que a recusa é neutra no ledger.

  Contexto:
    Dado que o motor está montado com fundo inicial de "1.000,00" para cada participante

  Cenário: chave que ninguém cadastrou no DICT é recusada sem tocar em nenhum dos três ledgers
    Dado que a chave "órfã" não está cadastrada no DICT
    E que os lançamentos dos três ledgers foram contados
    Quando o cliente pagador paga "250,00" para a chave "órfã"
    Então a resposta tem estado "REJEITADA"
    E a resposta traz motivo "CHAVE_NAO_ENCONTRADA"
    E não há mensagem pendente no barramento
    E o barramento nunca entregou nenhuma mensagem
    E nenhum lançamento novo entrou em nenhum dos três ledgers
    E nenhum débito foi lançado no ledger do pagador
    E nenhum estorno foi lançado no ledger do pagador
    E o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o espelho do pagador é "1.000,00"
    E o espelho do recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: conta de origem inexistente é recusada antes de o DICT ser consultado
    Quando o cliente pagador paga "250,00" a partir da conta "9999"
    Então a resposta tem estado "REJEITADA"
    E a resposta traz motivo "CONTA_DE_ORIGEM_DESCONHECIDA"
    E a transação não registrou destino
    E não há mensagem pendente no barramento
    E nenhum débito foi lançado no ledger do pagador
    E o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: saldo insuficiente é recusado depois de a chave já ter resolvido
    Quando o cliente pagador paga "1.250,00" para a chave do recebedor
    Então a resposta tem estado "REJEITADA"
    E a resposta traz motivo "SALDO_INSUFICIENTE"
    E a transação registrou o recebedor como destino
    E nenhum débito foi lançado no ledger do pagador
    E não há mensagem pendente no barramento
    E o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"

  Esquema do Cenário: qualquer valor acima do saldo é recusado e nada se move
    Quando o cliente pagador paga "<valor>" para a chave do recebedor
    Então a resposta tem estado "REJEITADA"
    E a resposta traz motivo "SALDO_INSUFICIENTE"
    E não há mensagem pendente no barramento
    E o saldo do cliente pagador é "1.000,00"
    E o espelho do pagador é "1.000,00"
    E o pool do SPI é "2.000,00"

    Exemplos:
      | valor    |
      | 1.000,01 |
      | 1.500,00 |
      | 9.999,99 |

  Cenário: pagar exatamente o saldo disponível é aceito e zera a conta
    A guarda de saldo compara com "menor que", não com "menor ou igual": zerar a conta é legítimo.
    Um off-by-one aqui recusaria justamente o pagamento da linha de fronteira acima.
    Quando o cliente pagador paga "1.000,00" para a chave do recebedor
    Então a resposta tem estado "ENVIADA_SPI"
    E a resposta não traz motivo de rejeição
    Quando o barramento é drenado
    Então a transação está em "LIQUIDADA"
    E o saldo do cliente pagador é "0,00"
    E o saldo do cliente recebedor é "2.000,00"
    E o espelho do pagador é "0,00"
    E a conta PI do pagador é "0,00"
    E a conta PI do recebedor é "2.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: chave que aponta para a própria conta de origem é recusada antes do débito
    O pacs.008 é montado antes do débito. Montá-lo depois deixaria o cliente debitado, a transação
    parada em ENVIADA_SPI e nenhuma mensagem a caminho — com a partida dobrada intacta.
    Dado que a chave "espelhada" aponta para a própria conta do pagador
    Quando o cliente pagador paga "250,00" para a chave "espelhada"
    Então a resposta tem estado "REJEITADA"
    E a resposta traz motivo "ORDEM_INVALIDA"
    E nenhum débito foi lançado no ledger do pagador
    E não há mensagem pendente no barramento
    E o saldo do cliente pagador é "1.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: o SPI recusa por falta de lastro na conta PI e o débito é estornado
    O cliente tem o dinheiro, o PSP não tem o lastro correspondente no SPI. Só quem detém a conta
    PI descobre isso, e a essa altura o débito interno já aconteceu.
    Dado que o pagador abriu o cliente "0003" com fundo de "5.000,00" sem lastro na conta PI
    E que os lançamentos dos três ledgers foram contados
    Quando o cliente "0003" paga "3.000,00" para a chave do recebedor
    Então a resposta tem estado "ENVIADA_SPI"
    E o débito da transação foi lançado no ledger do pagador
    E o saldo do cliente "0003" é "2.000,00"
    E o barramento entrega 2 mensagens ao ser drenado
    E o SPI respondeu RJCT com motivo "SALDO_INSUFICIENTE_NA_CONTA_PI"
    E a transação está em "REJEITADA"
    E a transação traz motivo "REJEITADA_PELO_SPI"
    E o débito da transação foi lançado no ledger do pagador
    E o débito da transação foi estornado no ledger do pagador
    E o saldo do cliente "0003" é "5.000,00"
    E o ledger do SPI não ganhou nenhum lançamento novo
    E a conta PI do pagador é "1.000,00"
    E a conta PI do recebedor é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o espelho do pagador é "6.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: duas rejeitadas no mesmo motor, com consequências contábeis opostas
    Mesmo estado final, ledgers diferentes. Estornar a que morreu em RECEBIDA devolveria um débito
    que nunca foi lançado, e isso é emissão pura de dinheiro.
    Dado que o pagador abriu o cliente "0003" com fundo de "5.000,00" sem lastro na conta PI
    E que a chave "órfã" não está cadastrada no DICT
    Quando o cliente "0003" paga "3.000,00" para a chave do recebedor, como pagamento "SEM LASTRO"
    E o cliente pagador paga "250,00" para a chave "órfã", como pagamento "SEM CHAVE"
    E o barramento é drenado
    Então o pagamento "SEM LASTRO" está em "REJEITADA"
    E o pagamento "SEM CHAVE" está em "REJEITADA"
    E o pagamento "SEM LASTRO" traz motivo "REJEITADA_PELO_SPI"
    E o pagamento "SEM CHAVE" traz motivo "CHAVE_NAO_ENCONTRADA"
    E o pagamento "SEM LASTRO" teve débito e estorno no ledger do pagador
    E o pagamento "SEM CHAVE" não teve débito nem estorno no ledger do pagador
    E o saldo do cliente "0003" é "5.000,00"
    E o saldo do cliente pagador é "1.000,00"
    E o espelho do pagador é "6.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: destino que não pode creditar faz o SPI recusar antes de mover as contas PI
    Sem essa guarda, a liquidação aconteceria e só a entrega ao recebedor estouraria: dinheiro
    movido entre contas PI sem contrapartida, com a soma de cada ledger ainda fechando em zero.
    Dado que a chave "fantasma" está vinculada a uma conta que o recebedor nunca abriu
    E que os lançamentos dos três ledgers foram contados
    Quando o cliente pagador paga "250,00" para a chave "fantasma"
    Então a resposta tem estado "ENVIADA_SPI"
    E o barramento entrega 2 mensagens ao ser drenado
    E o SPI respondeu RJCT com motivo "CONTA_DE_DESTINO_INDISPONIVEL"
    E o ledger do SPI não ganhou nenhum lançamento novo
    E a conta PI do pagador é "1.000,00"
    E a conta PI do recebedor é "1.000,00"
    E o recebedor não creditou nada
    E o saldo do cliente recebedor é "1.000,00"
    E a transação está em "REJEITADA"
    E o débito da transação foi lançado no ledger do pagador
    E o débito da transação foi estornado no ledger do pagador
    E o saldo do cliente pagador é "1.000,00"
    E o espelho do pagador é "1.000,00"
    E o pool do SPI é "2.000,00"

  Cenário: pacs.008 endereçado a participante fora do SPI é recusado sem liquidar
    A ordem chega direto ao SPI porque nenhum PSP montado consegue produzi-la: o destino sai do
    DICT, e o DICT só conhece participantes reais. O pacs.002 de volta fica pendente de propósito,
    já que o pagador não tem transação nenhuma para esse E2E.
    Dado que os lançamentos dos três ledgers foram contados
    Quando um pacs.008 de "250,00" chega ao SPI endereçado a um participante que ele não conhece
    Então o SPI respondeu RJCT com motivo "PARTICIPANTE_DESCONHECIDO"
    E há 1 mensagem pendente no barramento
    E nenhum lançamento novo entrou em nenhum dos três ledgers
    E a transação não é conhecida pelo pagador
    E a conta PI do pagador é "1.000,00"
    E a conta PI do recebedor é "1.000,00"
    E o saldo do cliente pagador é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o pool do SPI é "2.000,00"
