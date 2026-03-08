FROM node:24-alpine AS build
WORKDIR /app

COPY package*.json ./
COPY apps/admin-portal/package.json apps/admin-portal/package.json
COPY packages/contracts/package.json packages/contracts/package.json
COPY packages/theme/package.json packages/theme/package.json
RUN npm install

COPY . .
RUN npm run build --workspace @owlprotect/admin-portal

FROM nginx:1.29-alpine
COPY --from=build /app/apps/admin-portal/dist /usr/share/nginx/html

