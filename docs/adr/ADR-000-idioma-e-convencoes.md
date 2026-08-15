# ADR-000 — Idioma do código e convenções de projeto

**Contexto.** O projeto é de estudo e todo o material normativo (prompt e diagramas) está em português, mas o vocabulário de mensagens é ISO 20022 em inglês.

**Decisão.** Tipos, membros e namespaces de domínio em português (`Valor`, `Lancamento`, `TransicaoInvalidaException`); nomes ISO preservados como estão (`Pacs008`, `Pacs002`, `EndToEndId`, `Ispb`). Identificadores e literais de string em ASCII puro; acentuação apenas em comentários e documentação. `net8.0`, `Nullable=enable`, `TreatWarningsAsErrors`, `CheckForOverflowUnderflow` na solução inteira.

**Consequência.** A leitura do código acompanha a leitura dos diagramas sem tradução mental, e nenhum arquivo depende de encoding para compilar. O alvo `net8.0` é o mínimo que o prompt declara; o SDK instalado é 10.0.102 e compila esse alvo pelo targeting pack local.

**Alternativa descartada.** Domínio em inglês: alinharia com o ISO, mas afastaria o código dos três documentos que são a fonte da verdade.
