package my.webgso;

import java.time.*;
import java.util.*;
import my.webgso.Models.*;
import org.springframework.core.env.Environment;
import org.springframework.scheduling.annotation.Scheduled;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** A daily snapshot of saved test results; never runs API tests or data collections. */
@Service
public class ApiDailyDigest {
  private EmailPreferences preferences;

  @org.springframework.beans.factory.annotation.Autowired
  public void setPreferences(EmailPreferences preferences) {
    this.preferences = preferences;
  }

  private final Store db;
  private final ApiTestHistoryService history;
  private final Environment env;

  public ApiDailyDigest(Store db, ApiTestHistoryService history, Environment env) {
    this.db = db;
    this.history = history;
    this.env = env;
  }

  @Scheduled(cron = "${webgso.api-digest.cron:0 0 9 * * *}", zone = "Asia/Kuala_Lumpur")
  @Transactional
  public void queueDailySummary() {
    if (!env.getProperty("webgso.api-digest.enabled", Boolean.class, true)
        || !env.getProperty("webgso.api-alerts.enabled", Boolean.class, false)
        || !env.getProperty("webgso.mail.enabled", Boolean.class, false)) return;
    if (preferences != null) {
      for (var user : preferences.recipients())
        if (preferences.allows(user.id, "ApiDailySummary")) queueFor(user.email);
      return;
    }
    queueFor(env.getProperty("webgso.api-alerts.recipient", "").strip().toLowerCase(Locale.ROOT));
  }

  private void queueFor(String recipient) {
    if (recipient.isEmpty()) return;
    // Serialize checks across app instances before deciding whether today's message exists.
    var users =
        db.list(
            ApplicationUser.class,
            "WHERE LOWER(\"Email\")=? AND \"AccessStatus\"='Approved' FOR UPDATE",
            recipient);
    if (users.isEmpty()) return;
    var today = LocalDate.now(Policies.LOCAL);
    var start = today.atStartOfDay(Policies.LOCAL).toOffsetDateTime();
    var end = today.plusDays(1).atStartOfDay(Policies.LOCAL).toOffsetDateTime();
    if (!db.list(
            UserAccessNotification.class,
            "WHERE \"NotificationType\"='ApiDailySummary' AND \"Email\"=? AND \"CreatedAt\">=? AND"
                + " \"CreatedAt\"<?",
            recipient,
            start,
            end)
        .isEmpty()) return;
    var rows =
        ApiOverview.rows(
            db.list(LssPlant.class, ""),
            db.list(PlantApiConfiguration.class, ""),
            history.latest());
    var note = new UserAccessNotification();
    note.userId = users.getFirst().id;
    note.email = recipient;
    note.notificationType = "ApiDailySummary";
    note.subject = "GSO Web Services - Daily API summary - " + today;
    note.body = body(rows, today);
    note.deliveryStatus = "Pending";
    db.insert(note);
  }

  static String body(List<Map<String, Object>> rows, LocalDate date) {
    long success = rows.stream().filter(row -> "success".equals(row.get("tone"))).count();
    long failed = rows.stream().filter(row -> "failed".equals(row.get("tone"))).count();
    long untested = rows.stream().filter(row -> "untested".equals(row.get("tone"))).count();
    var text =
        new StringBuilder(
            "Daily API summary (MYT): "
                + date
                + "\n\nTotal configured APIs: "
                + rows.size()
                + "\nSuccess: "
                + success
                + "\nFailed: "
                + failed
                + "\nNot tested: "
                + untested
                + "\n\n"
                + "This is a snapshot of the latest saved test results, not a new API test or"
                + " continuous monitoring.\n");
    if (rows.isEmpty()) text.append("\nNo APIs are configured yet.\n");
    if (failed + untested > 0) {
      text.append("\nAPIs needing attention (up to 10):\n");
      rows.stream()
          .filter(row -> !"success".equals(row.get("tone")))
          .limit(10)
          .forEach(
              row ->
                  text.append("- ")
                      .append(row.get("name"))
                      .append(" / ")
                      .append(row.get("category"))
                      .append(": ")
                      .append(row.get("status"))
                      .append("\n"));
    }
    return text.append(
            "\n"
                + "Open Plant Config or API test history to review details. No API payloads or"
                + " credentials are included.")
        .toString();
  }
}
