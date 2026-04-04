# Stage 1: Build React frontend
FROM node:20-alpine AS frontend
WORKDIR /app/client
COPY client/package.json client/package-lock.json ./
RUN npm ci
COPY client/ ./
RUN npm run build

# Stage 2: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend
WORKDIR /app
COPY Directory.Build.props AskMyPdf.slnx ./
COPY src/AskMyPdf.Core/ src/AskMyPdf.Core/
COPY src/AskMyPdf.Infrastructure/ src/AskMyPdf.Infrastructure/
COPY src/AskMyPdf.Web/ src/AskMyPdf.Web/
RUN dotnet publish src/AskMyPdf.Web/AskMyPdf.Web.csproj -c Release -o /publish

# Copy frontend build into wwwroot
COPY --from=frontend /app/client/dist/ /publish/wwwroot/

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=backend /publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:5000

EXPOSE 5000
ENTRYPOINT ["dotnet", "AskMyPdf.Web.dll"]
