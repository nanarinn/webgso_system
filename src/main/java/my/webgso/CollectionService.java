package my.webgso;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.stereotype.Service;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

@Service
public class CollectionService {
  private final Store db;
  private final Accounts accounts;
  private final PlantApiClient api;
  private final PayloadTranslator translator;
  private final ValidationService validation;
  private final TransactionTemplate transaction;

  public CollectionService(
      Store db,
      Accounts accounts,
      PlantApiClient api,
      PayloadTranslator translator,
      ValidationService validation,
      PlatformTransactionManager manager) {
    this.db = db;
    this.accounts = accounts;
    this.api = api;
    this.translator = translator;
    this.validation = validation;
    transaction = new TransactionTemplate(manager);
  }

  public CollectionJobEntity collect(
      int plantId,
      String dataType,
      LocalDateTime submission,
      boolean replace,
      String actor,
      Integer scheduleId,
      String requestId,
      int retries) {
    if (requestId != null) {
      var existing = db.list(CollectionJobEntity.class, "WHERE \"CollectionId\"=?", requestId);
      if (!existing.isEmpty()) return existing.getFirst();
    }
    var plant =
        db.find(LssPlant.class, plantId)
            .orElseThrow(() -> new IllegalArgumentException("Plant not found"));
    if (!Boolean.TRUE.equals(plant.isActive))
      throw new IllegalArgumentException("This plant is inactive.");
    String canonical = Policies.canonical(dataType),
        apiType = canonical.equals("MMF") ? "MMF" : "DDQ";
    if (submission.isAfter(LocalDateTime.now(Policies.LOCAL).plusMinutes(1)))
      throw new IllegalArgumentException("Submission time cannot be in the future.");
    // Lock the plant row so manual and scheduled commands cannot replace the same data
    // concurrently.
    record Claim(CollectionJobEntity job, boolean created) {}
    var claim =
        transaction.execute(
            status -> {
              db.list(LssPlant.class, "WHERE \"Id\"=? FOR UPDATE", plantId);
              if (requestId != null) {
                var existing =
                    db.list(CollectionJobEntity.class, "WHERE \"CollectionId\"=?", requestId);
                if (!existing.isEmpty()) return new Claim(existing.getFirst(), false);
              }
              var running =
                  db.list(
                      CollectionJobEntity.class,
                      "WHERE \"PlantId\"=? AND \"DataType\"=? AND \"SubmissionDateTime\"=? AND"
                          + " \"Status\" IN ('Running','Processing','Queued')",
                      plantId,
                      canonical,
                      submission);
              if (!running.isEmpty())
                throw new IllegalArgumentException(
                    "A collection for this plant and submission is already running.");
              var existing =
                  db.list(
                      SolarApiReading.class,
                      "WHERE \"PlantId\"=? AND \"DataType\"=? AND \"SubmissionDateTime\"=? LIMIT 1",
                      plantId,
                      canonical,
                      submission);
              if (!apiType.equals("MMF") && !replace && !existing.isEmpty())
                throw new IllegalArgumentException(
                    "Existing DDQ data found. Select replacement explicitly to replace it.");
              var created = new CollectionJobEntity();
              created.collectionId = requestId == null ? "COL-" + UUID.randomUUID() : requestId;
              created.plantId = plantId;
              created.dataType = canonical;
              created.collectionScheduleId = scheduleId;
              created.mode = scheduleId == null ? "Manual" : "Scheduled";
              created.submissionDate = submission.toLocalDate();
              created.submissionDateTime = submission;
              created.startedAt = OffsetDateTime.now(ZoneOffset.UTC);
              created.status = "Running";
              created.requestedBy = actor;
              created.replaceExisting = replace && !apiType.equals("MMF");
              return new Claim(db.insert(created), true);
            });
    var job = claim.job();
    if (!claim.created()) return job;
    try {
      var links =
          db.list(
              PlantApiConfiguration.class,
              "WHERE \"PlantId\"=? AND \"ApiType\"=? AND \"DataType\"=?",
              plantId,
              apiType,
              canonical);
      String link =
          links.isEmpty()
              ? (apiType.equals("MMF") ? plant.mmfApiEndpoint : plant.ddqApiEndpoint)
              : links.getFirst().apiLink;
      if (link == null || link.isBlank())
        throw new IllegalArgumentException("No API link configured for this dataset.");
      link =
          link.replace("{date}", submission.toLocalDate().toString())
              .replace("{submissionDate}", submission.toLocalDate().toString())
              .replace("{interval}", "15");
      PlantApiClient.Response response = null;
      for (int attempt = 0; attempt <= Math.clamp(retries, 0, 5); attempt++) {
        try {
          response = api.fetch(link);
          break;
        } catch (RuntimeException e) {
          job.retryCount = attempt;
          if (attempt == Math.clamp(retries, 0, 5)) throw e;
        }
      }
      final var payload = response;
      transaction.executeWithoutResult(
          status -> {
            var raw = new RawApiResponse();
            raw.collectionJobId = job.id;
            raw.receivedAt = OffsetDateTime.now(ZoneOffset.UTC);
            raw.httpStatusCode = payload.status();
            raw.contentType = payload.contentType();
            raw.payload = payload.payload();
            try {
              raw.sha256 =
                  HexFormat.of()
                      .formatHex(
                          MessageDigest.getInstance("SHA-256")
                              .digest(raw.payload.getBytes(StandardCharsets.UTF_8)));
            } catch (Exception e) {
              throw new IllegalStateException(e);
            }
            db.insert(raw);
          });
      var translated =
          translator.translate(
              payload.payload(),
              payload.contentType(),
              apiType,
              canonical,
              plantId,
              job.id,
              submission,
              15);
      var issues =
          validation.evaluate(
              translated.readings(), apiType, plant.capacityMw, translated.issues());
      if (translated.readings().isEmpty())
        throw new IllegalArgumentException(
            "The API payload contains no supported measurements. Raw response retained for"
                + " investigation.");
      transaction.executeWithoutResult(
          status -> {
            if (Boolean.TRUE.equals(job.replaceExisting))
              db.jdbc()
                  .update(
                      "DELETE FROM \"SolarApiReadings\" WHERE \"PlantId\"=? AND \"DataType\"=? AND"
                          + " \"SubmissionDateTime\"=?",
                      plantId,
                      canonical,
                      submission);
            for (var reading : translated.readings()) db.insert(reading);
            var rules = db.list(ValidationRule.class, "");
            for (var issue : issues) {
              var anomaly = new DataAnomaly();
              anomaly.collectionJobId = job.id;
              anomaly.validationRuleId =
                  rules.stream()
                      .filter(r -> r.code.equals(issue.code()))
                      .map(r -> r.id)
                      .findFirst()
                      .orElse(null);
              anomaly.dataType = apiType;
              anomaly.fieldName = issue.field();
              anomaly.originalValue = issue.value();
              anomaly.reason = issue.reason();
              db.insert(anomaly);
            }
            job.status = "Completed";
            job.completedAt = OffsetDateTime.now(ZoneOffset.UTC);
            job.durationSeconds =
                Duration.between(job.startedAt, job.completedAt).toMillis() / 1000.0;
            job.responseMessage =
                translated.readings().size()
                    + " reading(s); "
                    + issues.size()
                    + " anomaly/anomalies; "
                    + translated.format();
            db.update(job);
            accounts.audit(
                actor,
                "Collection completed",
                "Collection",
                job.collectionId,
                "Success",
                job.responseMessage);
          });
    } catch (Exception e) {
      job.status = "Failed";
      job.errorMessage =
          e.getMessage() == null
              ? e.getClass().getSimpleName()
              : e.getMessage().substring(0, Math.min(950, e.getMessage().length()));
      job.completedAt = OffsetDateTime.now(ZoneOffset.UTC);
      job.durationSeconds = Duration.between(job.startedAt, job.completedAt).toMillis() / 1000.0;
      db.update(job);
      accounts.audit(
          actor, "Collection failed", "Collection", job.collectionId, "Failed", job.errorMessage);
    }
    return job;
  }

  public PlantApiClient.Response test(int id, String dataType, String actor) {
    var plant = db.find(LssPlant.class, id).orElseThrow();
    String type = Policies.canonical(dataType), apiType = type.equals("MMF") ? "MMF" : "DDQ";
    var links =
        db.list(
            PlantApiConfiguration.class,
            "WHERE \"PlantId\"=? AND \"ApiType\"=? AND \"DataType\"=?",
            id,
            apiType,
            type);
    String url =
        links.isEmpty()
            ? (apiType.equals("MMF") ? plant.mmfApiEndpoint : plant.ddqApiEndpoint)
            : links.getFirst().apiLink;
    if (url == null || url.isBlank())
      throw new IllegalArgumentException(
          "No API URL is configured for "
              + type
              + ". Choose a saved dataset or add its API URL in Edit.");
    try {
      var response = api.fetch(url);
      var parsed =
          translator.translate(
              response.payload(),
              response.contentType(),
              apiType,
              type,
              id,
              0,
              LocalDateTime.now(Policies.LOCAL),
              15);
      if (parsed.readings().isEmpty())
        throw new IllegalArgumentException("The API did not return readable measurements.");
      plant.apiConnectionStatus = "Connected";
      plant.lastApiTestMessage = parsed.readings().size() + " readable value(s)";
      plant.lastApiTestSource = apiType;
      plant.lastApiTestAt = OffsetDateTime.now(ZoneOffset.UTC);
      db.update(plant);
      accounts.audit(
          actor, "API test", "Plant", plant.plantId, "Success", plant.lastApiTestMessage);
      return response;
    } catch (RuntimeException e) {
      plant.apiConnectionStatus = "Failed";
      plant.lastApiTestMessage = e.getMessage();
      plant.lastApiTestAt = OffsetDateTime.now(ZoneOffset.UTC);
      db.update(plant);
      throw e;
    }
  }
}
