#!/usr/bin/env bash
# Publica o repositório no GitHub. Requer gh CLI autenticado.
set -euo pipefail

ORG="${GITHUB_ORG:-strenos}"
REPO="${REPO_NAME:-ears-integration}"

gh repo create "$ORG/$REPO" --private --source=. --remote=origin --push

# Proteção da branch main
gh api -X PUT "repos/$ORG/$REPO/branches/main/protection" \
  -f "required_status_checks[strict]=true" \
  -f "required_status_checks[contexts][]=Build e testes" \
  -F "enforce_admins=false" \
  -F "required_pull_request_reviews[required_approving_review_count]=0" \
  -F "restrictions=null" 2>/dev/null || echo "Proteção de branch requer plano pago em repositório privado."

echo "Repositório publicado: https://github.com/$ORG/$REPO"
