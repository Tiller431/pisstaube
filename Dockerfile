FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-env

WORKDIR /pisstaube

COPY . /pisstaube

RUN dotnet restore
RUN dotnet publish Pisstaube -c Release -o /pisstaube/out

FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /pisstaube

COPY --from=build-env /pisstaube/out .

RUN touch .env

VOLUME [ "/pisstaube/data" ]

ENTRYPOINT ["dotnet", "Pisstaube.dll"]
