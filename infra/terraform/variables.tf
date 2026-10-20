# ---------------------------------------------------------------------------
# Identidade e acesso ao Docker
# ---------------------------------------------------------------------------

variable "projeto" {
  description = "Prefixo de todos os recursos criados (containers, redes, volumes)."
  type        = string
  default     = "ears"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,20}$", var.projeto))
    error_message = "Use minúsculas, dígitos e hífen, começando por letra."
  }
}

variable "docker_host" {
  description = "Socket do Docker. Rodando na própria VPS, o padrão serve."
  type        = string
  default     = "unix:///var/run/docker.sock"
}

variable "fuso_horario" {
  description = "Fuso dos containers. Precisa casar com Automacao__FusoHorario."
  type        = string
  default     = "America/Sao_Paulo"
}

# ---------------------------------------------------------------------------
# Domínio e TLS
# ---------------------------------------------------------------------------

variable "dominio" {
  description = <<-TXT
    Domínio público da API, onde o webhook do SleekFlow vai bater.
    PRÉ-REQUISITO: um registro A apontando para o IP da VPS já propagado.
    Sem isso o desafio TLS-ALPN da Let's Encrypt falha e o Traefik serve
    certificado inválido.
  TXT
  type = string

  validation {
    condition     = can(regex("^[a-z0-9.-]+\\.[a-z]{2,}$", var.dominio))
    error_message = "Informe um domínio, sem esquema e sem barra final."
  }
}

variable "acme_email" {
  description = "E-mail de contato da Let's Encrypt (avisos de expiração)."
  type        = string
}

variable "acme_staging" {
  description = <<-TXT
    true usa o ambiente de staging da Let's Encrypt: certificado não confiável
    pelo navegador, mas sem limite de emissão. Suba a primeira vez com true,
    confirme que o roteamento funciona, depois vire para false.

    O limite de produção é de 5 falhas por conta, por hora: errar o DNS com
    staging desligado trava a emissão pelo resto da hora.
  TXT
  type    = bool
  default = true
}

# ---------------------------------------------------------------------------
# Imagem da aplicação
# ---------------------------------------------------------------------------

variable "app_image" {
  description = <<-TXT
    Imagem pronta a usar (ex.: "ghcr.io/strenos/ears-integration:1.0.3").
    Deixe null para o Terraform construir a partir do Dockerfile do repositório
    em app_contexto — que é o caminho natural numa VPS única, sem registry.
  TXT
  type    = string
  default = null
}

variable "app_contexto" {
  description = "Raiz do repositório na VPS, usada no build quando app_image é null."
  type        = string
  default     = "/opt/ears/src"
}

variable "app_versao" {
  description = <<-TXT
    Tag e gatilho de rebuild. Mudar este valor é o que faz o Terraform
    reconstruir a imagem e recriar o container; o conteúdo do diretório não é
    observado (varrer a árvore a cada plan seria lento e instável).
  TXT
  type    = string
  default = "1.0.0"
}

variable "app_porta" {
  description = "Porta onde a API escuta dentro do container. Casa com ASPNETCORE_URLS."
  type        = number
  default     = 8080
}

variable "ambiente" {
  description = "ASPNETCORE_ENVIRONMENT. Em Development o Swagger fica exposto."
  type        = string
  default     = "Production"

  validation {
    condition     = contains(["Production", "Staging", "Development"], var.ambiente)
    error_message = "Use Production, Staging ou Development."
  }
}

# ---------------------------------------------------------------------------
# Banco
# ---------------------------------------------------------------------------

variable "postgres_image" {
  description = "Imagem do Postgres. O serviço foi escrito para a 16."
  type        = string
  default     = "postgres:16-alpine"
}

variable "postgres_db" {
  type    = string
  default = "ears"
}

variable "postgres_user" {
  type    = string
  default = "ears"
}

variable "postgres_password" {
  description = <<-TXT
    Senha do Postgres. Deixe null para o Terraform gerar uma de 32 caracteres
    alfanuméricos — sem símbolos de propósito: ";" e "=" quebram a string de
    conexão do Npgsql, e o erro resultante não diz o que aconteceu.
  TXT
  type      = string
  default   = null
  sensitive = true
}

variable "postgres_porta_local" {
  description = <<-TXT
    Porta no host para acessar o banco, publicada SOMENTE em 127.0.0.1 — você
    chega nela por túnel SSH, não pela internet. null não publica nada, que é o
    padrão correto para produção.
  TXT
  type    = number
  default = null
}

# ---------------------------------------------------------------------------
# Segredos da aplicação
# ---------------------------------------------------------------------------

variable "admin_api_key" {
  description = "Cabeçalho X-Api-Key dos endpoints /admin."
  type        = string
  sensitive   = true

  validation {
    condition     = length(var.admin_api_key) >= 24
    error_message = "Use pelo menos 24 caracteres — esse endpoint lê a trilha de mensagens."
  }
}

variable "admin_ips" {
  description = <<-TXT
    Faixas CIDR autorizadas a alcançar /admin (ex.: ["203.0.113.4/32"]).

    Lista vazia significa que /admin fica acessível de qualquer IP, protegido
    apenas pela chave de API. Funciona, mas um endpoint que expõe a trilha de
    mensagens de pacientes merece as duas camadas. Vale preencher.
  TXT
  type    = list(string)
  default = []
}

variable "amplimed_base_url" {
  description = "Raiz do gateway da Amplimed, com barra final. Homologação: https://stgintegracoes.amplimed.cloud/"
  type        = string
  default     = "https://integracoes.amplimed.com.br/"
}

variable "amplimed_pacientes_token" {
  description = "Token da API de Cadastro de Pacientes (vai como Basic no x-Authorization)."
  type        = string
  sensitive   = true
}

variable "amplimed_agendamento_identificador" {
  description = "Identificador da API de Agendamento. Vai no caminho da rota."
  type        = string
  sensitive   = true
}

variable "amplimed_agendamento_token" {
  description = "Token da API de Agendamento (cabeçalho x-authorization)."
  type        = string
  sensitive   = true
}

variable "amplimed_usuclin" {
  description = <<-TXT
    Número da clínica na Amplimed. Obrigatório em listar-agendamentos e
    alterar-status-agendamento: sem ele o cenário A da Cláusula 3ª não roda e a
    confirmação cobre apenas o que foi marcado pelo WhatsApp.
  TXT
  type    = string
  default = ""
}

variable "sleekflow_api_key" {
  type      = string
  sensitive = true
}

variable "sleekflow_webhook_secret" {
  description = "Segredo compartilhado que o serviço confere no webhook."
  type        = string
  sensitive   = true
}

# ---------------------------------------------------------------------------
# Automação
# ---------------------------------------------------------------------------

variable "leitura_agenda_via_api" {
  description = "Cenário A da Cláusula 3ª. false degrada para o espelho local."
  type        = bool
  default     = true
}

variable "cron_sincronizacao_agenda" {
  description = "Polling da agenda. A Amplimed não oferece webhook nesta integração."
  type        = string
  default     = "0 0/15 * * * ?"
}

variable "dias_sincronizacao_agenda" {
  type    = number
  default = 2
}

variable "env_extra" {
  description = <<-TXT
    Variáveis adicionais no padrão Secao__Chave, para ajustar cron de
    confirmação, janela de envio ou nomes de template sem mexer no Terraform.
  TXT
  type    = map(string)
  default = {}
}

variable "log_max_size" {
  description = "Tamanho máximo de cada arquivo de log por container."
  type        = string
  default     = "10m"
}

variable "log_max_file" {
  description = "Quantos arquivos de log rotacionados manter."
  type        = string
  default     = "3"
}
