package my.webgso;

import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.http.*;
import org.springframework.security.core.Authentication;
import org.springframework.web.bind.annotation.*;

@RestController
public class ApiController {
  private final Store db;
  private final ReadingService readings;
  private final ManualBatchService batches;

  public ApiController(Store db, ReadingService readings, ManualBatchService batches) {
    this.db = db;
    this.readings = readings;
    this.batches = batches;
  }

  @GetMapping("/health")
  public ResponseEntity<Map<String, Object>> health() {
    try {
      db.jdbc().queryForObject("SELECT 1", Integer.class);
      return ResponseEntity.ok(Map.of("status", "Healthy", "service", "webgso-java"));
    } catch (Exception e) {
      return ResponseEntity.status(503)
          .body(Map.of("status", "Unhealthy", "service", "webgso-java"));
    }
  }

  @GetMapping("/api/ping")
  public Map<String, Object> ping() {
    return Map.of("status", "OK", "timestamp", OffsetDateTime.now(ZoneOffset.UTC));
  }

  @GetMapping("/api/collections/batches/{id}")
  public Map<String, Object> batch(@PathVariable String id, Authentication auth) {
    var batch = batches.get(id, WebController.requester(auth));
    return Map.of("complete", batch.complete(), "items", batch.items.values());
  }

  @GetMapping("/api/status")
  public Map<String, Object> status() {
    return Map.of(
        "connected",
        true,
        "plants",
        db.count(LssPlant.class),
        "readings",
        db.count(SolarApiReading.class),
        "collections",
        db.count(CollectionJobEntity.class),
        "reports",
        db.count(ReportExecution.class));
  }

  @GetMapping("/api/dashboard")
  public Map<String, Object> dashboard() {
    return Map.of(
        "status",
        status(),
        "plants",
        db.list(LssPlant.class, "ORDER BY \"PlantId\""),
        "recentCollections",
        db.list(CollectionJobEntity.class, "ORDER BY \"StartedAt\" DESC LIMIT 10"));
  }

  @GetMapping("/api/readings")
  public List<SolarApiReading> readings(
      @RequestParam(defaultValue = "Rolling 24 Hour") String dataType,
      @RequestParam(defaultValue = "") String plantIds,
      @RequestParam String from,
      @RequestParam String to,
      @RequestParam(defaultValue = "") String submission) {
    return readings.select(
        dataType,
        ReadingService.plantIds(plantIds),
        submission.isBlank() ? null : WebController.parseTime(submission),
        WebController.parseTime(from),
        WebController.parseTime(to));
  }

  @GetMapping("/api/notifications")
  public List<UserAccessNotification> notifications(Authentication auth) {
    return db.list(
        UserAccessNotification.class,
        "WHERE \"UserId\"=? ORDER BY \"Id\" DESC LIMIT 50",
        WebController.principal(auth).id());
  }
}
