# ---------------------------------------------------------------------------
# Imagem: construída no host a partir do repositório, ou puxada de um registry.
# Numa VPS única sem registry, construir ali é o caminho mais curto.
# ---------------------------------------------------------------------------

resource "docker_image" "app_build" {
  count = var.app_image == null ? 1 : 0

  name = "${var.projeto}-integration:${var.app_versao}"

  build {
    context    = var.app_contexto
    dockerfile = "Dockerfile"
    tag        = ["${var.projeto}-integration:${var.app_versao}"]
  }

  # Rebuild é disparado pela versão, não por varredura do diretório: hashear a
  # árvore a cada plan seria lento e mudaria a cada arquivo de build gerado.
  triggers = {
    versao = var.app_versao
  }
}

resource "docker_image" "app_pull" {
  count        = var.app_image == null ? 0 : 1
  name         = var.app_image
  keep_locally = true
}

locals {
  app_image_id = var.app_image == null ? docker_image.app_build[0].image_id : docker_image.app_pull[0].image_id

  # Variáveis no padrão Secao__Chave que o serviço lê. env_extra sobrescreve,
  # para ajustar cron e templates sem editar o Terraform.
  env_base = {
    "ASPNETCORE_ENVIRONMENT" = var.ambiente
    "ASPNETCORE_URLS"        = "http://+:${var.app_porta}"
    "TZ"                     = var.fuso_horario

    "ConnectionStrings__Postgres" = local.conexao_postgres
    "Admin__ApiKey"               = var.admin_api_key

    "Amplimed__Pacientes__BaseUrl"            = var.amplimed_base_url
    "Amplimed__Pacientes__Token"              = var.amplimed_pacientes_token
    "Amplimed__Agendamento__BaseUrl"          = var.amplimed_base_url
    "Amplimed__Agendamento__Identificador"    = var.amplimed_agendamento_identificador
    "Amplimed__Agendamento__Token"            = var.amplimed_agendamento_token
    "Amplimed__Agendamento__Usuclin"          = var.amplimed_usuclin

    "SleekFlow__ApiKey"                   = var.sleekflow_api_key
    "SleekFlow__WebhookSecret"            = var.sleekflow_webhook_secret
    "SleekFlow__Canal"                    = var.sleekflow_canal
    "SleekFlow__RemetenteTelefone"        = var.sleekflow_remetente
    "SleekFlow__TemplateNamespace"        = var.sleekflow_template_namespace
    "SleekFlow__EtiquetaAgendamentoEmCurso" = var.sleekflow_etiqueta_em_curso
    "SleekFlow__EtiquetaTransferirRecepcao" = var.sleekflow_etiqueta_recepcao
    "SleekFlow__StaffIdRecepcao"          = var.sleekflow_staff_id_recepcao

    "Automacao__FusoHorario"              = var.fuso_horario
    "Automacao__LeituraAgendaViaApi"      = tostring(var.leitura_agenda_via_api)
    "Automacao__CronSincronizacaoAgenda"  = var.cron_sincronizacao_agenda
    "Automacao__DiasSincronizacaoAgenda"  = tostring(var.dias_sincronizacao_agenda)
    "Automacao__ConfirmacaoAtiva"         = tostring(var.confirmacao_ativa)
    "Automacao__CronExpiracaoConversa"    = var.cron_expiracao_conversa
  }

  env_final = merge(local.env_base, var.env_extra)

  # Roteamento. Dois roteadores apontando para o mesmo serviço: o geral e o de
  # /admin, este com prioridade maior e faixa de IP quando configurada.
  labels_base = {
    "traefik.enable"         = "true"
    "traefik.docker.network" = docker_network.edge.name

    "traefik.http.routers.${var.projeto}.rule"             = "Host(`${var.dominio}`)"
    "traefik.http.routers.${var.projeto}.entrypoints"      = "websecure"
    "traefik.http.routers.${var.projeto}.tls.certresolver" = "le"
    "traefik.http.routers.${var.projeto}.priority"         = "10"
    "traefik.http.routers.${var.projeto}.middlewares"      = "${var.projeto}-seguranca@docker"

    "traefik.http.services.${var.projeto}.loadbalancer.server.port" = tostring(var.app_porta)

    "traefik.http.middlewares.${var.projeto}-seguranca.headers.stsSeconds"           = "31536000"
    "traefik.http.middlewares.${var.projeto}-seguranca.headers.stsIncludeSubdomains" = "false"
    "traefik.http.middlewares.${var.projeto}-seguranca.headers.frameDeny"            = "true"
    "traefik.http.middlewares.${var.projeto}-seguranca.headers.contentTypeNosniff"   = "true"
    "traefik.http.middlewares.${var.projeto}-seguranca.headers.referrerPolicy"       = "no-referrer"
  }

  labels_admin = local.admin_restrito ? {
    "traefik.http.routers.${var.projeto}-admin.rule"             = "Host(`${var.dominio}`) && PathPrefix(`/admin`)"
    "traefik.http.routers.${var.projeto}-admin.entrypoints"      = "websecure"
    "traefik.http.routers.${var.projeto}-admin.tls.certresolver" = "le"
    "traefik.http.routers.${var.projeto}-admin.priority"         = "100"
    "traefik.http.routers.${var.projeto}-admin.service"          = var.projeto
    "traefik.http.routers.${var.projeto}-admin.middlewares"      = "${var.projeto}-admin-ips@docker,${var.projeto}-seguranca@docker"

    "traefik.http.middlewares.${var.projeto}-admin-ips.ipallowlist.sourcerange" = join(",", var.admin_ips)
  } : {}

  labels_final = merge(local.labels_base, local.labels_admin)
}

# ---------------------------------------------------------------------------
# UMA instância, e só uma.
#
# O Quartz usa store em memória (ADR 0001): duas instâncias disparam a mesma
# confirmação de véspera duas vezes, e o paciente recebe a mensagem em
# duplicidade. Escalar isto exige antes mover o Quartz para store no banco.
# ---------------------------------------------------------------------------

resource "docker_container" "api" {
  name     = "${var.projeto}-api"
  image    = local.app_image_id
  restart  = "unless-stopped"
  must_run = true

  env = [for chave, valor in local.env_final : "${chave}=${valor}"]

  networks_advanced {
    name = docker_network.edge.name
  }

  networks_advanced {
    name = docker_network.dados.name
  }

  dynamic "labels" {
    for_each = local.labels_final
    content {
      label = labels.key
      value = labels.value
    }
  }

  # Sem ports: quem fala com a internet é o Traefik. A API só é alcançável
  # pela rede edge.

  depends_on = [docker_container.postgres]

  # Espera o /health passar antes de terminar o apply. O healthcheck vem do
  # próprio Dockerfile (HEALTHCHECK no estágio de runtime) e cobre API e
  # Postgres, então um deploy quebrado falha aqui em vez de ficar reiniciando
  # em silêncio até alguém reclamar.
  wait         = true
  wait_timeout = 180

  log_driver = "json-file"
  log_opts   = local.log_opts
}
