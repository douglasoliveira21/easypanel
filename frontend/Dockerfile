# =============================================================================
# Frontend EasyPanel — placeholder da Fase 1.
#
# A aplicação React + Vite completa é entregue em fase posterior. Na Fase 1
# provisionamos o serviço `frontend` (servido pelo Caddy) com um build estático
# mínimo, de forma que a topologia do Docker Compose e o roteamento do reverse
# proxy (R1.7) fiquem completos e verificáveis.
#
# Quando o app React existir, este Dockerfile passa a um build multi-stage:
#   FROM node:22 AS build ... npm ci && npm run build
#   FROM nginx:alpine  (copiando dist/)
# =============================================================================

FROM nginx:alpine
COPY dist/ /usr/share/nginx/html/
EXPOSE 80
