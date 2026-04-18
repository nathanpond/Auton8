FROM flowable/flowable-rest:latest

ARG POSTGRES_JDBC_VERSION=42.7.7

USER root

RUN mkdir -p /opt/flowable/drivers

ADD https://repo1.maven.org/maven2/org/postgresql/postgresql/${POSTGRES_JDBC_VERSION}/postgresql-${POSTGRES_JDBC_VERSION}.jar /opt/flowable/drivers/postgresql.jar

ENV LOADER_PATH=/opt/flowable/drivers
ENV JAVA_TOOL_OPTIONS="-Dloader.path=/opt/flowable/drivers"
