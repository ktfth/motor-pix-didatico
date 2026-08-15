# ADR-005 — Como os invariantes 1 e 9 são impostos (e onde o analyzer não alcança)

**Contexto.** O plano previa `Microsoft.CodeAnalysis.BannedApiAnalyzers` transformando os invariantes 1 (nada de `decimal`/`double`/`float`) e 9 (nada de `DateTime.UtcNow`) em erro de compilação. Sondagem empírica no gate 1 mostrou que a cobertura é parcial.

**O que foi verificado.** Com `T:System.Decimal` na lista de banidos, o analyzer **barra** usos de membro (`decimal.Parse`, `new Random()`, `Guid.NewGuid()`) e **não barra** declaração de campo, parâmetro, retorno, nem literal (`10.50m`, `1.5d`). `P:System.DateTime.UtcNow` funciona como esperado e falha o build. Ou seja: o invariante 9 está imposto pelo compilador; o invariante 1 **não estaria** se dependesse só do analyzer.

**Decisão.** Manter a lista de símbolos banidos pelo que ela cobre de fato, e fechar a lacuna com dois testes de arquitetura: (1) reflexão sobre todos os tipos de `MotorPix.Dominio`, reprovando qualquer campo, propriedade, parâmetro ou retorno `decimal`/`double`/`float`; (2) varredura textual dos `.cs` sob `src/`, com comentários e literais de string removidos antes da busca — sem essa remoção, os próprios comentários do domínio, que citam essas palavras, produzem falso positivo.

**Consequência.** O invariante 1 falha no teste e não na compilação, o que é um ciclo de feedback mais lento porém verificável. Um terceiro teste garante que `src/BannedSymbols.txt` não foi esvaziado, para que a metade coberta pelo compilador não desapareça em silêncio.
