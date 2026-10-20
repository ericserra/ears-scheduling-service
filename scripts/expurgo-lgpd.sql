-- Expurgo previsto na Cláusula 13ª, § 5º do contrato: retenção de 12 meses.
-- Agendar mensalmente no host:
--   0 4 1 * * docker exec -i ears-postgres psql -U ears -d ears < /opt/ears/scripts/expurgo-lgpd.sql

BEGIN;

-- Trilha de auditoria de mensagens.
DELETE FROM mensagens_log
WHERE "CriadoEm" < now() - interval '12 months';

-- Conversas antigas. Apaga a linha inteira, e com ela o que a coleta definida
-- pela clínica acrescentou ao registro: idade, convênio, preferência de dia e
-- turno, nome e data de nascimento informados na conversa. São dados pessoais
-- coletados para marcar uma consulta e não têm razão de sobreviver a ela.
DELETE FROM conversas
WHERE "UltimaInteracaoEm" < now() - interval '12 months';

-- Agendamentos encerrados além da retenção. O espelho local existe para operar
-- a confirmação; passada a retenção, a fonte da verdade segue sendo a Amplimed.
DELETE FROM agendamentos
WHERE "InicioEm" < now() - interval '12 months';

-- Pacientes sem agendamento e sem conversa: espelho órfão, sem finalidade.
DELETE FROM pacientes p
WHERE NOT EXISTS (SELECT 1 FROM agendamentos a WHERE a."PacienteId" = p."Id")
  AND NOT EXISTS (SELECT 1 FROM conversas c WHERE c."PacienteId" = p."Id")
  AND p."AtualizadoEm" < now() - interval '12 months';

DELETE FROM outbox
WHERE "Status" = 2 AND "AtualizadoEm" < now() - interval '3 months';

COMMIT;

-- ---------------------------------------------------------------------------
-- Diagnóstico operacional, fora da transação.
--
-- Conversa expirada que ainda consta com a etiqueta agendamento-em-curso é
-- paciente preso sem menu no WhatsApp: ele escreve e nada acontece. O job de
-- expiração tenta de novo a cada rodada, mas se esta consulta voltar linhas
-- com frequência, a chamada ao SleekFlow está falhando e vale investigar.
-- ---------------------------------------------------------------------------
SELECT "Telefone", "UltimaInteracaoEm"
FROM conversas
WHERE "EtiquetaAplicada" = true
  AND "Estado" IN (20, 21, 22)
ORDER BY "UltimaInteracaoEm";
