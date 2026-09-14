-- Expurgo previsto na Cláusula 13ª, § 5º do contrato: retenção de 12 meses.
-- Agendar mensalmente no host:
--   0 4 1 * * docker exec -i ears-postgres psql -U ears -d ears < /opt/ears/scripts/expurgo-lgpd.sql

BEGIN;

DELETE FROM mensagens_log
WHERE "CriadoEm" < now() - interval '12 months';

DELETE FROM conversas
WHERE "UltimaInteracaoEm" < now() - interval '12 months';

DELETE FROM outbox
WHERE "Status" = 2 AND "AtualizadoEm" < now() - interval '3 months';

COMMIT;
