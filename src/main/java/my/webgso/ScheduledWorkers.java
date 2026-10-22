package my.webgso;

import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import my.webgso.Models.*;
import org.slf4j.*;
import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Component;

@Component
@ConditionalOnProperty(name = "webgso.workers.enabled", havingValue = "true")
public class ScheduledWorkers {
  private static final Logger log = LoggerFactory.getLogger(ScheduledWorkers.class);
  private final Store db;
  private final CollectionService collection;
  private final ReportService reports;
  private final MailDelivery mail;

  public ScheduledWorkers(
      Store db, CollectionService collection, ReportService reports, MailDelivery mail) {
    this.db = db;
    this.collection = collection;
    this.reports = reports;
    this.mail = mail;
  }

  @Scheduled(fixedDelay = 30000, initialDelay = 15000)
  public void collections() {
    for (var schedule : db.list(CollectionSchedule.class, "WHERE \"IsActive\"=true"))
      try {
        var zone = ZoneId.of(schedule.timeZoneId);
        var now = LocalDateTime.now(zone);
        var latest =
            db.list(
                CollectionJobEntity.class,
                "WHERE \"CollectionScheduleId\"=? ORDER BY \"StartedAt\" DESC LIMIT 1",
                schedule.id);
        LocalDateTime last =
            latest.isEmpty()
                ? null
                : latest.getFirst().startedAt.atZoneSameInstant(zone).toLocalDateTime();
        if (!Policies.collectionDue(schedule, now, last)) continue;
        LocalDateTime submission =
            schedule.apiType.equals("MMF")
                ? now.withSecond(0).withNano(0)
                : now.toLocalDate().atTime(LocalTime.parse(schedule.runTime));
        String code =
            "SCH-"
                + schedule.id
                + "-"
                + submission.format(DateTimeFormatter.ofPattern("yyyyMMddHHmm"));
        if (!db.list(CollectionJobEntity.class, "WHERE \"CollectionId\"=?", code).isEmpty())
          continue;
        collection.collect(
            schedule.plantId,
            schedule.apiType.equals("MMF") ? "MMF" : Policies.canonical(schedule.dataType),
            submission,
            false,
            "System Scheduler",
            schedule.id,
            code,
            schedule.retryCount);
      } catch (Exception e) {
        log.warn(
            "Collection schedule {} could not run: {}", schedule.id, e.getClass().getSimpleName());
      }
  }

  @Scheduled(fixedDelay = 30000, initialDelay = 20000)
  public void reports() {
    var now = LocalDateTime.now(Policies.LOCAL);
    for (var schedule : db.list(ReportScheduleEntity.class, "WHERE \"IsActive\"=true")) {
      ReportDeliveryAttempt attempt = null;
      try {
        String slot = Policies.reportSlot(schedule, now);
        if (slot == null) continue;
        String code = "DEL-" + schedule.id + "-" + slot.replaceAll("[^0-9]", "");
        if (!db.list(ReportDeliveryAttempt.class, "WHERE \"DeliveryCode\"=?", code).isEmpty())
          continue;
        var slotInstant = LocalDateTime.parse(slot).atZone(Policies.LOCAL).toOffsetDateTime();
        if (!db.list(
                ReportDeliveryAttempt.class,
                "WHERE \"ReportScheduleId\"=? AND \"GeneratedAt\">=? LIMIT 1",
                schedule.id,
                slotInstant)
            .isEmpty()) continue;
        attempt = new ReportDeliveryAttempt();
        attempt.deliveryCode = code;
        attempt.reportScheduleId = schedule.id;
        attempt.scheduleName = schedule.name;
        attempt.apiType = schedule.apiType;
        attempt.generatedAt = OffsetDateTime.now(ZoneOffset.UTC);
        attempt.overallStatus = "Processing";
        try {
          db.insert(attempt);
        } catch (org.springframework.dao.DuplicateKeyException alreadyClaimed) {
          continue;
        }
        var recipients =
            db.list(ReportScheduleRecipient.class, "WHERE \"ReportScheduleId\"=?", schedule.id);
        for (var recipient : recipients) {
          var delivery = new ReportDeliveryRecipient();
          delivery.reportDeliveryAttemptId = attempt.id;
          delivery.email = recipient.email;
          db.insert(delivery);
        }
        String type =
            schedule.apiType.equals("MMF") ? "MMF" : Policies.canonical(schedule.dataType);
        LocalDateTime submission = now.toLocalDate().atStartOfDay();
        var latest =
            db.list(
                SolarApiReading.class,
                "WHERE \"DataType\"=? ORDER BY \"SubmissionDateTime\" DESC LIMIT 1",
                type);
        if (!latest.isEmpty()) submission = latest.getFirst().submissionDateTime;
        var report =
            reports.generate(
                type,
                Set.of(),
                submission,
                schedule.intervalMinutes,
                null,
                null,
                schedule.name,
                "System Scheduler");
        attempt.reportStatus = report.generationStatus;
        if (report.generationStatus.equals("Completed")) {
          try {
            mail.send(
                recipients.stream().map(r -> r.email).toList(),
                schedule.name,
                "Scheduled LSS repository report.",
                reports.attachment(report, "xlsx"));
            attempt.emailStatus = "Delivered";
            attempt.overallStatus = "Completed";
            attempt.message = "Email service accepted the report.";
          } catch (Exception e) {
            attempt.emailStatus = "Failed";
            attempt.overallStatus = "Failed";
            attempt.message = e.getMessage();
          }
        } else {
          attempt.overallStatus = "Failed";
          attempt.message = report.errorMessage;
        }
        db.update(attempt);
      } catch (Exception e) {
        if (attempt != null && attempt.id != null && attempt.id > 0) {
          attempt.overallStatus = "Failed";
          attempt.reportStatus = "Failed";
          attempt.message = "Schedule execution failed: " + e.getClass().getSimpleName();
          db.update(attempt);
        }
        log.warn("Report schedule {} could not run: {}", schedule.id, e.getClass().getSimpleName());
      }
    }
  }

  @Scheduled(fixedDelay = 60000, initialDelay = 30000)
  public void accessNotifications() {
    for (var item :
        db.list(
            UserAccessNotification.class,
            "WHERE \"DeliveryStatus\"='InApp' ORDER BY \"Id\" LIMIT 50"))
      try {
        mail.send(List.of(item.email), item.subject, item.body, null);
        item.deliveryStatus = "Delivered";
        item.sentAt = OffsetDateTime.now(ZoneOffset.UTC);
        db.update(item);
      } catch (Exception e) {
        item.deliveryStatus = "Failed";
        item.errorMessage = e.getMessage();
        db.update(item);
      }
  }
}
