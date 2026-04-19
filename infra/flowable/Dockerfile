FROM maven:3.9.9-eclipse-temurin-21 AS flowable-extension-build

WORKDIR /src

COPY flowable-extension/pom.xml ./pom.xml
COPY flowable-extension/src ./src

RUN mvn --batch-mode test package

FROM flowable/flowable-rest:latest

USER root

COPY --from=flowable-extension-build /src/target/autonate-flowable-events-1.0.0-SNAPSHOT.jar /app/WEB-INF/lib/autonate-flowable-events.jar
