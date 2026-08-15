# Prompt — Motor Pix Micro (prova de estudo)

## Papel e objetivo
Você é um engenheiro C# sênior de sistemas financeiros. Implemente um motor Pix didático em .NET, fiel aos dois diagramas Mermaid anexos. O objetivo é estudo: correção e clareza do domínio valem mais que performance, UI ou infraestrutura.

## Fonte da verdade e precedência
1. `motor-pix-maquina-estados.mermaid` — estados e transições da transação. Normativo: nenhuma transição fora dele existe.
2. `motor-pix-arquitetura.mermaid` — módulos, ledgers e fluxo de mensagens numerado (setas 1–7 = caminho feliz; pontilhadas = fases posteriores).
3. Este prompt — invariantes e convenções.

Em conflito ou ambiguidade entre os três: pare e pergunte antes de implementar. Não invente comportamento.

## Invariantes inegociáveis
Código que viole qualquer item abaixo está errado, mesmo que compile e passe nos testes:

1. Dinheiro é `long` em centavos, encapsulado em value object (`Valor`). `decimal`, `double` e `float` são proibidos no domínio.
2. Ledgers são append-only e de partidas dobradas: todo lançamento debita uma conta e credita outra pelo mesmo valor. Não existe UPDATE nem DELETE de lançamento; correção e estorno são lançamentos novos.
3. A soma de todos os saldos do sistema é constante em qualquer sequência de operações — inclusive com falhas no meio.
4. Transição de estado fora do diagrama lança `TransicaoInvalidaException`. Estado não tem setter público.
5. Idempotência por `EndToEndId`: unique constraint; reenvio retorna a resposta original, sem novo lançamento.
6. Timeout ≠ falha: de `EXPIRADA` só se sai por consulta de status ao SPI. Proibido estornar ou confirmar por suposição.
7. Devolução (`pacs.004`) é transação nova, com E2E próprio, referenciando a original. Nunca reabre nem altera a liquidada.
8. Conta PI não fica negativa e não tem crédito.
9. Tempo só entra via `IClock` injetado. `DateTime.UtcNow`/`Now` são proibidos fora da implementação de `IClock`.

## Convenções C#
- .NET 8+, `<Nullable>enable</Nullable>`, `record` para DTOs e `readonly record struct` para value objects.
- IDs fortemente tipados com validação no construtor: `EndToEndId` (formato `E` + ISPB 8 dígitos + `yyyyMMddHHmm` + 11 alfanuméricos), `Ispb`, `ContaId`, `ChavePix`.
- Mensagens nomeadas pelo vocabulário ISO 20022: `Pacs008`, `Pacs002` (status `ACSC`/`RJCT`), `Pacs004`. Sem XML, sem assinatura — só o vocabulário.
- Módulos `Spi`, `Dict`, `PspPagador`, `PspRecebedor` comunicam-se exclusivamente por interfaces públicas, in-process. Nenhum módulo referencia tipos internos de outro.
- Persistência mínima (in-memory ou SQLite); o investimento é no domínio.
- Exceções de domínio específicas; nada de `Exception`/`InvalidOperationException` genéricas para regra de negócio.

## Ordem de implementação (gates)
Implemente nesta ordem; só avance com os testes da etapa anterior verdes:

1. Ledger de partidas dobradas + invariantes 1–3.
2. Dict: chave → ISPB + conta.
3. Fluxo feliz (setas 1–7 do diagrama de arquitetura).
4. Idempotência: reenvio do mesmo E2E.
5. Timeout + consulta de status (setas pontilhadas).
6. Devolução `pacs.004`.
7. Conciliação PSP × SPI (fecha as EXPIRADA remanescentes).
8. Replay: reconstruir projeções de saldo do zero a partir do ledger.

## Critérios de aceite (testes obrigatórios)
- Propriedade: Σ saldos constante sob sequências aleatórias de operações com falhas injetadas (FsCheck ou gerador próprio).
- Replay do ledger reproduz exatamente as projeções atuais.
- Reenvio de E2E: mesma resposta, zero lançamento novo.
- Toda transição inválida lança exceção — teste exaustivo por par estado × evento.
- Timeout determinístico com `IClock` fake; sem `Task.Delay`/`Thread.Sleep` em teste.

## Fora de escopo — não implemente
Certificados ICP-Brasil, XML real das mensagens, QR Code/Pix Cobrança, MED completo, antifraude, comunicação HTTP entre módulos (fase futura de estudo).

## Formato de trabalho
- Uma etapa por vez: código + testes + nota curta das decisões de modelagem (3–5 linhas, estilo ADR).
- Para desviar de um diagrama ou deste prompt, proponha o desvio como ADR e aguarde aprovação.
