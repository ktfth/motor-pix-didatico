# ADR-007 — Até onde o motor valida uma chave Pix

**Contexto.** `ChavePix` é um sum type fechado sobre cinco tipos. Quanto validar de cada um é uma decisão de produto: validar de menos deixa lixo virar chave de endereçamento; validar de mais reimplementa o DICT real num projeto cujo investimento declarado é o domínio contábil.

**Decisão — o que é validado.** Forma e canonicidade, sempre dentro do value object: CPF e CNPJ reduzidos a dígitos com dígito verificador conferido; e-mail aparado, minúsculo, com arroba única e domínio pontuado, limitado a 77 caracteres; telefone em E.164, exigindo `+` explícito; chave aleatória como GUID no formato `D` minúsculo. A normalização remove **apenas** pontuação conhecida (`.`, `-`, `/`, parênteses e espaço) e recusa qualquer outro caractere não numérico.

**Por que a normalização é restritiva.** A primeira versão descartava todo não-dígito, e com isso `"cpf 529.982.247-25 ok"` era aceito e resolvia para a mesma entrada do DICT que o CPF limpo. Dois textos distintos colapsando na mesma chave de endereçamento é como dinheiro chega à conta errada — e o dia em que a sujeira contém dígitos, resolve para o titular errado sem que nada no caminho registre que os dois textos nunca foram o mesmo.

**Decisão — o tipo nunca é inferido.** `"12345678909"` é ambíguo entre CPF e telefone. O tipo vem explícito de quem constrói; adivinhar introduziria regra que nenhum documento autoriza.

**Fora de escopo, declarado.** Verificação de posse (SMS, e-mail de confirmação), resolução DNS do domínio, validação de operadora do telefone, limite de chaves por titular, portabilidade e reivindicação de chave, CNPJ alfanumérico, e qualquer consulta ao DICT real. Nenhum deles é exercitado pelos critérios de aceite do prompt.

**Fato que corrige o roteiro.** O plano previa a propriedade "mutar um dígito de CPF/CNPJ invalida". Ela é **falsa**: `12345678909` e `22345678909` são ambos CPFs válidos, porque restos 0 e 1 colapsam no mesmo dígito verificador e, no segundo dígito, o primeiro algarismo tem peso 11 — congruente a zero módulo 11. A robustez do DV é coberta por casos concretos conferidos à mão, com a conta no comentário, e não por uma propriedade que falharia.
