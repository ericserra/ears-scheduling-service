# ADR 0002 — Estratégia de leitura da agenda para a confirmação D-1

**Status:** pendente de verificação (semana 1)
**Data:** setembro de 2026

## Contexto

A documentação pública da Amplimed não descreve endpoints para listar
agendamentos de um período nem para gravar o status "confirmado". Isso está
formalizado na **Cláusula 3ª do contrato**, com dois cenários previstos e
faculdade de abatimento de R$ 1.900 caso o módulo seja excluído.

## Decisão

O código suporta **os dois cenários desde o início**, alternados por
configuração — `Automacao:LeituraAgendaViaApi`:

- **Cenário A** (`true`): `ListarPorPeriodoAsync` lê a agenda da Amplimed;
  a confirmação cobre consultas marcadas por qualquer canal.
- **Cenário B** (`false`, padrão): a varredura usa o espelho local
  (`agendamentos`), cobrindo apenas o que foi marcado pelo WhatsApp.

Os métodos afetados retornam `null`/`false` diante de 404, 501 ou 405, em vez de
lançar exceção — a degradação é explícita e registrada em log.

## Consequências

- Nenhum retrabalho ao confirmar o cenário: muda-se uma flag de configuração.
- O espelho local precisa ser mantido íntegro mesmo no cenário A, para servir de
  fallback quando a API estiver indisponível.
- A verificação e seu resultado devem ser **comunicados por escrito à clínica na
  primeira semana**, conforme o § 1º da Cláusula 3ª.
