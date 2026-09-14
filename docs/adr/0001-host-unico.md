# ADR 0001 — API e worker no mesmo host

**Status:** aceito
**Data:** setembro de 2026

## Contexto

A arquitetura aprovada prevê dois componentes de execução: uma API que recebe os
webhooks do SleekFlow e um agendador que dispara a varredura diária D-1.

O projeto tem implantação de R$ 3.200 e sustentação de R$ 350/mês, valor que
cobre hospedagem, banco gerenciado, backup e monitoramento. O volume esperado é
de algumas centenas de mensagens por mês, com um pico diário concentrado no
horário do disparo.

## Decisão

API e agendador rodam no **mesmo processo**: o Quartz sobe como hosted service
dentro do host ASP.NET Core, e não como um worker separado.

## Consequências

Positivas:

- um único container para implantar, monitorar e manter, compatível com o custo
  mensal contratado;
- sem necessidade de coordenação entre processos para acessar o banco;
- implantação e rollback mais simples.

Negativas:

- API e jobs compartilham recursos: um job pesado pode afetar a latência do
  webhook. Mitigado pelo volume baixo e pelo `DisallowConcurrentExecution`;
- não é possível escalar os dois de forma independente;
- **uma segunda instância da API executaria os jobs em duplicidade.** Enquanto o
  Quartz estiver com store em memória, a API roda em instância única. Se o
  escalonamento horizontal se tornar necessário, migrar o Quartz para
  AdoJobStore com clustering, ou extrair o worker para um projeto próprio.

## Alternativa considerada

Projetos separados (`Api` e `Worker`), como no serviço GMC. Descartada por
dobrar o custo de infraestrutura sem ganho proporcional neste volume.
