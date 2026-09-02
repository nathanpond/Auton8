FROM maven@sha256:3a4ab3276a087bf276f79cae96b1af04f53731bec53fb2e651aca79e4b10211e AS flowable-extension-build  # maven:3.9.9-eclipse-temurin-21

WORKDIR /src

COPY flowable-extension/pom.xml ./pom.xml
COPY flowable-extension/src ./src

RUN mvn --batch-mode test package

# Pinned by digest. This was `flowable/flowable-rest:latest`, the only genuinely
# floating tag in the repository — and the one where floating was most dangerous:
# flowable-extension/pom.xml compiles against <flowable.version>8.0.0</flowable.version>,
# and this digest resolves to Flowable 8.0.0. They agreed only by coincidence. A
# `latest` that moved to 8.1 or 9.0 would have put a compiled extension against an
# engine it was never built for, and the symptom would have been a runtime linkage
# error inside the JVM rather than anything a build would catch.
#
# Upgrading is deliberate: bump the digest here AND <flowable.version> in the pom
# together, or they diverge again.
FROM flowable/flowable-rest@sha256:708dfa32f27b93180bb6e7a30684d881d995fe257f5b8413e14b20a55c25672d  # flowable/flowable-rest:latest == 8.0.0

USER root

COPY --from=flowable-extension-build /src/target/autonate-flowable-events-1.0.0-SNAPSHOT.jar /app/WEB-INF/lib/autonate-flowable-events.jar
