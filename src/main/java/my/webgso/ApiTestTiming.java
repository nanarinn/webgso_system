package my.webgso;

import java.time.*;
import java.util.*;
import my.webgso.Models.CollectionSchedule;
import org.springframework.scheduling.support.CronExpression;
import org.springframework.stereotype.Service;

@Service
public class ApiTestTiming {
  private final Store db;
  private final ApiTestHistoryService history;

  public ApiTestTiming(Store db, ApiTestHistoryService history) {
    this.db = db;
    this.history = history;
  }

  public Map<String, OffsetDateTime> nextTests() {
    var result = new HashMap<String, OffsetDateTime>();
    var latest = history.latest();
    for (var schedule : db.list(CollectionSchedule.class, "WHERE \"IsActive\"=true")) {
      String dataset =
          "MMF".equals(schedule.apiType) ? "MMF" : Policies.canonical(schedule.dataType);
      String key = ApiTestHistoryService.key(schedule.plantId, dataset);
      var last = latest.get(key);
      if (last == null) continue;
      var next = next(schedule, last.testedAt());
      if (next != null) result.merge(key, next, (a, b) -> a.isBefore(b) ? a : b);
    }
    return result;
  }

  static OffsetDateTime next(CollectionSchedule schedule, OffsetDateTime last) {
    try {
      ZoneId zone = ZoneId.of(schedule.timeZoneId);
      var localLast = last.atZoneSameInstant(zone);
      if (schedule.cronExpression != null && !schedule.cronExpression.isBlank()) {
        var next = CronExpression.parse(schedule.cronExpression).next(localLast);
        return next == null ? null : next.toOffsetDateTime();
      }
      if ("MMF".equals(schedule.apiType) || "MMF".equals(schedule.dataType))
        return last.plusMinutes(Policies.interval(schedule.interval));
      var time = LocalTime.parse(schedule.runTime);
      // Calendar schedules use the next daily, weekly or configured monthly run.
      for (int days = 0; days <= 366; days++) {
        var candidate = localLast.toLocalDate().plusDays(days).atTime(time).atZone(zone);
        if (candidate.isAfter(localLast)
            && Policies.collectionDue(
                schedule, candidate.toLocalDateTime(), localLast.toLocalDateTime()))
          return candidate.toOffsetDateTime();
      }
    } catch (DateTimeException | IllegalArgumentException ignored) {
      // Invalid schedules do not impose a permanent manual-test lock.
    }
    return null;
  }
}
