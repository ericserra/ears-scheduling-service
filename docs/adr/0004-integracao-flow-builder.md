# ADR 0004 — Divisão de trabalho com o Flow Builder do SleekFlow

**Status:** aceito
**Data:** outubro de 2026
**Contexto de origem:** [handoff da SleekFlow, 20/10/2026](../integracao/handoff-sleekflow-2026-10-20.md)

## Contexto

O handoff inverte a premissa com que este serviço foi escrito. A primeira
versão tratava **toda** mensagem recebida e abria a máquina de estados: o motor
era a porta de entrada do WhatsApp.

Não é. A porta de entrada é o fluxo "EARS - Menu de entrada" no Advanced Flow
Builder. Ele mostra o menu, encaminha sete ramos para a IA ou para a Recepção, e
só nos ramos **1 (agendar consulta)** e **3 (remarcar ou cancelar, consulta)**
entrega a conversa ao motor.

O acoplamento entre os dois é uma etiqueta. O gatilho do fluxo de entrada tem
como condição de inscrição que o contato **NÃO** tenha a etiqueta
`agendamento-em-curso`. Enquanto o motor conduz o agendamento, ela precisa
estar aplicada; quando ele termina, precisa cair.

Três limitações da Platform API da SleekFlow restringem o que é possível:

| Precisamos | A API oferece |
|---|---|
| Aplicar e remover etiqueta de um contato | Não há rota de etiqueta por contato. O caminho é `POST /api/contact/addOrUpdate`, com `addLabels` e `removeLabels` |
| Atribuir conversa à **equipe** Recepção | `POST /api/conversation/{id}/assignee` aceita apenas `staffId`. Equipe, não |
| Saber se a janela de 24h está aberta | `POST /api/whatsapp/reply-window-status`, por telefone ou por conversa |

Também: a autenticação é `X-Sleekflow-Api-Key`, não Bearer; o envio é
`POST /api/message/send/json` e exige o campo `from` com o número remetente da
clínica; e os parâmetros de template são **posicionais**, não nomeados.

## Decisão

**O Flow Builder é dono do menu; o motor é dono do agendamento.** A fronteira
tem duas portas:

- `POST /webhooks/sleekflow/atendimento` — o nó HTTP do Flow Builder chama nos
  ramos 1 e 3, informando telefone e motivo. O motivo vem do ramo escolhido, não
  de inferência sobre o texto: quem já sabe o que o paciente pediu é o fluxo.
- `POST /webhooks/sleekflow` — mensagens recebidas. O motor só trata as de quem
  tem conversa ativa. Sem conversa, responde `204` e não faz nada: a mensagem é
  do menu.

**A etiqueta é aplicada antes da primeira pergunta** e removida em **toda**
saída — conclusão, transferência e expiração. Há um único ponto no código que
encerra conversa, e ele é o que remove a etiqueta.

**A transferência para a Recepção é feita por etiqueta**, já que a API não
atribui a equipes: o motor aplica `transferir-recepcao`, e um fluxo no Flow
Builder, disparado pelo gatilho "Label is added to a contact", faz a atribuição.
Um `staffId` configurável existe como reforço opcional.

**A ordem da coleta é a da clínica**, não a que era conveniente para a Amplimed:
para quem e com que idade, convênio, dia e turno, nome e data de nascimento.
Profissional não é perguntado, porque a preferência é opcional e os horários já
saem com o nome de quem atende.

**O CPF deixou de ser obrigatório.** Ele não está na ordem da clínica, mas a
Amplimed casa paciente por `idUnico` e depois por CPF + data de nascimento. A
saída: resolver por nome e data de nascimento, e pedir CPF **só** quando a busca
voltar ambígua. Quem já é cadastrado não paga o passo extra; homônimo é
desempatado.

**As regras clínicas vivem em configuração**, na seção `Clinica`: grade etária
por profissional, convênios de triagem humana, convênios só-consulta, termos de
urgência e de pedido de atendente, frase de transferência. São decisões da
clínica e mudam sem aviso; compilar cada mudança significaria um deploy por
grade de médica.

**O Módulo 2 nasce desligado** (`Automacao:ConfirmacaoAtiva = false`). O handoff
registra que a clínica ainda não decidiu se quer lembrete automático e alerta
para não duplicar o que ela já envia. Com a flag em `false` os jobs nem são
registrados no Quartz. O polling da agenda continua, porque é dele que sai o par
de identificadores da Amplimed (ADR 0003).

## Consequências

- **Um job novo que não é opcional.** `ExpiracaoConversaJob` roda a cada dez
  minutos e tira a etiqueta de quem abandonou. Antes, a expiração só era
  avaliada quando chegava mensagem nova — o que nunca acontece justamente no
  caso que importa. Sem ele, cada desistência deixa um paciente permanentemente
  sem menu: ele escreve, nada responde, e não há erro em lugar nenhum que
  aponte para a causa. É a falha mais silenciosa desta integração.
- **A etiqueta vira acoplamento de configuração.** O nome em
  `SleekFlow:EtiquetaAgendamentoEmCurso` tem de bater exatamente com a condição
  de inscrição do fluxo. Divergir faz o menu reabrir a cada resposta, e o
  sintoma no WhatsApp não sugere a causa.
- **Dois lugares guardam estado de conversa.** O Flow Builder tem o dele, o
  motor tem o seu. Eles se falam só pela etiqueta e pelas duas portas; qualquer
  outro ponto de sincronia que apareça é sinal de que a fronteira está errada.
- **Exame continua fora.** O ramo 3 pergunta "consulta ou exame" e devolve exame
  à Recepção, como o handoff pede. O motor não tenta cobrir o que não foi
  contratado.
- **Falha ao aplicar ou remover etiqueta não interrompe o atendimento**, mas vai
  para log de erro. Parar o agendamento por causa da etiqueta seria pior do que
  o menu reabrir.
