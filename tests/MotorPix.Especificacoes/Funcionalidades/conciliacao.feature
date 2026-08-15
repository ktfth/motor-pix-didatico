#language: pt
@gate7
Funcionalidade: Conciliação entre os ledgers dos PSPs e o ledger do SPI
  Como operador do motor Pix
  Quero que a conciliação diga onde está cada centavo em trânsito e mande perguntar ao SPI
  Para que nenhuma transação fique parada em EXPIRADA e nenhum dinheiro apareça ou suma sem alarme

  A conciliação detecta e manda perguntar; quem decide o desfecho continua sendo o SPI. Ela não
  tem porta que mova uma transação por conta própria, porque decidir a partir de evidência
  indireta e local é exatamente o que o invariante 6 proíbe.

  Toda ela lê um insumo só: a defasagem entre a conta PI, no ledger do SPI, e o espelho dela
  dentro do ledger do PSP. Essa defasagem é o dinheiro em trânsito enquanto a operação não
  completa, e volta a zero quando ela completa. O que recebe nome vira divergência explicada; o
  que sobra sem nome vira órfã, e órfã é alarme — dinheiro criado ou destruído.

  Os valores esperados são escritos à mão. Derivá-los das mesmas contas que o motor faz provaria
  apenas que o motor é determinístico, e o defeito que estes cenários existem para pegar era
  justamente uma conta errada.

  Contexto:
    Dado que o motor está montado com fundo inicial de "1.000,00" e timeout de 20 segundos

  Cenário: depois do caminho feliz não sobra nada a conciliar
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o barramento é drenado
    E a conciliação roda
    Então o relatório não acusa divergência
    E o relatório não acusa nenhuma órfã
    E o relatório não lista nenhuma expirada
    E o relatório não manda consultar o SPI sobre nada
    E o relatório não manda reprocessar crédito nenhum
    E a diferença apurada para o pagador é "0,00"
    E a diferença apurada para o recebedor é "0,00"
    E o espelho e a conta PI coincidem nos dois participantes
    E o relatório está em quiescência

  # O ponto do cenário não é achar a divergência: é NÃO acusar alarme por ela. Uma divergência com
  # E2E, valor e participante é dinheiro em trânsito explicado — há um lançamento que diz onde ele
  # está e uma pergunta que pode ser feita ao SPI a respeito dele.
  Cenário: pagamento debitado e sem liquidação no SPI vira pendência do pagador
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E a conciliação roda
    Então o relatório acusa exatamente 1 divergência
    E o relatório acusa uma divergência de "PENDENCIA_DO_PAGADOR" no valor de "250,00" atribuída ao pagador
    E o relatório não acusa nenhuma órfã
    E a diferença apurada para o pagador é "250,00"
    E a diferença apurada para o recebedor é "0,00"
    E o espelho do pagador é "750,00"
    E a conta PI do pagador é "1.000,00"
    E o relatório manda consultar o SPI sobre a transação
    E o relatório não manda reprocessar crédito nenhum
    E o relatório não está em quiescência

  # Regressão do resíduo dobrado. As duas classes de pendência entram na conta com o MESMO sinal,
  # porque nos dois casos o SPI segura mais do que o espelho do PSP reflete: na pendência do
  # pagador o espelho já caiu e a conta PI ainda não; na do recebedor a conta PI já subiu e o
  # espelho ainda não. Somar a pendência do pagador com sinal negativo fazia o resíduo sair 2V, e
  # toda pendência legítima virava uma órfã do dobro do valor — alarme falso, que é a maneira mais
  # eficiente de treinar o operador a ignorar o alarme.
  Esquema do Cenário: a divergência vale o pagamento, e não o dobro dele
    Quando o cliente pagador paga "<valor>" para a chave do recebedor
    E a conciliação roda
    Então o relatório acusa exatamente 1 divergência
    E o relatório acusa uma divergência de "PENDENCIA_DO_PAGADOR" no valor de "<valor>" atribuída ao pagador
    E a diferença apurada para o pagador é "<valor>"
    E o relatório não acusa nenhuma órfã
    E o relatório não está em quiescência

    Exemplos:
      | valor    |
      | 0,01     |
      | 250,00   |
      | 999,99   |
      | 1.000,00 |

  # "A resposta se perdeu" é uma drenagem que não aconteceu, e não uma espera que falhou: a ordem
  # é entregue na mão do SPI e o pacs.002 que ela gera fica na fila.
  Cenário: liquidado no SPI e sem crédito ao cliente vira pendência do recebedor
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o SPI recebe a ordem de "250,00" na mão, sem que a resposta seja entregue
    E a conciliação roda
    Então o relatório acusa exatamente 1 divergência
    E o relatório acusa uma divergência de "PENDENCIA_DO_RECEBEDOR" no valor de "250,00" atribuída ao recebedor
    E o relatório não acusa nenhuma órfã
    E a diferença apurada para o recebedor é "250,00"
    E a diferença apurada para o pagador é "0,00"
    E a conta PI do recebedor é "1.250,00"
    E o espelho do recebedor é "1.000,00"
    E o saldo do cliente recebedor é "1.000,00"
    E o relatório manda reprocessar o crédito da transação
    E o relatório não manda consultar o SPI sobre nada
    E o relatório não está em quiescência

  # O dinheiro chega ao cliente do recebedor sem que ninguém tenha inventado uma transição: o PSP
  # pede a confirmação de volta ao SPI e a reentrega a si mesmo. A transação do pagador continua
  # em ENVIADA_SPI justamente porque ela não é caso de consulta — o SPI já liquidou, e ninguém a
  # expirou. A quiescência é do dinheiro, não do estado.
  Cenário: o pacs.002 ACSC perdido é fechado pelo ciclo e o dinheiro chega ao cliente do recebedor
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o SPI recebe a ordem de "250,00" na mão, sem que a resposta seja entregue
    Então o saldo do cliente recebedor é "1.000,00"
    Quando a conciliação roda até estabilizar
    Então o saldo do cliente recebedor é "1.250,00"
    E o saldo do cliente pagador é "750,00"
    E o espelho e a conta PI coincidem nos dois participantes
    E o pool do SPI é "2.000,00"
    E o relatório não manda reprocessar crédito nenhum
    E o relatório não acusa divergência
    E o relatório está em quiescência
    E a transação está em "ENVIADA_SPI"

  # Órfã é saldo de um lado sem lançamento do outro que o justifique. Repare que a soma por ledger
  # continua fechando em zero nos dois participantes: as partidas dobradas não acusam nada aqui, e
  # é por isso que a conciliação precisa existir. Ela não é fechável por consulta — não há ao SPI
  # o que perguntar, porque não existe ordem correspondente.
  Cenário: crédito sem lastro no SPI vira órfã, e o ciclo automático não a resolve
    Quando um crédito de "77,00" é aplicado no recebedor sem lastro no SPI
    E a conciliação roda
    Então o relatório acusa exatamente 1 divergência
    E o relatório acusa uma órfã de "77,00" no recebedor
    E a diferença apurada para o recebedor é "-77,00"
    E o saldo do cliente recebedor é "1.077,00"
    E o espelho do recebedor é "1.077,00"
    E a conta PI do recebedor é "1.000,00"
    E o relatório não manda consultar o SPI sobre nada
    E o relatório não manda reprocessar crédito nenhum
    E o relatório não está em quiescência
    Quando a conciliação roda até estabilizar
    Então o relatório acusa uma órfã de "77,00" no recebedor
    E o relatório não está em quiescência

  # O pacs.002 chega depois de a varredura já ter expirado a transação, e não transiciona nada.
  # Descartá-lo jogaria fora a evidência autoritativa do SPI; aplicá-lo violaria o invariante 6.
  # Registrar que há o que consultar, e consultar, satisfaz os dois.
  Cenário: a expirada sai de EXPIRADA porque o SPI foi perguntado, não por suposição
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o relógio avança 20 segundos
    E o pagador varre os vencidos
    E o barramento é drenado
    Então a transação está em "EXPIRADA"
    E o saldo do cliente recebedor é "1.250,00"
    Quando a conciliação roda até estabilizar
    Então a transação está em "LIQUIDADA"
    E o relatório não lista nenhuma expirada
    E nenhum dos dois PSPs tem transação em expirada
    E o relatório está em quiescência
    E o saldo do cliente pagador é "750,00"
    E o pool do SPI é "2.000,00"
    E o histórico da transação é exatamente:
      | origem      | evento                              | destino     |
      | RECEBIDA    | VALIDACAO_LOCAL_OK                  | VALIDADA    |
      | VALIDADA    | DEBITO_LANCADO_E_PACS008_DESPACHADO | ENVIADA_SPI |
      | ENVIADA_SPI | TIMEOUT_DETECTADO                   | EXPIRADA    |
      | EXPIRADA    | CONSULTA_CONFIRMA_LIQUIDACAO        | LIQUIDADA   |

  # A tentação mais plausível do gate: olhar o espelho, olhar a conta PI, concluir que "não
  # liquidou" e estornar. O pacs.008 nunca chegou ao SPI, então a resposta honesta dele é "não
  # sei", e permanecer aberto é o desfecho certo. A comparação é do conjunto de chaves de
  # idempotência gravadas, e não da contagem de commits: um estorno indevido entraria como commit
  # novo com chave nova, e a contagem sozinha não diria qual apareceu.
  Cenário: consulta indeterminada mantém a expirada aberta e não move um centavo
    Quando o cliente pagador paga "250,00" para a chave do recebedor
    E o relógio avança 20 segundos
    E o pagador varre os vencidos
    E os três ledgers são fotografados
    E a conciliação roda
    E a conciliação fecha o que apurou
    Então a conciliação emitiu 1 consulta
    E os três ledgers estão idênticos à fotografia
    E há 1 mensagem pendente no barramento
    E a transação está em "EXPIRADA"
    E o saldo do cliente pagador é "750,00"
    E o saldo do cliente recebedor é "1.000,00"
    Quando a conciliação roda
    Então o relatório lista a transação como expirada
    E o relatório manda consultar o SPI sobre a transação
    E o relatório não está em quiescência
