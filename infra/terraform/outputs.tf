output "url_webhook" {
  description = "Endereço a cadastrar no SleekFlow."
  value       = "https://${var.dominio}/webhooks/sleekflow"
}

output "url_health" {
  value = "https://${var.dominio}/health"
}

output "certificado_de_staging" {
  description = "true significa certificado não confiável pelo navegador. Sem efeito quando o proxy é externo."
  value       = var.usar_traefik && var.acme_staging
}

output "cenario_clausula_terceira" {
  description = "Qual cenário da Cláusula 3ª está em vigor nesta instalação."
  value = (
    var.leitura_agenda_via_api && var.amplimed_usuclin != ""
    ? "A — agenda lida da Amplimed, confirmação cobre todos os canais"
    : "B — espelho local apenas; verifique Amplimed__Agendamento__Usuclin"
  )
}

output "admin_restrito_por_ip" {
  description = "false significa que /admin está protegido apenas pela chave de API."
  value       = local.admin_restrito
}

output "url_atendimento" {
  description = "Endereço a configurar no nó HTTP do Flow Builder, nos ramos 1 e 3 do menu."
  value       = "https://${var.dominio}/webhooks/sleekflow/atendimento"
}

output "modulo2_confirmacao" {
  description = "Estado do Módulo 2 nesta instalação."
  value = (
    var.confirmacao_ativa
    ? "ATIVO — confirmação de véspera e lembrete serão disparados"
    : "desligado — aguardando a clínica confirmar que não duplica lembrete existente"
  )
}

output "proxy" {
  description = "Quem atende 80 e 443, e como o proxy alcança a API."
  value = (
    var.usar_traefik
    ? "Traefik desta stack, com TLS automático da Let's Encrypt"
    : format(
        "proxy externo. Aponte-o para %s. TLS é responsabilidade dele.",
        var.rede_proxy_externa != ""
          ? "o container ${var.projeto}-api na rede ${var.rede_proxy_externa}, porta ${var.app_porta}"
          : "127.0.0.1:${var.porta_host_api}"
      )
  )
}

output "containers" {
  value = merge(
    {
      api      = docker_container.api.name
      postgres = docker_container.postgres.name
    },
    var.usar_traefik ? { traefik = docker_container.traefik[0].name } : {},
  )
}

output "postgres_password" {
  description = "Senha do banco, gerada quando não informada. terraform output -raw postgres_password"
  value       = local.postgres_password
  sensitive   = true
}
