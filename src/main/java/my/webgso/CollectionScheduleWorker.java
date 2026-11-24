package my.webgso;

import java.time.*;
import java.time.format.DateTimeFormatter;
import my.webgso.Models.*;
import org.slf4j.*;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

/** Runs API collection schedules without enabling report or email automation. */
@Component
@ConditionalOnProperty(name = "webgso.collection-workers.enabled", havingValue = "true")
public class CollectionScheduleWorker {
  private static final Logger log = LoggerFactory.getLogger(CollectionScheduleWorker.class);
  private final Store db;
  private final CollectionService collection;

  public CollectionScheduleWorker(Store db, CollectionService collection) {
    this.db = db;
    this.collection = collection;
  }

  @Scheduled(fixedDelay = 30000, initialDelay = 15000)
  public void collectDueApis() {
    for (var schedule : db.list(CollectionSchedule.class, "WHERE \"IsActive\"=true"))
      try {
        var zone = ZoneId.of(schedule.timeZoneId);
        var now = LocalDateTime.now(zone);
        var latest = db.list(CollectionJobEntity.class,
            "WHERE \"CollectionScheduleId\"=? ORDER BY \"StartedAt\" DESC LIMIT 1", schedule.id);
        LocalDateTime last = latest.isEmpty() ? null
            : latest.getFirst().startedAt.atZoneSameInstant(zone).toLocalDateTime();
        if (!Policies.collectionDue(schedule, now, last)) continue;
        LocalDateTime submission = schedule.apiType.equals("MMF")
            ? now.withSecond(0).withNano(0)
            : now.toLocalDate().atTime(LocalTime.parse(schedule.runTime));
        String code = "SCH-" + schedule.id + "-"
            + submission.format(DateTimeFormatter.ofPattern("yyyyMMddHHmm"));
        if (!db.list(CollectionJobEntity.class, "WHERE \"CollectionId\"=?", code).isEmpty()) continue;
        collection.collect(schedule.plantId,
            schedule.apiType.equals("MMF") ? "MMF" : Policies.canonical(schedule.dataType),
            submission, false, "Collection Scheduler", schedule.id, code, schedule.retryCount);
      } catch (Exception e) {
        log.warn("Collection schedule {} could not run: {}", schedule.id, e.getClass().getSimpleName());
      }
  }
}
