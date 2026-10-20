output "url_webhook" {
  description = "Endereço a cadastrar no SleekFlow."
  value       = "https://${var.dominio}/webhooks/sleekflow"
}

output "url_health" {
  value = "https://${var.dominio}/health"
}

output "certificado_de_staging" {
  description = "true significa certificado não confiável pelo navegador — vire acme_staging para false depois de validar o roteamento."
  value       = var.acme_staging
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

output "containers" {
  value = {
    traefik  = docker_container.traefik.name
    api      = docker_container.api.name
    postgres = docker_container.postgres.name
  }
}

output "postgres_password" {
  description = "Senha do banco, gerada quando não informada. terraform output -raw postgres_password"
  value       = local.postgres_password
  sensitive   = true
}
