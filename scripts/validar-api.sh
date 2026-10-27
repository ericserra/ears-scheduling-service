#!/usr/bin/env bash
# Validação da API do motor EARS após um deploy.
#
# Uso, na VPS:
#   bash scripts/validar-api.sh
#   bash scripts/validar-api.sh --com-envio      # inclui o que manda WhatsApp
#
# Por padrão roda só o que é leitura ou escrita local. Nada sai para o
# paciente sem --com-envio.

set -uo pipefail

BASE="${BASE:-https://ears.strenos.com.br}"
TFVARS="${TFVARS:-/opt/ears/src/infra/terraform/terraform.tfvars}"
CPF="${CPF:-24644763829}"
TELEFONE="${TELEFONE:-5516997598594}"
COM_ENVIO=0
[ "${1:-}" = "--com-envio" ] && COM_ENVIO=1

if [ -z "${ADMIN_KEY:-}" ]; then
  ADMIN_KEY=$(grep -E '^\s*admin_api_key' "$TFVARS" 2>/dev/null | cut -d'"' -f2)
fi

if [ -z "${ADMIN_KEY:-}" ]; then
  echo "ADMIN_KEY não encontrada. Exporte ADMIN_KEY=... ou ajuste TFVARS=$TFVARS" >&2
  exit 1
fi

if [ -z "${SIGN:-}" ]; then
  SIGN=$(grep -E '^\s*sleekflow_webhook_secret' "$TFVARS" 2>/dev/null | cut -d'"' -f2)
fi

verde=$'\033[32m'; vermelho=$'\033[31m'; amarelo=$'\033[33m'; zero=$'\033[0m'
falhas=0

secao () { printf '\n%s== %s%s\n' "$amarelo" "$1" "$zero"; }

# chamar <esperado> <metodo> <caminho> [curl extra...]
chamar () {
  local esperado="$1" metodo="$2" caminho="$3"; shift 3
  local corpo codigo
  corpo=$(curl -s -o /tmp/val.$$ -w '%{http_code}' -m 30 \
            -X "$metodo" "$BASE$caminho" -H "X-Api-Key: $ADMIN_KEY" "$@")
  codigo="$corpo"

  if [ "$codigo" = "$esperado" ]; then
    printf '%s  ok %s%s %s -> %s\n' "$verde" "$zero" "$metodo" "$caminho" "$codigo"
  else
    printf '%s FALHA%s %s %s -> %s (esperado %s)\n' "$vermelho" "$zero" "$metodo" "$caminho" "$codigo" "$esperado"
    falhas=$((falhas + 1))
  fi

  if command -v jq >/dev/null 2>&1; then
    jq -C . /tmp/val.$$ 2>/dev/null | head -25 || head -c 500 /tmp/val.$$
  else
    head -c 500 /tmp/val.$$
  fi
  echo; rm -f /tmp/val.$$
}

ontem=$(date -d 'yesterday' +%F)
hoje=$(date +%F)
amanha=$(date -d 'tomorrow' +%F)

# ---------------------------------------------------------------------------
secao "1. O serviço está de pé"

printf 'GET /health -> '
curl -s -o /dev/null -w '%{http_code}\n' -m 15 "$BASE/health"

chamar 200 GET /admin/prontidao
chamar 200 GET /admin/config

# ---------------------------------------------------------------------------
secao "2. Amplimed: leitura"

# A pergunta em aberto desde 23/10: a Amplimed respeita o filtro de CPF?
# Se filtroDeCpfRespeitado vier false, quem segura o paciente certo é a
# conferência que o client faz por conta.
chamar 200 GET "/admin/amplimed/paciente?cpf=$CPF"

# Puxa a agenda de amanhã, que é a janela da confirmação D-1.
chamar 200 POST "/admin/agenda/sincronizar?dia=$amanha"
chamar 200 GET  "/admin/agendamentos?dia=$amanha"
chamar 200 GET  "/admin/agendamentos?dia=$hoje"

# ---------------------------------------------------------------------------
secao "3. SleekFlow: credencial, sem mandar mensagem"

# apenasJanela=true consulta a janela de 24h e não dispara nada.
chamar 200 POST "/admin/sleekflow/teste?telefone=$TELEFONE&apenasJanela=true"

# ---------------------------------------------------------------------------
secao "4. Higiene do estado"

chamar 200 GET  "/admin/conversas/etiquetas-presas"
chamar 200 POST "/admin/conversas/expirar"
chamar 200 GET  "/admin/logs?pagina=1"

# ---------------------------------------------------------------------------
secao "5. Autenticação: o que TEM que falhar"

printf 'GET /admin/prontidao sem chave -> '
curl -s -o /dev/null -w '%{http_code}' -m 15 "$BASE/admin/prontidao"
printf '  (esperado 401)\n'

printf 'GET /admin/prontidao com chave errada -> '
curl -s -o /dev/null -w '%{http_code}' -m 15 "$BASE/admin/prontidao" -H 'X-Api-Key: errada'
printf '  (esperado 401)\n'

printf 'POST /webhooks/sleekflow com assinatura errada -> '
curl -s -o /dev/null -w '%{http_code}' -m 15 -X POST "$BASE/webhooks/sleekflow" \
  -H 'Content-Type: application/json' -H 'X-SleekFlow-Signature: errada' -d '{}'
printf '  (esperado 401)\n'

# ---------------------------------------------------------------------------
secao "6. Webhook de atendimento"

if [ -z "${SIGN:-}" ]; then
  echo "sleekflow_webhook_secret não encontrado no tfvars; pulando."
else
  # 200 = o motor assumiu a conversa. 503 = recusou, e o Flow Builder manda
  # para a Recepção. Com tudo configurado, espere 200.
  printf 'POST /webhooks/sleekflow/atendimento -> '
  curl -s -w ' [%{http_code}]\n' -m 30 -X POST "$BASE/webhooks/sleekflow/atendimento" \
    -H 'Content-Type: application/json' \
    -H "X-SleekFlow-Signature: $SIGN" \
    -d "{\"telefone\":\"$TELEFONE\",\"motivo\":\"agendar-consulta\",\"nome\":\"Teste Strenos\"}"

  echo
  echo "Motivo inválido tem que dar 400:"
  curl -s -w ' [%{http_code}]\n' -m 30 -X POST "$BASE/webhooks/sleekflow/atendimento" \
    -H 'Content-Type: application/json' \
    -H "X-SleekFlow-Signature: $SIGN" \
    -d "{\"telefone\":\"$TELEFONE\",\"motivo\":\"comprar-pizza\"}"
fi

# ---------------------------------------------------------------------------
if [ "$COM_ENVIO" = 1 ]; then
  secao "7. ENVIO REAL (sai mensagem no WhatsApp)"

  chamar 200 POST "/admin/sleekflow/teste?telefone=$TELEFONE&texto=Teste%20do%20motor%20Strenos"

  echo
  echo "A confirmação D-1 varre a agenda inteira de amanhã e manda template"
  echo "para TODOS os pacientes do dia. Rode só com a agenda vazia ou de teste:"
  echo "  curl -s -X POST '$BASE/admin/confirmacao/disparar' -H \"X-Api-Key: \$ADMIN_KEY\""
else
  secao "7. Envio real: pulado"
  echo "Use --com-envio para incluir. Fora disso, nada sai para paciente nenhum."
fi

# ---------------------------------------------------------------------------
secao "Resultado"

if [ "$falhas" -eq 0 ]; then
  printf '%sTodas as chamadas autenticadas responderam o esperado.%s\n' "$verde" "$zero"
else
  printf '%s%s chamada(s) fora do esperado. Veja os corpos acima.%s\n' "$vermelho" "$falhas" "$zero"
fi

echo
echo "Depois disso, confira sempre:"
echo "  /admin/prontidao      -> pronta: true, pendencias vazias"
echo "  amplimed/paciente     -> filtroDeCpfRespeitado: true ou false, nunca 502"
echo "  etiquetas-presas      -> total: 0"
echo "  docker logs --tail 50 ears-api"

exit $(( falhas > 0 ? 1 : 0 ))
