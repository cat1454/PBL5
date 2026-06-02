# syntax=docker/dockerfile:1

FROM node:20-alpine AS build
WORKDIR /app

ARG REACT_APP_API_BASE_URL=https://pbl5-api.danangtoiiu.live
ENV REACT_APP_API_BASE_URL=$REACT_APP_API_BASE_URL

COPY package*.json ./
RUN npm ci

COPY . ./
RUN npm run build

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/build /usr/share/nginx/html

EXPOSE 80
