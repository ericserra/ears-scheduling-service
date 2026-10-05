# ADR 0002 — Estratégia de leitura da agenda para a confirmação D-1

**Status:** aceito — cenário A
**Data:** setembro de 2026 (revisado em outubro de 2026, após a verificação da Cláusula 3ª)

## Contexto

Na redação do contrato, a documentação pública da Amplimed não deixava claro se
havia endpoint para listar agendamentos de um período e para gravar o status
"confirmado". A **Cláusula 3ª** formalizou a incerteza, com dois cenários
previstos e faculdade de abatimento de R$ 1.900 caso o módulo fosse excluído.

A verificação foi feita sobre as coleções de referência da Amplimed
("API Agendamento Amplimed Referência" e "API Cadastro de Pacientes Amplimed").
**Os dois endpoints existem:**

| Necessidade | Endpoint | Observações |
|---|---|---|
| Ler a agenda | `GET /integracoes/referencia/{identificador}/{usuclin}/listar-agendamentos?data=YYYY-MM-DD&page=N` | Um dia por chamada. Filtros opcionais de profissional e CPF. 50 por página sem filtro. Cobre agendamentos de qualquer canal. |
| Gravar confirmação | `POST /integracoes/referencia/{identificador}/alterar-status-agendamento` | Corpo `{ usuclin, idAgendamento, statusCode }`. `Confirmado = 0`, `Cancelado = 3`, `Reagendado = 7`. |

## Decisão

**Cenário A é o padrão.** `Automacao:LeituraAgendaViaApi` passa a vir `true`, e a
confirmação de véspera cobre consultas marcadas por qualquer canal.

O cenário B continua implementado, mas muda de papel: era a hipótese provável,
agora é **degradação**. Entra em vigor quando a clínica não libera o `usuclin`,
quando o plano dela não expõe os endpoints (404/405/501, tratados como
indisponibilidade e não como erro) ou quando a API está fora do ar.

Três consequências do formato real da API entraram no desenho:

1. **Não há webhook.** A documentação é explícita: para acompanhar novos
   agendamentos e mudanças de status, o integrador consulta periodicamente. Daí o
   `SincronizacaoAgendaJob`, com cron e janela de dias configuráveis.

2. **A listagem da agenda não devolve telefone.** O `beneficiario` traz codp,
   codRef, CPF, nome e carteirinha, mas nenhum contato. Para enviar WhatsApp a um
   paciente que chegou pela agenda é preciso uma segunda chamada, à API de
   Pacientes, buscando o celular por CPF. Sem CPF ou sem celular, o agendamento é
   ignorado no envio automático e registrado em log — vira tarefa da recepção.

3. **O espelho local segue sendo o modelo de leitura.** O disparo D-1 e o
   lembrete leem o banco local; o polling é o que o mantém fiel à agenda. Falha
   na conciliação degrada para o espelho em vez de abortar o disparo: é melhor
   confirmar o que se sabe do que não confirmar nada.

## Consequências

- O módulo 2 entrega o escopo integral previsto no contrato; o abatimento de
  R$ 1.900 não se aplica.
- Surge uma dependência nova de configuração: o **usuclin** da clínica. Sem ele,
  `listar-agendamentos` e `alterar-status-agendamento` não funcionam, e o serviço
  cai para o cenário B. É o primeiro item a pedir junto com as credenciais.
- O custo de chamadas cresce com o polling: uma chamada por dia da janela, mais
  as páginas adicionais em dias cheios, mais uma busca na API de Pacientes por
  paciente novo. Nada disso é caro, mas o cron não deve ser agressivo.
- A verificação e seu resultado devem ser **comunicados por escrito à clínica**,
  conforme o § 1º da Cláusula 3ª. O conteúdo da comunicação é este ADR.
