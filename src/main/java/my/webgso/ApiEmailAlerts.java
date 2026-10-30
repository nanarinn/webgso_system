package my.webgso;

import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.core.env.Environment;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;

/** A separate mail queue; enabling it never enables collection or report schedules. */
@Service
public class ApiEmailAlerts {
  private final Store db;
  private final MailDelivery mail;
  private final Environment env;
  private static final org.slf4j.Logger LOG =
      org.slf4j.LoggerFactory.getLogger(ApiEmailAlerts.class);

  public ApiEmailAlerts(Store db, MailDelivery mail, Environment env) {
    this.db = db;
    this.mail = mail;
    this.env = env;
  }

  public static boolean changed(String previous, boolean success) {
    return success ? "Failed".equals(previous) : !"Failed".equals(previous);
  }

  public void record(int plantId, String dataset, String previous, boolean success, Integer http) {
    if (!env.getProperty("webgso.api-alerts.enabled", Boolean.class, false)
        || !changed(previous, success)) return;
    String recipient = env.getProperty("webgso.api-alerts.recipient", "").strip();
    if (recipient.isEmpty()) return;
    var plant = db.find(LssPlant.class, plantId);
    if (plant.isEmpty()) return;
    var users =
        db.list(
            ApplicationUser.class,
            "WHERE LOWER(\"Email\")=? AND \"AccessStatus\"='Approved'",
            recipient.toLowerCase(Locale.ROOT));
    if (users.isEmpty()) return;
    var note = new UserAccessNotification();
    note.userId = users.getFirst().id;
    note.email = recipient;
    note.notificationType = "ApiTestAlert";
    String state = success ? "recovered" : "failed";
    note.subject = "GSO Web Services: API " + state;
    note.body =
        "Plant: "
            + plant.get().name
            + " ("
            + plant.get().plantId
            + ")\nAPI: "
            + dataset
            + "\nResult: "
            + (success ? "Success - recovered" : "Failed")
            + "\nHTTP: "
            + (http == null ? "No response" : http)
            + "\nTime (MYT): "
            + OffsetDateTime.now()
                .atZoneSameInstant(Policies.LOCAL)
                .format(java.time.format.DateTimeFormatter.ofPattern("dd MMM yyyy, HH:mm:ss"))
            + "\n\n"
            + "Open API test history in GSO Web Services for the full result. No API payload or"
            + " credentials are included in this email.";
    note.deliveryStatus = "Pending";
    db.insert(note);
  }

  @Scheduled(fixedDelay = 10000, initialDelay = 10000)
  public void deliver() {
    if (!env.getProperty("webgso.api-alerts.enabled", Boolean.class, false)
        || !env.getProperty("webgso.mail.enabled", Boolean.class, false)) return;
    for (var note :
        db.list(
            UserAccessNotification.class,
            "WHERE \"NotificationType\"='ApiTestAlert' AND \"DeliveryStatus\"='Pending' ORDER BY"
                + " \"Id\" LIMIT 10")) {
      if (db.jdbc()
              .update(
                  "UPDATE \"UserAccessNotifications\" SET \"DeliveryStatus\"='Sending' WHERE"
                      + " \"Id\"=? AND \"DeliveryStatus\"='Pending'",
                  note.id)
          != 1) continue;
      try {
        mail.send(List.of(note.email), note.subject, note.body, null);
        note.deliveryStatus = "Delivered";
        note.sentAt = OffsetDateTime.now(ZoneOffset.UTC);
      } catch (RuntimeException e) {
        note.deliveryStatus = "Failed";
        note.errorMessage = "Email delivery failed. Check Gmail settings and SMTP connectivity.";
        LOG.warn("API alert {} could not be sent ({})", note.id, e.getClass().getSimpleName());
      }
      db.update(note);
    }
  }
}
