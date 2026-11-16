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
  private EmailPreferences preferences;

  @org.springframework.beans.factory.annotation.Autowired
  public void setPreferences(EmailPreferences preferences) {
    this.preferences = preferences;
  }

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
    var plant = db.find(LssPlant.class, plantId);
    if (plant.isEmpty()) return;
    String recipient = env.getProperty("webgso.api-alerts.recipient", "").strip();
    var users =
        preferences == null
            ? (recipient.isEmpty()
                ? List.<ApplicationUser>of()
                : db.list(
                    ApplicationUser.class,
                    "WHERE LOWER(\"Email\")=? AND \"AccessStatus\"='Approved'",
                    recipient.toLowerCase(Locale.ROOT)))
            : preferences.recipients();
    for (var user : users) {
      if (preferences != null
          && !preferences.allows(user.id, success ? "ApiTestRecovered" : "ApiTestFailed")) continue;
      recipient = preferences == null ? recipient : user.email;
      var note = new UserAccessNotification();
      note.userId = user.id;
      note.email = recipient;
      note.notificationType =
          preferences == null ? "ApiTestAlert" : success ? "ApiTestRecovered" : "ApiTestFailed";
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
              + (success
                  ? "\nAction: The API is working again. No immediate action is required."
                  : "\nAction: " + guidance(http))
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
  }

  public void recordStale(int plantId, String dataset, String previousMessage, String message) {
    if (preferences == null
        || !env.getProperty("webgso.api-alerts.enabled", Boolean.class, false)
        || message == null
        || !message.contains(" Warning:")
        || previousMessage != null && previousMessage.contains("Warning:")) return;
    var plant = db.find(LssPlant.class, plantId);
    if (plant.isEmpty()) return;
    for (var user : preferences.recipients()) {
      if (!preferences.allows(user.id, "StaleDataAlert")) continue;
      var note = new UserAccessNotification();
      note.userId = user.id;
      note.email = user.email;
      note.notificationType = "StaleDataAlert";
      note.subject = "GSO Web Services - Data freshness warning";
      note.body =
          "Plant: "
              + plant.get().name
              + "\nAPI: "
              + dataset
              + "\n"
              + message
              + "\nReview the API provider's data update schedule. No raw API payload is included.";
      note.deliveryStatus = "Pending";
      db.insert(note);
    }
  }

  @Scheduled(fixedDelay = 10000, initialDelay = 10000)
  public void deliver() {
    if (!env.getProperty("webgso.mail.enabled", Boolean.class, false)) return;
    if (!env.getProperty("webgso.mail.dispatch-enabled", Boolean.class, true)) return;
    for (var note :
        db.list(
            UserAccessNotification.class,
            "WHERE \"NotificationType\" IN"
                + " ('ApiIssueMessage','ApiTestAlert','ApiTestFailed','ApiTestRecovered','StaleDataAlert','ApiDailySummary','EmailVerification','PasswordReset','AccessDecision','EmailChangeCode','EmailChangeNotice','PasswordChangeCode')"
                + " AND \"DeliveryStatus\"='Pending'"
                + (env.getProperty("webgso.mail.verification-only", Boolean.class, false)
                    ? " AND \"NotificationType\"='EmailVerification' ORDER BY \"Id\" LIMIT 1"
                    : " ORDER BY \"Id\" LIMIT 10"))) {
      if (db.jdbc()
              .update(
                  "UPDATE \"UserAccessNotifications\" SET \"DeliveryStatus\"='Sending' WHERE"
                      + " \"Id\"=? AND \"DeliveryStatus\"='Pending'",
                  note.id)
          != 1) continue;
      if (preferences != null && !preferences.allowsDelivery(note)) {
        note.deliveryStatus = "Cancelled";
        db.update(note);
        continue;
      }
      try {
        mail.send(List.of(note.email), note.subject, note.body, null);
        note.deliveryStatus = "Delivered";
        note.sentAt = OffsetDateTime.now(ZoneOffset.UTC);
        if (java.util.Set.of("EmailChangeCode", "PasswordChangeCode")
            .contains(note.notificationType)) note.body = "Verification code sent.";
      } catch (RuntimeException e) {
        note.deliveryStatus = "Failed";
        note.errorMessage = "Email delivery failed. Check the email provider, sending permissions and recipient verification.";
        LOG.warn("API alert {} could not be sent ({})", note.id, e.getClass().getSimpleName());
      }
      db.update(note);
    }
  }

  static String guidance(Integer http) {
    if (http == null)
      return "Check the API service, hostname, port and network/VPN connection. Open test history"
          + " for the specific connection error.";
    if (http >= 200 && http < 300)
      return "The server responded, but data validation failed. Check for empty readings, invalid"
          + " timestamps or unsupported data structure in test history.";
    return switch (http) {
      case 401, 403 -> "Check the API authentication and access permissions with the provider.";
      case 404 -> "Check the API URL and endpoint path.";
      case 429 -> "The API rate limit was reached. Wait before retrying.";
      default ->
          http >= 500
              ? "The API server reported an error. Contact the provider or retry later."
              : "Open API test history and review the HTTP response before retrying.";
    };
  }
}
