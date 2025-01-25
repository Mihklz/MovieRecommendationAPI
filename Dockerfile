# Используем официальный образ .NET для запуска приложения с .NET 9.0
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Используем официальный образ .NET SDK для сборки приложения с .NET 9.0
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем файл проекта и восстанавливаем зависимости
COPY ["MovieRecommendationAPI.csproj", "./"]
RUN dotnet restore "./MovieRecommendationAPI.csproj"

# Копируем все остальные файлы проекта
COPY . .

# Строим проект
WORKDIR "/src"
RUN dotnet build "MovieRecommendationAPI.csproj" -c Release -o /app/build

# Публикуем проект
FROM build AS publish
RUN dotnet publish "MovieRecommendationAPI.csproj" -c Release -o /app/publish

# Выполняем миграции перед созданием финального образа
FROM publish AS migrate
WORKDIR /src

# Устанавливаем EF Tools
RUN dotnet tool install --global dotnet-ef
ENV PATH="$PATH:/root/.dotnet/tools"

# Применяем миграции к базе данных
RUN dotnet ef database update --project "MovieRecommendationAPI.csproj"

# Используем базовый образ для финальной стадии, чтобы запустить приложение
FROM base AS final
WORKDIR /app

# Копируем опубликованные файлы в финальный образ
COPY --from=publish /app/publish .

# Указываем команду для запуска приложения
ENTRYPOINT ["dotnet", "MovieRecommendationAPI.dll"]



