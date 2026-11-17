#!/usr/bin/env bash
# Atalhos para operar o EARS.Integration na VPS.
#
# Uso (a cada login):   source /opt/ears/src/scripts/ears-vps.sh
# Depois:               ears_ajuda
#
# Nada aqui guarda segredo: a chave admin é lida do terraform.tfvars na hora.

EARS_SRC=/opt/ears/src
EARS_TF=$EARS_SRC/infra/terraform
EARS_API=https://ears.strenos.com.br

cd "$EARS_TF" 2>/dev/null || { echo "pasta $EARS_TF não existe"; return 1 2>/dev/null || exit 1; }
ADMIN_KEY=$(grep admin_api_key terraform.tfvars | sed 's/.*"\(.*\)".*/\1/')
export ADMIN_KEY

_json() { python3 -m json.tool 2>/dev/null || cat; }

ears_ajuda() {
cat <<'AJUDA'
Estado
  ears_versao                      versão no ar (deve bater com git rev-parse --short HEAD)
  ears_saude                       /health
Deploy
  ears_deploy                      git pull + terraform apply com a versão do commit
Logs (minutos opcional, padrão 20)
  ears_logs [min]                  tudo
  ears_erros [min]                 só WRN/ERR com stack trace
  ears_transferencias [min]        por que o bot mandou para a recepção
  ears_amplimed_erro [min]         recusas do cadastrar-agendamento (traz "campos enviados")
  ears_envios [min]                envios ao SleekFlow
Agenda e disparo D-1  (dia no formato AAAA-MM-DD)
  ears_sync DIA                    sincroniza a agenda do dia e lista descartados
  ears_simular DIA                 simula o disparo geral (não envia nada)
  ears_disparar DIA TEL[,TEL]      envio REAL só para esses telefones (ex.: 5516997598594)
  ears_reenviar DIA TEL[,TEL]      igual, ignorando a janela anti-duplicidade de 6h
Banco
  ears_sql "SELECT ..."            consulta no Postgres
  ears_conversas [n]               últimas conversas do chatbot (estado, última interação)
Config
  ears_env [filtro]                variáveis do container (valores ocultos), ex.: ears_env sleekflow
AJUDA
}

ears_versao() { curl -s -H "X-Api-Key: $ADMIN_KEY" "$EARS_API/admin/config" | grep -o '"versao":"[^"]*"'; echo "commit local: $(git -C "$EARS_SRC" rev-parse --short HEAD)"; }
ears_saude()  { curl -si "$EARS_API/health" | head -1; }

ears_deploy() {
  git -C "$EARS_SRC" pull && ( cd "$EARS_TF" && terraform apply -var="app_versao=$(git -C "$EARS_SRC" rev-parse --short HEAD)" )
}

ears_logs()           { docker logs ears-api --since "${1:-20}m" 2>&1 | tail -150; }
ears_erros()          { docker logs ears-api --since "${1:-20}m" 2>&1 | grep -E "\[(WRN|ERR|FTL)\]" -A12 | tail -120; }
ears_transferencias() { docker logs ears-api --since "${1:-20}m" 2>&1 | grep -E "Transferindo|Falha ao processar a conversa|urgência" -B2 -A12 | tail -80; }
ears_amplimed_erro()  { docker logs ears-api --since "${1:-20}m" 2>&1 | grep "cadastrar-agendamento falhou"; }
ears_envios()         { docker logs ears-api --since "${1:-20}m" 2>&1 | grep -iE "sleekflow|template|Etiquetas" | tail -60; }

_post() { curl -s -X POST -H "X-Api-Key: $ADMIN_KEY" "$1" | _json; }

ears_sync()    { _post "$EARS_API/admin/agenda/sincronizar?dia=$1"; }
ears_simular() { _post "$EARS_API/admin/confirmacao/disparar?dia=$1&todos=true&simular=true"; }
ears_disparar(){ _post "$EARS_API/admin/confirmacao/disparar?dia=$1&telefones=$2&simular=false&avisarRecepcao=false"; }
ears_reenviar(){ _post "$EARS_API/admin/confirmacao/disparar?dia=$1&telefones=$2&simular=false&avisarRecepcao=false&reenviar=true"; }

ears_sql()       { docker exec ears-postgres psql -U ears -d ears -c "$1"; }
ears_conversas() { ears_sql "SELECT \"Telefone\", \"Estado\", \"SexoBiologicoInformado\" AS sexo, \"UltimaInteracaoEm\" FROM conversas ORDER BY \"UltimaInteracaoEm\" DESC LIMIT ${1:-10};"; }

ears_env() { docker exec ears-api printenv | grep -iE "${1:-.}" | sed -E 's/(KEY|TOKEN|SECRET|PASSWORD)([^=]*)=.*/\1\2=…/I'; }

echo "EARS: comandos carregados. Digite ears_ajuda."
