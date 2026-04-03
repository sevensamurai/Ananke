# ── Build stage ──────────────────────────────────────────────────────
# Build context must be the solution root so all ProjectReferences resolve.
# docker-compose sets: context: ../..
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Directory.Build.props and Directory.Packages.props live at the solution root.
# Both must be present before dotnet restore runs: Build.props supplies
# <TargetFramework> and Packages.props supplies all central version pins.
COPY Directory.Build.props .
COPY Directory.Packages.props .

# Copy .csproj files first for layer-cached restore
COPY Ananke.Abstractions/Ananke.Abstractions.csproj          Ananke.Abstractions/
COPY Ananke.StateMachine/Ananke.StateMachine.csproj          Ananke.StateMachine/
COPY Ananke.Orchestration/Ananke.Orchestration.csproj        Ananke.Orchestration/
COPY Ananke.Learning/Ananke.Learning.csproj                  Ananke.Learning/
COPY Ananke/Ananke.csproj                                    Ananke/
COPY Ananke.AspNetCore/Ananke.AspNetCore.csproj              Ananke.AspNetCore/
COPY Ananke.Design/Ananke.Design.csproj                      Ananke.Design/
COPY Ananke.Documents/Ananke.Documents.csproj                Ananke.Documents/
COPY Ananke.MQTT/Ananke.MQTT.csproj                          Ananke.MQTT/
COPY Ananke.Orchestration.Google/Ananke.Orchestration.Google.csproj   Ananke.Orchestration.Google/
COPY Ananke.Orchestration.OpenAI/Ananke.Orchestration.OpenAI.csproj   Ananke.Orchestration.OpenAI/
COPY Ananke.Qdrant/Ananke.Qdrant.csproj                      Ananke.Qdrant/
COPY Ananke.Redis/Ananke.Redis.csproj                        Ananke.Redis/
COPY demos/PetAdoptionDemo/PetAdoptionDemo.csproj            demos/PetAdoptionDemo/

RUN dotnet restore demos/PetAdoptionDemo/PetAdoptionDemo.csproj

# Copy all required source trees
COPY Ananke.Abstractions/   Ananke.Abstractions/
COPY Ananke.StateMachine/   Ananke.StateMachine/
COPY Ananke.Orchestration/  Ananke.Orchestration/
COPY Ananke.Learning/       Ananke.Learning/
COPY Ananke/                Ananke/
COPY Ananke.AspNetCore/     Ananke.AspNetCore/
COPY Ananke.Design/         Ananke.Design/
COPY Ananke.Documents/      Ananke.Documents/
COPY Ananke.MQTT/           Ananke.MQTT/
COPY Ananke.Orchestration.Google/   Ananke.Orchestration.Google/
COPY Ananke.Orchestration.OpenAI/   Ananke.Orchestration.OpenAI/
COPY Ananke.Qdrant/         Ananke.Qdrant/
COPY Ananke.Redis/          Ananke.Redis/
COPY demos/PetAdoptionDemo/ demos/PetAdoptionDemo/

RUN dotnet publish demos/PetAdoptionDemo/PetAdoptionDemo.csproj \
    -c Release -o /app/publish --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Default to port 8080 (ASP.NET Core container default)
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PetAdoptionDemo.dll"]
