FROM maven:3.9.9-eclipse-temurin-21 AS build
WORKDIR /build
COPY pom.xml .
RUN mvn -B dependency:go-offline
COPY src src
RUN mvn -B verify

FROM eclipse-temurin:21-jre
WORKDIR /app
RUN groupadd --system webgso && useradd --system --gid webgso webgso && mkdir .reports && chown webgso:webgso .reports
COPY --from=build /build/target/webgso-system-2.0.0-SNAPSHOT.jar app.jar
USER webgso
ENV WEBGSO_ADDRESS=0.0.0.0
EXPOSE 5186
ENTRYPOINT ["java", "-jar", "app.jar"]
